using IAAutos.Dominio.Comun;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IAAutos.API.Seguridad;

/// <summary>
/// Restringe una accion a ciertos roles. El rol viene de la base de datos (no del token), asi que un cambio de
/// rol o una baja se aplican de inmediato. Responde 403 con el mensaje de la API original.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RolesPermitidosAttribute(params string[] roles) : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext contexto)
    {
        var rol = contexto.HttpContext.User.FindFirst(clsUsuarioActualHttp.CLAIM_ROL)?.Value;
        if (rol is null || !roles.Contains(rol))
        {
            throw new clsProhibidoException(
                $"No tiene permisos para esta acción (rol requerido: {string.Join(", ", roles)})");
        }
    }
}
