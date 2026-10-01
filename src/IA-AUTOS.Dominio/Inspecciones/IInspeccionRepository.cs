namespace IAAutos.Dominio.Inspecciones;

public interface IInspeccionRepository
{
    Task<clsInspeccion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Conteos de evidencias y hallazgos que determinan si la inspeccion puede cerrarse.</summary>
    Task<clsResumenCierre> ObtenerResumenCierreAsync(Guid inspeccionId, CancellationToken ct = default);

    Task<bool> ExisteHallazgoEnInspeccionAsync(Guid hallazgoId, Guid inspeccionId, CancellationToken ct = default);

    void Agregar(clsInspeccion inspeccion);

    void Agregar(clsHallazgo hallazgo);

    void Agregar(clsEvidencia evidencia);
}
