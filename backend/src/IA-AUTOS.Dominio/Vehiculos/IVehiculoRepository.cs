namespace IAAutos.Dominio.Vehiculos;

public interface IVehiculoRepository
{
    Task<clsVehiculo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Busca por VIN exacto (el VIN se normaliza a mayusculas).</summary>
    Task<clsVehiculo?> ObtenerPorVinAsync(string vin, CancellationToken ct = default);

    /// <summary>Precarga los vehiculos existentes de un lote de VINs (para cargas masivas).</summary>
    Task<IReadOnlyDictionary<string, clsVehiculo>> ObtenerPorVinesAsync(IReadOnlyCollection<string> vines, CancellationToken ct = default);

    Task<bool> ExisteAsync(Guid id, CancellationToken ct = default);

    void Agregar(clsVehiculo vehiculo);
}

public interface IEtapaRepository
{
    Task<clsEtapa?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>La etapa inmediatamente anterior del mismo VIN en el mismo viaje (la de mayor orden menor al indicado), o nulo si es la primera.</summary>
    Task<clsEtapa?> ObtenerPreviaAsync(Guid viajeId, Guid vehiculoId, int orden, CancellationToken ct = default);

    void Agregar(clsEtapa etapa);
}
