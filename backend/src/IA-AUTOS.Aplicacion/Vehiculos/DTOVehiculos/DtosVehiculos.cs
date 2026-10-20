using System.Text.Json.Serialization;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;

namespace IAAutos.Aplicacion.Vehiculos.DTOVehiculos;

public sealed record CargarVinsRequest(IReadOnlyList<VinCargaItem>? Items);

public sealed record VinCargaItem(string? Vin, string? Marca, string? Modelo, string? Color, string? Operacion);

public sealed record ErrorCargaDto(string Vin, string Error);

public sealed record ResultadoCargaDto(int Recibidos, int Creados, int Actualizados, int Fallidos, IReadOnlyList<ErrorCargaDto> Errores);

public sealed record VehiculoBusquedaDto(string Vin, string Marca, string? Modelo, string? Color, string Operacion);

public sealed record ListadoVehiculoDto(
    Guid Id,
    string Vin,
    string Marca,
    string? Modelo,
    string? Color,
    string Operacion,
    int Etapas,
    int Completas,
    int Novedades);

public sealed record ListadoPaginadoDto(
    IReadOnlyList<ListadoVehiculoDto> Rows,
    int Total,
    int Page,
    [property: JsonPropertyName("pageSize")] int PageSize);

public sealed record InventarioInspeccionDto(
    Guid Id,
    Guid EtapaId,
    Guid ViajeId,
    Guid UsuarioId,
    string Momento,
    DateTimeOffset Fecha,
    double? Lat,
    double? Lng,
    string Estado,
    string? ConductorNombre,
    string? Placa,
    string? Trailer,
    string? Notas,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset IniciadaAt,
    DateTimeOffset? CerradaAt,
    int Version,
    Guid? CorrigeInspeccionId,
    string MomentoNombre,
    IReadOnlyList<HallazgoDto> Hallazgos,
    IReadOnlyList<EvidenciaDto> Evidencias,
    TransferenciaDto? Transferencia);

public sealed record InventarioEtapaDto(
    Guid Id,
    int Orden,
    string Proceso,
    string? Lugar,
    bool RequiereInspeccion,
    Guid ViajeId,
    string? CodigoMtrack,
    bool ConPersonalApoyo,
    string EstadoUnidad,
    NoSaleDto? NoSale,
    IReadOnlyList<InventarioInspeccionDto> Inspecciones);

public sealed record InventarioDto(
    Guid Id,
    string Vin,
    string Marca,
    string? TipoVehiculo,
    string? Modelo,
    string? Chasis,
    string? Color,
    string Operacion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<InventarioEtapaDto> Etapas);
