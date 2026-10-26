using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Infraestructura.Notificaciones;
using IAAutos.Tests.Unitarios.Falsos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IAAutos.Tests.Unitarios.Notificaciones;

/// <summary>El envio del push por FCM: la cuenta de servicio, el token de acceso que se reutiliza, el mensaje y los errores que dejan el dispositivo inactivo.</summary>
public class EnviadorFcmTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly clsMensajePush MENSAJE = new("Novedad en ruta", "Un golpe en la unidad", new Dictionary<string, string> { ["tipo"] = "novedad_conductor" });

    /// <summary>Responde como lo harian Google (el token de acceso) y FCM (el envio) y guarda lo que recibio.</summary>
    private sealed class ManejadorFalso(Func<HttpRequestMessage, HttpResponseMessage>? fcm = null) : HttpMessageHandler
    {
        public List<(string Url, string? Autorizacion, string Cuerpo)> Peticiones { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            Peticiones.Add((peticion.RequestUri!.ToString(), peticion.Headers.Authorization?.ToString(), peticion.Content is null ? string.Empty : await peticion.Content.ReadAsStringAsync(ct)));
            if (peticion.RequestUri.Host == "oauth2.googleapis.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"acceso-1\",\"expires_in\":3600}", Encoding.UTF8, "application/json") };
            }

            return fcm?.Invoke(peticion) ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"name\":\"projects/p/messages/1\"}", Encoding.UTF8, "application/json") };
        }
    }

    private static clsEnviadorFcm Crear(ManejadorFalso manejador, clsRelojFijo? reloj = null, bool configurado = true)
    {
        var opciones = new clsOpcionesNotificaciones();
        if (configurado)
        {
            using var rsa = RSA.Create(2048);
            opciones.FcmProyectoId = "proyecto-de-pruebas";
            opciones.FcmClientEmail = "cuenta@proyecto-de-pruebas.iam.gserviceaccount.com";
            // Como llega en una variable de entorno: los saltos de linea de la clave escritos como «\n».
            opciones.FcmClavePrivada = rsa.ExportPkcs8PrivateKeyPem().Replace("\r", string.Empty).Replace("\n", "\\n");
        }

        return new clsEnviadorFcm(Options.Create(opciones), new HttpClient(manejador), reloj ?? new clsRelojFijo(AHORA), NullLogger<clsEnviadorFcm>.Instance);
    }

    [Fact]
    public async Task Sin_la_cuenta_de_servicio_no_envia_y_lo_dice()
    {
        var manejador = new ManejadorFalso();

        var r = await Crear(manejador, configurado: false).EnviarAsync("token-1", MENSAJE);

        Assert.False(r.Enviado);
        Assert.False(r.TokenInvalido);
        Assert.Equal("El envío por FCM no está configurado", r.Error);
        Assert.Empty(manejador.Peticiones);
    }

    [Fact]
    public async Task Envia_el_mensaje_al_token_con_el_acceso_de_la_cuenta_de_servicio_firmado()
    {
        var manejador = new ManejadorFalso();

        var r = await Crear(manejador).EnviarAsync("token-1", MENSAJE);

        Assert.True(r.Enviado);
        Assert.Equal(2, manejador.Peticiones.Count);
        // Primero pide el token de acceso con un JWT firmado (RS256) por la cuenta de servicio.
        var (urlToken, _, cuerpoToken) = manejador.Peticiones[0];
        Assert.Equal("https://oauth2.googleapis.com/token", urlToken);
        var asercion = Uri.UnescapeDataString(cuerpoToken.Split('&').Single(p => p.StartsWith("assertion=", StringComparison.Ordinal))[10..]);
        var partes = asercion.Split('.');
        Assert.Equal(3, partes.Length);
        using var contenido = JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(partes[1]));
        Assert.Equal("cuenta@proyecto-de-pruebas.iam.gserviceaccount.com", contenido.RootElement.GetProperty("iss").GetString());
        Assert.Equal("https://www.googleapis.com/auth/firebase.messaging", contenido.RootElement.GetProperty("scope").GetString());
        Assert.Equal(AHORA.ToUnixTimeSeconds() + 3600, contenido.RootElement.GetProperty("exp").GetInt64());
        // Despues envia el mensaje a ese token con el acceso recibido.
        var (urlEnvio, autorizacion, cuerpoEnvio) = manejador.Peticiones[1];
        Assert.Equal("https://fcm.googleapis.com/v1/projects/proyecto-de-pruebas/messages:send", urlEnvio);
        Assert.Equal("Bearer acceso-1", autorizacion);
        using var documentoEnvio = JsonDocument.Parse(cuerpoEnvio);
        var mensaje = documentoEnvio.RootElement.GetProperty("message");
        Assert.Equal("token-1", mensaje.GetProperty("token").GetString());
        Assert.Equal("Novedad en ruta", mensaje.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal("Un golpe en la unidad", mensaje.GetProperty("notification").GetProperty("body").GetString());
        Assert.Equal("novedad_conductor", mensaje.GetProperty("data").GetProperty("tipo").GetString());
    }

    [Fact]
    public async Task Reutiliza_el_acceso_mientras_no_este_por_vencer_y_lo_renueva_despues()
    {
        var reloj = new clsRelojFijo(AHORA);
        var manejador = new ManejadorFalso();
        var enviador = Crear(manejador, reloj);

        await enviador.EnviarAsync("token-1", MENSAJE);
        await enviador.EnviarAsync("token-2", MENSAJE);
        var pedidosDeAcceso = manejador.Peticiones.Count(p => p.Url.Contains("oauth2", StringComparison.Ordinal));
        reloj.Ahora = AHORA.AddMinutes(61);
        await enviador.EnviarAsync("token-3", MENSAJE);

        Assert.Equal(1, pedidosDeAcceso);
        Assert.Equal(2, manejador.Peticiones.Count(p => p.Url.Contains("oauth2", StringComparison.Ordinal)));
        Assert.Equal(3, manejador.Peticiones.Count(p => p.Url.Contains("fcm.googleapis.com", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{\"error\":{\"code\":404,\"message\":\"Requested entity was not found.\",\"status\":\"NOT_FOUND\"}}", true)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":{\"code\":400,\"message\":\"x\",\"details\":[{\"errorCode\":\"UNREGISTERED\"}]}}", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":503,\"message\":\"The service is currently unavailable.\"}}", false)]
    [InlineData(HttpStatusCode.InternalServerError, "no es json", false)]
    public async Task Un_error_de_fcm_se_informa_y_solo_un_token_desconocido_lo_deja_inactivo(HttpStatusCode estado, string cuerpo, bool tokenInvalido)
    {
        var manejador = new ManejadorFalso(_ => new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") });

        var r = await Crear(manejador).EnviarAsync("token-1", MENSAJE);

        Assert.False(r.Enviado);
        Assert.Equal(tokenInvalido, r.TokenInvalido);
        Assert.StartsWith($"FCM respondió {(int)estado}: ", r.Error);
    }

    [Fact]
    public async Task Una_falla_de_red_no_lanza_y_se_informa()
    {
        var manejador = new ManejadorFalso(_ => throw new HttpRequestException("sin red"));

        var r = await Crear(manejador).EnviarAsync("token-1", MENSAJE);

        Assert.False(r.Enviado);
        Assert.False(r.TokenInvalido);
        Assert.Contains("sin red", r.Error);
    }

    [Fact]
    public async Task Si_google_no_entrega_el_acceso_el_envio_falla_sin_lanzar()
    {
        var manejador = new ManejadorFalso();
        var enviador = new clsEnviadorFcm(
            Options.Create(new clsOpcionesNotificaciones { FcmProyectoId = "p", FcmClientEmail = "c@p.iam", FcmClavePrivada = "no es una clave" }),
            new HttpClient(manejador),
            new clsRelojFijo(AHORA),
            NullLogger<clsEnviadorFcm>.Instance);

        var r = await enviador.EnviarAsync("token-1", MENSAJE);

        Assert.False(r.Enviado);
        Assert.StartsWith("No se pudo enviar el push", r.Error);
    }
}
