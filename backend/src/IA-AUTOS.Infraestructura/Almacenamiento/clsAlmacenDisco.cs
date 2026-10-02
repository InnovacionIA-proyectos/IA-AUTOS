using IAAutos.Aplicacion.Archivos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IAAutos.Infraestructura.Almacenamiento;

/// <summary>Almacenamiento en disco (desarrollo local, o cuando no hay S3 configurado).</summary>
internal sealed class clsAlmacenDisco : IAlmacenArchivos
{
    private readonly string _carpeta;

    public clsAlmacenDisco(IOptions<clsOpcionesAlmacenamiento> opciones, IHostEnvironment entorno)
    {
        var ruta = opciones.Value.LocalPath;
        _carpeta = Path.IsPathRooted(ruta) ? ruta : Path.Combine(entorno.ContentRootPath, ruta);
    }

    public async Task GuardarAsync(string nombreArchivo, byte[] contenido, string tipoContenido, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_carpeta);
        await File.WriteAllBytesAsync(RutaSegura(nombreArchivo), contenido, ct);
    }

    public async Task<byte[]?> LeerAsync(string nombreArchivo, CancellationToken ct = default)
    {
        var ruta = RutaSegura(nombreArchivo);
        return File.Exists(ruta) ? await File.ReadAllBytesAsync(ruta, ct) : null;
    }

    /// <summary>Defensa en profundidad: aunque el servicio ya valida el nombre, aqui nunca se sale de la carpeta.</summary>
    private string RutaSegura(string nombreArchivo)
    {
        var ruta = Path.GetFullPath(Path.Combine(_carpeta, Path.GetFileName(nombreArchivo)));
        if (!ruta.StartsWith(Path.GetFullPath(_carpeta), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Nombre de archivo fuera del almacenamiento");
        }

        return ruta;
    }
}
