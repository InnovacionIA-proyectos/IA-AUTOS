namespace IAAutos.Aplicacion.Viajes.DTOViajes;

/// <summary>Viaje nuevo: unidad transportadora, placa, trailer, conductor, codigo MTrack y, opcionalmente, sus VIN.</summary>
public sealed record CrearViajeRequest(
    string? CodigoMtrack,
    Guid? TipoUnidadId,
    string? Placa,
    string? Trailer,
    Guid? ConductorId,
    Guid? InspectorId,
    Guid? OrigenId,
    Guid? DestinoId,
    bool? ConPersonalApoyo,
    IReadOnlyList<string>? Vines);

/// <summary>
/// Campos que admite el cambio de un viaje. Solo se cambia lo enviado; trailer, conductor, inspector, origen y destino admiten
/// nulo para quitarlos. Este tipo describe el cuerpo en el contrato: el servicio lo lee campo a campo.
/// </summary>
public sealed record ActualizarViajeRequest(
    string? CodigoMtrack,
    Guid? TipoUnidadId,
    string? Placa,
    string? Trailer,
    Guid? ConductorId,
    Guid? InspectorId,
    Guid? OrigenId,
    Guid? DestinoId,
    bool? ConPersonalApoyo);

public sealed record AgregarVinesRequest(IReadOnlyList<string>? Vines);

public sealed record CambiarEstadoViajeRequest(string? Estado);

/// <summary>Un VIN del viaje con el avance de sus etapas dentro de el.</summary>
public sealed record ViajeVehiculoDto(
    Guid VehiculoId,
    string Vin,
    string Marca,
    string? Modelo,
    string? Color,
    string Operacion,
    int Etapas,
    int Completas);

/// <summary>Viaje completo con sus VIN. <c>ConPersonalApoyo</c> es la marca de la etapa de Cargue.</summary>
public sealed record ViajeDto(
    Guid Id,
    string? CodigoMtrack,
    Guid? TipoUnidadId,
    string? Unidad,
    string? Placa,
    string? Trailer,
    Guid? ConductorId,
    string? ConductorNombre,
    Guid? InspectorId,
    string? InspectorNombre,
    Guid? OrigenId,
    string? Origen,
    Guid? DestinoId,
    string? Destino,
    string Estado,
    bool ConPersonalApoyo,
    IReadOnlyList<ViajeVehiculoDto> Vehiculos,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Fila del listado de viajes: los datos del viaje y el conteo de sus VIN.</summary>
public sealed record ViajeResumenDto(
    Guid Id,
    string? CodigoMtrack,
    Guid? TipoUnidadId,
    string? Unidad,
    string? Placa,
    string? Trailer,
    Guid? ConductorId,
    string? ConductorNombre,
    Guid? InspectorId,
    string? InspectorNombre,
    Guid? OrigenId,
    string? Origen,
    Guid? DestinoId,
    string? Destino,
    string Estado,
    bool ConPersonalApoyo,
    int TotalVehiculos,
    int VehiculosCompletos,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Filtros del listado tal como llegan en la consulta (texto); se validan en el servicio.</summary>
public sealed record FiltrosViajesRequest(string? Estado, string? CodigoMtrack, string? Vin, string? ConductorId, string? Desde, string? Hasta);

public sealed record FiltrosViajes(string? Estado, string? CodigoMtrack, string? Vin, Guid? ConductorId, DateTimeOffset? Desde, DateTimeOffset? Hasta);

/// <summary>
/// Fila de la carga masiva de viajes (una por VIN). Las filas con el mismo codigo MTrack forman un viaje. <c>Fila</c> es el
/// numero de fila del archivo (para mostrar el error donde esta); si no llega se usa la posicion en la lista.
/// </summary>
public sealed record FilaCargaViajeRequest(
    int? Fila,
    string? Vin,
    string? Placa,
    string? Trailer,
    string? CedulaConductor,
    string? Unidad,
    string? CodigoMtrack);

public sealed record CargarViajesRequest(IReadOnlyList<FilaCargaViajeRequest>? Filas);

/// <summary>Resultado de una fila: valida o con sus errores; si el viaje se guardo, su id.</summary>
public sealed record FilaCargaViajeDto(int Fila, string? Vin, string? CodigoMtrack, bool Valida, IReadOnlyList<string> Errores, Guid? ViajeId);

/// <summary>
/// Vista previa o resultado de la carga. <c>Confirmado</c> es falso en la vista previa (no se guarda nada). <c>Viajes</c> es la
/// cantidad de viajes que se crearian (vista previa) o que se crearon (confirmacion).
/// </summary>
public sealed record ResultadoCargaViajesDto(
    bool Confirmado,
    int Total,
    int Validas,
    int Invalidas,
    int Viajes,
    IReadOnlyList<FilaCargaViajeDto> Filas);
