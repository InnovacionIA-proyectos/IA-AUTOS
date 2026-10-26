using IAAutos.Aplicacion.Reportes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>Lo que tienen en comun las filas de las vistas de indicadores y permite filtrarlas igual: el dia (de Colombia), el cliente del vehiculo, la operacion, el origen, el destino, la marca y el trailer.</summary>
internal interface IFilaIndicador
{
    DateOnly Fecha { get; }

    Guid ClienteId { get; }

    string Operacion { get; }

    Guid? OrigenId { get; }

    Guid? DestinoId { get; }

    string Marca { get; }

    string? Trailer { get; }
}

/// <summary>Una fila de vwCobertura: una unidad despachada con sus registros cerrados.</summary>
internal sealed class clsFilaCobertura : IFilaIndicador
{
    public Guid ViajeId { get; set; }

    public Guid VehiculoId { get; set; }

    public Guid ClienteId { get; set; }

    public string Operacion { get; set; } = string.Empty;

    public Guid? OrigenId { get; set; }

    public Guid? DestinoId { get; set; }

    public string Marca { get; set; } = string.Empty;

    public string? Placa { get; set; }

    public string? Trailer { get; set; }

    public Guid? TipoUnidadId { get; set; }

    public Guid? ConductorId { get; set; }

    public DateOnly Fecha { get; set; }

    public int RegistrosCerrados { get; set; }

    public bool Completa { get; set; }
}

/// <summary>Una fila de vwAdopcion: los VIN programados de un dia, cliente y configuracion, y cuantos tienen salida registrada.</summary>
internal sealed class clsFilaAdopcion : IFilaIndicador
{
    public DateOnly Fecha { get; set; }

    public Guid ClienteId { get; set; }

    public string Operacion { get; set; } = string.Empty;

    public Guid? OrigenId { get; set; }

    public Guid? DestinoId { get; set; }

    public string Marca { get; set; } = string.Empty;

    public string? Trailer { get; set; }

    public int Programados { get; set; }

    public int ConSalidaDigital { get; set; }
}

/// <summary>Una fila de vwTiempoCaptura: una inspeccion cerrada de Cargue o Descargue con sus minutos.</summary>
internal sealed class clsFilaTiempoCaptura : IFilaIndicador
{
    public Guid InspeccionId { get; set; }

    public Guid ClienteId { get; set; }

    public string Operacion { get; set; } = string.Empty;

    public Guid? OrigenId { get; set; }

    public Guid? DestinoId { get; set; }

    public string Marca { get; set; } = string.Empty;

    public string? Trailer { get; set; }

    public string Proceso { get; set; } = string.Empty;

    public Guid UsuarioId { get; set; }

    public DateOnly Fecha { get; set; }

    public decimal Minutos { get; set; }
}

/// <summary>Una fila de vwAveriasPorConfiguracion: un hallazgo revisado de una inspeccion cerrada con la configuracion del viaje y la zona.</summary>
internal sealed class clsFilaAveria : IFilaIndicador
{
    public Guid HallazgoId { get; set; }

    public Guid ViajeId { get; set; }

    public Guid VehiculoId { get; set; }

    public Guid ClienteId { get; set; }

    public string Operacion { get; set; } = string.Empty;

    public Guid? OrigenId { get; set; }

    public Guid? DestinoId { get; set; }

    public string Marca { get; set; } = string.Empty;

    public string? Placa { get; set; }

    public string? Trailer { get; set; }

    public Guid? TipoUnidadId { get; set; }

    public Guid? ConductorId { get; set; }

    public string Proceso { get; set; } = string.Empty;

    public string? ClaseDano { get; set; }

    public string TamanoDano { get; set; } = string.Empty;

    public string? NivelSeveridad { get; set; }

    public int? Numero { get; set; }

    public string? Lado { get; set; }

    public string? ZonaCodigo { get; set; }

    public string? ZonaNombre { get; set; }

    public string? CuadranteNombre { get; set; }

    public DateOnly Fecha { get; set; }
}

/// <summary>Una fila de vwIndicadorDiario: las unidades despachadas de un dia, cliente y configuracion, y cuantas tuvieron un hallazgo V1 ese dia.</summary>
internal sealed class clsFilaIndicadorDiario : IFilaIndicador
{
    public DateOnly Fecha { get; set; }

    public Guid ClienteId { get; set; }

    public string Operacion { get; set; } = string.Empty;

    public Guid? OrigenId { get; set; }

    public Guid? DestinoId { get; set; }

    public string Marca { get; set; } = string.Empty;

    public string? Trailer { get; set; }

    public int Despachados { get; set; }

    public int ConNovedad { get; set; }
}

