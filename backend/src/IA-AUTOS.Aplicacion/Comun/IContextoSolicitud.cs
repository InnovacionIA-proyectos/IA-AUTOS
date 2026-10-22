namespace IAAutos.Aplicacion.Comun;

/// <summary>Desde donde llega la solicitud en curso, para la auditoria. Ambos son nulos fuera de una solicitud HTTP (procesos del sistema).</summary>
public interface IContextoSolicitud
{
    /// <summary>Direccion IP del cliente (la real detras del proxy, si esta configurado).</summary>
    string? Ip { get; }

    /// <summary>El identificador del dispositivo (<c>X-Dispositivo-Id</c>) o, si no llega, el User-Agent.</summary>
    string? Dispositivo { get; }
}
