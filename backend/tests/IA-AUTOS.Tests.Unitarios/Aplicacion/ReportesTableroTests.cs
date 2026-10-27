using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Unitarios.Falsos;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>Los reportes del tablero: la validacion de sus filtros, el alcance por cliente, la mediana y las marcas de umbral, sobre filas ya leidas.</summary>
public sealed class ReportesTableroTests
{
    // 2026-10-22 03:00 UTC = 2026-10-21 22:00 en Colombia: "hoy" es el 21.
    private static readonly DateTimeOffset AHORA = new(2026, 10, 22, 3, 0, 0, TimeSpan.Zero);

    private sealed class ConsultasFalsas : IConsultasIndicadores
    {
        public List<FiltrosReporte> Recibidos { get; } = [];

        public List<FilaCobertura> Cobertura { get; } = [];

        public List<FilaConteoHallazgo> Hallazgos { get; } = [];

        public List<FilaConteoHallazgo> HallazgosEnVentana { get; } = [];

        public List<FilaTiempo> Tiempos { get; } = [];

        public List<FilaNovedadProceso> Novedades { get; } = [];

        public Task<IReadOnlyList<FilaNovedadProceso>> NovedadesDeProcesoAsync(FiltrosReporte filtros, CancellationToken ct = default)
        {
            Recibidos.Add(filtros);
            return Task.FromResult<IReadOnlyList<FilaNovedadProceso>>(Novedades);
        }

        public Task<IReadOnlyList<FilaCobertura>> CoberturaAsync(FiltrosReporte filtros, CancellationToken ct = default)
        {
            Recibidos.Add(filtros);
            return Task.FromResult<IReadOnlyList<FilaCobertura>>(Cobertura);
        }

        public Task<IReadOnlyList<FilaAdopcion>> AdopcionAsync(FiltrosReporte filtros, CancellationToken ct = default)
        {
            Recibidos.Add(filtros);
            return Task.FromResult<IReadOnlyList<FilaAdopcion>>([]);
        }

        public Task<IReadOnlyList<FilaTiempo>> TiemposDeCapturaAsync(FiltrosReporte filtros, CancellationToken ct = default)
        {
            Recibidos.Add(filtros);
            return Task.FromResult<IReadOnlyList<FilaTiempo>>(Tiempos);
        }

        public Task<IReadOnlyList<FilaUnidad>> UnidadesDespachadasAsync(FiltrosReporte filtros, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FilaUnidad>>([]);

        public Task<IReadOnlyList<FilaUnidad>> UnidadesConHallazgosAsync(FiltrosReporte filtros, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FilaUnidad>>([]);

        public Task<IReadOnlyList<FilaConteoHallazgo>> ConteoDeHallazgosAsync(FiltrosReporte filtros, CancellationToken ct = default)
        {
            Recibidos.Add(filtros);
            // La primera lectura es la del periodo; la segunda, la de la ventana del umbral (empieza antes del "desde" del periodo si es mas larga).
            return Task.FromResult<IReadOnlyList<FilaConteoHallazgo>>(Recibidos.Count(f => f.Desde != default) % 2 == 1 || HallazgosEnVentana.Count == 0 ? Hallazgos : HallazgosEnVentana);
        }

        public Task<IReadOnlyList<FilaIndicador>> IndicadorDiarioAsync(FiltrosReporte filtros, CancellationToken ct = default)
        {
            Recibidos.Add(filtros);
            return Task.FromResult<IReadOnlyList<FilaIndicador>>([]);
        }

        public Task<IReadOnlyDictionary<Guid, UsuarioReporte>> UsuariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, UsuarioReporte>>(ids.ToDictionary(i => i, i => new UsuarioReporte(i, "Usuario " + i.ToString()[..4], null)));
    }

    private sealed class ParametrosFalsos(Dictionary<string, string?> valores) : IParametros
    {
        public Task<string?> ValorAsync(string clave, CancellationToken ct = default) => Task.FromResult(valores.GetValueOrDefault(clave));

        public Task<int> EnteroAsync(string clave, int porDefecto, CancellationToken ct = default) =>
            Task.FromResult(int.TryParse(valores.GetValueOrDefault(clave), out var n) ? n : porDefecto);

        public Task<IReadOnlyList<string>> ListaAsync(string clave, IReadOnlyList<string> porDefecto, CancellationToken ct = default) => Task.FromResult(porDefecto);

        public void Invalidar()
        {
        }
    }

    private static (clsReportesTableroService Servicio, ConsultasFalsas Consultas) Crear(string rol = clsRol.ADMIN, Guid? cliente = null, Dictionary<string, string?>? parametros = null)
    {
        var consultas = new ConsultasFalsas();
        var actual = new clsUsuarioActualFalso { Id = Guid.NewGuid(), Rol = rol, ClienteId = rol == clsRol.SUPER_ADMIN ? null : cliente ?? clsCatalogosLecturaFalso.SOFASA };
        return (new clsReportesTableroService(consultas, new clsCatalogosLecturaFalso(), new ParametrosFalsos(parametros ?? []), actual, new clsRelojFijo(AHORA)), consultas);
    }

    private static string? CampoConError(clsValidacionException ex, string campo) =>
        ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).GetValueOrDefault(campo)?.FirstOrDefault();

