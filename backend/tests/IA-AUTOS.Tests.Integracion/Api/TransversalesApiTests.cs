using System.Net;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class TransversalesApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    [Fact]
    public async Task Health_es_publico_y_confirma_la_base_de_datos()
    {
        var respuesta = await Cliente().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("db").GetProperty("connected").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("db").GetProperty("latencyMs").GetInt64() >= 0);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", doc.RootElement.GetProperty("timestamp").GetString()!);
    }

    [Fact]
    public async Task Health_responde_503_sin_exponer_el_error_cuando_la_base_no_responde()
    {
        var sinBase = CrearFabrica(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1,1;Database=IAAutos;User Id=x;Password=x;Connect Timeout=2;Encrypt=False",
        });

        var respuesta = await sinBase.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("unhealthy", doc.RootElement.GetProperty("status").GetString());
        Assert.False(doc.RootElement.GetProperty("db").GetProperty("connected").GetBoolean());
        Assert.DoesNotContain("127.0.0.1", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ruta_inexistente_responde_404_json_con_el_mensaje_original()
    {
        var respuesta = await Cliente().GetAsync("/api/no-existe");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("NOT_FOUND", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Ruta no encontrada: /api/no-existe", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Metodo_no_permitido_responde_405_json()
    {
        var usuario = await Fabrica.SembrarUsuarioAsync("ana@ejemplo.com", "x");
        var token = await LoginAsync("ana@ejemplo.com", "x");

        var respuesta = await Cliente(token).PostAsync("/api/auth/me", Json("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("METHOD_NOT_ALLOWED", doc.RootElement.GetProperty("code").GetString());
        Assert.StartsWith("Método POST no permitido en /api/auth/me", doc.RootElement.GetProperty("message").GetString());
        Assert.NotEqual(Guid.Empty, usuario.Id);
    }

    [Fact]
    public async Task Cors_responde_el_preflight_solo_a_los_origenes_configurados()
    {
        var cliente = Cliente();

        var permitido = new HttpRequestMessage(HttpMethod.Options, "/api/usuarios");
        permitido.Headers.Add("Origin", clsFabricaApi.ORIGEN_PERMITIDO);
        permitido.Headers.Add("Access-Control-Request-Method", "GET");
        permitido.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-dispositivo-id");
        var rp = await cliente.SendAsync(permitido);

        var ajeno = new HttpRequestMessage(HttpMethod.Options, "/api/usuarios");
        ajeno.Headers.Add("Origin", "https://malo.ejemplo.com");
        ajeno.Headers.Add("Access-Control-Request-Method", "GET");
        var ra = await cliente.SendAsync(ajeno);

        Assert.Equal(HttpStatusCode.NoContent, rp.StatusCode);
        Assert.Equal(clsFabricaApi.ORIGEN_PERMITIDO, rp.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("Authorization", string.Join(",", rp.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Dispositivo-Id", string.Join(",", rp.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("86400", rp.Headers.GetValues("Access-Control-Max-Age").Single());
        Assert.False(ra.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_no_se_habilita_si_no_hay_origenes_configurados()
    {
        var sinCors = CrearFabrica(new Dictionary<string, string?> { ["Cors:Origins"] = string.Empty });
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "/health");
        solicitud.Headers.Add("Origin", clsFabricaApi.ORIGEN_PERMITIDO);

        var respuesta = await sinCors.CreateClient().SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(respuesta.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Las_respuestas_llevan_cabeceras_de_seguridad_y_no_revelan_el_servidor()
    {
        var respuesta = await Cliente().GetAsync("/health");

        Assert.Equal("nosniff", respuesta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", respuesta.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", respuesta.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", respuesta.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
        Assert.False(respuesta.Headers.Contains("Server"));
    }

    [Fact]
    public async Task La_api_no_arranca_si_falta_el_secreto_jwt()
    {
        var sinSecreto = CrearFabrica(new Dictionary<string, string?> { ["Jwt:Secret"] = string.Empty });

        var ex = Assert.ThrowsAny<Exception>(() => sinSecreto.CreateClient());

        Assert.Contains("Jwt:Secret", ex.ToString());
    }

    [Fact]
    public async Task La_documentacion_openapi_solo_esta_disponible_si_se_habilita()
    {
        var sin = await Cliente().GetAsync("/openapi/v1.json");
        var habilitada = CrearFabrica(new Dictionary<string, string?> { ["Documentacion:Habilitada"] = "true" });
        var con = await habilitada.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, sin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, con.StatusCode);
        var texto = await con.Content.ReadAsStringAsync();
        Assert.Contains("/api/auth/login", texto);
        Assert.Contains("/api/usuarios", texto);
    }
}
