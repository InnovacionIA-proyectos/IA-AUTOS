using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// Quien puede ver y quien puede actuar sobre un viaje cuando es inspector. Antes solo el inspector del viaje; ahora tambien quien lo RECLAMO (el reclamo activo de cualquier proceso) y, para VER, los camiones
/// PENDIENTES de sus puntos: un cargue programado para hoy en un punto suyo o un viaje en curso que entrega en un punto suyo. Para ACTUAR (inspeccionar, firmar) hay que participar o haber reclamado. El conductor no
/// cambia: sigue viendo solo sus viajes. Los puntos de un inspector son los de <c>tblInspectorLugares</c>.
/// </summary>
public sealed class clsAccesoViajes(IReclamoRepository reclamos, IViajeRepository viajes, IUsuarioActual actual, IReloj reloj)
{
    private static readonly TimeSpan COLOMBIA = TimeSpan.FromHours(-5);

    /// <summary>El conductor o el inspector del viaje, o el inspector que reclamo alguno de sus procesos (reclamo activo).</summary>
    public async Task<bool> ParticipaOReclamaAsync(clsViaje viaje, Guid usuarioId, CancellationToken ct = default) =>
        viaje.Participa(usuarioId) || (actual.Rol == clsRol.INSPECTOR && await TieneReclamoActivoAsync(viaje.Id, usuarioId, ct));

    /// <summary>Lo anterior o, si es inspector, un camion pendiente de uno de sus puntos (aunque aun no lo haya reclamado).</summary>
    public async Task<bool> PuedeVerAsync(clsViaje viaje, Guid usuarioId, CancellationToken ct = default)
    {
        if (await ParticipaOReclamaAsync(viaje, usuarioId, ct))
        {
            return true;
        }

        return actual.Rol == clsRol.INSPECTOR && await EsPendienteDeSusPuntosAsync(viaje, usuarioId, ct);
    }

    /// <summary>El dia de hoy en Colombia (UTC-5, sin horario de verano): el dia que cuenta para los cargues programados.</summary>
    public DateOnly Hoy() => DateOnly.FromDateTime(reloj.Ahora.ToOffset(COLOMBIA).DateTime);

    private async Task<bool> TieneReclamoActivoAsync(Guid viajeId, Guid usuarioId, CancellationToken ct) =>
        (await reclamos.ObtenerDeViajeAsync(viajeId, ct)).Any(r => r.EstaActivo && r.InspectorId == usuarioId);

    private async Task<bool> EsPendienteDeSusPuntosAsync(clsViaje viaje, Guid usuarioId, CancellationToken ct)
    {
        if (!viaje.EstaActivo)
        {
            return false;
        }

        var puntos = await reclamos.LugaresDelInspectorAsync(usuarioId, ct);
        if (puntos.Count == 0)
        {
            return false;
        }

        if (viaje.LugarCargueId is { } cargue && puntos.Contains(cargue) && viaje.FechaProgramada is { } fecha && DateOnly.FromDateTime(fecha.ToOffset(COLOMBIA).DateTime) == Hoy())
        {
            return true;
        }

        return viaje.Estado == clsEstadoViaje.EN_CURSO && (await viajes.ObtenerVehiculosAsync(viaje.Id, ct)).Any(v => !v.NoSale && v.LugarDestinoId is { } d && puntos.Contains(d));
    }
}