internal sealed class clsConsultasIndicadores(clsAppDbContext contexto) : IConsultasIndicadores
{
    /// <summary>Aplica los filtros comunes: dias, operacion, cliente, punto (origen o destino), marca y trailer.</summary>
    private static IQueryable<T> Filtrar<T>(IQueryable<T> consulta, FiltrosReporte f)
        where T : class, IFilaIndicador
    {
        var desde = f.Desde;
        var hasta = f.Hasta;
        consulta = consulta.Where(x => x.Fecha >= desde && x.Fecha <= hasta);
        if (f.Operacion is { } operacion)
        {
            consulta = consulta.Where(x => x.Operacion == operacion);
        }

        if (f.ClienteId is { } cliente)
        {
            consulta = consulta.Where(x => x.ClienteId == cliente);
        }

        if (f.PuntoId is { } punto)
        {
            consulta = consulta.Where(x => x.OrigenId == punto || x.DestinoId == punto);
        }

        if (f.Marca is { } marca)
        {
            consulta = consulta.Where(x => x.Marca == marca);
        }

        if (f.Trailer is { } trailer)
        {
            consulta = consulta.Where(x => x.Trailer == trailer);
        }

        return consulta;
    }

    public async Task<IReadOnlyList<FilaCobertura>> CoberturaAsync(FiltrosReporte filtros, CancellationToken ct = default) =>
        (await Filtrar(contexto.Set<clsFilaCobertura>().AsNoTracking(), filtros)
            .GroupBy(x => new { x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId })
            .Select(g => new { g.Key.ClienteId, g.Key.Operacion, g.Key.OrigenId, g.Key.DestinoId, Despachadas = g.Count(), Completas = g.Count(x => x.Completa) })
            .ToListAsync(ct))
        .Select(x => new FilaCobertura(x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Despachadas, x.Completas))
        .ToList();

    public async Task<IReadOnlyList<FilaAdopcion>> AdopcionAsync(FiltrosReporte filtros, CancellationToken ct = default) =>
        (await Filtrar(contexto.Set<clsFilaAdopcion>().AsNoTracking(), filtros)
            .GroupBy(x => new { x.Fecha, x.ClienteId, x.Operacion })
            .Select(g => new { g.Key.Fecha, g.Key.ClienteId, g.Key.Operacion, Programados = g.Sum(x => x.Programados), ConSalidaDigital = g.Sum(x => x.ConSalidaDigital) })
            .ToListAsync(ct))
        .Select(x => new FilaAdopcion(x.Fecha, x.ClienteId, x.Operacion, x.Programados, x.ConSalidaDigital))
        .ToList();

    public async Task<IReadOnlyList<FilaTiempo>> TiemposDeCapturaAsync(FiltrosReporte filtros, CancellationToken ct = default)
    {
        var consulta = Filtrar(contexto.Set<clsFilaTiempoCaptura>().AsNoTracking(), filtros);
        if (filtros.Proceso is { } proceso)
        {
            consulta = consulta.Where(x => x.Proceso == proceso);
        }

        return (await consulta.Select(x => new { x.Proceso, x.UsuarioId, x.Minutos }).ToListAsync(ct))
            .Select(x => new FilaTiempo(x.Proceso, x.UsuarioId, x.Minutos))
            .ToList();
    }

    /// <summary>Las filas de averias con los filtros comunes, el proceso y el nivel.</summary>
    private IQueryable<clsFilaAveria> Averias(FiltrosReporte filtros)
    {
        var consulta = Filtrar(contexto.Set<clsFilaAveria>().AsNoTracking(), filtros);
        if (filtros.Proceso is { } proceso)
        {
            consulta = consulta.Where(x => x.Proceso == proceso);
        }

        if (filtros.Nivel is { } nivel)
        {
            consulta = consulta.Where(x => x.NivelSeveridad == nivel);
        }

        return consulta;
    }

    public async Task<IReadOnlyList<FilaNovedadProceso>> NovedadesDeProcesoAsync(FiltrosReporte filtros, CancellationToken ct = default)
    {
        // Los dias son de Colombia (UTC-5, sin horario de verano), con ambos extremos incluidos.
        var colombia = TimeSpan.FromHours(-5);
        var inicio = new DateTimeOffset(filtros.Desde.ToDateTime(TimeOnly.MinValue), colombia);
        var fin = new DateTimeOffset(filtros.Hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), colombia);
        var consulta = from n in contexto.NovedadesProceso.AsNoTracking()
                       join e in contexto.Etapas on n.EtapaId equals e.Id
                       join ec in contexto.EtapasCatalogo on e.EtapaCatalogoId equals ec.Id
                       join v in contexto.Vehiculos on n.VehiculoId equals v.Id
                       join vj in contexto.Viajes on n.ViajeId equals vj.Id
                       where n.Fecha >= inicio && n.Fecha < fin
                       select new { n.Tipo, n.Fecha, e.PuntoCustodiaId, v.ClienteId, v.Operacion, v.Marca, vj.Trailer, vj.OrigenId, vj.DestinoId, Proceso = ec.Codigo };
        if (filtros.Operacion is { } operacion)
        {
            consulta = consulta.Where(x => x.Operacion == operacion);
        }

        if (filtros.ClienteId is { } clienteId)
        {
            consulta = consulta.Where(x => x.ClienteId == clienteId);
        }

