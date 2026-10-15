namespace IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

/// <summary>La inspeccion toma del viaje de la etapa la placa, el trailer y el conductor: no se piden como texto.</summary>
public sealed record CrearInspeccionRequest(
    Guid? EtapaId,
    string? Momento,
    DateTimeOffset? Fecha,
    double? Lat,
    double? Lng,
    string? Estado,
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
public sealed record FiltrosInspeccionesRequest(string? Vin, string? UsuarioId, string? ViajeId, string? Momento, string? Estado, string? Proceso, string? Desde, string? Hasta);

public sealed record FiltrosInspecciones(
    string? Vin,
    Guid? UsuarioId,
    Guid? ViajeId,
    string? Momento,
    string? Estado,
    string? Proceso,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta);

/// <summary>Fila del listado de inspecciones: los datos de la inspeccion mas el VIN y el proceso. La placa, el trailer y el conductor salen del viaje.</summary>
public sealed record InspeccionListadoDto(
    Guid Id,
    Guid EtapaId,
    Guid ViajeId,
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
/// Informe completo para revision: inspeccion con su contexto (VIN, proceso, lugar, inspector), hallazgos, evidencias con URL
/// y transferencia. La placa, el conductor y el trailer son los del viaje (en informes anteriores a los viajes, los capturados en la app).
/// </summary>
public sealed record InformeDetalleDto(
    Guid Id,
    Guid EtapaId,
    Guid ViajeId,
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
    string? Lugar,
    bool RequiereInspeccion,
    string? InspectorNombre,
    string? Ninera,
    string? CodigoMtrack,
    string? Unidad,
    IReadOnlyList<HallazgoDto> Hallazgos,
    IReadOnlyList<EvidenciaDto> Evidencias,
    TransferenciaDetalleDto? Transferencia);

public sealed record ResumenCierreDto(int Evidencias, int Hallazgos);

public sealed record CerrarInspeccionResponse(InspeccionDto Inspeccion, ResumenCierreDto Resumen);
