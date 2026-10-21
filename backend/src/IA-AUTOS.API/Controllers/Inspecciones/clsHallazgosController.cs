using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Inspecciones;

/// <summary>
/// Seguimiento de los hallazgos: el coordinador de calidad, comercial y los administradores (capacidad <c>gestionar_seguimiento</c>) los llevan a validado, rechazado,
/// recuperado o derogado y cada cambio queda en un historial. Solo los hallazgos del cliente de quien consulta.
/// </summary>
[Route("api/hallazgos")]
[RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.COMERCIAL, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
public sealed class clsHallazgosController(clsSeguimientoService servicio) : clsControladorBase
{
    /// <summary>
    /// Cambia el estado del hallazgo. Un hallazgo V2 se valida con <c>evidencia_sello</c> (la foto del sello de quien valida, 400 si falta). 409 si su inspeccion esta en borrador
    /// o es una novedad del conductor sin revisar. Marcar rechazado nunca se bloquea por la evidencia: la respuesta trae <c>evidencia_completa</c> y <c>evidencia_faltante</c>.
    /// </summary>
    [HttpPost("{id:guid}/seguimiento")]
    [ProducesResponseType<HallazgoDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(CambiarSeguimientoRequest))]
    public async Task<IActionResult> CambiarSeguimiento(Guid id, CancellationToken ct) =>
        Ok(await servicio.CambiarAsync(id, await LeerCuerpoAsync<CambiarSeguimientoRequest>(ct), ct));

    /// <summary>El historial del hallazgo (cambios de seguimiento, revisiones y «no sale»), del mas antiguo al mas reciente.</summary>
    [HttpGet("{id:guid}/seguimiento")]
    [ProducesResponseType<IReadOnlyList<SeguimientoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Historial(Guid id, CancellationToken ct) => Ok(await servicio.HistorialAsync(id, ct));
}
