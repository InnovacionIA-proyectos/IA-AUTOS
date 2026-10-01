namespace IAAutos.Dominio.Custodia;

public interface ITransferenciaCustodiaRepository
{
    Task<clsTransferenciaCustodia?> ObtenerPorInspeccionAsync(Guid inspeccionId, CancellationToken ct = default);

    void Agregar(clsTransferenciaCustodia transferencia);
}
