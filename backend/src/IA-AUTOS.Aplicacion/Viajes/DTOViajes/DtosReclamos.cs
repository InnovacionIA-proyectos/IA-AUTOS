namespace IAAutos.Aplicacion.Viajes.DTOViajes;

/// <summary>Reclamar la revision de un viaje: <c>proceso</c> es <c>cargue</c> o <c>entrega</c> (la etapa Descargue).</summary>
public sealed record ReclamarViajeRequest(string? Proceso);

/// <summary>
/// El reclamo de un camion: quien lo tomo y cuando y, si ya se libero, quien y cuando. <c>Activo</c> es verdadero mientras no se libere; un reclamo no caduca solo.
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
    string? LiberadoPorNombre);
