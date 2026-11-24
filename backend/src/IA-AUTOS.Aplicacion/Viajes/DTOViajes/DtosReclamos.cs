namespace IAAutos.Aplicacion.Viajes.DTOViajes;

/// <summary>
/// Reclamar la revision de un viaje: <c>proceso</c> es <c>cargue</c> o <c>entrega</c> (la etapa Descargue). La entrega se reclama por PARADA: <c>lugar_id</c> es el lugar de destino y es obligatorio con <c>entrega</c>;
/// el cargue no lleva parada.
/// </summary>
public sealed record ReclamarViajeRequest(string? Proceso, Guid? LugarId = null);

/// <summary>
/// El reclamo de un camion: quien lo tomo y cuando y, si ya se libero, quien y cuando. <c>Activo</c> es verdadero mientras no se libere; un reclamo no caduca solo. <c>LugarId</c> y <c>Lugar</c> son la parada de un
/// reclamo de entrega y son nulos en el del cargue.
/// </summary>
public sealed record ReclamoDto(
    Guid Id,
    Guid ViajeId,
    string Proceso,
    Guid InspectorId,
    string? InspectorNombre,
    DateTimeOffset ReclamadoAt,
    bool Activo,
    DateTimeOffset? LiberadoAt,
    Guid? LiberadoPor,
    string? LiberadoPorNombre,
    Guid? LugarId = null,
    string? Lugar = null);
