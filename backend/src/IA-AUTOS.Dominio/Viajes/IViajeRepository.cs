using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Dominio.Viajes;

/// <summary>Un viaje activo (programado o en curso) que ya lleva un VIN.</summary>
public sealed record clsViajeActivoDeVin(Guid ViajeId, string? CodigoMtrack);

public interface IViajeRepository
{
    Task<clsViaje?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>True si otro viaje (distinto de <paramref name="exceptoViajeId"/>) ya usa ese codigo MTrack.</summary>
    Task<bool> ExisteCodigoMtrackAsync(string codigo, Guid? exceptoViajeId, CancellationToken ct = default);

    /// <summary>De los codigos recibidos, los que ya tiene algun viaje (para validar cargas masivas).</summary>
    Task<IReadOnlySet<string>> CodigosMtrackExistentesAsync(IReadOnlyCollection<string> codigos, CancellationToken ct = default);

    /// <summary>Vehiculo -> viaje activo que ya lo lleva, para los vehiculos recibidos (sin contar <paramref name="exceptoViajeId"/>).</summary>
    Task<IReadOnlyDictionary<Guid, clsViajeActivoDeVin>> ViajesActivosPorVehiculoAsync(
        IReadOnlyCollection<Guid> vehiculoIds,
        Guid? exceptoViajeId,
        CancellationToken ct = default);

    /// <summary>Los VIN del viaje (un enlace por vehiculo).</summary>
    Task<IReadOnlyList<clsViajeVehiculo>> ObtenerVehiculosAsync(Guid viajeId, CancellationToken ct = default);

    Task<clsViajeVehiculo?> ObtenerVehiculoAsync(Guid viajeId, Guid vehiculoId, CancellationToken ct = default);

    /// <summary>Etapas del viaje; con <paramref name="vehiculoId"/> solo las de ese VIN.</summary>
    Task<IReadOnlyList<clsEtapa>> ObtenerEtapasAsync(Guid viajeId, Guid? vehiculoId, CancellationToken ct = default);

    /// <summary>True si alguna etapa del viaje (o de ese VIN dentro del viaje) ya tiene una inspeccion.</summary>
    Task<bool> TieneInspeccionesAsync(Guid viajeId, Guid? vehiculoId, CancellationToken ct = default);

    /// <summary>Etapas del viaje (o del VIN) que ya tienen un informe cerrado: su lugar y su marca ya no se cambian.</summary>
    Task<IReadOnlySet<Guid>> EtapasConInformeCerradoAsync(Guid viajeId, CancellationToken ct = default);

    /// <summary>Id del viaje activo de un VIN, o el de un viaje con ese codigo MTrack que lo lleve (para vincular ordenes Sofasa).</summary>
    Task<Guid?> BuscarViajeParaOrdenAsync(Guid vehiculoId, string? codigoMtrack, CancellationToken ct = default);

    void Agregar(clsViaje viaje);

    void Agregar(clsViajeVehiculo vinEnViaje);

    void Eliminar(clsViaje viaje);

    void Eliminar(clsViajeVehiculo vinEnViaje);

    void Eliminar(IEnumerable<clsEtapa> etapas);
}
