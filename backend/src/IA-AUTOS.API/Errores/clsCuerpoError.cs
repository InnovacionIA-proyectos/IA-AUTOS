using System.Text.Json;
using IAAutos.Dominio.Comun;

namespace IAAutos.API.Errores;

/// <summary>
/// Cuerpo de error uniforme, identico al de la API original: siempre JSON con
/// <c>error</c> (mensaje, por compatibilidad), <c>code</c>, <c>message</c>, <c>timestamp</c> y opcionalmente <c>details</c>.
/// </summary>
public static class clsCuerpoError
{
    public static int EstadoPorCodigo(string codigo) => codigo switch
    {
        clsCodigosError.VALIDATION_ERROR => StatusCodes.Status400BadRequest,
        clsCodigosError.UNAUTHORIZED => StatusCodes.Status401Unauthorized,
        clsCodigosError.FORBIDDEN => StatusCodes.Status403Forbidden,
        clsCodigosError.PORTAL_USER_NOT_LINKED => StatusCodes.Status403Forbidden,
        clsCodigosError.NOT_FOUND => StatusCodes.Status404NotFound,
        clsCodigosError.METHOD_NOT_ALLOWED => StatusCodes.Status405MethodNotAllowed,
        clsCodigosError.CONFLICT => StatusCodes.Status409Conflict,
        clsCodigosError.RATE_LIMITED => StatusCodes.Status429TooManyRequests,
        clsCodigosError.NOT_IMPLEMENTED => StatusCodes.Status501NotImplemented,
        clsCodigosError.DB_UNAVAILABLE => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static Dictionary<string, object?> Crear(string codigo, string mensaje, object? detalles = null)
    {
        var cuerpo = new Dictionary<string, object?>
        {
            ["error"] = mensaje,
            ["code"] = codigo,
            ["message"] = mensaje,
            ["timestamp"] = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture),
        };
        if (detalles is not null)
        {
            cuerpo["details"] = detalles;
        }

        return cuerpo;
    }

    public static async Task EscribirAsync(HttpContext contexto, string codigo, string mensaje, object? detalles = null, int? estado = null)
    {
        contexto.Response.StatusCode = estado ?? EstadoPorCodigo(codigo);
        contexto.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(contexto.Response.Body, Crear(codigo, mensaje, detalles), clsJsonApi.OPCIONES, contexto.RequestAborted);
    }
}
