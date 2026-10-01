namespace IAAutos.Aplicacion.Archivos.DTOArchivos;

public sealed record SubirArchivoRequest(string? Data, string? Ext);

public sealed record ArchivoSubidoDto(string StoragePath, string Url);

public sealed record ArchivoLeidoDto(byte[] Contenido, string TipoContenido);
