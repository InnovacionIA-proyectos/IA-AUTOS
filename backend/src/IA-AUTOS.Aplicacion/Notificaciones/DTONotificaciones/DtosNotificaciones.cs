namespace IAAutos.Aplicacion.Notificaciones.DTONotificaciones;

/// <summary>La respuesta del coordinador a una novedad del conductor: el texto, el medio (app o llamada), quien respondio y cuando.</summary>
public sealed record RespuestaNotificacionDto(string Texto, string Medio, string? UsuarioNombre, DateTimeOffset Fecha);

/// <summary>
/// Una notificacion de la bandeja. <c>Respuesta</c> es nula mientras nadie responde la novedad. El viaje, su codigo MTrack y el VIN son los del hecho que se avisa
/// (nulos en lo que no es de un viaje).
/// </summary>
public sealed record NotificacionDto(
    Guid Id,
    string Tipo,
    string Urgencia,
    string Titulo,
    string Mensaje,
    Guid? ViajeId,
    string? CodigoMtrack,
    string? Vin,
    Guid? HallazgoId,
    Guid? InspeccionId,
    DateTimeOffset? LeidaAt,
    RespuestaNotificacionDto? Respuesta,
    DateTimeOffset CreatedAt);

/// <summary>Una pagina de la bandeja con el total de la busqueda y cuantas notificaciones sin leer hay en total (sin contar los filtros).</summary>
public sealed record ListadoNotificacionesDto(IReadOnlyList<NotificacionDto> Items, int Total, int Pagina, int Tamano, int NoLeidas);

/// <summary>Filtros de la bandeja tal como llegan en la consulta (texto); se validan en el servicio.</summary>
public sealed record FiltrosNotificacionesRequest(string? Leida, string? Tipo, string? Urgencia, int? Pagina, int? Tamano);

public sealed record FiltrosNotificaciones(bool? Leida, string? Tipo, string? Urgencia);

/// <summary>El coordinador responde la novedad del conductor: el texto (maximo 1000 caracteres) y el medio, <c>app</c> o <c>llamada</c>.</summary>
public sealed record ResponderNotificacionRequest(string? Texto, string? Medio);

/// <summary>El celular donde el usuario recibe las notificaciones push: su token de FCM, la plataforma (<c>android</c>, <c>ios</c> o <c>web</c>) y el modelo.</summary>
public sealed record RegistrarDispositivoRequest(string? TokenFcm, string? Plataforma, string? Modelo);
