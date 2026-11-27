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

/// <summary>Una firma del cargue de la ninera: quien firmo (<c>Nombre</c>), cuando llego al servidor (<c>FirmadaAt</c>) y la <c>Firma</c> (el trazo o la URL firmada de la imagen). Nunca trae la direccion de red ni ninguna cedula.</summary>
public sealed record FirmaDeLaNineraDto(string? Nombre, DateTimeOffset? FirmadaAt, string? Firma);

/// <summary>Las observaciones que quien recibe dejo sobre un carro al firmar el cargue «con reservas».</summary>
public sealed record ReservaDelCargueDto(string Vin, string Observaciones);

/// <summary>
/// La constancia de no firma del cargue de la ninera: <c>Parte</c> es la que se nego a firmar (<c>entrega</c> o <c>recibe</c>), con su <c>Motivo</c>, el nombre del testigo (nunca su cedula), la <c>Foto</c> de la constancia
/// (con latitud y longitud vacias si se tomo sin GPS), y el <c>Momento</c> y el GPS (<c>Lat</c>, <c>Lng</c>; vacios sin GPS) en que la otra parte la dejo.
/// </summary>
public sealed record ConstanciaDelCargueDto(string Parte, string? Motivo, string? TestigoNombre, FotoNovedadViajeDto? Foto, DateTimeOffset? Momento, double? Lat, double? Lng);

/// <summary>
/// El cargue de la ninera de un viaje como lo necesita el detalle: <c>Recibidos</c> son los carros que se recibieron con la firma de las dos partes, <c>Reservas</c> las observaciones de quien recibe por carro,
/// <c>Entrega</c> y <c>Recibe</c> las firmas, <c>Constancia</c> la de no firma (nula si no la hay) y <c>CarrosSinFirma</c> los carros cuyo cargue quedo con esa constancia. Solo existe cuando el cargue se firmo o tiene constancia.
/// </summary>
public sealed record CargueNineraDelViajeDto(
    int Recibidos,
    IReadOnlyList<ReservaDelCargueDto> Reservas,
    FirmaDeLaNineraDto? Entrega,
    FirmaDeLaNineraDto? Recibe,
    ConstanciaDelCargueDto? Constancia,
    int CarrosSinFirma);

