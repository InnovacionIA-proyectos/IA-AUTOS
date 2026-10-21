using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

namespace IAAutos.Aplicacion.Conductor.DTOConductor;

/// <summary>
/// La novedad que el conductor registra en ruta, con la fecha, hora y GPS del momento en el celular (se conservan aunque llegue despues, sin senal). Elige el tipo
/// (<c>golpe</c>, <c>piedra</c>, <c>granizo</c> o <c>incidente_vial</c>) y el tamano de las listas y adjunta su foto sellada: no hay texto libre ni se indica cuadrante, clase o nivel,
/// que completa el inspector al revisarla.
/// </summary>
public sealed record RegistrarNovedadRequest(
    DateTimeOffset? Fecha,
    double? Lat,
    double? Lng,
    string? TipoNovedad,
    string? TamanoDano,
    FotoEvidenciaRequest? Evidencia);

/// <summary>La llegada de una unidad a su destino, con la fecha, hora y GPS del momento en el celular.</summary>
public sealed record RegistrarLlegadaRequest(DateTimeOffset? Fecha, double? Lat, double? Lng);

/// <summary>
/// El resultado de confirmar la llegada: la inspeccion de llegada de Transporte (ya cerrada), cuantas novedades registro el conductor de esa unidad y si llego sin novedad.
/// </summary>
public sealed record LlegadaDto(InspeccionDto Inspeccion, int Novedades, bool SinNovedad);
