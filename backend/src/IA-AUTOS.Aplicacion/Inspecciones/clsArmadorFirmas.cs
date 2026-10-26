using System.Text.RegularExpressions;
using IAAutos.Aplicacion.Archivos;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Arma la firma de un PDF a partir de lo guardado en la transferencia: el nombre de un archivo subido (se incrusta como imagen) o un trazo SVG (se dibuja).</summary>
internal static partial class clsArmadorFirmas
{
    public static async Task<FirmaConstancia> ArmarAsync(IAlmacenArchivos almacen, string persona, string? firma, DateTimeOffset? fecha, string? ip, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(firma))
        {
            return new FirmaConstancia(persona, fecha, ip, null, null);
        }

        if (ArchivoSubido().IsMatch(firma))
        {
            var imagen = await almacen.LeerAsync(firma, ct);
            return new FirmaConstancia(persona, fecha, ip, imagen is null ? firma : null, imagen);
        }

        return new FirmaConstancia(persona, fecha, ip, firma, null);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,200}\.(jpg|jpeg|png)$", RegexOptions.IgnoreCase)]
    private static partial Regex ArchivoSubido();
}
