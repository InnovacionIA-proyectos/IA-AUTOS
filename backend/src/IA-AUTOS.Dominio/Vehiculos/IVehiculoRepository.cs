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

    void Agregar(clsEtapa etapa);
}
