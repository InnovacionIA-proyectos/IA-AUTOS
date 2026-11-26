using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El mapeo de columnas de la carga de viajes se guarda por cliente y por forma del archivo (titulos normalizados con su repeticion, sin importar el orden de las columnas): dos formas del mismo cliente conviven, un cliente nunca
/// recibe el mapeo de otro, solo lo usan quienes cargan viajes, no admite campos que no existen ni repetidos y cada guardado queda en la auditoria.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class MapeoDeLaCargaApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    private const string BUSCAR = "/api/viajes/cargar/mapeo/buscar";
    private const string GUARDAR = "/api/viajes/cargar/mapeo";

    private static object[] Columnas(params (string Titulo, int Repeticion)[] columnas) => [.. columnas.Select(c => new { encabezado = c.Titulo, ocurrencia = c.Repeticion })];

    private Task<HttpResponseMessage> GuardarAsync(Sesion quien, object[] columnas, string?[] campos, string consulta = "") =>
        Cliente(quien.Token).PutAsJsonAsync(GUARDAR + consulta, new { version = 1, columnas, campos });

    private Task<HttpResponseMessage> BuscarAsync(Sesion quien, object[] columnas, string consulta = "") =>
        Cliente(quien.Token).PostAsJsonAsync(BUSCAR + consulta, new { columnas });

    private async Task<Guid> OtroClienteAsync()
    {
        var otro = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otro));
        return otro;
    }

    private static List<string?> Campos(JsonElement mapeo) => mapeo.GetProperty("campos").EnumerateArray().Select(c => c.ValueKind == JsonValueKind.Null ? null : c.GetString()).ToList();

    [Fact]
    public async Task Guardar_y_buscar_devuelve_el_mismo_mapeo_con_quien_y_cuando_sin_importar_el_orden_de_las_columnas()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var columnas = Columnas(("VIN", 1), ("Placa", 1), ("Observaciones", 1));

        var guardado = await GuardarAsync(coordinador, columnas, ["vin", "placa", null]);
        Assert.Equal(HttpStatusCode.OK, guardado.StatusCode);

        var alReves = await BuscarAsync(coordinador, Columnas(("Observaciones", 1), ("Placa", 1), ("VIN", 1)));
        Assert.Equal(HttpStatusCode.OK, alReves.StatusCode);
        var mapeo = await ComoJsonAsync(alReves);
        Assert.Equal(1, mapeo.GetProperty("version").GetInt32());
        Assert.Equal(["vin", "placa", null], Campos(mapeo));
        Assert.Equal(["vin", "placa", "observaciones"], mapeo.GetProperty("columnas").EnumerateArray().Select(c => c.GetProperty("encabezado").GetString()).ToList());
        Assert.Equal(coordinador.UsuarioId, mapeo.GetProperty("actualizado_por").GetGuid());
        Assert.Equal("Nombre " + clsRol.COORDINADOR_CALIDAD, mapeo.GetProperty("actualizado_por_nombre").GetString());
        Assert.True(mapeo.TryGetProperty("actualizado_at", out _));
    }

    [Fact]
    public async Task Los_titulos_se_normalizan_sin_tildes_mayusculas_ni_espacios_de_mas()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        await GuardarAsync(coordinador, Columnas(("Cédula  Conductor ", 1), ("VIN", 1)), ["cedula_conductor", "vin"]);

        var respuesta = await BuscarAsync(coordinador, Columnas(("vin", 1), ("cedula conductor", 1)));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(["cedula_conductor", "vin"], Campos(await ComoJsonAsync(respuesta)));
    }

    [Fact]
    public async Task Dos_formas_del_mismo_cliente_conviven_y_guardar_una_no_pisa_a_la_otra_pero_guardar_la_misma_la_reemplaza()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var corta = Columnas(("VIN", 1), ("Placa", 1));
        var larga = Columnas(("VIN", 1), ("Placa", 1), ("Trailer", 1));

        await GuardarAsync(coordinador, corta, ["vin", "placa"]);
        await GuardarAsync(coordinador, larga, ["vin", "placa", "trailer"]);

        Assert.Equal(["vin", "placa"], Campos(await ComoJsonAsync(await BuscarAsync(coordinador, corta))));
        Assert.Equal(["vin", "placa", "trailer"], Campos(await ComoJsonAsync(await BuscarAsync(coordinador, larga))));
        Assert.Equal(2, Convert.ToInt32(await Sembrador.EscalarAsync("SELECT COUNT(*) FROM dbo.tblMapeosCargaViajes")));

        await GuardarAsync(coordinador, corta, ["placa", "vin"]);

        Assert.Equal(["placa", "vin"], Campos(await ComoJsonAsync(await BuscarAsync(coordinador, corta))));
        Assert.Equal(["vin", "placa", "trailer"], Campos(await ComoJsonAsync(await BuscarAsync(coordinador, larga))));
        Assert.Equal(2, Convert.ToInt32(await Sembrador.EscalarAsync("SELECT COUNT(*) FROM dbo.tblMapeosCargaViajes")));
    }

    [Fact]
    public async Task Un_titulo_repetido_se_distingue_por_su_numero_de_repeticion()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        await GuardarAsync(coordinador, Columnas(("Fecha", 1), ("Fecha", 2), ("VIN", 1)), ["fecha_programada", null, "vin"]);

        var igual = await BuscarAsync(coordinador, Columnas(("VIN", 1), ("Fecha", 2), ("Fecha", 1)));
        var sinSegunda = await BuscarAsync(coordinador, Columnas(("Fecha", 1), ("VIN", 1)));

        Assert.Equal(HttpStatusCode.OK, igual.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sinSegunda.StatusCode);
    }

    [Fact]
    public async Task Sin_mapeo_guardado_responde_404()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var respuesta = await BuscarAsync(coordinador, Columnas(("VIN", 1)));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Contains("Sin mapeo", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_cliente_nunca_recibe_el_mapeo_de_otro_y_el_super_administrador_indica_el_cliente()
    {
        var otro = await OtroClienteAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var delOtro = await SesionAsync(clsRol.COORDINADOR_CALIDAD, "coord-otro@ejemplo.com", otro);
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);
        var columnas = Columnas(("VIN", 1), ("Placa", 1));
        await GuardarAsync(coordinador, columnas, ["vin", "placa"]);

        // El otro cliente no ve el de Sofasa: para el es como si no existiera.
        Assert.Equal(HttpStatusCode.NotFound, (await BuscarAsync(delOtro, columnas)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await BuscarAsync(coordinador, columnas, $"?cliente_id={otro}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GuardarAsync(coordinador, columnas, ["placa", "vin"], $"?cliente_id={otro}")).StatusCode);

        // Cada cliente guarda el suyo para la misma forma y no se pisan.
        await GuardarAsync(delOtro, columnas, ["placa", "vin"]);
        Assert.Equal(["vin", "placa"], Campos(await ComoJsonAsync(await BuscarAsync(coordinador, columnas))));
        Assert.Equal(["placa", "vin"], Campos(await ComoJsonAsync(await BuscarAsync(delOtro, columnas))));

        // El super administrador debe indicar el cliente (no tiene uno) y lo elige.
        Assert.Equal(HttpStatusCode.BadRequest, (await BuscarAsync(superAdmin, columnas)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await BuscarAsync(superAdmin, columnas, "?cliente_id=no-es-uuid")).StatusCode);
        Assert.Equal(["placa", "vin"], Campos(await ComoJsonAsync(await BuscarAsync(superAdmin, columnas, $"?cliente_id={otro}"))));
        Assert.Equal(HttpStatusCode.OK, (await GuardarAsync(superAdmin, columnas, ["vin", "placa"], $"?cliente_id={otro}")).StatusCode);
        Assert.Equal(["vin", "placa"], Campos(await ComoJsonAsync(await BuscarAsync(delOtro, columnas))));
    }

    [Fact]
    public async Task Solo_lo_usan_quienes_cargan_viajes()
    {
        var columnas = Columnas(("VIN", 1));
        var admin = await SesionAsync(clsRol.ADMIN);
        var inspector = await SesionAsync(clsRol.INSPECTOR);
        var conductor = await SesionAsync(clsRol.CONDUCTOR, "conductor@ejemplo.com");

        Assert.Equal(HttpStatusCode.OK, (await GuardarAsync(admin, columnas, ["vin"])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BuscarAsync(admin, columnas)).StatusCode);
        foreach (var sesion in new[] { inspector, conductor })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await GuardarAsync(sesion, columnas, ["vin"])).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await BuscarAsync(sesion, columnas)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsJsonAsync(BUSCAR, new { columnas })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PutAsJsonAsync(GUARDAR, new { columnas, campos = new[] { "vin" } })).StatusCode);
    }

    [Fact]
    public async Task Rechaza_con_400_lo_que_no_es_un_mapeo_valido()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var columnas = Columnas(("VIN", 1), ("Placa", 1));

        // Un campo que no existe, uno elegido para dos columnas, otra cantidad de campos, sin columnas, sin campos.
        Assert.Equal(HttpStatusCode.BadRequest, (await GuardarAsync(coordinador, columnas, ["vin", "inventado"])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GuardarAsync(coordinador, columnas, ["vin", "vin"])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GuardarAsync(coordinador, columnas, ["vin"])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GuardarAsync(coordinador, [], [])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente(coordinador.Token).PutAsJsonAsync(GUARDAR, new { columnas })).StatusCode);
        // Un titulo vacio, una repeticion que no empieza en 1 o con saltos, y una version que no es la 1.
        Assert.Equal(HttpStatusCode.BadRequest, (await GuardarAsync(coordinador, Columnas(("  ", 1)), ["vin"])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GuardarAsync(coordinador, Columnas(("Fecha", 2)), ["fecha_programada"])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GuardarAsync(coordinador, Columnas(("Fecha", 1), ("Fecha", 3)), ["fecha_programada", null])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente(coordinador.Token).PutAsJsonAsync(GUARDAR, new { version = 2, columnas, campos = new[] { "vin", "placa" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await BuscarAsync(coordinador, [])).StatusCode);
        Assert.Equal(0, Convert.ToInt32(await Sembrador.EscalarAsync("SELECT COUNT(*) FROM dbo.tblMapeosCargaViajes")));
    }

    [Fact]
    public async Task Un_campo_que_no_se_declara_en_el_cuerpo_responde_400_y_no_guarda_nada()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var guardar = await Cliente(coordinador.Token).PutAsJsonAsync(GUARDAR, new { columnas = Columnas(("VIN", 1)), campos = new[] { "vin" }, filas = new[] { "9FB45RFL8NM600001" } });
        var buscar = await Cliente(coordinador.Token).PostAsJsonAsync(BUSCAR, new { columnas = Columnas(("VIN", 1)), valores = new[] { "x" } });

        // Tampoco dentro de una columna: un valor de la fila (el VIN) junto al titulo se rechaza y no se guarda.
        var conValor = await Cliente(coordinador.Token).PutAsJsonAsync(GUARDAR, new { columnas = new[] { new { encabezado = "VIN", ocurrencia = 1, valor = "9FB45RFL8NM600001" } }, campos = new[] { "vin" } });
        var buscarConValor = await Cliente(coordinador.Token).PostAsJsonAsync(BUSCAR, new { columnas = new[] { new { encabezado = "VIN", ocurrencia = 1, ejemplo = "x" } } });

        Assert.Equal(HttpStatusCode.BadRequest, guardar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, buscar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, conValor.StatusCode);
        Assert.Contains("columnas[0].valor", await conValor.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, buscarConValor.StatusCode);
        Assert.Equal(0, Convert.ToInt32(await Sembrador.EscalarAsync("SELECT COUNT(*) FROM dbo.tblMapeosCargaViajes")));
    }

    [Fact]
    public async Task Cada_guardado_queda_en_la_auditoria_con_quien_lo_hizo_y_el_cliente_y_solo_con_titulos_y_campos()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;

        await GuardarAsync(coordinador, Columnas(("VIN", 1), ("Placa", 1)), ["vin", "placa"]);
        await GuardarAsync(coordinador, Columnas(("VIN", 1), ("Placa", 1)), ["placa", "vin"]);

        Assert.Equal(2, Convert.ToInt32(await Sembrador.EscalarAsync(
            "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viajes.mapeo.guardar' AND UsuarioId = @u AND ClienteId = @c AND Entidad = 'mapeos_carga_viajes'", ("@u", coordinador.UsuarioId), ("@c", sofasa))));
        var detalle = (string)(await Sembrador.EscalarAsync("SELECT TOP 1 Detalle FROM dbo.tblAuditLog WHERE Accion = 'viajes.mapeo.guardar' ORDER BY CreatedAt DESC"))!;
        Assert.Contains("encabezado", detalle);
        Assert.Contains("placa", detalle);
    }
}
