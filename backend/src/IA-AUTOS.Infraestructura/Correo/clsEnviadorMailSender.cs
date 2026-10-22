using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.ReporteDiario;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAAutos.Infraestructura.Correo;

/// <summary>
/// Configuracion del servicio interno de correo (MailSender API). <c>Url</c> es su direccion base (solo alcanzable desde la red interna; la version 1 no usa autenticacion) y <c>Remitente</c> la
/// direccion desde la que se envia. Sin los dos el correo no se envia y el reporte queda registrado con su error de envio.
/// </summary>
public sealed class clsOpcionesCorreo
{
    public const string SECCION = "Correo";

    public string? Url { get; set; }

    public string? Remitente { get; set; }

    /// <summary>Cuanto se espera, despues de encolar el correo, a que el servicio lo confirme como enviado.</summary>
    public int EsperaConfirmacionSegundos { get; set; } = 30;

    /// <summary>Cada cuantos milisegundos se consulta el estado del correo mientras se espera su confirmacion.</summary>
    public int ConsultaCadaMilisegundos { get; set; } = 2000;

    public bool Configurado => Uri.TryCreate(Url, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(Remitente);
}

/// <summary>
/// Envia los correos por el servicio interno MailSender API: <c>POST /api/v1/emails</c> (multipart, con el HTML y los adjuntos) encola el correo y responde 202; un proceso del servicio lo entrega al
/// relay de correo. Despues de encolarlo se consulta <c>GET /api/v1/emails/{id}</c> hasta que quede enviado (estado 3) o se acabe la espera: si el servicio lo acepto y todavia no lo confirmo, se da por
/// enviado (la cola es durable y el servicio reintenta por su cuenta; volver a pedirlo duplicaria el correo), salvo que ya tenga un error. Nunca lanza por un fallo del envio.
/// </summary>
internal sealed class clsEnviadorMailSender(IOptions<clsOpcionesCorreo> opciones, HttpClient http, ILogger<clsEnviadorMailSender> registro) : IEnviadorCorreo
{
    private const int ESTADO_ENVIADO = 3;
    private const int ERROR_MAX = 400;
    private const string ENCABEZADO_CORRELACION = "X-Correlation-ID";

    public async Task<clsResultadoCorreo> EnviarAsync(clsCorreo correo, CancellationToken ct = default)
    {
        var o = opciones.Value;
        if (!o.Configurado)
        {
            return clsResultadoCorreo.Fallo("El envío de correo no está configurado (Correo:Url y Correo:Remitente)");
        }

        try
        {
            var raiz = o.Url!.TrimEnd('/');
            using var formulario = Armar(o.Remitente!, correo);
            using var peticion = new HttpRequestMessage(HttpMethod.Post, $"{raiz}/api/v1/emails") { Content = formulario };
            if (!string.IsNullOrWhiteSpace(correo.IdCorrelacion))
            {
                peticion.Headers.Add(ENCABEZADO_CORRELACION, correo.IdCorrelacion);
            }

            using var respuesta = await http.SendAsync(peticion, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            if (!respuesta.IsSuccessStatusCode)
            {
                return clsResultadoCorreo.Fallo(Recortar($"MailSender respondió {(int)respuesta.StatusCode}: {cuerpo}"));
            }

            return await ConfirmarAsync(raiz, IdDe(cuerpo), correo, o, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or FormatException)
        {
            registro.LogWarning(ex, "No se pudo enviar el correo «{Asunto}» por MailSender", correo.Asunto);
            return clsResultadoCorreo.Fallo(Recortar($"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    /// <summary>El correo en el formato que recibe el servicio: un campo por dato, <c>To</c> repetido por destinatario y un <c>Attachments</c> por archivo.</summary>
    private static MultipartFormDataContent Armar(string remitente, clsCorreo correo)
    {
        var formulario = new MultipartFormDataContent
        {
            { new StringContent(remitente), "From" },
            { new StringContent(correo.Asunto), "Subject" },
            { new StringContent(correo.CuerpoHtml, Encoding.UTF8), "Body" },
            { new StringContent("true"), "IsHtml" },
            { new StringContent("1"), "Priority" },
        };
        foreach (var destinatario in correo.Destinatarios)
        {
            formulario.Add(new StringContent(destinatario), "To");
        }

        foreach (var adjunto in correo.Adjuntos)
        {
            var archivo = new ByteArrayContent(adjunto.Contenido);
            archivo.Headers.ContentType = MediaTypeHeaderValue.Parse(adjunto.TipoContenido);
            formulario.Add(archivo, "Attachments", adjunto.Nombre);
        }

        return formulario;
    }

    /// <summary>Espera a que el servicio confirme el envio. Un correo aceptado que no se alcanza a confirmar se da por enviado; uno que sigue con error al terminar la espera falla.</summary>
    private async Task<clsResultadoCorreo> ConfirmarAsync(string raiz, Guid? id, clsCorreo correo, clsOpcionesCorreo o, CancellationToken ct)
    {
        if (id is null)
        {
            return clsResultadoCorreo.Exito;
        }

        var limite = DateTimeOffset.UtcNow.AddSeconds(Math.Max(o.EsperaConfirmacionSegundos, 0));
        string? ultimoError = null;
        while (true)
        {
            try
            {
                using var estado = await http.GetAsync($"{raiz}/api/v1/emails/{id}", ct);
                if (estado.IsSuccessStatusCode)
                {
                    using var documento = JsonDocument.Parse(await estado.Content.ReadAsStringAsync(ct));
                    if (documento.RootElement.TryGetProperty("status", out var codigo) && codigo.ValueKind == JsonValueKind.Number && codigo.GetInt32() == ESTADO_ENVIADO)
                    {
                        return clsResultadoCorreo.Exito;
                    }

                    ultimoError = documento.RootElement.TryGetProperty("lastError", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // No poder consultar el estado no prueba que el correo fallo: ya esta en la cola del servicio.
                registro.LogWarning(ex, "No se pudo consultar el estado del correo {Id} en MailSender", id);
                return clsResultadoCorreo.Exito;
            }

            if (DateTimeOffset.UtcNow >= limite)
            {
                break;
            }

            await Task.Delay(Math.Max(o.ConsultaCadaMilisegundos, 1), ct);
        }

        if (!string.IsNullOrWhiteSpace(ultimoError))
        {
            return clsResultadoCorreo.Fallo(Recortar($"MailSender no pudo enviarlo: {ultimoError}"));
        }

        registro.LogInformation("MailSender acepto el correo {Id} («{Asunto}») y aun no lo confirma como enviado", id, correo.Asunto);
        return clsResultadoCorreo.Exito;
    }

    /// <summary>El id con el que el servicio encolo el correo; nulo si la respuesta no lo trae (el correo ya fue aceptado de todos modos).</summary>
    private static Guid? IdDe(string cuerpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            return documento.RootElement.ValueKind == JsonValueKind.Object && documento.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var guid)
                ? guid
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Recortar(string texto) => texto.Length <= ERROR_MAX ? texto : texto[..ERROR_MAX];
}
