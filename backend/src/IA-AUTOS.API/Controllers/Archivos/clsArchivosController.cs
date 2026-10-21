using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Archivos.DTOArchivos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IAAutos.API.Controllers.Archivos;

[Route("api/uploads")]
public sealed class clsArchivosController(clsArchivosService servicio, IOptions<clsOpcionesAlmacenamiento> opciones) : clsControladorBase
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
    /// Sirve la imagen. Como en el sistema anterior es de lectura publica (las etiquetas de imagen no envian token y el
    /// nombre es un UUID no adivinable); con <c>Storage:LecturaPublica=false</c> exige autenticacion.
    /// </summary>
    [HttpGet("{archivo}")]
    [AllowAnonymous]
    public async Task<IActionResult> Obtener(string archivo, CancellationToken ct)
    {
        if (!opciones.Value.LecturaPublica && User.Identity?.IsAuthenticated != true)
        {
            throw new clsNoAutenticadoException("Falta el token de autenticación (Bearer)");
        }

        var leido = await servicio.LeerAsync(archivo, ct);
        Response.Headers.CacheControl = opciones.Value.LecturaPublica ? "public, max-age=31536000" : "private, max-age=3600";
        return File(leido.Contenido, leido.TipoContenido);
    }
}
