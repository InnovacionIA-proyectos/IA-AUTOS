using System.Text.RegularExpressions;
using FluentValidation;
using IAAutos.Aplicacion.Archivos.DTOArchivos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Archivos;

/// <summary>Almacenamiento de imagenes (S3 compatible o disco). El nombre del archivo lo decide la aplicacion.</summary>
public interface IAlmacenArchivos
{
    Task GuardarAsync(string nombreArchivo, byte[] contenido, string tipoContenido, CancellationToken ct = default);

    /// <summary>Devuelve el contenido o nulo si el archivo no existe.</summary>
    Task<byte[]?> LeerAsync(string nombreArchivo, CancellationToken ct = default);
}

public sealed class clsOpcionesAlmacenamiento
{
    public const string SECCION = "Storage";

    /// <summary>Tamano maximo de una imagen subida, en bytes (por defecto 10 MB).</summary>
    public int MaximoBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Si es true (como en el sistema anterior) las imagenes se leen sin token; el nombre es un UUID no adivinable.</summary>
    public bool LecturaPublica { get; set; } = true;

    /// <summary>Carpeta del almacenamiento en disco (solo si no hay S3 configurado).</summary>
    public string LocalPath { get; set; } = ".uploads";
}

public sealed class SubirArchivoRequestValidator : AbstractValidator<SubirArchivoRequest>
{
    public SubirArchivoRequestValidator()
    {
        RuleFor(x => x.Data).Must(d => !string.IsNullOrWhiteSpace(d)).WithMessage("data (base64) requerido");
        RuleFor(x => x.Ext).Must(e => e is null || clsArchivosService.EXTENSIONES_ACEPTADAS.Contains(e.ToLowerInvariant()))
            .WithMessage("Extensión no soportada (jpg, jpeg o png)");
    }
}

/// <summary>
/// Subida y lectura de fotos de evidencia. El tipo real se determina por el contenido (firma binaria JPEG o PNG),
/// no por lo que declare el cliente, y se aplica un tamano maximo.
/// </summary>
public sealed partial class clsArchivosService(
    IAlmacenArchivos almacen,
    IValidator<SubirArchivoRequest> validador,
    IOptions<clsOpcionesAlmacenamiento> opciones)
{
    public static readonly IReadOnlySet<string> EXTENSIONES_ACEPTADAS = new HashSet<string> { "jpg", "jpeg", "png" };

    public async Task<ArchivoSubidoDto> SubirAsync(SubirArchivoRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);

        var base64 = PrefijoDataUri().Replace(solicitud.Data!.Trim(), string.Empty);
        var maximo = opciones.Value.MaximoBytes;
        // Un base64 de N bytes ocupa ~4N/3 caracteres: se corta antes de reservar memoria.
        if (base64.Length > (maximo / 3 * 4) + 8)
        {
            throw TamanoExcedido(maximo);
        }

        var contenido = new byte[(base64.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(base64, contenido, out var escritos))
        {
            throw new clsValidacionException("La imagen no es un base64 válido");
        }

        if (escritos == 0)
        {
            throw new clsValidacionException("La imagen está vacía");
        }

        if (escritos > maximo)
        {
            throw TamanoExcedido(maximo);
        }

        var bytes = contenido.AsSpan(0, escritos).ToArray();
        var (extension, tipoContenido) = DetectarTipo(bytes)
            ?? throw new clsValidacionException("El contenido no es una imagen JPG o PNG válida");

        var nombre = $"{Guid.NewGuid()}.{extension}";
        await almacen.GuardarAsync(nombre, bytes, tipoContenido, ct);
        return new ArchivoSubidoDto(nombre, clsUrlsArchivos.RUTA_BASE + nombre);
    }

    public async Task<ArchivoLeidoDto> LeerAsync(string archivo, CancellationToken ct = default)
    {
        // Evita recorridos de ruta: solo nombre uuid + extension permitida.
        if (!NombreValido().IsMatch(archivo))
        {
            throw new clsEntidadNoEncontradaException("Archivo inválido");
        }

        var contenido = await almacen.LeerAsync(archivo, ct)
            ?? throw new clsEntidadNoEncontradaException("Archivo no encontrado");
        var tipo = archivo.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return new ArchivoLeidoDto(contenido, tipo);
    }

    private static (string Extension, string TipoContenido)? DetectarTipo(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
        {
            return ("jpg", "image/jpeg");
        }

        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
        {
            return ("png", "image/png");
        }

        return null;
    }

    private static clsValidacionException TamanoExcedido(int maximo) =>
        new($"La imagen excede el tamaño máximo permitido ({maximo / (1024 * 1024)} MB)");

    [GeneratedRegex(@"^data:[^;]+;base64,", RegexOptions.IgnoreCase)]
    private static partial Regex PrefijoDataUri();

    [GeneratedRegex(@"^[a-f0-9-]+\.(jpg|jpeg|png)$", RegexOptions.IgnoreCase)]
    private static partial Regex NombreValido();
}
