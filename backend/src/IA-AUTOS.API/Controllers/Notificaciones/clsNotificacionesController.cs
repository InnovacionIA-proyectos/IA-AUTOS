using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Notificaciones.DTONotificaciones;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Notificaciones;

/// <summary>
/// La bandeja de notificaciones (capacidad <c>ver_alertas</c>): cada usuario ve las suyas, el super administrador las de todos los clientes. El coordinador de calidad y los administradores
/// responden la novedad del conductor.
/// </summary>
[Route("api/notificaciones")]
[RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.INSPECTOR, clsRol.CONDUCTOR)]
public sealed class clsNotificacionesController(clsNotificacionesService servicio) : clsControladorBase
{
    /// <summary>
    /// Una pagina de la bandeja (<c>items</c>, <c>total</c>, <c>pagina</c>, <c>tamano</c> hasta 200 y <c>no_leidas</c>), las mas recientes primero, con los filtros <c>leida</c> (true o false), <c>tipo</c> y
    /// <c>urgencia</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<ListadoNotificacionesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? leida,
        [FromQuery] string? tipo,
        [FromQuery] string? urgencia,
        [FromQuery] int? pagina,
        [FromQuery] int? tamano,
        CancellationToken ct) =>
        Ok(await servicio.ListarAsync(new FiltrosNotificacionesRequest(leida, tipo, urgencia, pagina, tamano), ct));

    /// <summary>Marca la notificacion como leida (solo su destinatario; 403 si es de otro). Leerla de nuevo no cambia la primera lectura.</summary>
    [HttpPost("{id:guid}/leida")]
    [ProducesResponseType<NotificacionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarcarLeida(Guid id, CancellationToken ct) => Ok(await servicio.MarcarLeidaAsync(id, ct));

    /// <summary>
    /// Responde la novedad del conductor (<c>texto</c> de hasta 1000 caracteres y <c>medio</c>: <c>app</c> o <c>llamada</c>) y se la hace llegar al conductor del viaje. Solo el coordinador de calidad y los
    /// administradores, de una novedad de su cliente (403 los demas); 409 si la novedad ya fue respondida.
    /// </summary>
    [HttpPost("{id:guid}/respuesta")]
    [RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<NotificacionDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(ResponderNotificacionRequest))]
    public async Task<IActionResult> Responder(Guid id, CancellationToken ct) =>
        Ok(await servicio.ResponderAsync(id, await LeerCuerpoAsync<ResponderNotificacionRequest>(ct), ct));
}
