using System.Security.Cryptography;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// Arma las evidencias (fotos de una vista, de un hallazgo o de una constancia de no firma) con el sello del servidor y el hash SHA-256 del
/// archivo tal como quedo guardado. El archivo debe haberse subido antes (POST /api/uploads); si el cliente envia su hash, debe coincidir con
/// el del archivo recibido.
/// </summary>
public sealed class clsFabricaEvidencias(IAlmacenArchivos almacen, IReloj reloj)
{
    public async Task<clsEvidencia> CrearAsync(
        Guid inspeccionId,
        Guid? hallazgoId,
        Guid? vistaId,
        bool esConstancia,
        string storagePath,
        DateTimeOffset selloFecha,
        double? lat,
        double? lng,
        double? precision,
        string? dispositivoId,
        string? nota,
        string? hashCliente,
        CancellationToken ct)
    {
        var contenido = await almacen.LeerAsync(storagePath.Trim(), ct)
            ?? throw clsErroresCampos.De("storage_path", "El archivo no se ha subido: suba la foto con POST /api/uploads antes de registrarla");
        var hash = Convert.ToHexString(SHA256.HashData(contenido)).ToLowerInvariant();
        if (clsValidador.Opcional(hashCliente) is { } declarado && !string.Equals(declarado, hash, StringComparison.OrdinalIgnoreCase))
        {
            throw clsErroresCampos.De("hash_sha256", "El hash no coincide con el del archivo recibido: vuelva a subir la foto");
        }

        return esConstancia
            ? clsEvidencia.CrearConstancia(inspeccionId, storagePath, selloFecha, lat, lng, precision, dispositivoId, hash, nota, reloj.Ahora)
            : clsEvidencia.Crear(inspeccionId, hallazgoId, vistaId, storagePath, selloFecha, lat, lng, precision, dispositivoId, hash, nota, reloj.Ahora);
    }
}
