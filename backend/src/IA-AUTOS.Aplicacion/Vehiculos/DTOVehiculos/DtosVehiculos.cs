using System.Text.Json.Serialization;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;

namespace IAAutos.Aplicacion.Vehiculos.DTOVehiculos;

public sealed record CargarVinsRequest(IReadOnlyList<VinCargaItem>? Items);

public sealed record VinCargaItem(string? Vin, string? Marca, string? Modelo, string? Color, string? Operacion);

public sealed record ErrorCargaDto(string Vin, string Error);

public sealed record ResultadoCargaDto(int Recibidos, int Creados, int Actualizados, int Fallidos, IReadOnlyList<ErrorCargaDto> Errores);

public sealed record VehiculoBusquedaDto(string Vin, string Marca, string? Modelo, string? Color, string Operacion, bool Activo);

/// <summary>Resultado de dar de baja un vehiculo: siempre "inactivado" (nunca se borra: conserva su historial).</summary>
public sealed record BajaVehiculoResponse(string Modo);

public sealed record ListadoVehiculoDto(
    Guid Id,
    string Vin,
    string Marca,
    string? Modelo,
    string? Color,
    string Operacion,
    bool Activo,
    int Etapas,
    int Completas,
    int Novedades);

/// <summary>
/// Filtros del listado de vehiculos tal como llegan en la consulta (texto) y su pagina y orden: el servicio los valida juntos. <c>Tipo</c> es <c>movilizados</c> (por defecto), <c>pendientes</c> o
/// <c>novedad</c>; <c>EstadoUnidad</c>, el de la unidad en algun viaje (<c>activa</c> o <c>no_sale</c>); <c>Desde</c> y <c>Hasta</c> acotan la fecha de alta del vehiculo.
/// </summary>
public sealed record FiltrosVehiculosRequest(
    string? Tipo,
    string? Vin,
    string? Operacion,
    string? Marca,
    string? ClienteId,
    string? EstadoUnidad,
    string? Activo,
    string? Desde,
    string? Hasta,
    string? Pagina = null,
    string? Tamano = null,
    string? Orden = null);

/// <summary>Los filtros del listado ya validados.</summary>
public sealed record FiltrosVehiculos(
    string Tipo,
    string? Vin,
    string? Operacion,
    string? Marca,
    Guid? ClienteId,
    string? EstadoUnidad,
    bool? Activo,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta);

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
    TransferenciaDto? Transferencia,
    IReadOnlyList<IAAutos.Aplicacion.Inspecciones.NotaAclaratoriaDto> NotasAclaratorias,
    IAAutos.Aplicacion.Inspecciones.DTOInspecciones.MotivoSinInspectorDto? MotivoSinInspector,
    IReadOnlyList<IAAutos.Aplicacion.Inspecciones.DTOInspecciones.NovedadProcesoDto> NovedadesProceso);

public sealed record InventarioEtapaDto(
    Guid Id,
    int Orden,
    string Proceso,
    string? Lugar,
    bool RequiereInspeccion,
    Guid ViajeId,
    string? CodigoMtrack,
    string EstadoUnidad,
    NoSaleDto? NoSale,
    IReadOnlyList<InventarioInspeccionDto> Inspecciones,
    int? Posicion = null);

public sealed record InventarioDto(
    Guid Id,
    string Vin,
    string Marca,
    string? TipoVehiculo,
    string? Modelo,
    string? Chasis,
    string? Color,
    string Operacion,
    bool Activo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<InventarioEtapaDto> Etapas);
