namespace IAAutos.Aplicacion.Archivos.DTOArchivos;

public sealed record SubirArchivoRequest(string? Data, string? Ext);

/// <summary>
/// El archivo guardado: su nombre, su URL firmada (absoluta, con su vencimiento <c>UrlExpiraAt</c>) y el hash SHA-256 que calculo el servidor al recibirlo (el mismo que queda en la evidencia).
/// La URL y su vencimiento los firma la API al responder.
/// </summary>
public sealed record ArchivoSubidoDto(string StoragePath, string Url, DateTimeOffset? UrlExpiraAt, string HashSha256);

public sealed record ArchivoLeidoDto(byte[] Contenido, string TipoContenido);
