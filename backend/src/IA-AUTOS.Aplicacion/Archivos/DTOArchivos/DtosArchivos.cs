namespace IAAutos.Aplicacion.Archivos.DTOArchivos;

public sealed record SubirArchivoRequest(string? Data, string? Ext);

/// <summary>El archivo guardado: su nombre, su URL y el hash SHA-256 que calculo el servidor al recibirlo (el mismo que queda en la evidencia).</summary>
public sealed record ArchivoSubidoDto(string StoragePath, string Url, string HashSha256);

public sealed record ArchivoLeidoDto(byte[] Contenido, string TipoContenido);
