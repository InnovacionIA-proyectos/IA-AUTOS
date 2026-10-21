using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Notificaciones;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAAutos.Infraestructura.Notificaciones;

/// <summary>
/// Configuracion del envio de notificaciones. FCM usa una cuenta de servicio de Firebase: el proyecto, su correo y su clave privada (en la variable de entorno, los saltos de linea de la clave
/// pueden ir como «\n»). Sin esos tres valores el push no se envia y cada notificacion queda con su error de envio.
/// </summary>
public sealed class clsOpcionesNotificaciones
{
    public const string SECCION = "Notificaciones";

    public string? FcmProyectoId { get; set; }

    public string? FcmClientEmail { get; set; }

    public string? FcmClavePrivada { get; set; }

    public bool FcmConfigurado =>
        !string.IsNullOrWhiteSpace(FcmProyectoId) && !string.IsNullOrWhiteSpace(FcmClientEmail) && !string.IsNullOrWhiteSpace(FcmClavePrivada);
}

/// <summary>
/// Envia los push por Firebase Cloud Messaging (API HTTP v1). Obtiene el token de acceso de la cuenta de servicio (un JWT firmado con su clave privada, que Google cambia por un token de una
/// hora y se reutiliza) y envia el mensaje al token del dispositivo. Nunca lanza por un fallo del envio: lo devuelve como resultado, y marca el token como invalido cuando FCM ya no lo reconoce.
/// </summary>
internal sealed class clsEnviadorFcm(IOptions<clsOpcionesNotificaciones> opciones, HttpClient http, IReloj reloj, ILogger<clsEnviadorFcm> registro) : IEnviadorPush
{
    private const string URL_TOKEN = "https://oauth2.googleapis.com/token";
    private const string ALCANCE = "https://www.googleapis.com/auth/firebase.messaging";
    private static readonly TimeSpan MARGEN = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _bloqueo = new(1, 1);
    private string? _acceso;
    private DateTimeOffset _expira = DateTimeOffset.MinValue;

    public async Task<clsResultadoPush> EnviarAsync(string tokenFcm, clsMensajePush mensaje, CancellationToken ct = default)
    {
        var o = opciones.Value;
        if (!o.FcmConfigurado)
        {
            return clsResultadoPush.Fallo("El envío por FCM no está configurado");
        }

        try
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(o.FcmProyectoId!)}/messages:send");
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await ObtenerAccesoAsync(o, ct));
            peticion.Content = JsonContent.Create(new
            {
                message = new
                {
                    token = tokenFcm,
                    notification = new { title = mensaje.Titulo, body = mensaje.Mensaje },
                    data = mensaje.Datos,
                    android = new { priority = "HIGH" },
                },
            });
            using var respuesta = await http.SendAsync(peticion, ct);
            if (respuesta.IsSuccessStatusCode)
            {
                return clsResultadoPush.Exito;
            }

            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            // FCM responde 404 UNREGISTERED cuando la app se desinstalo o el token se renovo: ese dispositivo ya no recibe nada.
            var invalido = respuesta.StatusCode == HttpStatusCode.NotFound || cuerpo.Contains("UNREGISTERED", StringComparison.Ordinal)
                || cuerpo.Contains("not a valid FCM registration token", StringComparison.OrdinalIgnoreCase);
            return clsResultadoPush.Fallo($"FCM respondió {(int)respuesta.StatusCode}: {MensajeDeError(cuerpo)}", invalido);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException or JsonException or InvalidOperationException or ArgumentException)
        {
            registro.LogWarning(ex, "No se pudo enviar el push por FCM");
            return clsResultadoPush.Fallo($"No se pudo enviar el push: {ex.Message}");
        }
    }

    /// <summary>El token de acceso vigente de la cuenta de servicio; lo renueva (un solo envio a la vez) cuando esta por vencer.</summary>
    private async Task<string> ObtenerAccesoAsync(clsOpcionesNotificaciones o, CancellationToken ct)
    {
        if (_acceso is not null && reloj.Ahora < _expira - MARGEN)
        {
            return _acceso;
        }

        await _bloqueo.WaitAsync(ct);
        try
        {
            if (_acceso is not null && reloj.Ahora < _expira - MARGEN)
            {
                return _acceso;
            }

            using var peticion = new HttpRequestMessage(HttpMethod.Post, URL_TOKEN)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = Firmar(o),
                }),
            };
            using var respuesta = await http.SendAsync(peticion, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            if (!respuesta.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Google no entregó el token de acceso ({(int)respuesta.StatusCode})");
            }

            using var documento = JsonDocument.Parse(cuerpo);
            _acceso = documento.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google no entregó el token de acceso");
            _expira = reloj.Ahora.AddSeconds(documento.RootElement.TryGetProperty("expires_in", out var segundos) ? segundos.GetInt32() : 3600);
            return _acceso;
        }
        finally
        {
            _bloqueo.Release();
        }
    }

    /// <summary>El JWT (RS256) con el que la cuenta de servicio pide su token de acceso.</summary>
    private string Firmar(clsOpcionesNotificaciones o)
    {
        var ahora = reloj.Ahora.ToUnixTimeSeconds();
        var encabezado = Codificar(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var contenido = Codificar(JsonSerializer.SerializeToUtf8Bytes(new { iss = o.FcmClientEmail, scope = ALCANCE, aud = URL_TOKEN, iat = ahora, exp = ahora + 3600 }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(o.FcmClavePrivada!.Replace("\\n", "\n"));
        var firma = rsa.SignData(Encoding.ASCII.GetBytes($"{encabezado}.{contenido}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{encabezado}.{contenido}.{Codificar(firma)}";
    }

    private static string Codificar(byte[] datos) => Base64Url.EncodeToString(datos);

    /// <summary>El mensaje del error que devuelve FCM (o el cuerpo recortado si no viene con la forma esperada).</summary>
    private static string MensajeDeError(string cuerpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            if (documento.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var mensaje))
            {
                return mensaje.GetString() ?? cuerpo;
            }
        }
        catch (JsonException)
        {
        }

        return cuerpo.Length <= 200 ? cuerpo : cuerpo[..200];
    }
}
