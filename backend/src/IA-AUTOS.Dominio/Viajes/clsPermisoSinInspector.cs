using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Viajes;

/// <summary>El estado de la solicitud del permiso de la excepcion «No hay inspector».</summary>
public static class clsEstadoPermiso
{
    public const string PENDIENTE = "pendiente";
    public const string CONCEDIDO = "concedido";
}

/// <summary>
/// La solicitud del conductor para activar la excepcion «No hay inspector» cuando el punto de cargue TIENE inspectores asignados (donde no los hay la excepcion se activa en el momento). Una por viaje: guarda el motivo, su
/// texto y la fecha y el GPS del momento en que el conductor la pidio. La concede un coordinador de calidad o un administrador (<see cref="ConcedidoPor"/>) y el conductor queda habilitado de inmediato; si nadie responde
/// en <see cref="MINUTOS_DE_ESPERA"/> minutos la concede el reloj, marcada como autorizada por tiempo (<see cref="PorTiempo"/>), nunca antes de ese plazo.
/// </summary>
public sealed class clsPermisoSinInspector : clsEntidadBase
{
    /// <summary>Los minutos que se espera una respuesta antes de que el reloj conceda el permiso.</summary>
    public const int MINUTOS_DE_ESPERA = 10;

    public const int TEXTO_MAX = 300;

    private clsPermisoSinInspector()
    {
    }

    public Guid ViajeId { get; private set; }

    /// <summary>El conductor que pidio el permiso.</summary>
    public Guid ConductorId { get; private set; }

    /// <summary>El lugar de cargue del viaje, que tiene inspectores asignados.</summary>
    public Guid LugarId { get; private set; }

    /// <summary>El codigo del motivo (catalogo <c>motivos-sin-inspector</c>).</summary>
    public string MotivoCodigo { get; private set; } = string.Empty;

    public string? MotivoTexto { get; private set; }

    /// <summary>Cuando pidio el conductor el permiso (el momento en que lo pidio, aunque lo envie despues por falta de senal).</summary>
    public DateTimeOffset Fecha { get; private set; }

    public double? Lat { get; private set; }

    public double? Lng { get; private set; }

    public string Estado { get; private set; } = clsEstadoPermiso.PENDIENTE;

    public DateTimeOffset? ConcedidoAt { get; private set; }

    /// <summary>Quien lo concedio; nulo si lo concedio el reloj (<see cref="PorTiempo"/>) o mientras sigue pendiente.</summary>
    public Guid? ConcedidoPor { get; private set; }

    /// <summary>True si nadie respondio y el permiso lo concedio el reloj a los <see cref="MINUTOS_DE_ESPERA"/> minutos.</summary>
    public bool PorTiempo { get; private set; }

    public bool EstaConcedido => Estado == clsEstadoPermiso.CONCEDIDO;

    /// <summary>Desde cuando el reloj puede conceder el permiso: <see cref="MINUTOS_DE_ESPERA"/> minutos despues de la solicitud.</summary>
    public DateTimeOffset VenceAt => CreatedAt.AddMinutes(MINUTOS_DE_ESPERA);

    public static clsPermisoSinInspector Solicitar(
        Guid viajeId, Guid conductorId, Guid lugarId, string? motivoCodigo, string? motivoTexto, DateTimeOffset fecha, double? lat, double? lng, DateTimeOffset ahora)
    {
        clsValidador.Latitud(lat, "lat");
        clsValidador.Longitud(lng, "lng");
        var texto = clsValidador.Opcional(motivoTexto);
        if (texto is { Length: > TEXTO_MAX })
        {
            throw clsErroresCampos.De("motivo_sin_inspector_texto", $"El texto admite máximo {TEXTO_MAX} caracteres");
        }

        var permiso = new clsPermisoSinInspector
        {
            ViajeId = viajeId,
            ConductorId = conductorId,
            LugarId = lugarId,
            MotivoCodigo = clsValidador.Opcional(motivoCodigo) ?? throw clsErroresCampos.De("motivo_sin_inspector", "El motivo es obligatorio"),
            MotivoTexto = texto,
            Fecha = fecha,
            Lat = lat,
            Lng = lng,
        };
        permiso.Inicializar(ahora);
        return permiso;
    }

    /// <summary>Un coordinador de calidad o administrador concede el permiso: el conductor queda habilitado de inmediato. 409 si ya esta concedido.</summary>
    public void Conceder(Guid concedidoPor, DateTimeOffset ahora)
    {
        AsegurarPendiente();
        Estado = clsEstadoPermiso.CONCEDIDO;
        ConcedidoAt = ahora;
        ConcedidoPor = concedidoPor;
        PorTiempo = false;
        Tocar(ahora);
    }

    /// <summary>Concede el permiso el reloj, porque nadie respondio. Nunca antes de <see cref="MINUTOS_DE_ESPERA"/> minutos desde la solicitud (409); 409 si ya esta concedido.</summary>
    public void ConcederPorTiempo(DateTimeOffset ahora)
    {
        AsegurarPendiente();
        if (ahora < VenceAt)
        {
            throw new clsConflictoException($"El permiso no se concede por tiempo antes de {MINUTOS_DE_ESPERA} minutos desde la solicitud");
        }

        Estado = clsEstadoPermiso.CONCEDIDO;
        ConcedidoAt = ahora;
        ConcedidoPor = null;
        PorTiempo = true;
        Tocar(ahora);
    }

    private void AsegurarPendiente()
    {
        if (EstaConcedido)
        {
            throw new clsConflictoException("El permiso ya está concedido");
        }
    }
}

public interface IPermisoSinInspectorRepository
{
    /// <summary>La solicitud del viaje (una sola), o nulo si no hay.</summary>
    Task<clsPermisoSinInspector?> ObtenerDeViajeAsync(Guid viajeId, CancellationToken ct = default);

    /// <summary>Las solicitudes aun pendientes pedidas hasta <paramref name="pedidasHasta"/> (las que ya cumplieron la espera del reloj), de la mas antigua a la mas reciente.</summary>
    Task<IReadOnlyList<clsPermisoSinInspector>> ObtenerPendientesAsync(DateTimeOffset pedidasHasta, int limite, CancellationToken ct = default);

    void Agregar(clsPermisoSinInspector permiso);
}
