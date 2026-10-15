using System.Text.Json;
using System.Text.Json.Nodes;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El contrato OpenAPI es lo que consumen Angular y Flutter. Estas pruebas verifican que describa el JSON real y que el
/// archivo versionado (docs/openapi.json) no se desvie de lo que genera la API.
/// Para regenerarlo tras un cambio intencional: definir ACTUALIZAR_OPENAPI=1 y OPENAPI_DESTINO=&lt;ruta de docs/openapi.json&gt;.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class ContratoOpenApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private static readonly JsonSerializerOptions INDENTADO = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private async Task<JsonNode> ObtenerContratoAsync()
    {
        var fabrica = CrearFabrica(new Dictionary<string, string?> { ["Documentacion:Habilitada"] = "true" });
        var texto = await fabrica.CreateClient().GetStringAsync("/openapi/v1.json");
        return JsonNode.Parse(texto)!;
    }

    [Fact]
    public async Task Los_esquemas_usan_los_mismos_nombres_snake_case_que_el_json_real()
    {
        var contrato = await ObtenerContratoAsync();

        var inspeccion = contrato["components"]!["schemas"]!["InspeccionDto"]!["properties"]!.AsObject().Select(p => p.Key).ToList();
        var tasa = contrato["components"]!["schemas"]!["TasaRechazoDto"]!["properties"]!.AsObject().Select(p => p.Key).ToList();
        var pagina = contrato["components"]!["schemas"]!["ListadoPaginadoDto"]!["properties"]!.AsObject().Select(p => p.Key).ToList();

        Assert.Contains("etapa_id", inspeccion);
        Assert.Contains("viaje_id", inspeccion);
        Assert.Contains("conductor_nombre", inspeccion);
        Assert.Contains("created_at", inspeccion);
        Assert.DoesNotContain("etapaId", inspeccion);
        Assert.Equal(["tasaGlobal", "totalMovilizadas", "totalPendientes", "totalConNovedad", "totalNovedad", "porOperacion"], tasa);
        Assert.Equal(["rows", "total", "page", "pageSize"], pagina);
    }

    [Fact]
    public async Task Los_numeros_son_estrictos_y_no_aceptan_texto()
    {
        var contrato = await ObtenerContratoAsync();

        var lat = contrato["components"]!["schemas"]!["InspeccionDto"]!["properties"]!["lat"]!.ToJsonString();

        Assert.DoesNotContain("\"string\"", lat);
        Assert.DoesNotContain("pattern", lat);
    }

    [Fact]
    public async Task Toda_operacion_con_cuerpo_lo_declara_y_los_endpoints_publicos_no_exigen_bearer()
    {
        var contrato = await ObtenerContratoAsync();
        var rutas = contrato["paths"]!.AsObject();

        var conCuerpo = new Dictionary<string, string>
        {
            ["/api/auth/login|post"] = "LoginRequest",
            ["/api/auth/login-portal|post"] = "LoginPortalUsuarioDto",
            ["/api/usuarios|post"] = "CrearUsuarioRequest",
            ["/api/usuarios/{id}|patch"] = "ActualizarUsuarioRequest",
            ["/api/vehiculos/cargar|post"] = "CargarVinsRequest",
            ["/api/viajes|post"] = "CrearViajeRequest",
            ["/api/viajes/{id}|patch"] = "ActualizarViajeRequest",
            ["/api/viajes/{id}/estado|patch"] = "CambiarEstadoViajeRequest",
            ["/api/viajes/{id}/vehiculos|post"] = "AgregarVinesRequest",
            ["/api/viajes/cargar/previsualizar|post"] = "CargarViajesRequest",
            ["/api/viajes/cargar|post"] = "CargarViajesRequest",
            ["/api/inspecciones|post"] = "CrearInspeccionRequest",
            ["/api/inspecciones/{id}/evidencias|post"] = "CrearEvidenciaRequest",
            ["/api/inspecciones/{id}/hallazgos|post"] = "CrearHallazgoRequest",
            ["/api/inspecciones/{id}/transferencia|post"] = "FirmarTransferenciaRequest",
            ["/api/uploads|post"] = "SubirArchivoRequest",
            ["/api/etl/sofasa/ordenes|post"] = "IngestarOrdenesRequest",
        };
        foreach (var (clave, tipo) in conCuerpo)
        {
            var partes = clave.Split('|');
            var referencia = rutas[partes[0]]![partes[1]]!["requestBody"]!["content"]!["application/json"]!["schema"]!.ToJsonString();
            Assert.Contains(tipo, referencia);
        }

        Assert.NotNull(contrato["components"]!["securitySchemes"]!["bearerAuth"]);
        Assert.Null(rutas["/api/auth/login"]!["post"]!["security"]);
        Assert.Null(rutas["/api/auth/login-portal"]!["post"]!["security"]);
        Assert.Null(rutas["/api/auth/refresh"]!["post"]!["security"]);
        Assert.Null(rutas["/api/auth/logout"]!["post"]!["security"]);
        Assert.Null(rutas["/api/auth/login-portal"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!["properties"]);
        Assert.Equal("array", rutas["/api/auth/login-portal"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!["type"]!.GetValue<string>());
        Assert.Null(rutas["/health"]!["get"]!["security"]);
        Assert.Null(rutas["/api/etl/sofasa/ordenes"]!["post"]!["security"]);
        Assert.NotNull(rutas["/api/usuarios"]!["get"]!["security"]);
        Assert.Equal("Auth_Login", rutas["/api/auth/login"]!["post"]!["operationId"]!.GetValue<string>());
        Assert.NotNull(rutas["/api/usuarios"]!["get"]!["responses"]!["default"]);
    }

    [Fact]
    public async Task El_contrato_versionado_coincide_con_el_que_genera_la_api()
    {
        var generado = (await ObtenerContratoAsync()).ToJsonString(INDENTADO);
        var destino = Environment.GetEnvironmentVariable("OPENAPI_DESTINO");
        if (Environment.GetEnvironmentVariable("ACTUALIZAR_OPENAPI") == "1" && !string.IsNullOrEmpty(destino))
        {
            await File.WriteAllTextAsync(destino, generado + "\n");
            return;
        }

        var versionado = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "docs", "openapi.json")))!.ToJsonString(INDENTADO);

        Assert.True(
            versionado == generado,
            "docs/openapi.json no coincide con el contrato generado. Si el cambio es intencional, regenere el archivo con ACTUALIZAR_OPENAPI=1 y OPENAPI_DESTINO.");
    }
}
