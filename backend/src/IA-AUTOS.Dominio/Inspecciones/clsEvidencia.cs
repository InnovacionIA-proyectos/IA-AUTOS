using System.Text.RegularExpressions;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>
/// Foto sellada. Es de una vista (una de las 7 obligatorias o de las 2 adicionales) o de un hallazgo, y lleva el sello del dispositivo (fecha,
/// ubicacion y precision), el del servidor (cuando la recibio) y el hash SHA-256 del archivo. Append-only por diseno.
/// </summary>
public sealed partial class clsEvidencia : clsEntidadBase
{
    public const int NOTA_MAX = 500;
    public const int DISPOSITIVO_MAX = 100;

    private clsEvidencia()
    {
    }

    public Guid InspeccionId { get; private set; }

    public Guid? HallazgoId { get; private set; }

    /// <summary>Vista de foto del catalogo (frontal, trasera, piezas en el baul...). Nula en las fotos de un hallazgo.</summary>
    public Guid? VistaId { get; private set; }

    public string StoragePath { get; private set; } = string.Empty;

    /// <summary>Fecha y hora de la foto segun el dispositivo.</summary>
    public DateTimeOffset SelloFecha { get; private set; }

    /// <summary>Ubicacion de la foto. (0, 0) significa que se tomo sin senal de GPS: la foto esta «sin ubicacion».</summary>
    public double SelloLat { get; private set; }

    public double SelloLng { get; private set; }

    /// <summary>Hora en que el servidor recibio la foto (no la puede fijar el cliente).</summary>
    public DateTimeOffset SelloServidorAt { get; private set; }

    /// <summary>Precision del GPS en metros, si el dispositivo la informa.</summary>
    public double? Precision { get; private set; }

    public string? DispositivoId { get; private set; }

    /// <summary>SHA-256 del archivo en minuscula hexadecimal (64 caracteres).</summary>
    public string? HashSha256 { get; private set; }

    /// <summary>El faltante o dano que muestran las fotos adicionales (piezas en el baul, cojineria).</summary>
    public string? Nota { get; private set; }

    public bool SinUbicacion => SelloLat == 0 && SelloLng == 0;

    public static clsEvidencia Crear(
        Guid inspeccionId,
        Guid? hallazgoId,
        Guid? vistaId,
        string storagePath,
        DateTimeOffset selloFecha,
        double? selloLat,
        double? selloLng,
        double? precision,
        string? dispositivoId,
        string? hashSha256,
        string? nota,
        DateTimeOffset ahora)
    {
        clsValidador.Latitud(selloLat, "sello_lat");
        clsValidador.Longitud(selloLng, "sello_lng");
        if (hallazgoId is null && vistaId is null)
        {
            throw clsErroresCampos.De("vista_id", "Indique la vista de la foto o el hallazgo al que pertenece");
        }

        if (precision is { } metros && (metros < 0 || !double.IsFinite(metros)))
        {
            throw clsErroresCampos.De("precision", "La precisión debe ser un número en metros mayor o igual a 0");
        }

        var dispositivo = clsValidador.Opcional(dispositivoId);
        if (dispositivo is { Length: > DISPOSITIVO_MAX })
        {
            throw clsErroresCampos.De("dispositivo_id", $"El dispositivo admite máximo {DISPOSITIVO_MAX} caracteres");
        }

        var notaLimpia = clsValidador.Opcional(nota);
        if (notaLimpia is { Length: > NOTA_MAX })
        {
            throw clsErroresCampos.De("nota", $"La nota admite máximo {NOTA_MAX} caracteres");
        }

        // Sin una ubicacion completa la foto queda en (0, 0): «sin ubicacion».
        var conUbicacion = selloLat is not null && selloLng is not null;
        var evidencia = new clsEvidencia
        {
            InspeccionId = inspeccionId,
            HallazgoId = hallazgoId,
            VistaId = vistaId,
            StoragePath = clsValidador.Requerido(storagePath, "storage_path es requerido"),
            SelloFecha = selloFecha,
            SelloLat = conUbicacion ? selloLat!.Value : 0,
            SelloLng = conUbicacion ? selloLng!.Value : 0,
            SelloServidorAt = ahora,
            Precision = precision,
            DispositivoId = dispositivo,
            HashSha256 = HashValido(hashSha256),
            Nota = notaLimpia,
        };
        evidencia.Inicializar(ahora);
        return evidencia;
    }

    /// <summary>Hash en 64 caracteres hexadecimales; se guarda en minuscula. Vacio = sin hash.</summary>
    public static string? HashValido(string? hash)
    {
        var limpio = clsValidador.Opcional(hash)?.ToLowerInvariant();
        return limpio is null || FormatoHash().IsMatch(limpio)
            ? limpio
            : throw clsErroresCampos.De("hash_sha256", "El hash SHA-256 debe tener 64 caracteres hexadecimales");
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex FormatoHash();
}
