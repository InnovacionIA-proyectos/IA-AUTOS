using IAAutos.Aplicacion.Auth.DTOAuth;

namespace IAAutos.Aplicacion.Viajes.DTOViajes;

/// <summary>Una unidad (VIN) que entra a un viaje con el lugar donde se entrega: el destino es de cada unidad.</summary>
public sealed record UnidadDelViajeRequest(string? Vin, Guid? LugarDestinoId);

/// <summary>
/// Viaje nuevo: unidad transportadora, placa, trailer, conductor, codigo MTrack y, opcionalmente, sus unidades (cada una con su VIN y el lugar de destino, un lugar activo del catalogo <c>lugares</c>). El viaje
/// ya no tiene un destino unico: <c>destino_id</c> y <c>vines</c> se rechazan.
/// </summary>
public sealed record CrearViajeRequest(
    string? CodigoMtrack,
    Guid? TipoUnidadId,
    string? Placa,
    string? Trailer,
    Guid? ConductorId,
    Guid? InspectorId,
    Guid? OrigenId,
    IReadOnlyList<UnidadDelViajeRequest>? Unidades);

/// <summary>
/// Campos que admite el cambio de un viaje. Solo se cambia lo enviado; trailer, conductor, inspector y origen admiten
/// nulo para quitarlos. El destino es de cada unidad (<c>PATCH /api/viajes/{id}/vehiculos/{vin}</c>) y el inspector lo llena el reclamo del cargue: <c>inspector_id</c> ya no se acepta. <c>FechaProgramada</c> (fecha y hora con zona horaria) y <c>LugarCargueId</c> (un lugar activo del catalogo <c>lugares</c>) se corrigen solo mientras el viaje esta programado. Este tipo describe el cuerpo en el contrato: el servicio lo lee campo a campo.
/// </summary>
public sealed record ActualizarViajeRequest(
    string? CodigoMtrack,
    Guid? TipoUnidadId,
    string? Placa,
    string? Trailer,
    Guid? ConductorId,
    Guid? OrigenId,
    DateTimeOffset? FechaProgramada = null,
    Guid? LugarCargueId = null);

/// <summary>Unidades que se agregan a un viaje activo: cada una con su VIN y su lugar de destino.</summary>
public sealed record AgregarUnidadesRequest(IReadOnlyList<UnidadDelViajeRequest>? Unidades);

/// <summary>Cambia el lugar de destino de una unidad del viaje (un lugar activo del catalogo <c>lugares</c>).</summary>
public sealed record CambiarDestinoUnidadRequest(Guid? LugarDestinoId);

public sealed record CambiarEstadoViajeRequest(string? Estado);

/// <summary>
/// Una unidad que no sale del viaje: el motivo, el hallazgo que la causo, quien la marco (y su nombre), cuando y el VIN que la reemplaza (si ya se
/// asigno).
/// </summary>
public sealed record NoSaleDto(
    string Motivo,
    Guid HallazgoId,
    Guid MarcadoPor,
    string? MarcadoPorNombre,
    DateTimeOffset Fecha,
    string? VinReemplazo);

/// <summary>
/// Un VIN del viaje con el avance de sus etapas dentro de el. <c>EstadoUnidad</c> es <c>activa</c> o <c>no_sale</c>; en ese caso <c>NoSale</c> trae
/// el motivo y el hallazgo. <c>EstadoTransporte</c> dice como va la unidad en su recorrido (<c>sin_registro</c>, <c>con_novedad</c>, <c>sin_novedad</c> o <c>llegada_con_novedad</c>) y
/// <c>Novedades</c> cuantas novedades registro el conductor.
/// </summary>
public sealed record ViajeVehiculoDto(
    Guid VehiculoId,
    string Vin,
    string Marca,
    string? Modelo,
    string? Color,
    string Operacion,
    int Etapas,
    int Completas,
    string EstadoUnidad,
    NoSaleDto? NoSale,
    string EstadoTransporte,
    int Novedades,
    Guid? LugarDestinoId = null,
    string? LugarDestino = null);

/// <summary>
/// Algo que paso en el proceso del viaje y no es un dano (una novedad de proceso): <c>Tipo</c> es <c>cargue_sin_inspector</c> (la excepcion «No hay inspector» que el conductor activo en el momento) o
/// <c>recibo_sin_entrega</c>. Trae el motivo (codigo y nombre del catalogo <c>motivos-sin-inspector</c> y el texto), quien la registro, cuando, el VIN de la unidad y la inspeccion a la que corresponde. Es un registro
/// aparte: no cuenta en los patrones por zona ni en la tasa de novedad.
/// </summary>
public sealed record NovedadDelViajeDto(
    string Tipo,
    string? MotivoCodigo,
    string? Motivo,
    string? Texto,
    string? UsuarioNombre,
    DateTimeOffset Fecha,
    string Vin,
    Guid InspeccionId);

