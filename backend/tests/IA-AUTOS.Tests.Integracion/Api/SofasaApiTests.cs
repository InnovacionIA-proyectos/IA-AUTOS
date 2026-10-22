using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class SofasaApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string RUTA_ETL = "/api/etl/sofasa/ordenes";

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private HttpClient ClienteEtl(string? token = clsFabricaApi.TOKEN_ETL)
    {
        var cliente = Fabrica.CreateClient();
        if (token is not null)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
    }

    private static async Task<JsonElement> ClonarAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    /// <summary>Forma exacta de fila que envia el ETL (index.mjs: mapRow).</summary>
    private static Dictionary<string, object?> FilaEtl(long id, string? vin, string? make = "Renault") => new()
    {
        ["source_id"] = id,
        ["status_code"] = "EN_RUTA",
        ["datetime_event"] = "2026-07-17T08:30:15.123Z",
        ["orden_number"] = "OC-" + id,
        ["orden_sap"] = null,
        ["origin_cedi"] = "CEDI Envigado",
        ["vin"] = vin,
        ["make"] = make,
        ["model_description"] = "Duster \"Oroch\" 1.6",
        ["dealer_code"] = "D-10",
        ["destination_city"] = "Bogotá",
        ["destination_address"] = "Calle 1 # 2-3\nBodega 'Norte'",
        ["loading_date"] = "2026-07-17T05:00:00.000Z",
        ["appointment"] = null,
        ["loading_dock"] = "Muelle 3",
        ["weight"] = 1234.5678,
    };

    [Fact]
    public async Task Ingesta_con_token_valido_materializa_el_vehiculo_sin_etapas_y_guarda_la_orden()
    {
        var r = await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { FilaEtl(1001, "9fbhsr4e8pm000777"), FilaEtl(1002, null) } });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await ClonarAsync(r);
        // Nombres exactos que lee el ETL ya desplegado.
        Assert.Equal(2, cuerpo.GetProperty("recibidas").GetInt32());
        Assert.Equal(2, cuerpo.GetProperty("guardadas").GetInt32());
        Assert.Equal(1, cuerpo.GetProperty("vehiculosCreados").GetInt32());
        Assert.Equal(0, cuerpo.GetProperty("vehiculosActualizados").GetInt32());
        Assert.Equal(0, cuerpo.GetProperty("errores").GetArrayLength());

        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblOrdenesSofasa"));
        Assert.Equal("nacional", await Sembrador.EscalarAsync("SELECT Operacion FROM dbo.tblVehiculos WHERE Vin = '9FBHSR4E8PM000777'"));
        // Las etapas nacen cuando el VIN entra a un viaje, no al ingerir la orden.
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblOrdenesSofasa WHERE SourceId = 1001 AND VehiculoId IS NOT NULL"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblOrdenesSofasa WHERE SourceId = 1002 AND VehiculoId IS NULL"));
        Assert.Equal("Calle 1 # 2-3\nBodega 'Norte'", await Sembrador.EscalarAsync("SELECT DestinationAddress FROM dbo.tblOrdenesSofasa WHERE SourceId = 1001"));
        Assert.Equal(1234.5678m, await Sembrador.EscalarAsync("SELECT Weight FROM dbo.tblOrdenesSofasa WHERE SourceId = 1001"));
        // La ingesta del ETL queda auditada como accion del sistema (sin usuario).
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'sofasa.ingesta' AND UsuarioId IS NULL"));
    }

    [Fact]
    public async Task Los_vehiculos_de_la_ingesta_quedan_en_el_cliente_sofasa()
    {
        await BaseDatos.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (NEWID(), 'aaa_otro', N'Otro', 0)");

        var r = await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { FilaEtl(7001, "VIN-SOF-CLI-1") } });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("sofasa", await Sembrador.EscalarAsync("SELECT c.Codigo FROM dbo.tblVehiculos v JOIN dbo.tblClientes c ON c.Id = v.ClienteId WHERE v.Vin = 'VIN-SOF-CLI-1'"));
    }

    [Fact]
    public async Task Reingestar_el_mismo_lote_es_idempotente_y_actualiza_los_datos()
    {
        await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { FilaEtl(2001, "VIN-SOF-0001") } });
        var cambio = FilaEtl(2001, "VIN-SOF-0001", "Nissan");
        cambio["status_code"] = "ENTREGADA";
        cambio["weight"] = null;

        var r = await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { cambio } });

        var cuerpo = await ClonarAsync(r);
        Assert.Equal(1, cuerpo.GetProperty("guardadas").GetInt32());
        Assert.Equal(0, cuerpo.GetProperty("vehiculosCreados").GetInt32());
        Assert.Equal(1, cuerpo.GetProperty("vehiculosActualizados").GetInt32());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblOrdenesSofasa"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
        Assert.Equal("ENTREGADA", await Sembrador.EscalarAsync("SELECT StatusCode FROM dbo.tblOrdenesSofasa WHERE SourceId = 2001"));
        Assert.Equal("Nissan", await Sembrador.EscalarAsync("SELECT Marca FROM dbo.tblVehiculos WHERE Vin = 'VIN-SOF-0001'"));
        Assert.Null(await Sembrador.EscalarAsync("SELECT Weight FROM dbo.tblOrdenesSofasa WHERE SourceId = 2001"));
    }

    [Fact]
    public async Task Una_orden_con_fecha_invalida_se_reporta_y_las_demas_se_guardan()
    {
        var mala = FilaEtl(3001, "VIN-SOF-MALA");
        mala["datetime_event"] = "esto-no-es-una-fecha";

        var r = await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { FilaEtl(3000, "VIN-SOF-OK1"), mala, FilaEtl(3002, "VIN-SOF-OK2") } });

        var cuerpo = await ClonarAsync(r);
        Assert.Equal(2, cuerpo.GetProperty("guardadas").GetInt32());
        Assert.Equal(1, cuerpo.GetProperty("errores").GetArrayLength());
        var error = cuerpo.GetProperty("errores")[0];
        Assert.Equal(3001, error.GetProperty("source_id").GetInt64());
        Assert.Contains("datetime_event", error.GetProperty("error").GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblOrdenesSofasa WHERE SourceId = 3001"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Vin = 'VIN-SOF-MALA'"));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblOrdenesSofasa"));
    }

    [Fact]
    public async Task Acepta_numeros_como_texto_fechas_sin_zona_y_vacios_como_nulos()
    {
        var fila = new Dictionary<string, object?>
        {
            ["source_id"] = "4001",
            ["weight"] = "850.25",
            ["datetime_event"] = "2026-07-17T08:30:15",
            ["vin"] = "   ",
            ["make"] = "",
        };

        var r = await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { fila } });

        Assert.Equal(1, (await ClonarAsync(r)).GetProperty("guardadas").GetInt32());
        Assert.Equal(850.25m, await Sembrador.EscalarAsync("SELECT Weight FROM dbo.tblOrdenesSofasa WHERE SourceId = 4001"));
        Assert.Null(await Sembrador.EscalarAsync("SELECT Vin FROM dbo.tblOrdenesSofasa WHERE SourceId = 4001"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos"));
        Assert.Equal(2026, await Sembrador.ContarAsync("SELECT YEAR(DatetimeEvent) FROM dbo.tblOrdenesSofasa WHERE SourceId = 4001"));
    }

    [Fact]
    public async Task Un_lote_vacio_responde_cero_y_las_solicitudes_invalidas_responden_400()
    {
        var vacio = await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = Array.Empty<object>() });
        var sinLista = await ClienteEtl().PostAsync(RUTA_ETL, Json("{}"));
        var sinId = await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new object[] { new { vin = "X" } } });
        var demasiadas = await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = Enumerable.Range(1, 5001).Select(i => new { source_id = i }).ToArray() });

        Assert.Equal(HttpStatusCode.OK, vacio.StatusCode);
        Assert.Equal(0, (await ClonarAsync(vacio)).GetProperty("recibidas").GetInt32());
        foreach (var r in new[] { sinLista, sinId, demasiadas })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("Datos inválidos", (await ClonarAsync(r)).GetProperty("message").GetString());
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblOrdenesSofasa"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("token-incorrecto")]
    [InlineData("")]
    public async Task La_ingesta_rechaza_tokens_ausentes_o_incorrectos(string? token)
    {
        var r = await ClienteEtl(token).PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { FilaEtl(5001, "VIN-X") } });

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("Token de ingesta inválido", (await ClonarAsync(r)).GetProperty("message").GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblOrdenesSofasa"));
    }

    [Fact]
    public async Task La_ingesta_no_acepta_un_token_de_usuario_y_el_token_del_etl_no_abre_otras_rutas()
    {
        await Fabrica.SembrarUsuarioAsync("admin@ejemplo.com", "Clave-Segura-1", clsRol.ADMIN);
        var tokenUsuario = await LoginAsync("admin@ejemplo.com", "Clave-Segura-1");

        var conTokenDeUsuario = await ClienteEtl(tokenUsuario).PostAsJsonAsync(RUTA_ETL, new { ordenes = Array.Empty<object>() });
        var etlEnOtraRuta = await ClienteEtl().GetAsync("/api/sofasa/ordenes");

        Assert.Equal(HttpStatusCode.Unauthorized, conTokenDeUsuario.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, etlEnOtraRuta.StatusCode);
    }

    [Fact]
    public async Task Sin_token_configurado_la_ingesta_siempre_se_rechaza()
    {
        var sinToken = CrearFabrica(new Dictionary<string, string?> { ["Etl:IngestToken"] = string.Empty });
        var cliente = sinToken.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Empty);

        var r = await cliente.PostAsJsonAsync(RUTA_ETL, new { ordenes = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task El_listado_requiere_usuario_filtra_y_ordena_las_mas_recientes_primero_con_nulos_al_final()
    {
        await Fabrica.SembrarUsuarioAsync("admin@ejemplo.com", "Clave-Segura-1", clsRol.COORDINADOR_CALIDAD);
        var token = await LoginAsync("admin@ejemplo.com", "Clave-Segura-1");
        var antigua = FilaEtl(6001, "VINLIST0001");
        antigua["datetime_event"] = "2026-01-01T00:00:00Z";
        var reciente = FilaEtl(6002, "VINLIST0002");
        reciente["datetime_event"] = "2026-07-01T00:00:00Z";
        reciente["destination_city"] = "Medellín";
        reciente["status_code"] = "ENTREGADA";
        var sinFecha = FilaEtl(6003, "OTRO-0003");
        sinFecha["datetime_event"] = null;
        await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { antigua, reciente, sinFecha } });

        var todas = Items(await ClonarAsync(await Cliente(token).GetAsync("/api/sofasa/ordenes")));
        var porVin = Items(await ClonarAsync(await Cliente(token).GetAsync("/api/sofasa/ordenes?vin=vinlist")));
        var porCiudad = Items(await ClonarAsync(await Cliente(token).GetAsync("/api/sofasa/ordenes?ciudad=medell")));
        var porEstado = Items(await ClonarAsync(await Cliente(token).GetAsync("/api/sofasa/ordenes?estado=ENTREGADA")));
        var porRango = Items(await ClonarAsync(await Cliente(token).GetAsync("/api/sofasa/ordenes?desde=2026-06-01T00:00:00Z&hasta=2026-08-01T00:00:00Z")));
        var sinAuth = await Cliente().GetAsync("/api/sofasa/ordenes");

        Assert.Equal([6002L, 6001L, 6003L], todas.EnumerateArray().Select(o => o.GetProperty("source_id").GetInt64()).ToList());
        Assert.Equal(2, porVin.GetArrayLength());
        Assert.Equal(6002, porCiudad[0].GetProperty("source_id").GetInt64());
        Assert.Equal(1, porEstado.GetArrayLength());
        Assert.Equal(1, porRango.GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, sinAuth.StatusCode);
        Assert.Equal(
            ["id", "source_id", "status_code", "datetime_event", "orden_number", "orden_sap", "origin_cedi", "vin", "make", "model_description",
             "dealer_code", "destination_city", "destination_address", "loading_date", "appointment", "loading_dock", "weight", "vehiculo_id",
             "codigo_mtrack", "viaje_id", "synced_at", "created_at", "updated_at"],
            todas[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(1234.5678, todas[0].GetProperty("weight").GetDouble());
    }


    [Fact]
    public async Task Una_orden_de_un_vin_que_ya_esta_en_un_viaje_queda_vinculada_a_ese_viaje()
    {
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-ENVIAJE-01");
        var viajeId = await Sembrador.ViajeDeEtapaAsync(etapas[0]);

        await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { FilaEtl(7001, "VIN-ENVIAJE-01"), FilaEtl(7002, "VIN-SIN-VIAJE") } });

        Assert.Equal(viajeId, await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblOrdenesSofasa WHERE SourceId = 7001"));
        Assert.Null(await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblOrdenesSofasa WHERE SourceId = 7002"));
    }

    [Fact]
    public async Task Cuando_la_orden_trae_codigo_mtrack_solo_se_vincula_al_viaje_con_ese_codigo_y_ese_vin()
    {
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-CODIGO-01");
        var viajeId = await Sembrador.ViajeDeEtapaAsync(etapas[0]);
        var codigo = (string)(await Sembrador.EscalarAsync("SELECT CodigoMtrack FROM dbo.tblViajes WHERE Id = @v", ("@v", viajeId)))!;
        var coincide = FilaEtl(7101, "VIN-CODIGO-01");
        coincide["codigo_mtrack"] = codigo.ToLowerInvariant();
        var distinto = FilaEtl(7102, "VIN-CODIGO-01");
        distinto["codigo_mtrack"] = "MTK-OTRO";

        await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { coincide, distinto } });

        Assert.Equal(viajeId, await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblOrdenesSofasa WHERE SourceId = 7101"));
        Assert.Null(await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblOrdenesSofasa WHERE SourceId = 7102"));
        Assert.Equal("MTK-OTRO", await Sembrador.EscalarAsync("SELECT CodigoMtrack FROM dbo.tblOrdenesSofasa WHERE SourceId = 7102"));
    }

    [Fact]
    public async Task Reingestar_la_orden_la_vincula_cuando_el_vin_entra_despues_a_un_viaje()
    {
        await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { FilaEtl(7201, "VIN-TARDE-01") } });
        Assert.Null(await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblOrdenesSofasa WHERE SourceId = 7201"));
        // El VIN entra a un viaje por una via que no vincula ordenes (como los viajes anteriores a esta regla).
        var viajeId = await Sembrador.ViajeAsync();
        await Sembrador.EjecutarAsync("INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) SELECT NEWID(), @v, Id FROM dbo.tblVehiculos WHERE Vin = 'VIN-TARDE-01'", ("@v", viajeId));

        await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { FilaEtl(7201, "VIN-TARDE-01") } });

        Assert.Equal(viajeId, await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblOrdenesSofasa WHERE SourceId = 7201"));
    }

    [Fact]
    public async Task Inspectores_y_conductores_ven_solo_las_ordenes_de_sus_viajes()
    {
        var inspector = await Fabrica.SembrarUsuarioAsync("insp@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        var token = await LoginAsync("insp@ejemplo.com", "Clave-Segura-1");
        await Sembrador.VehiculoConRutaAsync("VIN-MIO-01", inspectorId: inspector.Id);
        await Sembrador.VehiculoConRutaAsync("VIN-AJENO-01");
        await ClienteEtl().PostAsJsonAsync(RUTA_ETL, new { ordenes = new[] { FilaEtl(7301, "VIN-MIO-01"), FilaEtl(7302, "VIN-AJENO-01"), FilaEtl(7303, "VIN-SIN-VIAJE") } });

        var propias = Items(await ClonarAsync(await Cliente(token).GetAsync("/api/sofasa/ordenes")));

        Assert.Equal([7301L], propias.EnumerateArray().Select(o => o.GetProperty("source_id").GetInt64()).ToList());
    }


    [Fact]
    public async Task Fechas_invalidas_en_los_filtros_responden_400()
    {
        await Fabrica.SembrarUsuarioAsync("admin@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        var token = await LoginAsync("admin@ejemplo.com", "Clave-Segura-1");

        var r = await Cliente(token).GetAsync("/api/sofasa/ordenes?desde=ayer");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("desde", out _));
    }
}
