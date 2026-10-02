using System.Text.Json.Serialization;
using IAAutos.Aplicacion.Comun;

namespace IAAutos.Aplicacion.Sofasa.DTOSofasa;

/// <summary>Orden de carga tal como la envia el ETL (nombres snake_case). Solo source_id es obligatorio.</summary>
public sealed record OrdenSofasaIngestDto(
    [property: JsonConverter(typeof(clsConvertidorLongFlexible))] long? SourceId,
    string? StatusCode,
    string? DatetimeEvent,
    string? OrdenNumber,
    string? OrdenSap,
    string? OriginCedi,
    string? Vin,
    string? Make,
    string? ModelDescription,
    string? DealerCode,
    string? DestinationCity,
    string? DestinationAddress,
    string? LoadingDate,
    string? Appointment,
    string? LoadingDock,
    [property: JsonConverter(typeof(clsConvertidorDecimalFlexible))] decimal? Weight);

public sealed record IngestarOrdenesRequest(IReadOnlyList<OrdenSofasaIngestDto>? Ordenes);

public sealed record ErrorIngestaDto(long SourceId, string Error);

/// <summary>Respuesta de la ingesta. Los nombres en camelCase son los que consume el ETL ya desplegado.</summary>
public sealed record ResultadoIngestaDto(
    [property: JsonPropertyName("recibidas")] int Recibidas,
    [property: JsonPropertyName("guardadas")] int Guardadas,
    [property: JsonPropertyName("vehiculosCreados")] int VehiculosCreados,
    [property: JsonPropertyName("vehiculosActualizados")] int VehiculosActualizados,
    [property: JsonPropertyName("errores")] IReadOnlyList<ErrorIngestaDto> Errores);

public sealed record OrdenSofasaDto(
    Guid Id,
    long SourceId,
    string? StatusCode,
    DateTimeOffset? DatetimeEvent,
    string? OrdenNumber,
    string? OrdenSap,
    string? OriginCedi,
    string? Vin,
    string? Make,
    string? ModelDescription,
    string? DealerCode,
    string? DestinationCity,
    string? DestinationAddress,
    DateTimeOffset? LoadingDate,
    DateTimeOffset? Appointment,
    string? LoadingDock,
    decimal? Weight,
    Guid? VehiculoId,
    DateTimeOffset SyncedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record FiltrosOrdenesSofasa(string? Vin, string? Estado, string? Ciudad, DateTimeOffset? Desde, DateTimeOffset? Hasta);
