using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Reportes del tablero (GET /api/reportes/*): quien los ve (coordinador, comercial y administradores, con lo de su cliente), cada formula (cobertura, adopcion, tiempo de captura, averias, patrones
/// e indicador diario), los filtros y los encabezados de las rutas obsoletas.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_1)]
public sealed class ReportesTableroApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    private static readonly string[] RUTAS =
        ["/api/reportes/cobertura", "/api/reportes/adopcion", "/api/reportes/tiempo-captura", "/api/reportes/averias", "/api/reportes/patrones", "/api/reportes/indicador-diario"];

    private static List<JsonElement> Lista(JsonElement e, string campo) => e.GetProperty(campo).EnumerateArray().ToList();

    // ------------------------------------------------------------------------------------ quien los ve

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.SUPER_ADMIN)]
    public async Task El_coordinador_el_comercial_y_los_administradores_ven_todos_los_reportes(string rol)
    {
        var sesion = await SesionAsync(rol);

        foreach (var ruta in RUTAS)
        {
            Assert.Equal(HttpStatusCode.OK, (await Cliente(sesion.Token).GetAsync(ruta)).StatusCode);
        }
    }

    [Theory]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    public async Task El_inspector_y_el_conductor_reciben_403_en_todos_los_reportes(string rol)
    {
        var sesion = await SesionAsync(rol);

        foreach (var ruta in RUTAS)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(sesion.Token).GetAsync(ruta)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync(RUTAS[0])).StatusCode);
    }

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD, true)]
    [InlineData(clsRol.COMERCIAL, true)]
    [InlineData(clsRol.ADMIN, true)]
    [InlineData(clsRol.SUPER_ADMIN, true)]
    [InlineData(clsRol.INSPECTOR, false)]
    [InlineData(clsRol.CONDUCTOR, false)]
    public async Task La_capacidad_ver_reportes_solo_la_tienen_quienes_ven_el_tablero(string rol, bool esperado)
    {
        var sesion = await SesionAsync(rol);

        var me = await ComoJsonAsync(await Cliente(sesion.Token).GetAsync("/api/auth/me"));

        Assert.Equal(esperado, me.GetProperty("capacidades").EnumerateArray().Select(c => c.GetString()).Contains("ver_reportes"));
    }

    // ------------------------------------------------------------------------------------ cobertura

    [Fact]
    public async Task La_cobertura_cuenta_las_unidades_despachadas_con_sus_seis_registros_cerrados_de_su_cliente()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var r = await ConsultarAsync(coordinador, "/api/reportes/cobertura");

        Assert.Equal(["despachadas", "completas", "cobertura", "meta", "por_operacion", "por_cliente", "por_punto"], r.EnumerateObject().Select(p => p.Name).ToList());
        // V1, V2 y V3 se despacharon (V4 es de otro cliente, V5 fue hace cuarenta dias y V6 nunca salio); solo V1 tiene los seis registros.
        Assert.Equal(3, r.GetProperty("despachadas").GetInt32());
        Assert.Equal(1, r.GetProperty("completas").GetInt32());
        Assert.Equal(33.33m, r.GetProperty("cobertura").GetDecimal());
        Assert.Equal(90m, r.GetProperty("meta").GetDecimal());
        var porOperacion = Lista(r, "por_operacion");
        Assert.Equal(["importacion", "nacional"], porOperacion.Select(o => o.GetProperty("operacion").GetString()).ToList());
        Assert.Equal(["despachadas", "completas", "cobertura"], porOperacion[0].EnumerateObject().Skip(1).Select(p => p.Name).ToList());
        Assert.Equal(0m, porOperacion[0].GetProperty("cobertura").GetDecimal());
        Assert.Equal(50m, porOperacion[1].GetProperty("cobertura").GetDecimal());
        var porCliente = Assert.Single(Lista(r, "por_cliente"));
        Assert.Equal(e.Sofasa, porCliente.GetProperty("cliente_id").GetGuid());
        Assert.Equal(3, porCliente.GetProperty("despachadas").GetInt32());
        // El viaje A sale del CEDI 1 y llega al concesionario: sus dos unidades cuentan en cada punto.
        var porPunto = Lista(r, "por_punto");
        Assert.Equal(2, porPunto.Count);
        Assert.All(porPunto, p => Assert.Equal(2, p.GetProperty("despachadas").GetInt32()));
        Assert.Contains(porPunto, p => p.GetProperty("punto_id").GetGuid() == e.Origen);
        Assert.Contains(porPunto, p => p.GetProperty("punto_id").GetGuid() == e.Destino);
    }

    [Fact]
    public async Task El_super_administrador_ve_todos_los_clientes_y_elige_uno_con_cliente_id_y_los_demas_reciben_403()
    {
        var e = await EscenarioAsync();
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var delOtro = await SesionAsync(clsRol.COORDINADOR_CALIDAD, "coord-otro@ejemplo.com", e.Otro);

        var todos = await ConsultarAsync(superAdmin, "/api/reportes/cobertura");
        var soloOtro = await ConsultarAsync(superAdmin, "/api/reportes/cobertura", $"?cliente_id={e.Otro}");
        var propio = await ConsultarAsync(delOtro, "/api/reportes/cobertura");
        var propioExplicito = await ConsultarAsync(coordinador, "/api/reportes/cobertura", $"?cliente_id={e.Sofasa}");

        Assert.Equal(4, todos.GetProperty("despachadas").GetInt32());
        Assert.Equal(2, Lista(todos, "por_cliente").Count);
        Assert.Equal(1, soloOtro.GetProperty("despachadas").GetInt32());
        Assert.Equal(1, propio.GetProperty("despachadas").GetInt32());
        Assert.Equal(3, propioExplicito.GetProperty("despachadas").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(coordinador.Token).GetAsync($"/api/reportes/cobertura?cliente_id={e.Otro}")).StatusCode);
        foreach (var ruta in RUTAS)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(delOtro.Token).GetAsync($"{ruta}?cliente_id={e.Sofasa}")).StatusCode);
        }
    }

    [Fact]
    public async Task Los_filtros_recalculan_la_cobertura()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        Assert.Equal(2, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?operacion=nacional")).GetProperty("despachadas").GetInt32());
        Assert.Equal(1, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?marca=Toyota")).GetProperty("despachadas").GetInt32());
        Assert.Equal(1, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?trailer=TR-2")).GetProperty("despachadas").GetInt32());
        Assert.Equal(2, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", $"?punto_id={e.Origen}")).GetProperty("despachadas").GetInt32());
        Assert.Equal(2, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", $"?punto_id={e.Destino}")).GetProperty("despachadas").GetInt32());
        Assert.Equal(0, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", $"?punto_id={Guid.NewGuid()}")).GetProperty("despachadas").GetInt32());
        Assert.Equal(0m, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?operacion=exportacion")).GetProperty("cobertura").GetDecimal());
        // Un dia sin despachos: el rango incluye ambos extremos.
        Assert.Equal(3, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", $"?desde={e.Dias.Dia}&hasta={e.Dias.Dia}")).GetProperty("despachadas").GetInt32());
        Assert.Equal(0, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", $"?desde={e.Dias.Hoy}&hasta={e.Dias.Hoy}")).GetProperty("despachadas").GetInt32());
        // El de hace cuarenta dias entra si el rango lo incluye.
        Assert.Equal(4, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", $"?desde={DiaDeColombia(DateTimeOffset.UtcNow.AddDays(-60))}")).GetProperty("despachadas").GetInt32());
    }

    // ------------------------------------------------------------------------------------ adopcion

    [Fact]
    public async Task La_adopcion_es_de_los_vin_programados_cuantos_tienen_su_salida_en_el_sistema()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var r = await ConsultarAsync(coordinador, "/api/reportes/adopcion");

        Assert.Equal(["programados", "con_salida_digital", "adopcion", "meta", "por_dia", "por_operacion", "por_cliente"], r.EnumerateObject().Select(p => p.Name).ToList());
        // El dia del despacho se programaron V1, V2, V3 y V6 de Sofasa (V4 es de otro cliente); V6 no tiene inspeccion de salida.
        Assert.Equal(4, r.GetProperty("programados").GetInt32());
        Assert.Equal(3, r.GetProperty("con_salida_digital").GetInt32());
        Assert.Equal(75m, r.GetProperty("adopcion").GetDecimal());
        Assert.Equal(95m, r.GetProperty("meta").GetDecimal());
        var dia = Assert.Single(Lista(r, "por_dia"));
        Assert.Equal(e.Dias.Dia, dia.GetProperty("fecha").GetString());
        Assert.Equal(["fecha", "programados", "con_salida_digital", "adopcion"], dia.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(["importacion", "nacional"], Lista(r, "por_operacion").Select(o => o.GetProperty("operacion").GetString()).ToList());
        Assert.Equal(e.Sofasa, Assert.Single(Lista(r, "por_cliente")).GetProperty("cliente_id").GetGuid());
        var todos = await ConsultarAsync(await SesionAsync(clsRol.SUPER_ADMIN), "/api/reportes/adopcion");
        Assert.Equal(5, todos.GetProperty("programados").GetInt32());
        Assert.Equal(4, todos.GetProperty("con_salida_digital").GetInt32());
    }

    // ------------------------------------------------------------------------------------ tiempo de captura

    [Fact]
    public async Task El_tiempo_de_captura_es_la_mediana_de_las_inspecciones_cerradas_de_cargue_y_descargue_por_proceso_y_por_inspector()
    {
        var e = await EscenarioAsync();
        var admin = await SesionAsync(clsRol.ADMIN);

        var r = await ConsultarAsync(admin, "/api/reportes/tiempo-captura");

        Assert.Equal(["mediana_minutos", "por_proceso", "por_inspector"], r.EnumerateObject().Select(p => p.Name).ToList());
        // Cargue de Sofasa: 20, 10, 40, 5 y 15 minutos; Descargue: 10 y 30. En total 5, 10, 10, 15, 20, 30 y 40: la mediana es 15. Transporte no cuenta.
        Assert.Equal(15m, r.GetProperty("mediana_minutos").GetDecimal());
        var porProceso = Lista(r, "por_proceso");
        Assert.Equal(["cargue", "descargue"], porProceso.Select(p => p.GetProperty("proceso").GetString()).ToList());
        Assert.Equal(15m, porProceso[0].GetProperty("mediana_minutos").GetDecimal());
        Assert.Equal(5, porProceso[0].GetProperty("inspecciones").GetInt32());
        Assert.Equal(20m, porProceso[1].GetProperty("mediana_minutos").GetDecimal());
        var porInspector = Lista(r, "por_inspector");
        Assert.Equal(["Ines Uno", "Ivan Dos"], porInspector.Select(p => p.GetProperty("nombre").GetString()).ToList());
        Assert.Equal(e.I1, porInspector[0].GetProperty("usuario_id").GetGuid());
        Assert.Equal(20m, porInspector[0].GetProperty("mediana_minutos").GetDecimal());
        Assert.Equal(10m, porInspector[1].GetProperty("mediana_minutos").GetDecimal());
        Assert.Equal(["usuario_id", "nombre", "mediana_minutos", "inspecciones"], porInspector[0].EnumerateObject().Select(p => p.Name).ToList());
        var soloDescargue = await ConsultarAsync(admin, "/api/reportes/tiempo-captura", "?proceso=descargue");
        Assert.Equal(20m, soloDescargue.GetProperty("mediana_minutos").GetDecimal());
    }

    [Fact]
    public async Task Sin_inspecciones_la_mediana_es_nula_y_las_listas_vienen_vacias()
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        var r = await ConsultarAsync(admin, "/api/reportes/tiempo-captura");

        Assert.Equal(JsonValueKind.Null, r.GetProperty("mediana_minutos").ValueKind);
        Assert.Empty(Lista(r, "por_proceso"));
        Assert.Empty(Lista(r, "por_inspector"));
        var cobertura = await ConsultarAsync(admin, "/api/reportes/cobertura");
        Assert.Equal(0, cobertura.GetProperty("despachadas").GetInt32());
        Assert.Equal(0m, cobertura.GetProperty("cobertura").GetDecimal());
    }

    // ------------------------------------------------------------------------------------ averias

    [Fact]
    public async Task Las_averias_agrupan_por_configuracion_con_los_vin_los_que_tienen_novedad_y_los_hallazgos()
    {
        var e = await EscenarioAsync();
        var comercial = await SesionAsync(clsRol.COMERCIAL);

        var r = await ConsultarAsync(comercial, "/api/reportes/averias");

        Assert.Equal(["por_trailer", "por_placa", "por_conductor", "por_unidad", "por_marca", "por_clase", "por_nivel"], r.EnumerateObject().Select(p => p.Name).ToList());
        var trailers = Lista(r, "por_trailer");
        Assert.Equal(["trailer", "vines", "con_novedad", "hallazgos"], trailers[0].EnumerateObject().Select(p => p.Name).ToList());
        var tr1 = trailers.Single(t => t.GetProperty("trailer").GetString() == "TR-1");
        Assert.Equal((2, 1, 1), (tr1.GetProperty("vines").GetInt32(), tr1.GetProperty("con_novedad").GetInt32(), tr1.GetProperty("hallazgos").GetInt32()));
        var tr2 = trailers.Single(t => t.GetProperty("trailer").GetString() == "TR-2");
        Assert.Equal((1, 1, 1), (tr2.GetProperty("vines").GetInt32(), tr2.GetProperty("con_novedad").GetInt32(), tr2.GetProperty("hallazgos").GetInt32()));
        Assert.DoesNotContain(trailers, t => t.GetProperty("trailer").GetString() is "TR-9" or "TR-5");
        Assert.Equal(["ABC123", "DEF456"], Lista(r, "por_placa").Select(p => p.GetProperty("placa").GetString()).Order().ToList());
        var conductores = Lista(r, "por_conductor");
        Assert.Equal(["usuario_id", "nombre", "cedula", "vines", "con_novedad", "hallazgos"], conductores[0].EnumerateObject().Select(p => p.Name).ToList());
        var delConductor = conductores.Single(c => c.GetProperty("usuario_id").ValueKind == JsonValueKind.String);
        Assert.Equal(e.Conductor, delConductor.GetProperty("usuario_id").GetGuid());
        Assert.Equal("Carlos Conductor", delConductor.GetProperty("nombre").GetString());
        Assert.Equal("1032456789", delConductor.GetProperty("cedula").GetString());
        Assert.Equal(2, delConductor.GetProperty("vines").GetInt32());
        Assert.Equal(1, conductores.Single(c => c.GetProperty("usuario_id").ValueKind == JsonValueKind.Null).GetProperty("vines").GetInt32());
        // Dos configuraciones: la ninera (V1 y V2, con una novedad) y la nodriza (V3, con su hallazgo).
        var unidades = Lista(r, "por_unidad");
        Assert.Equal(["tipo_unidad_id", "unidad", "vines", "con_novedad", "hallazgos"], unidades[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal([(2, 1, 1), (1, 1, 1)], unidades.Select(u => (u.GetProperty("vines").GetInt32(), u.GetProperty("con_novedad").GetInt32(), u.GetProperty("hallazgos").GetInt32())).ToList());
        Assert.All(unidades, u => Assert.False(string.IsNullOrWhiteSpace(u.GetProperty("unidad").GetString())));
        var marcas = Lista(r, "por_marca");
        Assert.Equal(["Renault", "Toyota"], marcas.Select(m => m.GetProperty("marca").GetString()).ToList());
        Assert.Equal([("golpe", 1), ("rotura", 1)], Lista(r, "por_clase").Select(c => (c.GetProperty("clase").GetString()!, c.GetProperty("total").GetInt32())).Order().ToList());
        Assert.Equal([("v1", 1), ("v3", 1)], Lista(r, "por_nivel").Select(n => (n.GetProperty("nivel").GetString()!, n.GetProperty("total").GetInt32())).Order().ToList());
    }

    [Fact]
    public async Task El_filtro_de_nivel_deja_en_las_averias_solo_los_hallazgos_de_ese_nivel()
    {
        await EscenarioAsync();
        var admin = await SesionAsync(clsRol.ADMIN);

        var r = await ConsultarAsync(admin, "/api/reportes/averias", "?nivel=v1");

        Assert.Equal([("v1", 1)], Lista(r, "por_nivel").Select(n => (n.GetProperty("nivel").GetString()!, n.GetProperty("total").GetInt32())).ToList());
        Assert.Equal(1, Lista(r, "por_marca").Sum(m => m.GetProperty("hallazgos").GetInt32()));
    }

    // ------------------------------------------------------------------------------------ patrones

    [Fact]
    public async Task Los_patrones_cuentan_los_hallazgos_por_proceso_y_por_zona_con_lado_y_numero_y_marcan_el_umbral()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var r = await ConsultarAsync(coordinador, "/api/reportes/patrones");

        Assert.Equal(["total", "por_proceso", "por_region", "por_region_proceso", "por_nivel", "por_clase", "por_tamano", "umbral"], r.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(2, r.GetProperty("total").GetInt32());
        var proceso = Assert.Single(Lista(r, "por_proceso"));
        Assert.Equal(["proceso", "total", "supera_umbral"], proceso.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("cargue", proceso.GetProperty("proceso").GetString());
        Assert.Equal(2, proceso.GetProperty("total").GetInt32());
        Assert.False(proceso.GetProperty("supera_umbral").GetBoolean());
        var regiones = Lista(r, "por_region");
        Assert.Equal(["cuadrante_numero", "lado", "nombre", "zona", "total", "supera_umbral"], regiones[0].EnumerateObject().Select(p => p.Name).ToList());
        var izquierdo7 = regiones.Single(g => g.GetProperty("cuadrante_numero").ValueKind == JsonValueKind.Number);
        Assert.Equal(7, izquierdo7.GetProperty("cuadrante_numero").GetInt32());
        Assert.Equal("izquierdo", izquierdo7.GetProperty("lado").GetString());
        Assert.Equal("Puerta delantera", izquierdo7.GetProperty("nombre").GetString());
        Assert.StartsWith("Izquierdo 7", izquierdo7.GetProperty("zona").GetString());
        Assert.Equal(1, izquierdo7.GetProperty("total").GetInt32());
        Assert.Equal(2, regiones.Count);
        Assert.Equal(2, Lista(r, "por_region_proceso").Count);
        Assert.Equal(["v1", "v3"], Lista(r, "por_nivel").Select(n => n.GetProperty("nivel").GetString()).Order().ToList());
        Assert.Equal(["golpe", "rotura"], Lista(r, "por_clase").Select(n => n.GetProperty("clase").GetString()).Order().ToList());
        Assert.Equal(["1_5cm", "5_10cm"], Lista(r, "por_tamano").Select(n => n.GetProperty("tamano").GetString()).Order().ToList());
        Assert.Equal(10, r.GetProperty("umbral").GetProperty("cantidad").GetInt32());
        Assert.Equal(30, r.GetProperty("umbral").GetProperty("ventana_dias").GetInt32());
    }

    [Fact]
    public async Task La_marca_de_umbral_sale_del_parametro_y_se_aplica_sin_desplegar()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        await CambiarParametroAsync("umbral_patron_cantidad", "2");

        var r = await ConsultarAsync(coordinador, "/api/reportes/patrones");

        // En la ventana hay dos hallazgos de Cargue: el proceso supera el umbral de dos, pero cada zona (uno cada una) no.
        Assert.True(Lista(r, "por_proceso")[0].GetProperty("supera_umbral").GetBoolean());
        Assert.All(Lista(r, "por_region"), g => Assert.False(g.GetProperty("supera_umbral").GetBoolean()));
        Assert.Equal(2, r.GetProperty("umbral").GetProperty("cantidad").GetInt32());

        await CambiarParametroAsync("umbral_patron_cantidad", "1");
        var otra = await ConsultarAsync(coordinador, "/api/reportes/patrones");
        Assert.All(Lista(otra, "por_region"), g => Assert.True(g.GetProperty("supera_umbral").GetBoolean()));
        Assert.All(Lista(otra, "por_region_proceso"), g => Assert.True(g.GetProperty("supera_umbral").GetBoolean()));
    }

    [Fact]
    public async Task Los_patrones_cuentan_los_hallazgos_de_inspecciones_cerradas_aunque_el_viaje_no_haya_terminado()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        // V2 y V3 aun no terminan su ruta (les falta el Transporte), pero sus hallazgos ya cuentan.
        var patrones = await ConsultarAsync(coordinador, "/api/reportes/patrones");

        Assert.Equal(2, patrones.GetProperty("total").GetInt32());
    }

    // ------------------------------------------------------------------------------------ indicador diario

    [Fact]
    public async Task El_indicador_diario_es_el_porcentaje_de_unidades_despachadas_ese_dia_con_un_hallazgo_v1()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var r = await ConsultarAsync(coordinador, "/api/reportes/indicador-diario", $"?fecha={e.Dias.Dia}");

        Assert.Equal(["fecha", "despachados", "con_novedad", "porcentaje", "objetivo", "por_operacion", "por_cliente"], r.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(e.Dias.Dia, r.GetProperty("fecha").GetString());
        // V1, V2 y V3 se despacharon; solo V2 tuvo un hallazgo V1 (el V3 de V3 no cuenta).
        Assert.Equal(3, r.GetProperty("despachados").GetInt32());
        Assert.Equal(1, r.GetProperty("con_novedad").GetInt32());
        Assert.Equal(33.33m, r.GetProperty("porcentaje").GetDecimal());
        Assert.Equal(1.2m, r.GetProperty("objetivo").GetDecimal());
        var porOperacion = Lista(r, "por_operacion");
        Assert.Equal(["importacion", "nacional"], porOperacion.Select(o => o.GetProperty("operacion").GetString()).ToList());
        Assert.Equal(0, porOperacion[0].GetProperty("con_novedad").GetInt32());
        Assert.Equal(50m, porOperacion[1].GetProperty("porcentaje").GetDecimal());
        Assert.Equal(3, Assert.Single(Lista(r, "por_cliente")).GetProperty("despachados").GetInt32());
        var todos = await ConsultarAsync(await SesionAsync(clsRol.SUPER_ADMIN), "/api/reportes/indicador-diario", $"?fecha={e.Dias.Dia}");
        Assert.Equal(4, todos.GetProperty("despachados").GetInt32());
        Assert.Equal(2, todos.GetProperty("con_novedad").GetInt32());
        Assert.Equal(2, Lista(todos, "por_cliente").Count);
    }

    [Fact]
    public async Task El_objetivo_del_indicador_diario_sale_del_parametro_y_un_dia_sin_despachos_da_cero()
    {
        var e = await EscenarioAsync();
        var admin = await SesionAsync(clsRol.ADMIN);
        await CambiarParametroAsync("objetivo_diario_novedad", "2.5");

        var r = await ConsultarAsync(admin, "/api/reportes/indicador-diario", $"?fecha={e.Dias.Hoy}");

        Assert.Equal(2.5m, r.GetProperty("objetivo").GetDecimal());
        Assert.Equal(0, r.GetProperty("despachados").GetInt32());
        Assert.Equal(0m, r.GetProperty("porcentaje").GetDecimal());
        Assert.Equal(e.Dias.Hoy, r.GetProperty("fecha").GetString());
        // Sin fecha, el indicador es el de hoy.
        Assert.Equal(e.Dias.Hoy, (await ConsultarAsync(admin, "/api/reportes/indicador-diario")).GetProperty("fecha").GetString());
    }

    // ------------------------------------------------------------------------------------ validacion

    [Theory]
    [InlineData("desde=ayer", "desde")]
    [InlineData("hasta=2026-13-45", "hasta")]
    [InlineData("desde=2026-07-01T00:00:00Z", "desde")]
    [InlineData("desde=2026-07-10&hasta=2026-07-01", "desde")]
    [InlineData("desde=2020-01-01&hasta=2026-07-01", "desde")]
    [InlineData("operacion=aerea", "operacion")]
    [InlineData("proceso=bodega", "proceso")]
    [InlineData("nivel=v9", "nivel")]
    [InlineData("cliente_id=no-es-uuid", "cliente_id")]
    [InlineData("punto_id=123", "punto_id")]
    public async Task Un_filtro_mal_formado_responde_400_con_el_campo_en_todos_los_reportes(string consulta, string campo)
    {
        var admin = await SesionAsync(clsRol.SUPER_ADMIN);

        foreach (var ruta in RUTAS.Where(r => !r.EndsWith("indicador-diario", StringComparison.Ordinal)))
        {
            var r = await Cliente(admin.Token).GetAsync($"{ruta}?{consulta}");

            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{ruta}?{consulta} dio {(int)r.StatusCode}");
            Assert.True((await ComoJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty(campo, out _));
        }
    }

    [Fact]
    public async Task Una_fecha_mal_escrita_del_indicador_diario_responde_400()
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        var r = await Cliente(admin.Token).GetAsync("/api/reportes/indicador-diario?fecha=21/10/2026");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await ComoJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("fecha", out _));
    }

    // ------------------------------------------------------------------------------------ rutas retiradas

    [Theory]
    [InlineData("/api/patrones")]
    [InlineData("/api/patrones/analisis")]
    public async Task Las_rutas_anteriores_de_patrones_se_retiraron_y_responden_404(string ruta)
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        var r = await Cliente(admin.Token).GetAsync(ruta);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task La_tasa_de_rechazo_sigue_publicada_sin_encabezado_obsoleto_y_no_hay_reporte_de_rechazo_nuevo()
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        var anterior = await Cliente(admin.Token).GetAsync("/api/metricas/rechazo");
        var nuevo = await Cliente(admin.Token).GetAsync("/api/reportes/rechazo");

        Assert.Equal(HttpStatusCode.OK, anterior.StatusCode);
        Assert.False(anterior.Headers.Contains("Deprecation"));
        Assert.Equal(HttpStatusCode.NotFound, nuevo.StatusCode);
    }
}
