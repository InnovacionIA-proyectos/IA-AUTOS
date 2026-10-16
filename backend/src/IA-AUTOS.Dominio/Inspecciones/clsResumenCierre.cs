namespace IAAutos.Dominio.Inspecciones;

/// <summary>
/// Lo que tiene una inspeccion al intentar cerrarla: fotos (y cuantas sin ubicacion), hallazgos (y cuantos sin foto propia) y las vistas
/// de foto que ya tienen al menos una foto.
/// </summary>
public sealed record clsResumenCierre(
    int TotalEvidencias,
    int EvidenciasSinUbicacion,
    int TotalHallazgos,
    int HallazgosSinEvidencia,
    IReadOnlyCollection<Guid> VistasFotografiadas);
