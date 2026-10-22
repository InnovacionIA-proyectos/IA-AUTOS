using System.Net;
using System.Text;
using IAAutos.Aplicacion.ReporteDiario;
using IAAutos.Infraestructura.Correo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IAAutos.Tests.Unitarios.Notificaciones;

/// <summary>El envio del correo por el servicio MailSender: lo que se le manda, cuando se da por enviado y los fallos que quedan en el resultado.</summary>
public class EnviadorMailSenderTests
{
    private const string URL = "http://mailsender.interno";
    private const string ID = "277173f4-b174-447e-9df1-80e4a5255c98";

    private static readonly clsCorreo CORREO = new(
        ["calidad@ejemplo.com", "comercial@ejemplo.com"],
        "Reporte diario IA Autos 2026-10-21",
        "<html><body>Hola</body></html>",
        [new clsAdjuntoCorreo("hallazgos-2026-10-21.xlsx", [1, 2, 3], "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")],
        "ia-autos-reporte-diario-2026-10-21-intento-1");

    /// <summary>Responde como el servicio: 202 al encolar y el estado que se le indique al consultarlo. Guarda lo que recibio.</summary>
    private sealed class ManejadorFalso(Func<int, HttpResponseMessage>? estado = null, Func<HttpResponseMessage>? encolar = null) : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, string Url, string? Correlacion, string Cuerpo)> Peticiones { get; } = [];

        public int Consultas { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            Peticiones.Add((peticion.Method, peticion.RequestUri!.ToString(), peticion.Headers.TryGetValues("X-Correlation-ID", out var v) ? v.Single() : null, peticion.Content is null ? string.Empty : await peticion.Content.ReadAsStringAsync(ct)));
            if (peticion.Method == HttpMethod.Post)
            {
                return encolar?.Invoke() ?? Json(HttpStatusCode.Accepted, $"{{\"id\":\"{ID}\",\"status\":1,\"statusUrl\":\"/api/v1/emails/{ID}\"}}");
            }

            Consultas++;
            return estado?.Invoke(Consultas) ?? Json(HttpStatusCode.OK, "{\"status\":3,\"lastError\":null}");
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode codigo, string cuerpo) => new(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static clsEnviadorMailSender Crear(ManejadorFalso manejador, bool configurado = true, int esperaSegundos = 1) =>
        new(
            Options.Create(new clsOpcionesCorreo
            {
                Url = configurado ? URL + "/" : null,
                Remitente = configurado ? "reportes@ejemplo.com" : null,
                EsperaConfirmacionSegundos = esperaSegundos,
                ConsultaCadaMilisegundos = 5,
            }),
            new HttpClient(manejador),
            NullLogger<clsEnviadorMailSender>.Instance);

    [Fact]
    public async Task Sin_la_url_y_el_remitente_no_envia_y_lo_dice()
    {
        var manejador = new ManejadorFalso();

        var r = await Crear(manejador, configurado: false).EnviarAsync(CORREO);

        Assert.False(r.Enviado);
        Assert.Equal("El envío de correo no está configurado (Correo:Url y Correo:Remitente)", r.Error);
        Assert.Empty(manejador.Peticiones);
    }

    [Fact]
    public async Task Encola_el_correo_en_el_formulario_del_servicio_con_cada_destinatario_el_html_el_adjunto_y_la_correlacion()
    {
        var manejador = new ManejadorFalso();

        var r = await Crear(manejador).EnviarAsync(CORREO);

        Assert.True(r.Enviado);
        var (metodo, url, correlacion, cuerpo) = manejador.Peticiones[0];
        Assert.Equal(HttpMethod.Post, metodo);
        Assert.Equal(URL + "/api/v1/emails", url);
        Assert.Equal("ia-autos-reporte-diario-2026-10-21-intento-1", correlacion);
        Assert.Contains("name=From", cuerpo);
        Assert.Contains("reportes@ejemplo.com", cuerpo);
        Assert.Equal(2, cuerpo.Split("name=To", StringSplitOptions.None).Length - 1);
        Assert.Contains("calidad@ejemplo.com", cuerpo);
        Assert.Contains("comercial@ejemplo.com", cuerpo);
        Assert.Contains("Reporte diario IA Autos 2026-10-21", cuerpo);
        Assert.Contains("<html><body>Hola</body></html>", cuerpo);
        Assert.Contains("name=IsHtml", cuerpo);
        Assert.Contains("name=Attachments; filename=hallazgos-2026-10-21.xlsx", cuerpo);
    }

    [Fact]
    public async Task Da_el_correo_por_enviado_cuando_el_servicio_lo_confirma_con_el_estado_3()
    {
        var manejador = new ManejadorFalso(n => Json(HttpStatusCode.OK, n < 3 ? "{\"status\":1,\"lastError\":null}" : "{\"status\":3,\"lastError\":null}"));

        var r = await Crear(manejador, esperaSegundos: 5).EnviarAsync(CORREO);

        Assert.True(r.Enviado);
        Assert.Equal(3, manejador.Consultas);
        Assert.Equal($"{URL}/api/v1/emails/{ID}", manejador.Peticiones[^1].Url);
    }

    [Fact]
    public async Task Un_correo_aceptado_que_no_se_alcanza_a_confirmar_se_da_por_enviado_para_no_duplicarlo()
    {
        var manejador = new ManejadorFalso(_ => Json(HttpStatusCode.OK, "{\"status\":1,\"lastError\":null}"));

        var r = await Crear(manejador, esperaSegundos: 0).EnviarAsync(CORREO);

        Assert.True(r.Enviado);
    }

    [Fact]
    public async Task Un_correo_que_al_terminar_la_espera_sigue_con_error_falla_con_ese_error()
    {
        var manejador = new ManejadorFalso(_ => Json(HttpStatusCode.OK, "{\"status\":2,\"lastError\":\"relay no responde\"}"));

        var r = await Crear(manejador, esperaSegundos: 0).EnviarAsync(CORREO);

        Assert.False(r.Enviado);
        Assert.Equal("MailSender no pudo enviarlo: relay no responde", r.Error);
    }

    [Fact]
    public async Task Si_no_se_puede_consultar_el_estado_el_correo_ya_encolado_se_da_por_enviado()
    {
        var manejador = new ManejadorFalso(_ => throw new HttpRequestException("sin red"));

        var r = await Crear(manejador, esperaSegundos: 5).EnviarAsync(CORREO);

        Assert.True(r.Enviado);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "{\"title\":\"Email request is invalid.\"}")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "")]
    [InlineData(HttpStatusCode.InternalServerError, "error")]
    public async Task Un_rechazo_del_servicio_falla_con_su_codigo_y_su_respuesta(HttpStatusCode codigo, string cuerpo)
    {
        var manejador = new ManejadorFalso(encolar: () => Json(codigo, cuerpo));

        var r = await Crear(manejador).EnviarAsync(CORREO);

        Assert.False(r.Enviado);
        Assert.StartsWith($"MailSender respondió {(int)codigo}:", r.Error);
        Assert.Equal(0, manejador.Consultas);
    }

    [Fact]
    public async Task Un_servicio_caido_no_lanza_y_deja_el_error_en_el_resultado()
    {
        var manejador = new ManejadorFalso(encolar: () => throw new HttpRequestException("Connection refused"));

        var r = await Crear(manejador).EnviarAsync(CORREO);

        Assert.False(r.Enviado);
        Assert.Equal("HttpRequestException: Connection refused", r.Error);
    }

    [Fact]
    public async Task Una_respuesta_sin_id_se_toma_como_aceptada_sin_consultar_el_estado()
    {
        var manejador = new ManejadorFalso(encolar: () => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(string.Empty) });

        var r = await Crear(manejador).EnviarAsync(CORREO);

        Assert.True(r.Enviado);
        Assert.Equal(0, manejador.Consultas);
    }

    [Fact]
    public async Task El_error_de_un_rechazo_se_recorta_a_400_caracteres()
    {
        var manejador = new ManejadorFalso(encolar: () => Json(HttpStatusCode.BadRequest, new string('x', 2000)));

        var r = await Crear(manejador).EnviarAsync(CORREO);

        Assert.Equal(400, r.Error!.Length);
    }
}
