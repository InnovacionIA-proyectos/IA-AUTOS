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
        var pagina = contrato["components"]!["schemas"]!["PaginaDtoOfListadoVehiculoDto"]!["properties"]!.AsObject().Select(p => p.Key).ToList();

        Assert.Contains("etapa_id", inspeccion);
        Assert.Contains("viaje_id", inspeccion);
        Assert.Contains("conductor_nombre", inspeccion);
        Assert.Contains("created_at", inspeccion);
        Assert.DoesNotContain("etapaId", inspeccion);
        Assert.Equal(["tasaGlobal", "totalMovilizadas", "totalPendientes", "totalConNovedad", "totalNovedad", "porOperacion"], tasa);
        Assert.Equal(["items", "total", "pagina", "tamano"], pagina);
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
            ["/api/auth/login-conductor|post"] = "LoginConductorRequest",
            ["/api/auth/cambiar-pin|post"] = "CambiarPinRequest",
            ["/api/usuarios|post"] = "CrearUsuarioRequest",
            ["/api/usuarios/{id}/pin|post"] = "AsignarPinRequest",
            ["/api/usuarios/cargar/previsualizar|post"] = "CargarConductoresRequest",
            ["/api/usuarios/cargar|post"] = "CargarConductoresRequest",
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
            ["/api/inspecciones/{id}/transferencia/reservas|post"] = "RecibirConReservasRequest",
            ["/api/inspecciones/{id}/transferencia/no-firma|post"] = "RegistrarNoFirmaRequest",
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
        Assert.Null(rutas["/api/auth/login-conductor"]!["post"]!["security"]);
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

    // ---------------------------------------------------------------------------- publicacion y versionado

    private static string RutaDocs(string archivo) => Path.Combine(AppContext.BaseDirectory, "docs", archivo);

    private static string HuellaDe(string contrato) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contrato))).ToLowerInvariant();

    [Fact]
    public async Task El_contrato_versionado_coincide_con_el_que_genera_la_api()
    {
        var generado = (await ObtenerContratoAsync()).ToJsonString(INDENTADO);
        var destino = Environment.GetEnvironmentVariable("OPENAPI_DESTINO");
        if (Environment.GetEnvironmentVariable("ACTUALIZAR_OPENAPI") == "1" && !string.IsNullOrEmpty(destino))
        {
            await File.WriteAllTextAsync(destino, generado + "\n");

            // Publicar una version: deja su huella en docs/openapi-versiones.json. Una version ya registrada no se sobrescribe: si el contrato cambia, hay que subir info.version.
            if (Environment.GetEnvironmentVariable("OPENAPI_PUBLICAR") == "1")
            {
                var registro = Path.Combine(Path.GetDirectoryName(destino)!, "openapi-versiones.json");
                var versiones = File.Exists(registro) ? JsonNode.Parse(await File.ReadAllTextAsync(registro))!.AsObject() : new JsonObject();
                var version = JsonNode.Parse(generado)!["info"]!["version"]!.GetValue<string>();
                var huella = HuellaDe(generado);
                if (versiones[version] is { } registrada && registrada.GetValue<string>() != huella)
                {
                    Assert.Fail($"La version {version} ya esta publicada con otro contenido: suba info.version (clsOpenApi.VERSION) antes de publicar el contrato nuevo.");
                }

                versiones[version] = huella;
                await File.WriteAllTextAsync(registro, versiones.ToJsonString(INDENTADO) + "\n");
            }

            return;
        }

        var versionado = JsonNode.Parse(await File.ReadAllTextAsync(RutaDocs("openapi.json")))!.ToJsonString(INDENTADO);

        Assert.True(
            versionado == generado,
            "docs/openapi.json no coincide con el contrato generado. Si el cambio es intencional, suba info.version (clsOpenApi.VERSION) y regenere el archivo con ACTUALIZAR_OPENAPI=1, OPENAPI_PUBLICAR=1 y OPENAPI_DESTINO.");
    }

    [Fact]
    public async Task El_contrato_no_cambia_sin_cambiar_la_version()
    {
        var generado = (await ObtenerContratoAsync()).ToJsonString(INDENTADO);
        var version = JsonNode.Parse(generado)!["info"]!["version"]!.GetValue<string>();
        var registro = JsonNode.Parse(await File.ReadAllTextAsync(RutaDocs("openapi-versiones.json")))!.AsObject();

        Assert.True(registro.ContainsKey(version), $"La version {version} no esta registrada en docs/openapi-versiones.json: publiquela con ACTUALIZAR_OPENAPI=1 y OPENAPI_PUBLICAR=1.");
        Assert.True(
            registro[version]!.GetValue<string>() == HuellaDe(generado),
            $"El contrato cambio sin cambiar info.version ({version}): suba la version en clsOpenApi.VERSION y publique el contrato nuevo (una version publicada no se modifica).");
    }

    [Fact]
    public async Task La_version_del_contrato_es_la_2_o_posterior_en_la_misma_ruta_api_sin_prefijo_de_version()
    {
        var contrato = await ObtenerContratoAsync();

        var version = Version.Parse(contrato["info"]!["version"]!.GetValue<string>());

        Assert.True(version >= new Version(2, 0, 0), $"info.version es {version}");
        Assert.All(contrato["paths"]!.AsObject().Select(p => p.Key), ruta => Assert.DoesNotMatch(@"^/api/v\d+/", ruta));
        Assert.Contains("docs/migracion-v1-v2.md", contrato["info"]!["description"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------------------ errores y ejemplos

    private static IEnumerable<(string Ruta, string Metodo, JsonNode Operacion)> Operaciones(JsonNode contrato) =>
        contrato["paths"]!.AsObject().SelectMany(r => r.Value!.AsObject().Where(m => m.Value is JsonObject && m.Key is "get" or "post" or "put" or "patch" or "delete").Select(m => (r.Key, m.Key, m.Value!)));

    private static JsonNode? Resolver(JsonNode contrato, JsonNode? esquema)
    {
        var referencia = esquema?["$ref"]?.GetValue<string>();
        return referencia is null ? esquema : contrato["components"]!["schemas"]![referencia[(referencia.LastIndexOf('/') + 1)..]];
    }

    [Fact]
    public async Task Cada_operacion_documenta_sus_errores_por_codigo_http_con_el_cuerpo_comun_y_un_ejemplo_por_codigo()
    {
        var contrato = await ObtenerContratoAsync();

        foreach (var (ruta, metodo, operacion) in Operaciones(contrato))
        {
            var respuestas = operacion["responses"]!.AsObject();
            Assert.True(respuestas.ContainsKey("default") && respuestas["default"]!["content"]!["application/json"]!["schema"]!.ToJsonString().Contains("ErrorApiDto"), $"{metodo} {ruta}: falta la respuesta de error por defecto");
            foreach (var (estado, respuesta) in respuestas.Where(r => r.Key is not "default" && int.Parse(r.Key, System.Globalization.CultureInfo.InvariantCulture) >= 400))
            {
                var ejemplos = respuesta!["content"]!["application/json"]!["examples"]!.AsObject();
                Assert.NotEmpty(ejemplos);
                foreach (var (codigo, ejemplo) in ejemplos)
                {
                    Assert.Equal(codigo, ejemplo!["value"]!["code"]!.GetValue<string>());
                    Assert.NotNull(ejemplo["value"]!["error"]);
                    Assert.NotNull(ejemplo["value"]!["message"]);
                    Assert.NotNull(ejemplo["value"]!["timestamp"]);
                }

                Assert.Contains(estado, new[] { "400", "401", "403", "404", "409", "423", "429" });
            }
        }
    }

    [Theory]
    [InlineData("/api/inspecciones", "post", "409", "ETAPA_PREVIA_SIN_CIERRE")]
    [InlineData("/api/viajes/{id}/vehiculos/{vin}/novedades", "post", "409", "UNIDAD_NO_SALE")]
    [InlineData("/api/viajes/{id}/vehiculos/{vin}/novedades", "post", "409", "ETAPA_PREVIA_SIN_CIERRE")]
    [InlineData("/api/viajes/{id}/vehiculos/{vin}/llegada", "post", "409", "LLEGADA_YA_CERRADA")]
    [InlineData("/api/hallazgos/{id}/revision", "post", "409", "HALLAZGO_YA_REVISADO")]
    [InlineData("/api/uploads/{archivo}", "get", "403", "URL_FIRMADA_INVALIDA")]
    [InlineData("/api/auth/login-conductor", "post", "423", "ACCOUNT_LOCKED")]
    [InlineData("/api/auth/login-portal", "post", "403", "PORTAL_USER_NOT_LINKED")]
    [InlineData("/api/auth/login", "post", "429", "RATE_LIMITED")]
    [InlineData("/api/auth/login", "post", "401", "UNAUTHORIZED")]
    [InlineData("/api/viajes", "post", "409", "CONFLICT")]
    [InlineData("/api/viajes/{id}", "get", "404", "NOT_FOUND")]
    [InlineData("/api/viajes", "get", "400", "VALIDATION_ERROR")]
    [InlineData("/api/viajes", "get", "403", "FORBIDDEN")]
    [InlineData("/api/viajes", "get", "403", "PIN_CHANGE_REQUIRED")]
    public async Task Los_errores_propios_de_cada_endpoint_salen_en_su_codigo_http(string ruta, string metodo, string estado, string codigo)
    {
        var contrato = await ObtenerContratoAsync();

        var ejemplos = contrato["paths"]![ruta]![metodo]!["responses"]![estado]!["content"]!["application/json"]!["examples"]!.AsObject();

        Assert.True(ejemplos.ContainsKey(codigo), $"{metodo} {ruta} {estado} no documenta {codigo} (trae {string.Join(", ", ejemplos.Select(e => e.Key))})");
    }

    [Fact]
    public async Task Los_codigos_nuevos_estan_en_la_tabla_de_errores_de_la_documentacion()
    {
        var contrato = await ObtenerContratoAsync();

        var codigos = Operaciones(contrato).SelectMany(o => o.Operacion["responses"]!.AsObject().Where(r => r.Key != "default" && int.Parse(r.Key, System.Globalization.CultureInfo.InvariantCulture) >= 400).SelectMany(r => r.Value!["content"]!["application/json"]!["examples"]!.AsObject().Select(e => e.Key))).ToHashSet();

        Assert.Contains("HALLAZGO_YA_REVISADO", codigos);
        Assert.Contains("URL_FIRMADA_INVALIDA", codigos);
        var tabla = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "CONTRATO.md"));
        foreach (var codigo in codigos)
        {
            Assert.Contains($"`{codigo}`", tabla);
        }
    }

    [Fact]
    public async Task El_cuerpo_de_error_conserva_error_code_message_timestamp_y_details_es_opcional()
    {
        var contrato = await ObtenerContratoAsync();

        var error = contrato["components"]!["schemas"]!["ErrorApiDto"]!;

        Assert.Equal(["error", "code", "message", "timestamp", "details"], error["properties"]!.AsObject().Select(p => p.Key).ToList());
        Assert.Equal(["error", "code", "message", "timestamp"], error["required"]!.AsArray().Select(r => r!.GetValue<string>()).ToList());
    }

    [Fact]
    public async Task Los_cuerpos_y_las_respuestas_exitosas_traen_ejemplo_con_los_nombres_reales()
    {
        var contrato = await ObtenerContratoAsync();
        var sinEjemplo = new List<string>();

        foreach (var (ruta, metodo, operacion) in Operaciones(contrato))
        {
            var esquemas = new List<JsonNode?>();
            if (operacion["requestBody"]?["content"]?["application/json"]?["schema"] is { } cuerpo)
            {
                esquemas.Add(cuerpo["items"] ?? cuerpo);
            }

            foreach (var (estado, respuesta) in operacion["responses"]!.AsObject().Where(r => r.Key.StartsWith('2')))
            {
                if (respuesta!["content"]?["application/json"]?["schema"] is { } esquema)
                {
                    esquemas.Add(esquema["items"] ?? esquema);
                }
            }

            foreach (var esquema in esquemas)
            {
                var resuelto = Resolver(contrato, esquema);
                if (resuelto is not null && resuelto["type"]?.ToJsonString() is "\"object\"" or "[\"object\",\"null\"]" && resuelto["examples"] is null && esquema!["$ref"] is not null)
                {
                    sinEjemplo.Add($"{metodo} {ruta}: {esquema["$ref"]}");
                }
            }
        }

        Assert.True(sinEjemplo.Count == 0, "Sin ejemplo: " + string.Join("; ", sinEjemplo.Distinct().Take(20)));
        var inspeccion = contrato["components"]!["schemas"]!["InspeccionDto"]!["examples"]![0]!.AsObject();
        Assert.Equal("salida", inspeccion["momento"]!.GetValue<string>());
        Assert.Contains("etapa_id", inspeccion.Select(p => p.Key));
        Assert.Equal("9FBHSR4E8PM000001", contrato["components"]!["schemas"]!["CargarVinsRequest"]!["examples"]![0]!["items"]![0]!["vin"]!.GetValue<string>());
        var pagina = contrato["components"]!["schemas"]!["PaginaDtoOfListadoVehiculoDto"]!["examples"]![0]!.AsObject();
        Assert.Equal(["items", "total", "pagina", "tamano"], pagina.Select(p => p.Key).ToList());
    }

    // ----------------------------------------------------------------------------------------- snake_case

    private static readonly string[] EXCEPCIONES_CAMEL_CASE =
    [
        "LoginPortalUsuarioDto",     // el cuerpo de login-portal lo fija el portal
        "ResultadoIngestaDto",       // la respuesta de la ingesta Sofasa
        "PatronesDto", "AnalisisDto", // /api/patrones y /api/patrones/analisis: obsoletos
        "TasaRechazoDto", "TasaOperacionDto", // /api/metricas/rechazo: se retira con S6-HU-04.11
    ];

    [Fact]
    public async Task Todo_el_contrato_esta_en_snake_case_salvo_las_excepciones_documentadas()
    {
        var contrato = await ObtenerContratoAsync();
        var snake = new System.Text.RegularExpressions.Regex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$");
        var mal = new List<string>();

        foreach (var (nombre, esquema) in contrato["components"]!["schemas"]!.AsObject())
        {
            if (EXCEPCIONES_CAMEL_CASE.Contains(nombre))
            {
                continue;
            }

            mal.AddRange((esquema!["properties"]?.AsObject() ?? []).Select(p => p.Key).Where(p => !snake.IsMatch(p)).Select(p => $"{nombre}.{p}"));
        }

        foreach (var (ruta, metodo, operacion) in Operaciones(contrato))
        {
            mal.AddRange((operacion["parameters"]?.AsArray() ?? []).Select(p => p!["name"]!.GetValue<string>()).Where(p => !snake.IsMatch(p)).Select(p => $"{metodo} {ruta}?{p}"));
        }

        Assert.True(mal.Count == 0, "Nombres que no son snake_case: " + string.Join(", ", mal));
        var todo = contrato.ToJsonString();
        foreach (var retirado in new[] { "\"pageSize\"", "\"codigoMtrack\"", "\"conductorId\"", "\"rows\"", "\"clienteId\"", "\"usuarioId\"", "\"viajeId\"" })
        {
            Assert.DoesNotContain(retirado, todo);
        }
    }

    [Fact]
    public async Task Las_rutas_obsoletas_se_publican_como_deprecated_y_las_vigentes_no()
    {
        var contrato = await ObtenerContratoAsync();

        Assert.True(contrato["paths"]!["/api/patrones"]!["get"]!["deprecated"]!.GetValue<bool>());
        Assert.True(contrato["paths"]!["/api/patrones/analisis"]!["get"]!["deprecated"]!.GetValue<bool>());
        Assert.Null(contrato["paths"]!["/api/reportes/patrones"]!["get"]!["deprecated"]);
        Assert.Null(contrato["paths"]!["/api/vehiculos/listado"]!["get"]!["deprecated"]);
    }
}
