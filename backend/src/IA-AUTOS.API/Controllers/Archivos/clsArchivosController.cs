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
    /// <summary>Sube una foto (base64, con o sin prefijo data URI). Solo JPG y PNG; el tipo se verifica por contenido.</summary>
    [HttpPost]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<ArchivoSubidoDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Archivos.DTOArchivos.SubirArchivoRequest))]
    public async Task<IActionResult> Subir(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<SubirArchivoRequest>(ct);
        return Creado(await servicio.SubirAsync(solicitud, ct));
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
