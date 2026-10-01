using System.Globalization;
using IAAutos.Aplicacion.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Salud;

/// <summary>Verificacion de salud publica: 200 si la base de datos responde, 503 si no.</summary>
[ApiController]
public sealed class clsSaludController(ISaludBaseDatos salud) : ControllerBase
{
    [HttpGet("/health")]
    [AllowAnonymous]
    public async Task<IActionResult> Obtener(CancellationToken ct)
    {
        var bd = await salud.VerificarAsync(ct);
        var cuerpo = new Dictionary<string, object?>
        {
            ["status"] = bd.Conectada ? "ok" : "unhealthy",
            ["db"] = new Dictionary<string, object?> { ["connected"] = bd.Conectada, ["latencyMs"] = bd.LatenciaMs },
            ["timestamp"] = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        };
        return StatusCode(bd.Conectada ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, cuerpo);
    }
}
