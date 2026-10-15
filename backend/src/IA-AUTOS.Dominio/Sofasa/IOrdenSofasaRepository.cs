namespace IAAutos.Dominio.Sofasa;

public interface IOrdenSofasaRepository
{
    Task<clsOrdenSofasa?> ObtenerPorSourceIdAsync(long sourceId, CancellationToken ct = default);

    /// <summary>Ordenes sin viaje cuyo VIN esta entre los recibidos (para vincularlas al agregar esos VIN a un viaje).</summary>
    Task<IReadOnlyList<clsOrdenSofasa>> ObtenerSinViajePorVinesAsync(IReadOnlyCollection<string> vines, CancellationToken ct = default);

    void Agregar(clsOrdenSofasa orden);
}
