namespace IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

/// <summary>Posicion marcada sobre el diagrama del vehiculo, en coordenadas normalizadas [0, 1].</summary>
public sealed record CoordsDiagramaDto(double X, double Y);

public sealed record InspeccionDto(
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
    DateTimeOffset UpdatedAt);

public sealed record HallazgoDto(
    Guid Id,
    Guid InspeccionId,
    string ParteVehiculo,
    string ClaseDano,
    string TamanoDano,
    CoordsDiagramaDto? CoordsDiagrama,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>La ruta interna de almacenamiento no se expone: el cliente recibe solo la URL servible.</summary>
public sealed record EvidenciaDto(
    Guid Id,
    Guid InspeccionId,
    Guid? HallazgoId,
    DateTimeOffset SelloFecha,
    double? SelloLat,
    double? SelloLng,
    string? Url,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Las firmas dibujadas no se exponen en los listados: solo el indicador de transferencia firmada.</summary>
public sealed record TransferenciaDto(
    Guid Id,
    Guid InspeccionId,
    Guid EntregaUsuarioId,
    Guid RecibeUsuarioId,
    bool Firmada,
    DateTimeOffset? FirmadaAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
