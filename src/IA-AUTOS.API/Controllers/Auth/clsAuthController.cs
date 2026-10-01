using IAAutos.API.Controllers.Comun;
using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IAAutos.API.Controllers.Auth;

[Route("api/auth")]
public sealed class clsAuthController(clsAuthService servicio, IUsuarioActual actual) : clsControladorBase
{
    public const string POLITICA_LIMITE_LOGIN = "login";

    /// <summary>Inicio de sesion por correo y contrasena. Devuelve el JWT y los datos basicos del usuario.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(POLITICA_LIMITE_LOGIN)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<LoginRequest>(ct);
        return Ok(await servicio.LoginAsync(solicitud, ct));
    }

    /// <summary>Datos del usuario autenticado.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UsuarioAutenticadoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Ok(await servicio.ObtenerPerfilAsync(actual.IdObligatorio(), ct));
}
