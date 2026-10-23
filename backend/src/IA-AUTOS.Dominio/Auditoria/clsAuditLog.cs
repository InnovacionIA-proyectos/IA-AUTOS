using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Auditoria;

/// <summary>
/// Registro append-only de toda accion relevante: quien, que, sobre que registro, desde donde (IP y dispositivo), el detalle del cambio
/// (<c>{"antes": ..., "despues": ...}</c>, nulo en las lecturas) y el viaje y el cliente de lo auditado. UsuarioId nulo indica una accion del sistema (ETL).
/// El viaje y el cliente no tienen llave foranea: el registro sobrevive a lo que audita y nunca se modifica ni se borra.
/// </summary>
public sealed class clsAuditLog : clsEntidadBase
{
    public const int IP_MAX = 45;
    public const int DISPOSITIVO_MAX = 200;

    private clsAuditLog()
    {
    }

    public Guid? UsuarioId { get; private set; }

    public string Accion { get; private set; } = string.Empty;

    public string? Entidad { get; private set; }

    public Guid? EntidadId { get; private set; }

    /// <summary>Direccion IP desde la que llego la solicitud; nula en las acciones del sistema.</summary>
    public string? Ip { get; private set; }

    /// <summary>Identificador del dispositivo (<c>X-Dispositivo-Id</c>) o, si no llega, el User-Agent; nulo en las acciones del sistema.</summary>
    public string? Dispositivo { get; private set; }

    /// <summary>JSON con el estado <c>antes</c> y <c>despues</c> del cambio, sin contrasenas, PIN ni tokens; nulo si la accion no cambia datos.</summary>
    public string? Detalle { get; private set; }

    public Guid? ViajeId { get; private set; }

    public Guid? ClienteId { get; private set; }

    public static clsAuditLog Crear(
        Guid? usuarioId,
        string accion,
        string? entidad,
        Guid? entidadId,
        DateTimeOffset ahora,
        string? ip = null,
        string? dispositivo = null,
        string? detalle = null,
        Guid? viajeId = null,
        Guid? clienteId = null)
    {
        var registro = new clsAuditLog
        {
            UsuarioId = usuarioId,
            Accion = clsValidador.Requerido(accion, "La acción es requerida"),
            Entidad = clsValidador.Opcional(entidad),
            EntidadId = entidadId,
            Ip = Recortar(ip, IP_MAX),
            Dispositivo = Recortar(dispositivo, DISPOSITIVO_MAX),
            Detalle = clsValidador.Opcional(detalle),
            ViajeId = viajeId,
            ClienteId = clienteId,
        };
        registro.Inicializar(ahora);
        return registro;
    }

    private static string? Recortar(string? valor, int maximo)
    {
        var limpio = clsValidador.Opcional(valor);
        return limpio is not null && limpio.Length > maximo ? limpio[..maximo] : limpio;
    }
}
