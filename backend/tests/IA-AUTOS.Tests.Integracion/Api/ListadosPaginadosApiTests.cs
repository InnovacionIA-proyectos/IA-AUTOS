using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Sofasa;
using IAAutos.Aplicacion.Usuarios;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El formato unico de los listados (vehiculos, inspecciones, viajes, ordenes Sofasa y usuarios): <c>{items, total, pagina, tamano}</c>, 50 por defecto y maximo 200, orden <c>campo:asc|desc</c> solo por los
/// campos permitidos, los filtros en snake_case y la regla unica del VIN, todo con 400 y su motivo cuando esta mal formado.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class ListadosPaginadosApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    private const string VIN_CON_O = "9FBHSR4E8PM0O0001";

    private static List<JsonElement> Filas(JsonElement pagina) => pagina.GetProperty("items").EnumerateArray().ToList();

    private static List<string?> Textos(JsonElement pagina, string campo) => Filas(pagina).Select(i => i.GetProperty(campo).GetString()).ToList();

    /// <summary>Los VIN del escenario pasan de «VIN-IND-000N» a un VIN ISO valido («9FBHSR4E8PM00000N») para poder filtrarlos.</summary>
    private async Task<(Dias Dias, Guid Sofasa, Guid Otro, Guid Conductor, Guid I1, Guid I2, Guid Origen, Guid Destino)> EscenarioConVinesValidosAsync()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET Vin = REPLACE(Vin, 'VIN-IND-', '9FBHSR4E8PM00') WHERE Vin LIKE 'VIN-IND-%'");
        return e;
    }

    private async Task<HttpResponseMessage> PedirAsync(Sesion quien, string ruta) => await Cliente(quien.Token).GetAsync(ruta);

    private static async Task<Dictionary<string, string[]>> ErroresDeCamposAsync(HttpResponseMessage r)
    {
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>();
        var campos = cuerpo.GetProperty("details").GetProperty("fieldErrors");
        return campos.EnumerateObject().ToDictionary(c => c.Name, c => c.Value.EnumerateArray().Select(m => m.GetString()!).ToArray());
    }

    public static TheoryData<string, string> RutasYCampos()
    {
        var datos = new TheoryData<string, string>();
        void Agregar(string ruta, IEnumerable<string> campos)
        {
            foreach (var campo in campos)
            {
                datos.Add(ruta, campo);
            }
        }

        Agregar("/api/vehiculos/listado", clsVehiculosService.CAMPOS_ORDEN);
        Agregar("/api/inspecciones", clsInspeccionesService.CAMPOS_ORDEN);
        Agregar("/api/viajes", clsViajesService.CAMPOS_ORDEN);
        Agregar("/api/sofasa/ordenes", clsSofasaService.CAMPOS_ORDEN);
        Agregar("/api/usuarios", clsUsuariosService.CAMPOS_ORDEN);
        return datos;
    }

    public static TheoryData<string> Rutas() => new() { "/api/vehiculos/listado", "/api/inspecciones", "/api/viajes", "/api/sofasa/ordenes", "/api/usuarios" };

    // ------------------------------------------------------------------------------------------- el formato

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task Cada_listado_responde_items_total_pagina_y_tamano_con_50_por_defecto(string ruta)
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        var r = await ConsultarAsync(admin, ruta);

        Assert.Equal(["items", "total", "pagina", "tamano"], r.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(1, r.GetProperty("pagina").GetInt32());
        Assert.Equal(50, r.GetProperty("tamano").GetInt32());
    }

    [Theory]
    [MemberData(nameof(Rutas))]
    public async Task El_tamano_maximo_es_200_y_un_valor_que_no_es_numero_responde_400(string ruta)
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        var enorme = await ConsultarAsync(admin, ruta, "?tamano=5000&pagina=-3");
        var rara = await PedirAsync(admin, $"{ruta}?pagina=abc&tamano=muchos");

        Assert.Equal(200, enorme.GetProperty("tamano").GetInt32());
        Assert.Equal(1, enorme.GetProperty("pagina").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, rara.StatusCode);
        var campos = await ErroresDeCamposAsync(rara);
        Assert.Contains("pagina", campos.Keys);
        Assert.Contains("tamano", campos.Keys);
    }

    [Theory]
    [MemberData(nameof(RutasYCampos))]
    public async Task Cada_campo_permitido_ordena_en_los_dos_sentidos(string ruta, string campo)
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        var ascendente = await PedirAsync(admin, $"{ruta}?orden={campo}:asc");
        var descendente = await PedirAsync(admin, $"{ruta}?orden={campo}:DESC");
        var sinSentido = await PedirAsync(admin, $"{ruta}?orden={campo}");

        Assert.Equal(HttpStatusCode.OK, ascendente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, descendente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sinSentido.StatusCode);
    }

    [Theory]
    [InlineData("/api/vehiculos/listado")]
    [InlineData("/api/inspecciones")]
    [InlineData("/api/viajes")]
    [InlineData("/api/sofasa/ordenes")]
    [InlineData("/api/usuarios")]
    public async Task Un_orden_con_un_campo_no_permitido_o_mal_formado_responde_400_con_el_motivo(string ruta)
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        foreach (var orden in new[] { "password_hash:asc", "ESTE_NO_EXISTE", "vin:sideways", "fecha:asc:desc", ":asc" })
        {
            var r = await PedirAsync(admin, $"{ruta}?orden={orden}");
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var campos = await ErroresDeCamposAsync(r);
            Assert.Contains("Orden inválido", Assert.Single(campos["orden"]));
        }
    }

    [Fact]
    public async Task Los_errores_de_los_filtros_la_pagina_y_el_orden_llegan_juntos()
    {
        await EscenarioConVinesValidosAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var r = await PedirAsync(coordinador, "/api/inspecciones?vin=corto&usuario_id=no&viaje_id=no&momento=x&estado=y&proceso=z&operacion=aerea&cliente_id=no&origen_id=no&destino_id=no&desde=ayer&pagina=a&orden=zzz");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var campos = await ErroresDeCamposAsync(r);
        foreach (var campo in new[] { "vin", "usuario_id", "viaje_id", "momento", "estado", "proceso", "operacion", "cliente_id", "origen_id", "destino_id", "desde", "pagina", "orden" })
        {
            Assert.Contains(campo, campos.Keys);
        }
    }

    // ------------------------------------------------------------------------------ paginas y orden estable

    [Fact]
    public async Task Las_paginas_se_recorren_sin_repetir_ni_saltar_filas_y_el_total_cuenta_todas()
    {
        await EscenarioConVinesValidosAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var primera = await ConsultarAsync(coordinador, "/api/inspecciones", "?tamano=4&pagina=1&orden=momento:asc");
        var segunda = await ConsultarAsync(coordinador, "/api/inspecciones", "?tamano=4&pagina=2&orden=momento:asc");
        var tercera = await ConsultarAsync(coordinador, "/api/inspecciones", "?tamano=4&pagina=3&orden=momento:asc");
        var cuarta = await ConsultarAsync(coordinador, "/api/inspecciones", "?tamano=4&pagina=4&orden=momento:asc");

        // El cliente Sofasa tiene 10 informes (V1: 6, V2: 1, V3: 2, V5: 1); el del otro cliente no se cuenta.
        Assert.Equal(10, primera.GetProperty("total").GetInt32());
        var ids = new[] { primera, segunda, tercera }.SelectMany(p => Textos(p, "id")).ToList();
        Assert.Equal([4, 4, 2], new[] { primera, segunda, tercera }.Select(p => Filas(p).Count).ToList());
        Assert.Empty(Filas(cuarta));
        Assert.Equal(10, ids.Distinct().Count());
        var momentos = new[] { primera, segunda, tercera }.SelectMany(p => Textos(p, "momento")).ToList();
        Assert.Equal(momentos.Order(StringComparer.Ordinal).ToList(), momentos);
    }

    [Fact]
    public async Task El_orden_pedido_se_aplica_en_el_servidor_y_el_sentido_lo_invierte()
    {
        await EscenarioConVinesValidosAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var porVinAsc = await ConsultarAsync(coordinador, "/api/vehiculos/listado", "?tipo=pendientes&orden=vin:asc");
        var porVinDesc = await ConsultarAsync(coordinador, "/api/vehiculos/listado", "?tipo=pendientes&orden=vin:desc");
        var porMarca = await ConsultarAsync(coordinador, "/api/vehiculos/listado", "?tipo=pendientes&orden=marca:desc&tamano=2");

        Assert.Equal(["9FBHSR4E8PM000002", "9FBHSR4E8PM000003", "9FBHSR4E8PM000006"], Textos(porVinAsc, "vin"));
        Assert.Equal(Textos(porVinAsc, "vin").AsEnumerable().Reverse().ToList(), Textos(porVinDesc, "vin"));
        Assert.Equal(["Toyota", "Renault"], Textos(porMarca, "marca"));
        Assert.Equal(3, porMarca.GetProperty("total").GetInt32());
        Assert.Equal(2, porMarca.GetProperty("tamano").GetInt32());
    }

    [Fact]
    public async Task Los_usuarios_se_ordenan_y_se_paginan_con_el_mismo_formato()
    {
        var admin = await SesionAsync(clsRol.ADMIN);
        foreach (var nombre in new[] { "Ana", "Beto", "Carla", "Dario" })
        {
            await Fabrica.SembrarUsuarioAsync($"{nombre.ToLowerInvariant()}@ejemplo.com", "Clave-Segura-1", clsRol.COMERCIAL, nombre: nombre);
        }

        var descendente = await ConsultarAsync(admin, "/api/usuarios", "?rol=comercial&orden=nombre:desc&tamano=3");
        var segunda = await ConsultarAsync(admin, "/api/usuarios", "?rol=comercial&orden=nombre:desc&tamano=3&pagina=2");

        Assert.Equal(["Dario", "Carla", "Beto"], Textos(descendente, "nombre"));
        Assert.Equal(["Ana"], Textos(segunda, "nombre"));
        Assert.Equal(4, descendente.GetProperty("total").GetInt32());
    }

    // --------------------------------------------------------------------------------------- los filtros

    [Fact]
    public async Task Los_filtros_de_las_inspecciones_son_los_de_la_tarea_en_snake_case()
    {
        var e = await EscenarioConVinesValidosAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);

        async Task<int> TotalAsync(Sesion quien, string consulta) => (await ConsultarAsync(quien, "/api/inspecciones", consulta)).GetProperty("total").GetInt32();

        Assert.Equal(10, await TotalAsync(coordinador, string.Empty));
        Assert.Equal(6, await TotalAsync(coordinador, "?vin=9fbhsr4e8pm000001"));
        Assert.Equal(1, await TotalAsync(coordinador, "?vin=PM000002"));
        Assert.Equal(2, await TotalAsync(coordinador, "?operacion=importacion"));
        Assert.Equal(8, await TotalAsync(coordinador, "?marca=Renault"));
        Assert.Equal(7, await TotalAsync(coordinador, $"?origen_id={e.Origen}"));
        Assert.Equal(7, await TotalAsync(coordinador, $"?destino_id={e.Destino}"));
        Assert.Equal(0, await TotalAsync(coordinador, $"?origen_id={e.Destino}"));
        Assert.Equal(1, await TotalAsync(coordinador, "?viaje_id=" + await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = 'MTK-IND-B'") + "&momento=llegada"));
        Assert.Equal(8, await TotalAsync(coordinador, $"?usuario_id={e.I1}"));
        Assert.Equal(2, await TotalAsync(coordinador, $"?usuario_id={e.I2}"));
        Assert.Equal(11, await TotalAsync(superAdmin, string.Empty));
        Assert.Equal(1, await TotalAsync(superAdmin, $"?cliente_id={e.Otro}"));
        Assert.Equal(10, await TotalAsync(superAdmin, $"?cliente_id={e.Sofasa}"));

        // Los nombres anteriores ya no se leen: el filtro se ignora y trae todo.
        Assert.Equal(10, await TotalAsync(coordinador, $"?usuarioId={e.I2}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await PedirAsync(coordinador, $"/api/inspecciones?cliente_id={e.Otro}")).StatusCode);
    }

    [Fact]
    public async Task Los_filtros_de_los_viajes_son_los_de_la_tarea_en_snake_case()
    {
        var e = await EscenarioConVinesValidosAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET InspectorId = @i WHERE CodigoMtrack = 'MTK-IND-B'", ("@i", e.I2));
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);

        async Task<List<string?>> CodigosAsync(Sesion quien, string consulta) => Textos(await ConsultarAsync(quien, "/api/viajes", consulta), "codigo_mtrack");

        Assert.Equal(["MTK-IND-A", "MTK-IND-B", "MTK-IND-E", "MTK-IND-F"], (await CodigosAsync(coordinador, string.Empty)).Order().ToList());
        Assert.Equal(["MTK-IND-B"], await CodigosAsync(coordinador, $"?inspector_id={e.I2}"));
        Assert.Equal(["MTK-IND-A"], await CodigosAsync(coordinador, $"?origen_id={e.Origen}"));
        Assert.Equal(["MTK-IND-A"], await CodigosAsync(coordinador, $"?destino_id={e.Destino}"));
        Assert.Equal(["MTK-IND-A"], await CodigosAsync(coordinador, $"?conductor_id={e.Conductor}"));
        Assert.Equal(["MTK-IND-A"], await CodigosAsync(coordinador, "?codigo_mtrack=ind-a"));
        Assert.Equal(["MTK-IND-A"], await CodigosAsync(coordinador, "?vin=9FBHSR4E8PM000002"));
        Assert.Equal(["MTK-IND-B"], await CodigosAsync(coordinador, "?vin=PM000003"));
        Assert.Equal(["MTK-IND-F"], await CodigosAsync(coordinador, "?estado=programado"));
        Assert.Equal(["MTK-IND-O"], await CodigosAsync(superAdmin, $"?cliente_id={e.Otro}"));
        Assert.Equal(["MTK-IND-A", "MTK-IND-B", "MTK-IND-E", "MTK-IND-F"], (await CodigosAsync(superAdmin, $"?cliente_id={e.Sofasa}")).Order().ToList());

        // Los nombres anteriores ya no se aceptan: se ignoran.
        Assert.Equal(4, (await CodigosAsync(coordinador, $"?conductorId={e.I2}&codigoMtrack=nada")).Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await PedirAsync(coordinador, $"/api/viajes?cliente_id={e.Otro}")).StatusCode);
    }

    [Fact]
    public async Task Los_filtros_del_listado_de_vehiculos_son_los_de_la_tarea_en_snake_case()
    {
        var e = await EscenarioConVinesValidosAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET Activo = 0 WHERE Vin = '9FBHSR4E8PM000006'");
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);

        async Task<List<string?>> VinesAsync(Sesion quien, string consulta) => Textos(await ConsultarAsync(quien, "/api/vehiculos/listado", consulta), "vin");

        // V5 solo tiene la etapa de Cargue y cerrada: tambien es movilizada.
        Assert.Equal(["9FBHSR4E8PM000001", "9FBHSR4E8PM000005"], await VinesAsync(coordinador, string.Empty));
        Assert.Equal(["9FBHSR4E8PM000002", "9FBHSR4E8PM000003", "9FBHSR4E8PM000006"], await VinesAsync(coordinador, "?tipo=pendientes"));
        Assert.Equal(["9FBHSR4E8PM000002", "9FBHSR4E8PM000003"], await VinesAsync(coordinador, "?tipo=novedad"));
        Assert.Equal(["9FBHSR4E8PM000003"], await VinesAsync(coordinador, "?tipo=pendientes&operacion=importacion"));
        Assert.Equal(["9FBHSR4E8PM000003"], await VinesAsync(coordinador, "?tipo=pendientes&marca=Toyota"));
        Assert.Equal(["9FBHSR4E8PM000006"], await VinesAsync(coordinador, "?tipo=pendientes&activo=false"));
        Assert.Equal(2, (await VinesAsync(coordinador, "?tipo=pendientes&activo=true")).Count);
        Assert.Equal(["9FBHSR4E8PM000002"], await VinesAsync(coordinador, "?tipo=pendientes&vin=9FBHSR4E8PM000002"));
        Assert.Equal(["9FBHSR4E8PM000005"], await VinesAsync(coordinador, "?tipo=movilizados&vin=pm000005"));
        Assert.Equal(["9FBHSR4E8PM000004"], await VinesAsync(superAdmin, $"?tipo=movilizados&cliente_id={e.Otro}"));
        Assert.Equal(["9FBHSR4E8PM000002", "9FBHSR4E8PM000003", "9FBHSR4E8PM000006"], await VinesAsync(coordinador, "?tipo=pendientes&estado_unidad=activa"));
        Assert.Equal(["9FBHSR4E8PM000001", "9FBHSR4E8PM000005"], await VinesAsync(coordinador, "?estado_unidad=activa"));
        Assert.Empty(await VinesAsync(coordinador, "?tipo=pendientes&estado_unidad=no_sale"));

        var manana = DateTimeOffset.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Empty(await VinesAsync(coordinador, $"?tipo=pendientes&desde={manana}"));
        Assert.Equal(3, (await VinesAsync(coordinador, $"?tipo=pendientes&hasta={manana}")).Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await PedirAsync(coordinador, $"/api/vehiculos/listado?cliente_id={e.Otro}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PedirAsync(coordinador, "/api/vehiculos/listado?estado_unidad=volando&activo=quizas&operacion=aerea&desde=ayer")).StatusCode);
    }

    [Fact]
    public async Task El_listado_de_vehiculos_ya_no_responde_rows_page_ni_pageSize()
    {
        await EscenarioConVinesValidosAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var r = await ConsultarAsync(coordinador, "/api/vehiculos/listado", "?page=2&pageSize=1");

        Assert.False(r.TryGetProperty("rows", out _));
        Assert.False(r.TryGetProperty("page", out _));
        Assert.False(r.TryGetProperty("pageSize", out _));
        Assert.Equal(1, r.GetProperty("pagina").GetInt32());
        Assert.Equal(50, r.GetProperty("tamano").GetInt32());
    }

    // ------------------------------------------------------------------------------------------- el VIN

    [Theory]
    [InlineData("/api/vehiculos/listado?vin=")]
    [InlineData("/api/inspecciones?vin=")]
    [InlineData("/api/viajes?vin=")]
    [InlineData("/api/sofasa/ordenes?vin=")]
    [InlineData("/api/exportaciones/viajes.xlsx?vin=")]
    [InlineData("/api/exportaciones/inspecciones.xlsx?vin=")]
    public async Task Un_vin_o_chasis_que_no_cumple_la_regla_unica_responde_400_con_el_motivo_en_los_filtros(string ruta)
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        // 17 caracteres con la letra O; menos de 6; mas de 20; con guion; y 17 con Q o I.
        foreach (var vin in new[] { VIN_CON_O, "AB123", "ABCDEFGHJKLMNPRSTUVWX", "ABC-123456", "9FBHSR4E8PM00Q001", "9FBHSR4E8PM00I001" })
        {
            var r = await PedirAsync(admin, ruta + vin);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("VIN", Assert.Single((await ErroresDeCamposAsync(r))["vin"]));
        }

        var iso = await PedirAsync(admin, ruta + "9FBHSR4E8PM000001");
        var chasis = await PedirAsync(admin, ruta + "abc123");
        var chasisLargo = await PedirAsync(admin, ruta + "ABCDEFGHJKLMNPRSTUVW");
        Assert.Equal(HttpStatusCode.OK, iso.StatusCode);
        Assert.Equal(HttpStatusCode.OK, chasis.StatusCode);
        Assert.Equal(HttpStatusCode.OK, chasisLargo.StatusCode);
    }

    [Fact]
    public async Task El_motivo_del_vin_de_17_caracteres_dice_que_no_lleva_I_O_ni_Q()
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        var r = await PedirAsync(admin, $"/api/vehiculos/listado?vin={VIN_CON_O}");

        Assert.Equal("VIN inválido: los de 17 caracteres son alfanuméricos y no llevan las letras I, O ni Q", Assert.Single((await ErroresDeCamposAsync(r))["vin"]));
    }

    [Fact]
    public async Task La_carga_de_vines_rechaza_un_vin_que_no_cumple_la_regla_y_acepta_un_iso_valido_o_un_chasis()
    {
        var admin = await SesionAsync(clsRol.ADMIN);
        object Cuerpo(string vin) => new { items = new[] { new { vin, marca = "Renault", operacion = "nacional" } } };

        var conO = await Cliente(admin.Token).PostAsJsonAsync("/api/vehiculos/cargar", Cuerpo(VIN_CON_O));
        var corto = await Cliente(admin.Token).PostAsJsonAsync("/api/vehiculos/cargar", Cuerpo("AB123"));
        var conGuion = await Cliente(admin.Token).PostAsJsonAsync("/api/vehiculos/cargar", Cuerpo("VIN-UNO-0001"));
        var iso = await Cliente(admin.Token).PostAsJsonAsync("/api/vehiculos/cargar", Cuerpo("9fbhsr4e8pm000001"));
        var chasis = await Cliente(admin.Token).PostAsJsonAsync("/api/vehiculos/cargar", Cuerpo("CHS123456"));

        Assert.Equal(HttpStatusCode.BadRequest, conO.StatusCode);
        Assert.Contains("VIN inválido", await conO.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, corto.StatusCode);
        Assert.Contains("chasis de 6 a 20", await corto.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, conGuion.StatusCode);
        Assert.Equal(HttpStatusCode.Created, iso.StatusCode);
        Assert.Equal(HttpStatusCode.Created, chasis.StatusCode);
    }

    [Fact]
    public async Task El_autocompletado_sigue_buscando_por_fragmento_pero_un_texto_de_17_alfanumericos_debe_ser_un_vin_valido()
    {
        var admin = await SesionAsync(clsRol.ADMIN);
        await Cliente(admin.Token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = new[] { new { vin = "9FBHSR4E8PM000001", marca = "Renault", operacion = "nacional" } } });

        var fragmento = await ConsultarArregloAsync(admin, "/api/vehiculos?vin=PM0000");
        var corto = await ConsultarArregloAsync(admin, "/api/vehiculos?vin=ab");
        var marca = await ConsultarArregloAsync(admin, "/api/vehiculos?vin=renault");
        var completo = await ConsultarArregloAsync(admin, "/api/vehiculos?vin=9fbhsr4e8pm000001");
        var invalido = await PedirAsync(admin, $"/api/vehiculos?vin={VIN_CON_O}");

        Assert.Single(fragmento);
        Assert.Empty(corto);
        Assert.Single(marca);
        Assert.Single(completo);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Contains("VIN inválido", Assert.Single((await ErroresDeCamposAsync(invalido))["vin"]));
    }

    private async Task<List<JsonElement>> ConsultarArregloAsync(Sesion quien, string ruta)
    {
        var r = await PedirAsync(quien, ruta);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }
}
