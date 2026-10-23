using IAAutos.API.Controllers.Comun;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Reportes;

[Route("api")]
public sealed class clsReportesController(clsReportesService servicio) : clsControladorBase
{
    /// <summary>Tasa de rechazo por operacion frente a la linea base historica del PRD.</summary>
    [HttpGet("metricas/rechazo")]
    [ProducesResponseType<TasaRechazoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rechazo(CancellationToken ct) => Ok(await servicio.ObtenerTasaRechazoAsync(ct));
}
