using IAAutos.Aplicacion.Viajes.DTOViajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>Consultas de solo lectura de viajes y de su visibilidad por usuario.</summary>
public interface IConsultasViajes
{
    /// <summary>Listado (mas recientes primero). Con <paramref name="soloDeUsuario"/> solo los viajes en los que participa ese usuario.</summary>
    Task<IReadOnlyList<ViajeResumenDto>> ListarAsync(FiltrosViajes filtros, Guid? soloDeUsuario, int limite, CancellationToken ct = default);

    /// <summary>Viajes (con sus VIN) en los que el usuario es conductor o inspector, en los estados indicados.</summary>
    Task<IReadOnlyList<ViajeDto>> MiosAsync(Guid usuarioId, IReadOnlyCollection<string> estados, CancellationToken ct = default);

    Task<ViajeDto?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>True si el VIN esta en algun viaje no cancelado en el que el usuario es conductor o inspector.</summary>
    Task<bool> UsuarioVeVinAsync(string vin, Guid usuarioId, CancellationToken ct = default);
}
