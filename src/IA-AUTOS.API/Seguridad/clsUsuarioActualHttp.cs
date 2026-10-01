using System.Security.Claims;
using IAAutos.Aplicacion.Comun;
using Microsoft.IdentityModel.JsonWebTokens;

namespace IAAutos.API.Seguridad;

/// <summary>Usuario de la solicitud HTTP en curso, tomado de los claims ya validados contra la base de datos.</summary>
internal sealed class clsUsuarioActualHttp(IHttpContextAccessor accesor) : IUsuarioActual
{
    public const string CLAIM_ROL = "user_role";

    private ClaimsPrincipal? Principal => accesor.HttpContext?.User;

    public Guid? Id =>
        Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    public string? Rol => Principal?.FindFirstValue(CLAIM_ROL);

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);
}
