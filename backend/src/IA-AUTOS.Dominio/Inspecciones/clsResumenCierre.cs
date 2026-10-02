namespace IAAutos.Dominio.Inspecciones;

/// <summary>Conteos necesarios para decidir si una inspeccion puede cerrarse.</summary>
public sealed record clsResumenCierre(int TotalEvidencias, int EvidenciasSinGeo, int TotalHallazgos);
