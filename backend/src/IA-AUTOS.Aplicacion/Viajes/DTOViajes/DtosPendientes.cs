namespace IAAutos.Aplicacion.Viajes.DTOViajes;

/// <summary>Los estados de un camion en la lista de pendientes del inspector.</summary>
public static class clsEstadoPendiente
{
    /// <summary>Nadie lo ha reclamado: el inspector puede tomarlo.</summary>
    public const string DISPONIBLE = "disponible";

    /// <summary>Lo reclamo otro inspector («en revision por» su nombre).</summary>
    public const string EN_REVISION = "en_revision";

    /// <summary>Lo reclamo el propio inspector.</summary>
    public const string MIO = "mio";
}

/// <summary>
/// Un camion pendiente en uno de los puntos del inspector: el cargue (<c>proceso = cargue</c>, programado para hoy en el punto) o la entrega (<c>proceso = entrega</c>, un viaje en curso con unidades que se entregan en el
/// punto). <c>Estado</c> es <c>disponible</c>, <c>en_revision</c> (lo tiene otro inspector, que sale en <c>ReclamadoPor</c>/<c>ReclamadoPorNombre</c>) o <c>mio</c>. <c>Unidades</c> cuenta los VIN: los activos del viaje en el cargue y los
/// pendientes de entrega en el punto en la entrega.
/// </summary>
public sealed record PendienteDto(
    Guid ViajeId,
    string? CodigoMtrack,
    string? Placa,
    string Proceso,
    Guid LugarId,
    string Lugar,
    DateTimeOffset? FechaProgramada,
    string Estado,
    Guid? ReclamadoPor,
    string? ReclamadoPorNombre,
    DateTimeOffset? ReclamadoAt,
    int Unidades);

/// <summary>Lectura de la lista de pendientes de un inspector.</summary>
public interface IConsultasPendientes
{
    /// <summary>
    /// Los cargues programados entre <paramref name="desde"/> (incluido) y <paramref name="hasta"/> (excluido) en alguno de <paramref name="lugares"/> y los viajes en curso con unidades pendientes de entrega en alguno de
    /// ellos, de viajes activos, cada uno con su estado respecto de <paramref name="usuarioId"/>.
    /// </summary>
    Task<IReadOnlyList<PendienteDto>> ListarAsync(Guid usuarioId, IReadOnlyCollection<Guid> lugares, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct = default);
}
