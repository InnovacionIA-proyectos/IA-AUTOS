namespace IAAutos.Aplicacion.Archivos;

/// <summary>Una URL absoluta firmada de una foto y cuando vence.</summary>
public sealed record clsUrlFirmada(string Url, DateTimeOffset ExpiraAt);

/// <summary>
/// Firma y valida las URL de las fotos. Una foto solo se ve con una URL firmada por el servidor y vigente (<c>url_firmada_minutos</c>): no hace falta el encabezado de autenticacion, asi que
/// sirve en una etiqueta de imagen, pero un enlace sin firma, con otra firma o vencido no muestra nada.
/// </summary>
public interface IFirmadorUrls
{
    /// <summary>
    /// La URL firmada del archivo (<paramref name="baseUrl"/> + ruta + exp + firma) con la vigencia indicada; nula si el archivo no es uno de los que sirve la API (por ejemplo, una ruta con carpetas
    /// de una integracion anterior).
    /// </summary>
    clsUrlFirmada? Firmar(string? rutaArchivo, string baseUrl, int vigenciaMinutos);

    /// <summary>True si la firma es la del archivo y el vencimiento (<paramref name="exp"/>, segundos desde 1970) no ha pasado.</summary>
    bool Validar(string archivo, string? exp, string? firma);
}
