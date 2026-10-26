using System.Globalization;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Parametros;
using IAAutos.Dominio.Reportes;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Reportes;

/// <summary>
/// Los reportes del tablero (cobertura, adopcion, tiempo de captura, averias, patrones e indicador diario). Todos aceptan los mismos filtros (dias, operacion, cliente, punto, marca, trailer, proceso
/// y nivel), cuentan hallazgos de inspecciones cerradas aunque la unidad no haya terminado el viaje y calculan el cliente por el vehiculo: quien no es super administrador solo recibe lo de su cliente.
/// </summary>
public sealed class clsReportesTableroService(
    IConsultasIndicadores consultas,
    ICatalogosLectura catalogos,
    IParametros parametros,
    IUsuarioActual actual,
    IReloj reloj)
{
    public const decimal META_COBERTURA = 90m;
    public const decimal META_ADOPCION = 95m;
    public const int DIAS_POR_DEFECTO = 30;
    public const int DIAS_MAXIMO = 366;

    private const int UMBRAL_CANTIDAD_POR_DEFECTO = 10;
    private const int UMBRAL_VENTANA_POR_DEFECTO = 30;
    private const decimal OBJETIVO_POR_DEFECTO = 1.2m;

    private static readonly IReadOnlySet<string> PROCESOS = new HashSet<string> { clsCodigosEtapa.CARGUE, clsCodigosEtapa.TRANSPORTE, clsCodigosEtapa.DESCARGUE };

    private static readonly IReadOnlySet<string> NIVELES = new HashSet<string> { clsCodigosNivel.V1_PLUS, clsCodigosNivel.V1, clsCodigosNivel.V2, clsCodigosNivel.V3 };

    // -------------------------------------------------------------------------------------------- cobertura

    public async Task<CoberturaDto> CoberturaAsync(FiltrosReporteRequest solicitud, CancellationToken ct = default)
    {
        var filas = await consultas.CoberturaAsync(Parsear(solicitud), ct);
        var clientes = await NombresAsync(clsNombresCatalogo.CLIENTES, ct);
        var puntos = await NombresAsync(clsNombresCatalogo.PUNTOS_CUSTODIA, ct);

        // Una unidad cuenta en su punto de origen y en el de destino (una vez cada uno si son distintos).
        var porPunto = new Dictionary<Guid, (int Despachadas, int Completas)>();
        foreach (var fila in filas)
        {
            foreach (var punto in new[] { fila.OrigenId, fila.DestinoId }.OfType<Guid>().Distinct())
            {
                var (d, c) = porPunto.GetValueOrDefault(punto);
                porPunto[punto] = (d + fila.Despachadas, c + fila.Completas);
            }
        }

        var despachadas = filas.Sum(f => f.Despachadas);
        var completas = filas.Sum(f => f.Completas);
        return new CoberturaDto(
            despachadas,
            completas,
            clsReglasReportes.Porcentaje(completas, despachadas),
            META_COBERTURA,
            filas.GroupBy(f => f.Operacion).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new CoberturaOperacionDto(g.Key, g.Sum(f => f.Despachadas), g.Sum(f => f.Completas), clsReglasReportes.Porcentaje(g.Sum(f => f.Completas), g.Sum(f => f.Despachadas))))
                .ToList(),
            filas.GroupBy(f => f.ClienteId).OrderBy(g => clientes.GetValueOrDefault(g.Key), StringComparer.Ordinal)
                .Select(g => new CoberturaClienteDto(g.Key, clientes.GetValueOrDefault(g.Key), g.Sum(f => f.Despachadas), g.Sum(f => f.Completas), clsReglasReportes.Porcentaje(g.Sum(f => f.Completas), g.Sum(f => f.Despachadas))))
                .ToList(),
            porPunto.OrderByDescending(p => p.Value.Despachadas).ThenBy(p => puntos.GetValueOrDefault(p.Key), StringComparer.Ordinal)
                .Select(p => new CoberturaPuntoDto(p.Key, puntos.GetValueOrDefault(p.Key), p.Value.Despachadas, p.Value.Completas, clsReglasReportes.Porcentaje(p.Value.Completas, p.Value.Despachadas)))
                .ToList());
    }

    // ------------------------------------------------------------------------------------ novedades de proceso

    /// <summary>El conteo de las novedades de proceso del periodo por tipo, punto de custodia, cliente y mes, con los mismos filtros de los demas reportes (el nivel no aplica).</summary>
    public async Task<NovedadesProcesoDto> NovedadesDeProcesoAsync(FiltrosReporteRequest solicitud, CancellationToken ct = default)
    {
        var filas = await consultas.NovedadesDeProcesoAsync(Parsear(solicitud), ct);
        var clientes = await NombresAsync(clsNombresCatalogo.CLIENTES, ct);
        var puntos = await NombresAsync(clsNombresCatalogo.PUNTOS_CUSTODIA, ct);
        return new NovedadesProcesoDto(
            filas.Sum(f => f.Total),
            filas.GroupBy(f => f.Tipo).OrderByDescending(g => g.Sum(f => f.Total)).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new NovedadProcesoTipoDto(g.Key, g.Sum(f => f.Total))).ToList(),
            filas.GroupBy(f => f.PuntoId).OrderByDescending(g => g.Sum(f => f.Total)).ThenBy(g => g.Key is { } p ? puntos.GetValueOrDefault(p) : null, StringComparer.Ordinal)
                .Select(g => new NovedadProcesoPuntoDto(g.Key, g.Key is { } p ? puntos.GetValueOrDefault(p) : null, g.Sum(f => f.Total))).ToList(),
            filas.GroupBy(f => f.ClienteId).OrderByDescending(g => g.Sum(f => f.Total)).ThenBy(g => clientes.GetValueOrDefault(g.Key), StringComparer.Ordinal)
                .Select(g => new NovedadProcesoClienteDto(g.Key, clientes.GetValueOrDefault(g.Key), g.Sum(f => f.Total))).ToList(),
            filas.GroupBy(f => f.Mes).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new NovedadProcesoMesDto(g.Key, g.Sum(f => f.Total))).ToList());
    }

    // --------------------------------------------------------------------------------------------- adopcion

    public async Task<AdopcionDto> AdopcionAsync(FiltrosReporteRequest solicitud, CancellationToken ct = default)
    {
        var filas = await consultas.AdopcionAsync(Parsear(solicitud), ct);
        var clientes = await NombresAsync(clsNombresCatalogo.CLIENTES, ct);
        var programados = filas.Sum(f => f.Programados);
        var conSalida = filas.Sum(f => f.ConSalidaDigital);
        return new AdopcionDto(
            programados,
            conSalida,
            clsReglasReportes.Porcentaje(conSalida, programados),
            META_ADOPCION,
            filas.GroupBy(f => f.Fecha).OrderBy(g => g.Key)
                .Select(g => new AdopcionDiaDto(g.Key, g.Sum(f => f.Programados), g.Sum(f => f.ConSalidaDigital), clsReglasReportes.Porcentaje(g.Sum(f => f.ConSalidaDigital), g.Sum(f => f.Programados))))
                .ToList(),
            filas.GroupBy(f => f.Operacion).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new AdopcionOperacionDto(g.Key, g.Sum(f => f.Programados), g.Sum(f => f.ConSalidaDigital), clsReglasReportes.Porcentaje(g.Sum(f => f.ConSalidaDigital), g.Sum(f => f.Programados))))
                .ToList(),
            filas.GroupBy(f => f.ClienteId).OrderBy(g => clientes.GetValueOrDefault(g.Key), StringComparer.Ordinal)
                .Select(g => new AdopcionClienteDto(g.Key, clientes.GetValueOrDefault(g.Key), g.Sum(f => f.Programados), g.Sum(f => f.ConSalidaDigital), clsReglasReportes.Porcentaje(g.Sum(f => f.ConSalidaDigital), g.Sum(f => f.Programados))))
                .ToList());
    }

    // -------------------------------------------------------------------------------------- tiempo de captura

    public async Task<TiempoCapturaDto> TiempoDeCapturaAsync(FiltrosReporteRequest solicitud, CancellationToken ct = default)
    {
        var filas = await consultas.TiemposDeCapturaAsync(Parsear(solicitud), ct);
        var inspectores = await consultas.UsuariosAsync(filas.Select(f => f.UsuarioId).Distinct().ToList(), ct);
        return new TiempoCapturaDto(
            filas.Count == 0 ? null : Mediana(filas.Select(f => f.Minutos)),
            filas.GroupBy(f => f.Proceso).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new TiempoProcesoDto(g.Key, Mediana(g.Select(f => f.Minutos)), g.Count()))
                .ToList(),
            filas.GroupBy(f => f.UsuarioId)
                .Select(g => new TiempoInspectorDto(g.Key, inspectores.GetValueOrDefault(g.Key)?.Nombre, Mediana(g.Select(f => f.Minutos)), g.Count()))
                .OrderByDescending(t => t.Inspecciones).ThenBy(t => t.Nombre, StringComparer.Ordinal)
                .ToList());
    }

    /// <summary>La mediana con dos decimales: el valor del medio o, con una cantidad par, el promedio de los dos del medio.</summary>
    internal static decimal Mediana(IEnumerable<decimal> valores)
    {
        var ordenados = valores.Order().ToList();
        var medio = ordenados.Count / 2;
        var mediana = ordenados.Count % 2 == 1 ? ordenados[medio] : (ordenados[medio - 1] + ordenados[medio]) / 2m;
        return Math.Round(mediana, 2, MidpointRounding.AwayFromZero);
    }

    // ------------------------------------------------------------------------------------------- averias

    public async Task<AveriasDto> AveriasAsync(FiltrosReporteRequest solicitud, CancellationToken ct = default)
    {
        var filtros = Parsear(solicitud);
        var despachadas = await consultas.UnidadesDespachadasAsync(filtros, ct);
        var conHallazgos = await consultas.UnidadesConHallazgosAsync(filtros, ct);

        // El universo de cada configuracion: las unidades despachadas y las que tienen hallazgos aunque no se hayan despachado (por ejemplo, la que no sale).
        var unidades = new Dictionary<(Guid Viaje, Guid Vehiculo), FilaUnidad>();
        foreach (var unidad in despachadas)
        {
            unidades[(unidad.ViajeId, unidad.VehiculoId)] = unidad;
        }

        foreach (var unidad in conHallazgos)
        {
            unidades[(unidad.ViajeId, unidad.VehiculoId)] = unidad;
        }

        var conductores = await consultas.UsuariosAsync(unidades.Values.Select(u => u.ConductorId).OfType<Guid>().Distinct().ToList(), ct);
        var tiposUnidad = await NombresAsync(clsNombresCatalogo.TIPOS_UNIDAD, ct);
        var cuentas = await consultas.ConteoDeHallazgosAsync(filtros, ct);

        List<TItem> Agrupar<TClave, TItem>(Func<FilaUnidad, TClave> clave, Func<TClave, int, int, int, TItem> crear, Func<TClave, string?> orden) =>
            unidades.Values.GroupBy(clave)
                .Select(g => (Clave: g.Key, Vines: g.Count(), ConNovedad: g.Count(u => u.Hallazgos > 0), Hallazgos: g.Sum(u => u.Hallazgos)))
                .OrderByDescending(x => x.Vines).ThenByDescending(x => x.Hallazgos).ThenBy(x => orden(x.Clave), StringComparer.Ordinal)
                .Select(x => crear(x.Clave, x.Vines, x.ConNovedad, x.Hallazgos))
                .ToList();

        return new AveriasDto(
            Agrupar(u => u.Trailer, (t, v, c, h) => new AveriaTrailerDto(t, v, c, h), t => t),
            Agrupar(u => u.Placa, (p, v, c, h) => new AveriaPlacaDto(p, v, c, h), p => p),
            Agrupar(
                u => u.ConductorId,
                (id, v, c, h) =>
                {
                    var usuario = id is { } conductor ? conductores.GetValueOrDefault(conductor) : null;
                    return new AveriaConductorDto(id, usuario?.Nombre, usuario?.Cedula, v, c, h);
                },
                id => id is { } conductor ? conductores.GetValueOrDefault(conductor)?.Nombre : null),
            Agrupar(
                u => u.TipoUnidadId,
                (id, v, c, h) => new AveriaUnidadDto(id, id is { } tipo ? tiposUnidad.GetValueOrDefault(tipo) : null, v, c, h),
                id => id is { } tipo ? tiposUnidad.GetValueOrDefault(tipo) : null),
            Agrupar(u => u.Marca, (m, v, c, h) => new AveriaMarcaDto(m, v, c, h), m => m),
            cuentas.GroupBy(c => c.Clase).Select(g => new ConteoClaseTableroDto(g.Key, g.Sum(c => c.Total))).OrderByDescending(c => c.Total).ThenBy(c => c.Clase, StringComparer.Ordinal).ToList(),
            cuentas.GroupBy(c => c.Nivel).Select(g => new ConteoNivelTableroDto(g.Key, g.Sum(c => c.Total))).OrderByDescending(c => c.Total).ThenBy(c => c.Nivel, StringComparer.Ordinal).ToList());
    }

    // ------------------------------------------------------------------------------------------ patrones

    public async Task<PatronesTableroDto> PatronesAsync(FiltrosReporteRequest solicitud, CancellationToken ct = default)
    {
        var filtros = Parsear(solicitud);
        var cantidad = await parametros.EnteroAsync(clsClavesParametros.UMBRAL_PATRON_CANTIDAD, UMBRAL_CANTIDAD_POR_DEFECTO, ct);
        var ventana = await parametros.EnteroAsync(clsClavesParametros.UMBRAL_PATRON_VENTANA_DIAS, UMBRAL_VENTANA_POR_DEFECTO, ct);
        var cuentas = await consultas.ConteoDeHallazgosAsync(filtros, ct);

        // La marca de umbral mira la ventana que termina en el ultimo dia del reporte, con los mismos filtros, como lo hacen las alertas.
        var enVentana = await consultas.ConteoDeHallazgosAsync(filtros with { Desde = filtros.Hasta.AddDays(-(Math.Max(ventana, 1) - 1)) }, ct);
        var ventanaPorProceso = enVentana.GroupBy(c => c.Proceso).ToDictionary(g => g.Key, g => g.Sum(c => c.Total));
        var ventanaPorRegion = enVentana.GroupBy(c => ClaveRegion(c)).ToDictionary(g => g.Key, g => g.Sum(c => c.Total));
        var ventanaPorRegionProceso = enVentana.GroupBy(c => (Region: ClaveRegion(c), c.Proceso)).ToDictionary(g => g.Key, g => g.Sum(c => c.Total));
        bool Supera(int total) => total >= cantidad;

        return new PatronesTableroDto(
            cuentas.Sum(c => c.Total),
            cuentas.GroupBy(c => c.Proceso).Select(g => (Proceso: g.Key, Total: g.Sum(c => c.Total)))
                .OrderByDescending(x => x.Total).ThenBy(x => x.Proceso, StringComparer.Ordinal)
                .Select(x => new PatronProcesoDto(x.Proceso, x.Total, Supera(ventanaPorProceso.GetValueOrDefault(x.Proceso))))
                .ToList(),
            cuentas.GroupBy(c => ClaveRegion(c))
                .Select(g => (Muestra: g.First(), Total: g.Sum(c => c.Total), Clave: g.Key))
                .OrderByDescending(x => x.Total).ThenBy(x => x.Muestra.ZonaNombre, StringComparer.Ordinal)
                .Select(x => new PatronRegionDto(x.Muestra.Numero, x.Muestra.Lado, x.Muestra.CuadranteNombre ?? x.Muestra.ZonaNombre, x.Muestra.ZonaNombre, x.Total, Supera(ventanaPorRegion.GetValueOrDefault(x.Clave))))
                .ToList(),
            cuentas.GroupBy(c => (Region: ClaveRegion(c), c.Proceso))
                .Select(g => (Muestra: g.First(), Total: g.Sum(c => c.Total), Clave: g.Key))
                .OrderByDescending(x => x.Total).ThenBy(x => x.Clave.Proceso, StringComparer.Ordinal).ThenBy(x => x.Muestra.ZonaNombre, StringComparer.Ordinal)
                .Select(x => new PatronRegionProcesoDto(
                    x.Muestra.Numero, x.Muestra.Lado, x.Muestra.CuadranteNombre ?? x.Muestra.ZonaNombre, x.Muestra.ZonaNombre, x.Clave.Proceso, x.Total, Supera(ventanaPorRegionProceso.GetValueOrDefault(x.Clave))))
                .ToList(),
            cuentas.GroupBy(c => c.Nivel).Select(g => new PatronNivelDto(g.Key, g.Sum(c => c.Total))).OrderByDescending(n => n.Total).ThenBy(n => n.Nivel, StringComparer.Ordinal).ToList(),
            cuentas.GroupBy(c => c.Clase).Select(g => new PatronClaseDto(g.Key, g.Sum(c => c.Total))).OrderByDescending(n => n.Total).ThenBy(n => n.Clase, StringComparer.Ordinal).ToList(),
            cuentas.GroupBy(c => c.Tamano).Select(g => new PatronTamanoDto(g.Key, g.Sum(c => c.Total))).OrderByDescending(n => n.Total).ThenBy(n => n.Tamano, StringComparer.Ordinal).ToList(),
            new UmbralPatronDto(cantidad, ventana));
    }

    /// <summary>La zona de un hallazgo: lado + numero del cuadrante, o el codigo de la zona provisional (sin zona, un grupo aparte).</summary>
    private static string ClaveRegion(FilaConteoHallazgo c) =>
        c.Numero is { } numero ? $"{c.Lado ?? "sin-lado"}-{numero.ToString(CultureInfo.InvariantCulture)}" : c.ZonaCodigo ?? "sin-zona";

    // ------------------------------------------------------------------------------------ indicador diario

    public async Task<IndicadorDiarioDto> IndicadorDiarioAsync(FiltrosReporteRequest solicitud, CancellationToken ct = default)
    {
        var filtros = Parsear(solicitud, indicadorDiario: true);
        var filas = await consultas.IndicadorDiarioAsync(filtros, ct);
        var clientes = await NombresAsync(clsNombresCatalogo.CLIENTES, ct);
        var objetivo = decimal.TryParse(await parametros.ValorAsync(clsClavesParametros.OBJETIVO_DIARIO_NOVEDAD, ct), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor) ? valor : OBJETIVO_POR_DEFECTO;
        var despachados = filas.Sum(f => f.Despachados);
        var conNovedad = filas.Sum(f => f.ConNovedad);
        return new IndicadorDiarioDto(
            filtros.Hasta,
            despachados,
            conNovedad,
            clsReglasReportes.Porcentaje(conNovedad, despachados),
            objetivo,
            filas.GroupBy(f => f.Operacion).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new IndicadorOperacionDto(g.Key, g.Sum(f => f.Despachados), g.Sum(f => f.ConNovedad), clsReglasReportes.Porcentaje(g.Sum(f => f.ConNovedad), g.Sum(f => f.Despachados))))
                .ToList(),
            filas.GroupBy(f => f.ClienteId).OrderBy(g => clientes.GetValueOrDefault(g.Key), StringComparer.Ordinal)
                .Select(g => new IndicadorClienteDto(g.Key, clientes.GetValueOrDefault(g.Key), g.Sum(f => f.Despachados), g.Sum(f => f.ConNovedad), clsReglasReportes.Porcentaje(g.Sum(f => f.ConNovedad), g.Sum(f => f.Despachados))))
                .ToList());
    }

    // ----------------------------------------------------------------------------------------------- filtros

    /// <summary>El dia de hoy en Colombia (UTC-5, sin horario de verano).</summary>
    private DateOnly Hoy() => DateOnly.FromDateTime(reloj.Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    private FiltrosReporte Parsear(FiltrosReporteRequest s, bool indicadorDiario = false)
    {
        var errores = new Dictionary<string, string[]>();
        var hasta = Dia(s.Hasta, "hasta", errores);
        var desde = Dia(s.Desde, "desde", errores);
        var fecha = Dia(s.Fecha, "fecha", errores);
        var operacion = clsParseoFiltros.Catalogo(s.Operacion, clsOperacion.VALORES, "operacion", errores);
        var proceso = clsParseoFiltros.Catalogo(s.Proceso, PROCESOS, "proceso", errores);
        var nivel = clsParseoFiltros.Catalogo(s.Nivel, NIVELES, "nivel", errores);
        var clienteElegido = clsParseoFiltros.Uuid(s.ClienteId, "cliente_id", errores);
        var punto = clsParseoFiltros.Uuid(s.PuntoId, "punto_id", errores);

        if (indicadorDiario)
        {
            // El indicador es de un solo dia: la fecha (o, si no llega, el ultimo dia pedido o hoy).
            hasta = fecha ?? hasta ?? Hoy();
            desde = hasta;
        }
        else
        {
            hasta ??= Hoy();
            desde ??= hasta.Value.AddDays(-(DIAS_POR_DEFECTO - 1));
            if (!errores.ContainsKey("desde") && !errores.ContainsKey("hasta"))
            {
                if (desde > hasta)
                {
                    errores["desde"] = ["desde no puede ser posterior a hasta"];
                }
                else if (hasta.Value.DayNumber - desde.Value.DayNumber + 1 > DIAS_MAXIMO)
                {
                    errores["desde"] = [$"El periodo no puede pasar de {DIAS_MAXIMO} dias"];
                }
            }
        }

        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        return new FiltrosReporte(
            desde!.Value,
            hasta!.Value,
            operacion,
            actual.ClienteDelFiltro(clienteElegido),
            punto,
            clsValidador.Opcional(s.Marca),
            clsValidador.Opcional(s.Trailer),
            proceso,
            nivel);
    }

    /// <summary>Un dia como AAAA-MM-DD (vacio = sin valor); otro formato agrega el error del campo.</summary>
    private static DateOnly? Dia(string? texto, string campo, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (DateOnly.TryParseExact(texto.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
        {
            return dia;
        }

        errores[campo] = [$"{campo} debe ser una fecha AAAA-MM-DD"];
        return null;
    }

    /// <summary>El nombre de cada valor de un catalogo, por su id.</summary>
    private async Task<Dictionary<Guid, string>> NombresAsync(string catalogo, CancellationToken ct) =>
        (await catalogos.ValoresAsync(catalogo, ct))
            .Where(v => v.TryGetValue("nombre", out var nombre) && nombre is string)
            .ToDictionary(v => v.Id, v => (string)v["nombre"]!);
}