/// <summary>
/// Viaje completo con sus VIN. <c>NovedadesProceso</c> son las novedades de proceso de sus unidades (la excepcion «No hay inspector» y el recibo sin entrega), de la mas antigua a la mas reciente. El viaje no tiene un cliente propio: <c>Clientes</c>
/// son los de los VIN que quien consulta puede ver (todos para el conductor, el inspector y el super administrador; solo los de su cliente
/// para el resto) y <c>Vehiculos</c> trae esos mismos VIN.
/// </summary>
public sealed record ViajeDto(
    Guid Id,
    string? CodigoMtrack,
    Guid? TipoUnidadId,
    string? Unidad,
    string? Placa,
    string? Trailer,
    Guid? ConductorId,
    string? ConductorNombre,
    Guid? InspectorId,
    string? InspectorNombre,
    Guid? OrigenId,
    string? Origen,
    Guid? DestinoId,
    string? Destino,
    string Estado,
    IReadOnlyList<ViajeVehiculoDto> Vehiculos,
    IReadOnlyList<ClienteDto> Clientes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<NovedadDelViajeDto> NovedadesProceso,
    DateTimeOffset? FechaProgramada = null,
    Guid? LugarCargueId = null,
    string? LugarCargue = null);

/// <summary>Fila del listado de viajes: los datos del viaje, el conteo de sus VIN y los clientes de esos VIN (los que quien consulta puede ver).</summary>
public sealed record ViajeResumenDto(
    Guid Id,
    string? CodigoMtrack,
    Guid? TipoUnidadId,
    string? Unidad,
    string? Placa,
    string? Trailer,
    Guid? ConductorId,
    string? ConductorNombre,
    Guid? InspectorId,
    string? InspectorNombre,
    Guid? OrigenId,
    string? Origen,
    Guid? DestinoId,
    string? Destino,
    string Estado,
    int TotalVehiculos,
    int VehiculosCompletos,
    IReadOnlyList<ClienteDto> Clientes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FechaProgramada = null,
    Guid? LugarCargueId = null,
    string? LugarCargue = null);

/// <summary>
/// Filtros del listado tal como llegan en la consulta (texto); se validan en el servicio. <c>Pagina</c>, <c>Tamano</c> y <c>Orden</c> son el formato comun de los listados; la exportacion a Excel los
/// ignora.
/// </summary>
public sealed record FiltrosViajesRequest(
    string? Estado,
    string? CodigoMtrack,
    string? Vin,
    string? ConductorId,
    string? InspectorId,
    string? ClienteId,
    string? OrigenId,
    string? DestinoId,
    string? Desde,
    string? Hasta,
    string? Pagina = null,
    string? Tamano = null,
    string? Orden = null);

/// <summary>Los filtros ya validados. <c>ClienteId</c> pide los viajes con algun VIN de ese cliente.</summary>
public sealed record FiltrosViajes(
    string? Estado,
    string? CodigoMtrack,
    string? Vin,
    Guid? ConductorId,
    Guid? InspectorId,
    Guid? ClienteId,
    Guid? OrigenId,
    Guid? DestinoId,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta);

/// <summary>
/// Fila de la carga masiva de viajes (una por VIN). Las filas con el mismo codigo MTrack forman un viaje. <c>Fila</c> es el
/// numero de fila del archivo (para mostrar el error donde esta); si no llega se usa la posicion en la lista. <c>FechaProgramada</c> es la fecha y hora programada del cargue
/// («2026-11-03 06:00», hora de Colombia salvo que traiga su zona horaria), <c>Origen</c> el nombre del lugar de cargue y <c>Destino</c> el nombre del lugar de destino de ESA unidad
/// (ambos del catalogo <c>lugares</c>); los tres son obligatorios, y la fecha y el origen deben ser los mismos en todas las filas de un codigo MTrack.
/// </summary>
public sealed record FilaCargaViajeRequest(
    int? Fila,
    string? Vin,
    string? Placa,
    string? Trailer,
    string? CedulaConductor,
    string? Unidad,
    string? CodigoMtrack,
    string? FechaProgramada = null,
    string? Origen = null,
    string? Destino = null);

public sealed record CargarViajesRequest(IReadOnlyList<FilaCargaViajeRequest>? Filas);

/// <summary>Resultado de una fila: valida o con sus errores; si el viaje se guardo, su id.</summary>
public sealed record FilaCargaViajeDto(int Fila, string? Vin, string? CodigoMtrack, bool Valida, IReadOnlyList<string> Errores, Guid? ViajeId);

/// <summary>
/// Vista previa o resultado de la carga. <c>Confirmado</c> es falso en la vista previa (no se guarda nada). <c>Viajes</c> es la
/// cantidad de viajes que se crearian (vista previa) o que se crearon (confirmacion).
/// </summary>
public sealed record ResultadoCargaViajesDto(
    bool Confirmado,
    int Total,
    int Validas,
    int Invalidas,
    int Viajes,
    IReadOnlyList<FilaCargaViajeDto> Filas);
