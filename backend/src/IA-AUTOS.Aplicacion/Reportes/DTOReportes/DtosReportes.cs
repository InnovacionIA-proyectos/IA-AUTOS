using System.Text.Json.Serialization;

namespace IAAutos.Aplicacion.Reportes.DTOReportes;

public sealed record ConteoProcesoDto(string Proceso, int Total);

public sealed record ConteoZonaDto(string ParteVehiculo, int Total);

public sealed record ConteoZonaProcesoDto(string Proceso, string ParteVehiculo, int Total);

/// <summary>Mapa de calor: hallazgos de unidades movilizadas por proceso y por zona del vehiculo.</summary>
public sealed record PatronesDto(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("porProceso")] IReadOnlyList<ConteoProcesoDto> PorProceso,
    [property: JsonPropertyName("porZona")] IReadOnlyList<ConteoZonaDto> PorZona,
    [property: JsonPropertyName("porZonaProceso")] IReadOnlyList<ConteoZonaProcesoDto> PorZonaProceso);

public sealed record ConteoClaseDto(string ClaseDano, int Total);

public sealed record ConteoTamanoDto(string TamanoDano, int Total);

public sealed record AnalisisDto(
    [property: JsonPropertyName("porClase")] IReadOnlyList<ConteoClaseDto> PorClase,
    [property: JsonPropertyName("porTamano")] IReadOnlyList<ConteoTamanoDto> PorTamano,
    [property: JsonPropertyName("sugerencias")] IReadOnlyList<string> Sugerencias);

public sealed record TasaOperacionDto(
    [property: JsonPropertyName("operacion")] string Operacion,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("conNovedad")] int ConNovedad,
    [property: JsonPropertyName("tasaActual")] decimal TasaActual,
    [property: JsonPropertyName("lineaBase")] decimal LineaBase,
    [property: JsonPropertyName("variacion")] decimal Variacion);

/// <summary>Tasa de rechazo (unidades movilizadas con al menos un hallazgo) frente a la linea base historica del PRD.</summary>
public sealed record TasaRechazoDto(
    [property: JsonPropertyName("tasaGlobal")] decimal TasaGlobal,
    [property: JsonPropertyName("totalMovilizadas")] int TotalMovilizadas,
    [property: JsonPropertyName("totalPendientes")] int TotalPendientes,
    [property: JsonPropertyName("totalConNovedad")] int TotalConNovedad,
    [property: JsonPropertyName("totalNovedad")] int TotalNovedad,
    [property: JsonPropertyName("porOperacion")] IReadOnlyList<TasaOperacionDto> PorOperacion);

/// <summary>Conteos crudos por operacion sobre unidades movilizadas.</summary>
public sealed record ConteoOperacion(string Operacion, int Total, int ConNovedad);

/// <summary>Totales globales para la tasa de rechazo.</summary>
public sealed record ConteosRechazo(int TotalVehiculos, int Movilizadas, int ConNovedad, IReadOnlyList<ConteoOperacion> PorOperacion);