    // ------------------------------------------------------------------------------------ novedades de proceso

    [Fact]
    public async Task Las_novedades_de_proceso_se_cuentan_por_tipo_punto_cliente_y_mes()
    {
        var (servicio, consultas) = Crear();
        var cedi = Guid.NewGuid();
        var cliente = clsCatalogosLecturaFalso.SOFASA;
        consultas.Novedades.AddRange(
        [
            new FilaNovedadProceso("cargue_sin_inspector", cedi, cliente, "2026-09", 2),
            new FilaNovedadProceso("cargue_sin_inspector", cedi, cliente, "2026-10", 3),
            new FilaNovedadProceso("recibo_sin_entrega", null, cliente, "2026-10", 1),
        ]);

        var r = await servicio.NovedadesDeProcesoAsync(new FiltrosReporteRequest("2026-09-01", "2026-10-21", null, null, null, null, null, null, null));

        Assert.Equal(6, r.Total);
        Assert.Equal(["cargue_sin_inspector=5", "recibo_sin_entrega=1"], r.PorTipo.Select(x => $"{x.Tipo}={x.Total}").ToList());
        Assert.Equal([5, 1], r.PorPunto.Select(x => x.Total).ToList());
        Assert.Equal(cedi, r.PorPunto[0].PuntoId);
        Assert.Null(r.PorPunto[1].PuntoId);
        Assert.Equal([6], r.PorCliente.Select(x => x.Total).ToList());
        Assert.Equal(["2026-09=2", "2026-10=4"], r.PorMes.Select(x => $"{x.Mes}={x.Total}").ToList());
    }

    [Fact]
    public async Task Sin_novedades_de_proceso_el_reporte_responde_en_cero_y_usa_los_mismos_filtros_que_los_demas()
    {
        var (servicio, consultas) = Crear(clsRol.COORDINADOR_CALIDAD);

        var r = await servicio.NovedadesDeProcesoAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));