        if (filtros.PuntoId is { } puntoId)
        {
            consulta = consulta.Where(x => x.OrigenId == puntoId || x.DestinoId == puntoId);
        }

        if (filtros.Marca is { } marca)
        {
            consulta = consulta.Where(x => x.Marca == marca);
        }

        if (filtros.Trailer is { } trailer)
        {
            consulta = consulta.Where(x => x.Trailer == trailer);
        }

        if (filtros.Proceso is { } proceso)
        {
            consulta = consulta.Where(x => x.Proceso == proceso);
        }

        // Son pocas (una por cargue hecho sin inspector): el mes se arma en memoria con la hora de Colombia.
        return (await consulta.Select(x => new { x.Tipo, x.PuntoCustodiaId, x.ClienteId, x.Fecha }).ToListAsync(ct))
            .GroupBy(x => (x.Tipo, x.PuntoCustodiaId, x.ClienteId, Mes: x.Fecha.ToOffset(colombia).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)))
            .Select(g => new FilaNovedadProceso(g.Key.Tipo, g.Key.PuntoCustodiaId, g.Key.ClienteId, g.Key.Mes, g.Count()))
            .ToList();
    }

    public async Task<IReadOnlyList<FilaUnidad>> UnidadesDespachadasAsync(FiltrosReporte filtros, CancellationToken ct = default) =>
        (await Filtrar(contexto.Set<clsFilaCobertura>().AsNoTracking(), filtros)
            .Select(x => new { x.ViajeId, x.VehiculoId, x.Placa, x.Trailer, x.ConductorId, x.TipoUnidadId, x.Marca })
            .ToListAsync(ct))
        .Select(x => new FilaUnidad(x.ViajeId, x.VehiculoId, x.Placa, x.Trailer, x.ConductorId, x.TipoUnidadId, x.Marca, 0))
        .ToList();

    public async Task<IReadOnlyList<FilaUnidad>> UnidadesConHallazgosAsync(FiltrosReporte filtros, CancellationToken ct = default) =>
        (await Averias(filtros)
            .GroupBy(x => new { x.ViajeId, x.VehiculoId, x.Placa, x.Trailer, x.ConductorId, x.TipoUnidadId, x.Marca })
            .Select(g => new { g.Key.ViajeId, g.Key.VehiculoId, g.Key.Placa, g.Key.Trailer, g.Key.ConductorId, g.Key.TipoUnidadId, g.Key.Marca, Hallazgos = g.Count() })
            .ToListAsync(ct))
        .Select(x => new FilaUnidad(x.ViajeId, x.VehiculoId, x.Placa, x.Trailer, x.ConductorId, x.TipoUnidadId, x.Marca, x.Hallazgos))
        .ToList();

    public async Task<IReadOnlyList<FilaConteoHallazgo>> ConteoDeHallazgosAsync(FiltrosReporte filtros, CancellationToken ct = default) =>
        (await Averias(filtros)
            .GroupBy(x => new { x.Proceso, x.Numero, x.Lado, x.ZonaCodigo, x.ZonaNombre, x.CuadranteNombre, x.NivelSeveridad, x.ClaseDano, x.TamanoDano })
            .Select(g => new
            {
                g.Key.Proceso, g.Key.Numero, g.Key.Lado, g.Key.ZonaCodigo, g.Key.ZonaNombre, g.Key.CuadranteNombre, g.Key.NivelSeveridad, g.Key.ClaseDano, g.Key.TamanoDano, Total = g.Count(),
            })
            .ToListAsync(ct))
        .Select(x => new FilaConteoHallazgo(x.Proceso, x.Numero, x.Lado, x.ZonaCodigo, x.ZonaNombre, x.CuadranteNombre, x.NivelSeveridad, x.ClaseDano, x.TamanoDano, x.Total))
        .ToList();

    public async Task<IReadOnlyList<FilaIndicador>> IndicadorDiarioAsync(FiltrosReporte filtros, CancellationToken ct = default) =>
        (await Filtrar(contexto.Set<clsFilaIndicadorDiario>().AsNoTracking(), filtros)
            .GroupBy(x => new { x.ClienteId, x.Operacion })
            .Select(g => new { g.Key.ClienteId, g.Key.Operacion, Despachados = g.Sum(x => x.Despachados), ConNovedad = g.Sum(x => x.ConNovedad) })
            .ToListAsync(ct))
        .Select(x => new FilaIndicador(x.ClienteId, x.Operacion, x.Despachados, x.ConNovedad))
        .ToList();

    public async Task<IReadOnlyDictionary<Guid, UsuarioReporte>> UsuariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var resultado = new Dictionary<Guid, UsuarioReporte>();
        foreach (var lote in ids.Chunk(1000))
        {
            foreach (var u in await contexto.Usuarios.AsNoTracking().Where(u => lote.Contains(u.Id)).Select(u => new { u.Id, u.Nombre, u.Cedula }).ToListAsync(ct))
            {
                resultado[u.Id] = new UsuarioReporte(u.Id, u.Nombre, u.Cedula);
            }
        }

        return resultado;
    }
}
