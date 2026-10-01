namespace IAAutos.Dominio.Sofasa;

public interface IOrdenSofasaRepository
{
    Task<clsOrdenSofasa?> ObtenerPorSourceIdAsync(long sourceId, CancellationToken ct = default);

    void Agregar(clsOrdenSofasa orden);
}
