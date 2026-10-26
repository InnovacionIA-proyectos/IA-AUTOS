using System.Text.Json.Serialization;

namespace IAAutos.Aplicacion.Reportes.DTOReportes;

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
