using IAAutos.API.Controllers.Comun;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Reportes;

[Route("api")]
public sealed class clsReportesController(clsReportesService servicio) : clsControladorBase
{
    /// <summary>Mapa de calor: hallazgos de unidades movilizadas por proceso y por zona del vehiculo.</summary>
    [HttpGet("patrones")]
    [ProducesResponseType<PatronesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Patrones(CancellationToken ct) => Ok(await servicio.ObtenerPatronesAsync(ct));

    /// <summary>Clases y tamanos de dano con sugerencias de mejora basadas en las categorias predominantes.</summary>
    [HttpGet("patrones/analisis")]
    [ProducesResponseType<AnalisisDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Analisis(CancellationToken ct) => Ok(await servicio.ObtenerAnalisisAsync(ct));

    /// <summary>Tasa de rechazo por operacion frente a la linea base historica del PRD.</summary>
    [HttpGet("metricas/rechazo")]
    [ProducesResponseType<TasaRechazoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rechazo(CancellationToken ct) => Ok(await servicio.ObtenerTasaRechazoAsync(ct));
}
