using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Archivos.DTOArchivos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Archivos;

[Route("api/uploads")]
public sealed class clsArchivosController(clsArchivosService servicio, IFirmadorUrls firmador) : clsControladorBase
{
    /// <summary>
    /// Sube una foto como JSON (base64, con o sin prefijo data URI) o como multipart/form-data (campo <c>archivo</c>). Solo JPG y PNG; el
    /// tipo se verifica por contenido. Lo suben quien registra informes y quien da seguimiento a los hallazgos (la foto del sello de quien valida). Devuelve el nombre del archivo, su URL y el hash SHA-256 calculado por el servidor.
    /// </summary>
    [HttpPost]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN, clsRol.COORDINADOR_CALIDAD, clsRol.COMERCIAL, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<ArchivoSubidoDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Archivos.DTOArchivos.SubirArchivoRequest), TambienMultipart = true)]
    public async Task<IActionResult> Subir(CancellationToken ct)
    {
        if (!Request.HasFormContentType)
        {
            var solicitud = await LeerCuerpoAsync<SubirArchivoRequest>(ct);
            return Creado(await servicio.SubirAsync(solicitud, ct));
        }

        IFormCollection formulario;
        try
        {
            formulario = await Request.ReadFormAsync(ct);
        }
        catch (InvalidDataException)
        {
            throw new clsValidacionException("El cuerpo multipart no es válido");
        }

        var archivo = formulario.Files.GetFile("archivo") ?? formulario.Files.FirstOrDefault()
            ?? throw new clsValidacionException("Falta el archivo: envíelo en el campo «archivo»");
        servicio.AsegurarTamano(archivo.Length);
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria, ct);
        return Creado(await servicio.SubirBytesAsync(memoria.ToArray(), ct));
    }

    /// <summary>
    /// Sirve la imagen a quien tiene una URL firmada por el sistema y vigente (<c>?exp=&amp;firma=</c>): no hace falta el encabezado de autenticacion, asi que sirve en una etiqueta de imagen.
    /// Sin firma, con una firma que no es la del archivo o vencida responde 403 URL_FIRMADA_INVALIDA; con una firma valida de un archivo que no existe, 404.
    /// </summary>
    [HttpGet("{archivo}")]
    [AllowAnonymous]
    public async Task<IActionResult> Obtener(string archivo, [FromQuery] string? exp, [FromQuery] string? firma, CancellationToken ct)
    {
        if (!firmador.Validar(archivo, exp, firma))
        {
            throw new clsUrlFirmadaInvalidaException();
        }

        var leido = await servicio.LeerAsync(archivo, ct);
        // La foto puede guardarse en cache hasta que venza la URL (nunca mas de una hora), y solo en el navegador de quien la pidio.
        var restante = Math.Clamp(long.Parse(exp!, System.Globalization.CultureInfo.InvariantCulture) - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0, 3600);
        Response.Headers.CacheControl = $"private, max-age={restante}";
        return File(leido.Contenido, leido.TipoContenido);
    }
}
