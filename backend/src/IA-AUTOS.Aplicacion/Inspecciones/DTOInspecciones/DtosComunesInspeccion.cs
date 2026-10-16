namespace IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

/// <summary>Posicion marcada sobre el diagrama del vehiculo, en coordenadas normalizadas [0, 1].</summary>
public sealed record CoordsDiagramaDto(double X, double Y);

/// <summary>
/// Una inspeccion de salida o de llegada de una etapa. <c>Version</c> es 1 en la original y 2, 3... en las correcciones, que referencian
/// en <c>CorrigeInspeccionId</c> a la version anterior. <c>MomentoNombre</c> es el texto para pantalla («Inspección de salida»).
/// </summary>
public sealed record InspeccionDto(
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
    DateTimeOffset IniciadaAt,
    DateTimeOffset? CerradaAt,
    int Version,
    Guid? CorrigeInspeccionId,
    string MomentoNombre);

public sealed record HallazgoDto(
    Guid Id,
    Guid InspeccionId,
    string ParteVehiculo,
    string ClaseDano,
    string TamanoDano,
    CoordsDiagramaDto? CoordsDiagrama,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>El hallazgo recien guardado junto con la foto propia que se guardo con el.</summary>
public sealed record HallazgoCreadoDto(
    Guid Id,
    Guid InspeccionId,
    string ParteVehiculo,
    string ClaseDano,
    string TamanoDano,
    CoordsDiagramaDto? CoordsDiagrama,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    EvidenciaDto Evidencia);

/// <summary>
/// La ruta interna de almacenamiento no se expone: el cliente recibe solo la URL servible. Cada foto trae su propio sello (fecha, ubicacion y
/// precision del dispositivo), el del servidor (<c>SelloServidorAt</c>) y el hash SHA-256 del archivo. <c>SinUbicacion</c> marca la foto
/// tomada sin senal de GPS (hay que repetirla antes de cerrar).
/// </summary>
public sealed record EvidenciaDto(
    Guid Id,
    Guid InspeccionId,
    Guid? HallazgoId,
    DateTimeOffset SelloFecha,
    double SelloLat,
    double SelloLng,
    string? Url,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? VistaId,
    string? HashSha256,
    DateTimeOffset SelloServidorAt,
    double? Precision,
    string? DispositivoId,
    string? Nota,
    bool SinUbicacion);

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
