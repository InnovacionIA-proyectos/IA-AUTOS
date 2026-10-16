namespace IAAutos.Dominio.Inspecciones;

public interface IInspeccionRepository
{
    Task<clsInspeccion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lo que tiene la inspeccion (fotos, ubicaciones, hallazgos sin foto y vistas fotografiadas) para decidir si puede cerrarse.</summary>
    Task<clsResumenCierre> ObtenerResumenCierreAsync(Guid inspeccionId, CancellationToken ct = default);

    /// <summary>True si la etapa tiene una inspeccion de llegada cerrada (de cualquiera de sus versiones).</summary>
    Task<bool> TieneLlegadaCerradaAsync(Guid etapaId, CancellationToken ct = default);

    /// <summary>True si alguna inspeccion de la etapa tiene una transferencia sin tipo de cierre (abierta): bloquea iniciar la etapa siguiente.</summary>
    Task<bool> TieneTransferenciaSinCierreAsync(Guid etapaId, CancellationToken ct = default);

    /// <summary>Una evidencia por id (por ejemplo, la foto de una constancia de no firma).</summary>
    Task<clsEvidencia?> ObtenerEvidenciaAsync(Guid id, CancellationToken ct = default);

    /// <summary>Id de la version que corrige a esta inspeccion, o nulo si nadie la ha corregido.</summary>
    Task<Guid?> ObtenerCorreccionIdAsync(Guid inspeccionId, CancellationToken ct = default);

    Task<bool> ExisteHallazgoEnInspeccionAsync(Guid hallazgoId, Guid inspeccionId, CancellationToken ct = default);

    void Agregar(clsInspeccion inspeccion);

    void Agregar(clsHallazgo hallazgo);

    void Agregar(clsEvidencia evidencia);
}