        Assert.Equal(0, r.Total);
        Assert.Empty(r.PorTipo);
        Assert.Equal(clsCatalogosLecturaFalso.SOFASA, Assert.Single(consultas.Recibidos).ClienteId);
    }

    // ------------------------------------------------------------------------------------ mediana

    [Theory]
    [InlineData(new[] { 5.0, 10.0, 15.0 }, 10.0)]
    [InlineData(new[] { 40.0, 5.0, 20.0, 10.0 }, 15.0)]
    [InlineData(new[] { 7.0 }, 7.0)]
    [InlineData(new[] { 1.0, 2.0 }, 1.5)]
    public void La_mediana_es_el_valor_del_medio_o_el_promedio_de_los_dos_del_medio(double[] valores, double esperada) =>
        Assert.Equal((decimal)esperada, clsReportesTableroService.Mediana(valores.Select(v => (decimal)v)));

    [Fact]
    public void La_mediana_se_redondea_a_dos_decimales()
    {
        Assert.Equal(0.67m, clsReportesTableroService.Mediana([0.33m, 1.01m]));
    }

    // ------------------------------------------------------------------------------------ filtros

    [Fact]
    public async Task Sin_fechas_el_reporte_cubre_los_ultimos_treinta_dias_hasta_hoy_en_Colombia()
    {
        var (servicio, consultas) = Crear();

        await servicio.CoberturaAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));

        var filtros = Assert.Single(consultas.Recibidos);
        Assert.Equal(new DateOnly(2026, 10, 21), filtros.Hasta);
        Assert.Equal(new DateOnly(2026, 9, 22), filtros.Desde);
        Assert.Equal(30, filtros.Hasta.DayNumber - filtros.Desde.DayNumber + 1);
    }

    [Fact]
    public async Task Las_fechas_se_leen_como_dias_y_los_demas_filtros_se_pasan_limpios()
    {
        var (servicio, consultas) = Crear();
        var punto = Guid.NewGuid();

        await servicio.AdopcionAsync(new FiltrosReporteRequest(" 2026-10-01 ", "2026-10-15", "nacional", null, punto.ToString(), "  Renault ", " TR-1 ", "cargue", "v1"));

        var filtros = Assert.Single(consultas.Recibidos);
        Assert.Equal(new DateOnly(2026, 10, 1), filtros.Desde);
        Assert.Equal(new DateOnly(2026, 10, 15), filtros.Hasta);
        Assert.Equal("nacional", filtros.Operacion);
        Assert.Equal(punto, filtros.PuntoId);
        Assert.Equal("Renault", filtros.Marca);
        Assert.Equal("TR-1", filtros.Trailer);
        Assert.Equal("cargue", filtros.Proceso);
        Assert.Equal("v1", filtros.Nivel);
    }

    [Fact]
    public async Task Todos_los_filtros_mal_formados_se_responden_juntos()
    {
        var (servicio, _) = Crear();

        var ex = await Assert.ThrowsAsync<clsValidacionException>(() =>
            servicio.CoberturaAsync(new FiltrosReporteRequest("ayer", "2026-13-01", "aerea", "x", "y", null, null, "bodega", "v9")));

        Assert.NotNull(CampoConError(ex, "desde"));
        Assert.NotNull(CampoConError(ex, "hasta"));
        Assert.NotNull(CampoConError(ex, "operacion"));
        Assert.NotNull(CampoConError(ex, "cliente_id"));
        Assert.NotNull(CampoConError(ex, "punto_id"));
        Assert.NotNull(CampoConError(ex, "proceso"));
        Assert.NotNull(CampoConError(ex, "nivel"));
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-01", "desde no puede ser posterior a hasta")]
    [InlineData("2025-01-01", "2026-10-01", "366")]
    public async Task Un_periodo_al_reves_o_mayor_a_un_ano_se_rechaza(string desde, string hasta, string mensaje)
    {
        var (servicio, _) = Crear();

        var ex = await Assert.ThrowsAsync<clsValidacionException>(() =>
            servicio.AveriasAsync(new FiltrosReporteRequest(desde, hasta, null, null, null, null, null, null, null)));

        Assert.Contains(mensaje, CampoConError(ex, "desde"));
    }

    [Fact]
    public async Task Un_periodo_de_366_dias_se_admite()
    {
        var (servicio, consultas) = Crear();

        await servicio.CoberturaAsync(new FiltrosReporteRequest("2025-10-01", "2026-09-30", null, null, null, null, null, null, null));

        Assert.Single(consultas.Recibidos);
    }

    // ------------------------------------------------------------------------------------ cliente

    [Fact]
    public async Task Quien_se_limita_a_su_cliente_siempre_recibe_el_suyo_y_pedir_otro_es_403()
    {
        var (servicio, consultas) = Crear(clsRol.COORDINADOR_CALIDAD, clsCatalogosLecturaFalso.SOFASA);

        await servicio.CoberturaAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));
        await servicio.CoberturaAsync(new FiltrosReporteRequest(null, null, null, clsCatalogosLecturaFalso.SOFASA.ToString(), null, null, null, null, null));

        Assert.All(consultas.Recibidos, f => Assert.Equal(clsCatalogosLecturaFalso.SOFASA, f.ClienteId));
        await Assert.ThrowsAsync<clsProhibidoException>(() =>
            servicio.CoberturaAsync(new FiltrosReporteRequest(null, null, null, clsCatalogosLecturaFalso.OTRO.ToString(), null, null, null, null, null)));
    }

    [Fact]
    public async Task El_super_administrador_ve_todos_los_clientes_o_elige_uno()
    {
        var (servicio, consultas) = Crear(clsRol.SUPER_ADMIN);

        await servicio.CoberturaAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));
        await servicio.CoberturaAsync(new FiltrosReporteRequest(null, null, null, clsCatalogosLecturaFalso.OTRO.ToString(), null, null, null, null, null));

        Assert.Equal([null, clsCatalogosLecturaFalso.OTRO], consultas.Recibidos.Select(f => f.ClienteId).ToList());
    }

    // ------------------------------------------------------------------------------------ formulas

    [Fact]
    public async Task La_cobertura_es_completas_sobre_despachadas_y_una_unidad_cuenta_en_su_origen_y_en_su_destino()
    {
        var (servicio, consultas) = Crear();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        consultas.Cobertura.Add(new FilaCobertura(clsCatalogosLecturaFalso.SOFASA, "nacional", a, b, 4, 3));
        consultas.Cobertura.Add(new FilaCobertura(clsCatalogosLecturaFalso.SOFASA, "importacion", a, a, 1, 0));

        var r = await servicio.CoberturaAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));

        Assert.Equal((5, 3, 60m, 90m), (r.Despachadas, r.Completas, r.Cobertura, r.Meta));
        Assert.Equal(["importacion", "nacional"], r.PorOperacion.Select(o => o.Operacion).ToList());
        var puntoA = r.PorPunto.Single(p => p.PuntoId == a);
        var puntoB = r.PorPunto.Single(p => p.PuntoId == b);
        // El origen y el destino iguales cuentan una sola vez.
        Assert.Equal((5, 3), (puntoA.Despachadas, puntoA.Completas));
        Assert.Equal((4, 3), (puntoB.Despachadas, puntoB.Completas));
        Assert.Equal("Sofasa", Assert.Single(r.PorCliente).ClienteNombre);
    }

    [Fact]
    public async Task Sin_unidades_la_cobertura_es_cero_y_no_divide_por_cero()
    {
        var (servicio, _) = Crear();

        var r = await servicio.CoberturaAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));

        Assert.Equal((0, 0, 0m), (r.Despachadas, r.Completas, r.Cobertura));
        Assert.Empty(r.PorOperacion);
    }

    [Fact]
    public async Task El_tiempo_de_captura_trae_la_mediana_por_proceso_y_por_inspector_y_nula_sin_inspecciones()
    {
        var (servicio, consultas) = Crear();
        var uno = Guid.NewGuid();
        var dos = Guid.NewGuid();
        var vacio = await servicio.TiempoDeCapturaAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));
        consultas.Tiempos.AddRange([new FilaTiempo("cargue", uno, 10m), new FilaTiempo("cargue", uno, 20m), new FilaTiempo("descargue", dos, 30m)]);

        var r = await servicio.TiempoDeCapturaAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));

        Assert.Null(vacio.MedianaMinutos);
        Assert.Equal(20m, r.MedianaMinutos);
        Assert.Equal([("cargue", 15m, 2), ("descargue", 30m, 1)], r.PorProceso.Select(p => (p.Proceso, p.MedianaMinutos, p.Inspecciones)).ToList());
        Assert.Equal([(uno, 15m, 2), (dos, 30m, 1)], r.PorInspector.Select(p => (p.UsuarioId, p.MedianaMinutos, p.Inspecciones)).ToList());
    }

    [Fact]
    public async Task Los_patrones_marcan_el_umbral_con_los_hallazgos_de_la_ventana_y_traen_el_umbral_del_parametro()
    {
        var (servicio, consultas) = Crear(parametros: new() { [IAAutos.Dominio.Parametros.clsClavesParametros.UMBRAL_PATRON_CANTIDAD] = "3", [IAAutos.Dominio.Parametros.clsClavesParametros.UMBRAL_PATRON_VENTANA_DIAS] = "7" });
        consultas.Hallazgos.Add(new FilaConteoHallazgo("cargue", 7, "izquierdo", null, "Izquierdo 7", "Puerta", "v1", "golpe", "5_10cm", 2));
        consultas.Hallazgos.Add(new FilaConteoHallazgo("descargue", null, null, "frente", "Frente", null, "v3", "rotura", "1_5cm", 1));

        var r = await servicio.PatronesAsync(new FiltrosReporteRequest("2026-10-01", "2026-10-15", null, null, null, null, null, null, null));

        Assert.Equal(3, r.Total);
        Assert.Equal((3, 7), (r.Umbral.Cantidad, r.Umbral.VentanaDias));
        Assert.Equal([("cargue", 2), ("descargue", 1)], r.PorProceso.Select(p => (p.Proceso, p.Total)).ToList());
        // La ventana de 7 dias termina el 15 y empieza el 9; el segundo recibido es el de la ventana.
        var ventana = consultas.Recibidos.Last();
        Assert.Equal((new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 15)), (ventana.Desde, ventana.Hasta));
        Assert.Equal(new DateOnly(2026, 10, 1), consultas.Recibidos.First().Desde);
        var region = r.PorRegion.First();
        Assert.Equal((7, "izquierdo", "Puerta", 2), (region.CuadranteNumero, region.Lado, region.Nombre, region.Total));
        Assert.Equal("Frente", r.PorRegion.Last().Nombre);
        Assert.Equal(["golpe", "rotura"], r.PorClase.Select(c => c.Clase).ToList());
    }

    [Fact]
    public async Task Un_grupo_supera_el_umbral_si_sus_hallazgos_en_la_ventana_llegan_a_la_cantidad()
    {
        var (servicio, consultas) = Crear(parametros: new() { [IAAutos.Dominio.Parametros.clsClavesParametros.UMBRAL_PATRON_CANTIDAD] = "3" });
        consultas.Hallazgos.Add(new FilaConteoHallazgo("cargue", 7, "izquierdo", null, "Izquierdo 7", "Puerta", "v1", "golpe", "5_10cm", 1));
        consultas.HallazgosEnVentana.Add(new FilaConteoHallazgo("cargue", 7, "izquierdo", null, "Izquierdo 7", "Puerta", "v1", "golpe", "5_10cm", 3));
        consultas.HallazgosEnVentana.Add(new FilaConteoHallazgo("cargue", 8, "derecho", null, "Derecho 8", "Puerta", "v1", "golpe", "5_10cm", 2));

        var r = await servicio.PatronesAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));

        Assert.True(r.PorProceso.Single().SuperaUmbral);
        Assert.True(r.PorRegion.Single().SuperaUmbral);
        Assert.True(r.PorRegionProceso.Single().SuperaUmbral);
    }

    [Fact]
    public async Task El_indicador_diario_es_de_un_dia_y_usa_el_objetivo_del_parametro_o_el_de_origen()
    {
        var (servicio, consultas) = Crear();
        var conParametro = Crear(parametros: new() { [IAAutos.Dominio.Parametros.clsClavesParametros.OBJETIVO_DIARIO_NOVEDAD] = "2.5" });

        var r = await servicio.IndicadorDiarioAsync(new FiltrosReporteRequest("2026-01-01", "2026-10-10", null, null, null, null, null, null, null, "2026-10-05"));
        var sinFecha = await conParametro.Servicio.IndicadorDiarioAsync(new FiltrosReporteRequest(null, "2026-10-10", null, null, null, null, null, null, null));
        var hoy = await conParametro.Servicio.IndicadorDiarioAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null));

        var filtros = Assert.Single(consultas.Recibidos);
        Assert.Equal((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5)), (filtros.Desde, filtros.Hasta));
        Assert.Equal(new DateOnly(2026, 10, 5), r.Fecha);
        Assert.Equal(1.2m, r.Objetivo);
        Assert.Equal((new DateOnly(2026, 10, 10), 2.5m), (sinFecha.Fecha, sinFecha.Objetivo));
        Assert.Equal(new DateOnly(2026, 10, 21), hoy.Fecha);
    }
}
