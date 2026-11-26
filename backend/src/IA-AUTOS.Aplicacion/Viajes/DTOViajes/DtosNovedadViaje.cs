using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

namespace IAAutos.Aplicacion.Viajes.DTOViajes;

/// <summary>
/// Lo que reporta el conductor o un inspector sobre el viaje (no sobre un carro): <c>tipo</c> (codigo activo del catalogo <c>tipos-novedad-viaje</c> para su rol), <c>texto</c> (1 a 300 caracteres), <c>fecha</c> del celular,
/// <c>lat</c> y <c>lng</c> del celular (opcionales, juntas: sin GPS la novedad se acepta y queda «sin ubicacion»), la <c>evidencia</c> opcional (la misma foto sellada de la novedad de un carro) y <c>id_envio</c> (el UUID que el
/// celular le pone al envio: un reenvio no duplica la novedad). Solo el inspector indica <c>proceso</c> (<c>cargue</c> o <c>entrega</c>) y, en la entrega, la parada con <c>lugar_id</c>.
/// </summary>
public sealed record RegistrarNovedadViajeRequest(
    string? Tipo,
    string? Texto,
    DateTimeOffset? Fecha,
    double? Lat,
    double? Lng,
    FotoEvidenciaRequest? Evidencia,
    Guid? IdEnvio,
    string? Proceso,
    Guid? LugarId);

/// <summary>
/// La foto de una novedad del viaje con el sello del celular. <c>Url</c> es la URL firmada de lectura (la API la reemplaza al responder, a partir de <c>RutaArchivo</c>, que no se expone); <c>SinUbicacion</c> marca la foto
/// tomada sin senal de GPS.
/// </summary>
public sealed record FotoNovedadViajeDto(
    DateTimeOffset SelloFecha,
    double SelloLat,
    double SelloLng,
    string? Url,
    DateTimeOffset? UrlExpiraAt,
    string? HashSha256,
    double? Precision,
    string? DispositivoId,
    bool SinUbicacion,
    [property: System.Text.Json.Serialization.JsonIgnore] string? RutaArchivo = null);

/// <summary>
/// Una novedad del viaje tal como se lee: su tipo (codigo, nombre del catalogo y la urgencia de su aviso), el rol de quien la reporto, el proceso y la parada (solo si la reporto un inspector), el texto, quien la reporto, la
/// fecha y el GPS del celular, <c>SinUbicacion</c> (sin GPS) y su foto (nula si no la trae). <c>CreatedAt</c> es la hora en que llego al servidor. No es de un carro: no cuenta como novedad ni como dano de ninguna unidad.
/// </summary>
public sealed record NovedadViajeDto(
    Guid Id,
    string Tipo,
    string TipoNombre,
    string Urgencia,
    string Rol,
    string? Proceso,
    Guid? LugarId,
    string? Lugar,
    string Texto,
    string? UsuarioNombre,
    DateTimeOffset Fecha,
    double? Lat,
    double? Lng,
    bool SinUbicacion,
    FotoNovedadViajeDto? Foto,
    DateTimeOffset CreatedAt);

/// <summary>Lectura de las novedades del viaje con el nombre y la urgencia de su tipo, quien las reporto, la parada y la URL de su foto.</summary>
public interface IConsultasNovedadesViaje
{
    /// <summary>Las novedades de cada viaje, de la mas antigua a la mas reciente (por la fecha del celular).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<NovedadViajeDto>>> DeViajesAsync(IReadOnlyCollection<Guid> viajeIds, CancellationToken ct = default);

    /// <summary>Una novedad ya guardada, o nula.</summary>
    Task<NovedadViajeDto?> ObtenerAsync(Guid id, CancellationToken ct = default);
}
