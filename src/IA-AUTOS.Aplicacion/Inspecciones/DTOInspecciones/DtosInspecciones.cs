namespace IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

public sealed record CrearInspeccionRequest(
    Guid? EtapaId,
    string? Momento,
    DateTimeOffset? Fecha,
    double? Lat,
    double? Lng,
    string? Estado,
    string? ConductorNombre,
    string? Placa,
    string? Trailer,
    string? Notas);

public sealed record CrearHallazgoRequest(string? ParteVehiculo, string? ClaseDano, string? TamanoDano, CoordsDiagramaDto? CoordsDiagrama);

public sealed record CrearEvidenciaRequest(
    Guid? HallazgoId,
    string? StoragePath,
    DateTimeOffset? SelloFecha,
    double? SelloLat,
    double? SelloLng);

/// <summary>Firma parcial permitida: quien entrega y quien recibe pueden firmar por separado.</summary>
public sealed record FirmarTransferenciaRequest(Guid? EntregaUsuarioId, Guid? RecibeUsuarioId, string? FirmaEntrega, string? FirmaRecibe);

/// <summary>Filtros del listado tal como llegan en la consulta (texto); se validan en el servicio.</summary>
public sealed record FiltrosInspeccionesRequest(string? Vin, string? UsuarioId, string? Momento, string? Estado, string? Proceso, string? Desde, string? Hasta);

public sealed record FiltrosInspecciones(
    string? Vin,
    Guid? UsuarioId,
    string? Momento,
    string? Estado,
    string? Proceso,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta);

/// <summary>Fila del listado de inspecciones: los datos de la inspeccion mas el VIN y el proceso (punto de custodia).</summary>
public sealed record InspeccionListadoDto(
    Guid Id,
    Guid EtapaId,
    Guid UsuarioId,
    string Momento,
    DateTimeOffset Fecha,
    double? Lat,
    double? Lng,
    string Estado,
    string? ConductorNombre,
    string? Placa,
    string? Trailer,
    string? Notas,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Vin,
    string Proceso);

/// <summary>Transferencia con nombres y firmas dibujadas, para la revision del informe en la web.</summary>
public sealed record TransferenciaDetalleDto(
    Guid Id,
    Guid InspeccionId,
    Guid EntregaUsuarioId,
    Guid RecibeUsuarioId,
    bool Firmada,
    DateTimeOffset? FirmadaAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? EntregaNombre,
    string? RecibeNombre,
    string? FirmaEntrega,
    string? FirmaRecibe);

/// <summary>
/// Informe completo para revision: inspeccion con su contexto (VIN, proceso, inspector), hallazgos, evidencias con URL
/// y transferencia. La placa, el conductor y el trailer capturados en la app tienen prioridad sobre los de la asignacion.
/// </summary>
public sealed record InformeDetalleDto(
    Guid Id,
    Guid EtapaId,
    Guid UsuarioId,
    string Momento,
    DateTimeOffset Fecha,
    double? Lat,
    double? Lng,
    string Estado,
    string? ConductorNombre,
    string? Placa,
    string? Trailer,
    string? Notas,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Vin,
    string Marca,
    string? Modelo,
    string Proceso,
    bool RequiereInspeccion,
    string? InspectorNombre,
    string? Ninera,
    IReadOnlyList<HallazgoDto> Hallazgos,
    IReadOnlyList<EvidenciaDto> Evidencias,
    TransferenciaDetalleDto? Transferencia);

public sealed record ResumenCierreDto(int Evidencias, int Hallazgos);

public sealed record CerrarInspeccionResponse(InspeccionDto Inspeccion, ResumenCierreDto Resumen);
