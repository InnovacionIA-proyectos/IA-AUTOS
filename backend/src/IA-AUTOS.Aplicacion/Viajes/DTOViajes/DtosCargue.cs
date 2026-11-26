using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

namespace IAAutos.Aplicacion.Viajes.DTOViajes;

/// <summary>La reserva de quien recibe sobre UN carro de la niñera: el VIN y sus observaciones (de 1 a 1 000 caracteres).</summary>
public sealed record ReservaDelCargueRequest(string? Vin, string? Observaciones);

/// <summary>
/// La firma del cargue por niñera: el trazo (<c>Firma</c>), la parte que firma (<c>Parte</c>: <c>entrega</c> para el inspector del viaje, <c>recibe</c> para el conductor), la hora del momento en que se firmo
/// (<c>FechaMomento</c>, ISO 8601 con zona: una firma hecha sin senal llega despues), el GPS del momento (<c>Lat</c>, <c>Lng</c> y, si se conoce, <c>PrecisionM</c>) y, solo quien recibe, las <c>Reservas</c> por carro.
/// </summary>
public sealed record FirmarCargueRequest(string? Firma, string? Parte, string? FechaMomento, double? Lat, double? Lng, double? PrecisionM, IReadOnlyList<ReservaDelCargueRequest>? Reservas);

/// <summary>
/// La constancia de no firma de toda la niñera: <c>Parte</c> es la que se nego a firmar (la deja la otra), con el <c>Motivo</c>, un testigo con usuario, la foto de la constancia (<c>Evidencia</c>) y la hora y el GPS del
/// momento.
/// </summary>
public sealed record NoFirmaDelCargueRequest(
    string? Parte,
    string? Motivo,
    Guid? TestigoUsuarioId,
    FotoEvidenciaRequest? Evidencia,
    string? FechaMomento,
    double? Lat,
    double? Lng,
    double? PrecisionM);

/// <summary>El cargue de un carro: <c>TipoCierre</c> es <c>abierta</c> mientras falta alguna firma, o <c>firmada</c>, <c>con_reservas</c>, <c>no_firma</c> o <c>recibo_sin_entrega</c>; <c>PorNinera</c> dice si se firmo por niñera.</summary>
public sealed record CargueUnidadDto(string Vin, string TipoCierre, bool PorNinera);

/// <summary>
/// El resultado de firmar el cargue de la niñera o de dejar su constancia: <c>Parte</c> es la del usuario que actuo, <c>Unidades</c> los carros activos del viaje y <c>Cerradas</c> cuantos ya tienen su entrega del cargue
/// cerrada. <c>CargueFirmadoPorNineraAt</c> es cuando se cerro la firma por niñera (nulo mientras falte la otra parte).
/// </summary>
public sealed record CargueNineraDto(Guid ViajeId, string Parte, int Unidades, int Cerradas, DateTimeOffset? CargueFirmadoPorNineraAt, IReadOnlyList<CargueUnidadDto> Detalle);
