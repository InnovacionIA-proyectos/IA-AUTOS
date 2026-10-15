using IAAutos.Dominio.Comun;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IAAutos.API.Seguridad;

/// <summary>Marca las acciones que una persona con PIN temporal si puede usar (cambiar el PIN y consultar su perfil).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PermitirConCambioDePinAttribute : Attribute;

/// <summary>
/// Filtro global: mientras el usuario tenga un PIN temporal (asignado o reiniciado por un administrador), toda la API responde
/// 403 PIN_CHANGE_REQUIRED salvo las acciones marcadas con <see cref="PermitirConCambioDePinAttribute"/>. La marca sale de la
/// base de datos en cada solicitud, asi que no se puede esquivar con un token anterior.
/// </summary>
internal sealed class clsFiltroCambioPin : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext contexto)
    {
        if (contexto.HttpContext.User.FindFirst(clsUsuarioActualHttp.CLAIM_CAMBIAR_PIN) is null)
        {
            return;
        }

        if (contexto.ActionDescriptor.EndpointMetadata.OfType<PermitirConCambioDePinAttribute>().Any())
        {
            return;
        }

        throw new clsCambioPinRequeridoException();
    }
}
