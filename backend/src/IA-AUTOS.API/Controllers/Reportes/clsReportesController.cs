using IAAutos.API.Controllers.Comun;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Reportes;

[Route("api")]
public sealed class clsReportesController(clsReportesService servicio) : clsControladorBase
{
    /// <summary>OBSOLETO: usar <c>GET /api/reportes/patrones</c>. Mapa de calor: hallazgos de unidades movilizadas por proceso y por zona del vehiculo. Responde el encabezado <c>Deprecation</c>.</summary>
    [HttpGet("patrones")]
    [Obsolete("Usar GET /api/reportes/patrones")]
    [ProducesResponseType<PatronesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Patrones(CancellationToken ct)
    {
        MarcarObsoleto("/api/reportes/patrones");
        return Ok(await servicio.ObtenerPatronesAsync(ct));
    }

    /// <summary>OBSOLETO: usar <c>GET /api/reportes/patrones</c> (trae las clases y los tamanos). Clases y tamanos de dano con sugerencias de mejora basadas en las categorias predominantes. Responde el encabezado <c>Deprecation</c>.</summary>
    [HttpGet("patrones/analisis")]
    [Obsolete("Usar GET /api/reportes/patrones")]
    [ProducesResponseType<AnalisisDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Analisis(CancellationToken ct)
    {
        MarcarObsoleto("/api/reportes/patrones");
        return Ok(await servicio.ObtenerAnalisisAsync(ct));
    }

    /// <summary>Tasa de rechazo por operacion frente a la linea base historica del PRD.</summary>
    [HttpGet("metricas/rechazo")]
    [ProducesResponseType<TasaRechazoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rechazo(CancellationToken ct) => Ok(await servicio.ObtenerTasaRechazoAsync(ct));

    /// <summary>Avisa que la ruta se retira: <c>Deprecation: true</c> y un enlace a la ruta que la reemplaza.</summary>
    private void MarcarObsoleto(string sucesora)
    {
        Response.Headers["Deprecation"] = "true";
        Response.Headers.Append("Link", $"<{sucesora}>; rel=\"successor-version\"");
    }
}
