using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Auditoria;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Auditoria;

/// <summary>
/// La auditoria de todo lo que se hace en el sistema (capacidad <c>ver_auditoria</c>): el administrador ve la de su cliente y el super administrador la de todos. Solo se consulta: los
/// registros no se modifican ni se borran.
/// </summary>
[Route("api/auditoria")]
[RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN)]
public sealed class clsAuditoriaController(clsAuditoriaService servicio) : clsControladorBase
{
    /// <summary>
    /// Una pagina de la auditoria (<c>items</c>, <c>total</c>, <c>pagina</c> y <c>tamano</c> hasta 200), la mas reciente primero, con los filtros <c>usuario_id</c>, <c>entidad</c>, <c>entidad_id</c>,
    /// <c>viaje_id</c>, <c>accion</c>, <c>desde</c> y <c>hasta</c> (fecha ISO con zona). 400 con un filtro mal formado.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<ListadoAuditoriaDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery(Name = "usuario_id")] string? usuarioId,
        [FromQuery] string? entidad,
        [FromQuery(Name = "entidad_id")] string? entidadId,
        [FromQuery(Name = "viaje_id")] string? viajeId,
        [FromQuery] string? accion,
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] int? pagina,
        [FromQuery] int? tamano,
        CancellationToken ct) =>
        Ok(await servicio.ListarAsync(new FiltrosAuditoriaRequest(usuarioId, entidad, entidadId, viajeId, accion, desde, hasta, pagina, tamano), ct));

    /// <summary>Un registro de la auditoria con su detalle (<c>antes</c> y <c>despues</c>). 404 si no existe; 403 si es de otro cliente.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AuditoriaDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct) => Ok(await servicio.ObtenerAsync(id, ct));
}
