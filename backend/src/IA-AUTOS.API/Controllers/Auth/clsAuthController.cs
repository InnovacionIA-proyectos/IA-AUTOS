using System.Security.Claims;
using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace IAAutos.API.Controllers.Auth;

[Route("api/auth")]
public sealed class clsAuthController(
    clsAuthService servicio,
    clsSesionPortalService sesionPortal,
    IUsuarioActual actual) : clsControladorBase
{
    public const string POLITICA_LIMITE_LOGIN = "login";
    public const string POLITICA_LIMITE_LOGIN_PORTAL = "login-portal";
    public const string POLITICA_LIMITE_REFRESH = "refresh";

    /// <summary>Inicio de sesion por correo y contrasena. Devuelve el JWT y los datos basicos del usuario.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(POLITICA_LIMITE_LOGIN)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Auth.DTOAuth.LoginRequest))]
    public async Task<IActionResult> Login(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<LoginRequest>(ct);
        return Ok(await servicio.LoginAsync(solicitud, ct));
    }

    /// <summary>
    /// Entrada desde el portal de administracion sin segundo login. El cuerpo es el arreglo del usuario del portal tal cual
    /// (se usa solo el primer elemento y solo su id): el servidor revalida el id contra TI e ignora el nombre y los permisos
    /// que envie el navegador. Devuelve access, refresh y el usuario. 403 PORTAL_USER_NOT_LINKED si no tiene acceso a IA Autos.
    /// </summary>
    [HttpPost("login-portal")]
    [AllowAnonymous]
    [EnableRateLimiting(POLITICA_LIMITE_LOGIN_PORTAL)]
    [ProducesResponseType<LoginPortalResponse>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(LoginPortalUsuarioDto), EsArreglo = true)]
    public async Task<IActionResult> LoginPortal(CancellationToken ct)
    {
        var solicitud = await LeerArregloAsync<LoginPortalUsuarioDto>(ct);
        var sesion = await sesionPortal.LoginPortalAsync(solicitud, Cliente(), ct);
        return Ok(new LoginPortalResponse(sesion.Access, sesion.RefreshPlano, sesion.User));
    }

    /// <summary>
    /// Renueva la sesion con el refresh token del cuerpo: devuelve un access y un refresh nuevos, y el refresh anterior queda
    /// revocado (usarlo otra vez responde 401). 401 si el refresh vencio, fue revocado o no existe.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(POLITICA_LIMITE_REFRESH)]
    [ProducesResponseType<RefreshResponse>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(RefreshRequest))]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<RefreshRequest>(ct);
        var sesion = await sesionPortal.RefrescarAsync(solicitud.Refresh, Cliente(), ct);
        return Ok(new RefreshResponse(sesion.Access, sesion.RefreshPlano));
    }

    /// <summary>
    /// Cierra la sesion: revoca el refresh token del cuerpo y, con el, el access token de esa sesion (usarlos despues responde
    /// 401). Si ademas viene el Bearer, tambien se revoca la sesion de ese access. Siempre responde 204 vacio.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [CuerpoSolicitud(typeof(RefreshRequest))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        // Sin cuerpo tambien se cierra la sesion del Bearer, si lo hay: quien perdio su refresh igual puede salir.
        var solicitud = Request.ContentLength is > 0 ? await LeerCuerpoAsync<RefreshRequest>(ct) : new RefreshRequest(null);
        var sid = Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sid), out var familia) ? familia : (Guid?)null;
        await sesionPortal.CerrarSesionAsync(solicitud.Refresh, sid, ct);
        return NoContent();
    }

    /// <summary>Datos del usuario autenticado.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UsuarioAutenticadoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Ok(await servicio.ObtenerPerfilAsync(actual.IdObligatorio(), ct));

    private ContextoCliente Cliente() =>
        new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
}
