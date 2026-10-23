using IAAutos.Aplicacion.Comun;

namespace IAAutos.API.Seguridad;

/// <summary>La IP y el dispositivo de la solicitud HTTP en curso. Detras del proxy configurado la IP es la real (cabecera X-Forwarded-For).</summary>
internal sealed class clsContextoSolicitudHttp(IHttpContextAccessor accesor) : IContextoSolicitud
{
    public const string CABECERA_DISPOSITIVO = "X-Dispositivo-Id";

    public string? Ip => accesor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? Dispositivo
    {
        get
        {
            var cabeceras = accesor.HttpContext?.Request.Headers;
            if (cabeceras is null)
            {
                return null;
            }

            var dispositivo = cabeceras[CABECERA_DISPOSITIVO].ToString();
            return string.IsNullOrWhiteSpace(dispositivo) ? cabeceras.UserAgent.ToString() : dispositivo;
        }
    }
}
