namespace IAAutos.Aplicacion.Reportes;

/// <summary>Los filtros de un reporte ya validados. Los dias son dias calendario de Colombia, con ambos extremos incluidos.</summary>
public sealed record FiltrosReporte(
    DateOnly Desde,
    DateOnly Hasta,
    string? Operacion,
    Guid? ClienteId,
    Guid? PuntoId,
    string? Marca,
    string? Trailer,
    string? Proceso,
    string? Nivel);

/// <summary>Filtros de un reporte tal como llegan en la consulta (texto); el servicio los valida y responde todos los errores juntos.</summary>
public sealed record FiltrosReporteRequest(
    string? Desde,
    string? Hasta,
    string? Operacion,
    string? ClienteId,
    string? PuntoId,
    string? Marca,
    string? Trailer,
    string? Proceso,
    string? Nivel,
    string? Fecha = null);

/// <summary>Unidades despachadas de un cliente, operacion, origen y destino, y cuantas tienen sus seis registros cerrados.</summary>
public sealed record FilaCobertura(Guid ClienteId, string Operacion, Guid? OrigenId, Guid? DestinoId, int Despachadas, int Completas);

public sealed record FilaAdopcion(DateOnly Fecha, Guid ClienteId, string Operacion, int Programados, int ConSalidaDigital);

public sealed record FilaTiempo(string Proceso, Guid UsuarioId, decimal Minutos);

/// <summary>Una unidad de un viaje con la configuracion con la que viajo y sus hallazgos (cero si solo se cuenta como despachada).</summary>
public sealed record FilaUnidad(
    Guid ViajeId,
    Guid VehiculoId,
    string? Placa,
    string? Trailer,
    Guid? ConductorId,
    Guid? TipoUnidadId,
    string Marca,
    int Hallazgos);

/// <summary>Los hallazgos de un grupo (proceso, zona, nivel, clase y tamano).</summary>
public sealed record FilaConteoHallazgo(
    string Proceso,
    int? Numero,
    string? Lado,
    string? ZonaCodigo,
    string? ZonaNombre,
    string? CuadranteNombre,
    string? Nivel,
    string? Clase,
    string Tamano,
    int Total);

public sealed record FilaIndicador(Guid ClienteId, string Operacion, int Despachados, int ConNovedad);

public sealed record UsuarioReporte(Guid Id, string Nombre, string? Cedula);

/// <summary>
/// Lectura de los indicadores del tablero sobre las vistas vwCobertura, vwAdopcion, vwTiempoCaptura, vwAveriasPorConfiguracion y vwIndicadorDiario. Todas aplican los filtros del reporte
/// (dias, operacion, cliente, punto de origen o destino, marca y trailer); el proceso y el nivel solo a lo que tiene hallazgos o inspecciones por proceso.
/// </summary>
public interface IConsultasIndicadores
{
    Task<IReadOnlyList<FilaCobertura>> CoberturaAsync(FiltrosReporte filtros, CancellationToken ct = default);

    Task<IReadOnlyList<FilaAdopcion>> AdopcionAsync(FiltrosReporte filtros, CancellationToken ct = default);

    Task<IReadOnlyList<FilaTiempo>> TiemposDeCapturaAsync(FiltrosReporte filtros, CancellationToken ct = default);

    /// <summary>Las unidades despachadas del periodo (sin hallazgos contados: <c>Hallazgos</c> = 0).</summary>
    Task<IReadOnlyList<FilaUnidad>> UnidadesDespachadasAsync(FiltrosReporte filtros, CancellationToken ct = default);

    /// <summary>Las unidades con hallazgos en el periodo (de inspecciones cerradas, aunque la unidad no se haya despachado), con la cantidad de sus hallazgos.</summary>
    Task<IReadOnlyList<FilaUnidad>> UnidadesConHallazgosAsync(FiltrosReporte filtros, CancellationToken ct = default);

    Task<IReadOnlyList<FilaConteoHallazgo>> ConteoDeHallazgosAsync(FiltrosReporte filtros, CancellationToken ct = default);

    Task<IReadOnlyList<FilaIndicador>> IndicadorDiarioAsync(FiltrosReporte filtros, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, UsuarioReporte>> UsuariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}
