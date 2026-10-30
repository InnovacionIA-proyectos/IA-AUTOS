namespace IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

/// <summary>
/// La inspeccion toma del viaje de la etapa la placa, el trailer y el conductor: no se piden como texto. <c>Momento</c> es <c>salida</c> o
/// <c>llegada</c>. <c>MotivoSinInspector</c> (codigo activo del catalogo <c>motivos-sin-inspector</c>) y <c>MotivoSinInspectorTexto</c> (de 1 a 300 caracteres, solo si el motivo lo
/// requiere) los envia el conductor del viaje cuando el inspector previsto no esta en la salida de un Cargue con personal de apoyo; en cualquier otro caso no se admiten (400).
/// </summary>
public sealed record CrearInspeccionRequest(
    Guid? EtapaId,
    string? Momento,
    DateTimeOffset? Fecha,
    double? Lat,
    double? Lng,
    string? Estado,
    string? Notas,
    string? MotivoSinInspector = null,
    string? MotivoSinInspectorTexto = null);

/// <summary>Cuerpo (opcional) de la correccion: la fecha por defecto es ahora y las notas empiezan vacias.</summary>
public sealed record CorregirInspeccionRequest(DateTimeOffset? Fecha, double? Lat, double? Lng, string? Notas);

/// <summary>
/// Una foto con su sello (la de un hallazgo o la de una constancia de no firma). <c>StoragePath</c> es el nombre del archivo que devolvio
/// <c>POST /api/uploads</c>. <c>HashSha256</c> es opcional: si llega se compara con el del archivo recibido y debe coincidir.
/// </summary>
public sealed record FotoEvidenciaRequest(
    string? StoragePath,
    DateTimeOffset? SelloFecha,
    double? SelloLat,
    double? SelloLng,
    double? Precision,
    string? DispositivoId,
    string? Nota,
    string? HashSha256);

/// <summary>
/// Un hallazgo exige su foto propia: <c>Evidencia</c> es obligatoria y se guarda junto con el hallazgo. La zona es <c>CuadranteNumero</c> + <c>Lado</c>
/// (lado obligatorio del 1 al 25 y prohibido del 26 en adelante) o <c>ZonaProvisional</c>: exactamente una de las dos formas. El nivel de severidad es
/// obligatorio para el inspector y el administrador; el conductor no lo envia. <c>EsPreexistente</c> exige <c>HallazgoOrigenId</c>: un hallazgo del mismo
/// vehiculo con la misma zona y clase.
/// </summary>
public sealed record CrearHallazgoRequest(
    int? CuadranteNumero,
    string? Lado,
    string? ZonaProvisional,
    string? ClaseDano,
    string? TamanoDano,
    string? NivelSeveridad,
    bool? EsPreexistente,
    Guid? HallazgoOrigenId,
    string? Nota,
    FotoEvidenciaRequest? Evidencia);

/// <summary>
/// Foto de una inspeccion: de una vista (<c>VistaId</c>, del catalogo vistas-foto), de un hallazgo (<c>HallazgoId</c>) o de un extra del vehiculo (<c>ExtraId</c>). Sin ubicacion
/// completa (<c>SelloLat</c> y <c>SelloLng</c>) la foto queda «sin ubicacion». Las fotos de vistas adicionales exigen <c>Nota</c>.
/// </summary>
public sealed record CrearEvidenciaRequest(
    Guid? HallazgoId,
    Guid? VistaId,
    string? StoragePath,
    DateTimeOffset? SelloFecha,
    double? SelloLat,
    double? SelloLng,
    double? Precision,
    string? DispositivoId,
    string? Nota,
    string? HashSha256,
    Guid? ExtraId = null);

/// <summary>Un extra nuevo: <c>Tipo</c> es el codigo de un tipo activo de <c>tipos-extra</c>; <c>Texto</c> es obligatorio con el tipo «otro» y no se admite en los demas; <c>Observacion</c> es opcional (hasta 500 caracteres).</summary>
public sealed record CrearExtraRequest(string? Tipo, string? Texto, string? Observacion);

/// <summary>Cambia un extra: se envian los tres campos y reemplazan a los guardados (lo que no se envia queda sin valor).</summary>
public sealed record ActualizarExtraRequest(string? Tipo, string? Texto, string? Observacion);

/// <summary>
/// Firma de la transferencia. La firma es SIEMPRE la de quien la envia (sale del token): si es quien entrega firma la entrega y si es quien
/// recibe firma el recibo; nadie firma por otro. Los dos ids son obligatorios la primera vez (crean la transferencia, y deben ser usuarios
/// del viaje) y opcionales despues (si llegan, deben ser los mismos).
/// </summary>
public sealed record FirmarTransferenciaRequest(Guid? EntregaUsuarioId, Guid? RecibeUsuarioId, string? Firma);

/// <summary>
/// El recibo sin entrega: la firma de quien recibe (el conductor del viaje) cuando el conductor hizo la salida del cargue y nadie de Botero Soto firma la entrega. La identidad sale del token.
/// </summary>
public sealed record ReciboSinEntregaRequest(string? Firma);

/// <summary>Quien recibe firma «con reservas»: su firma mas las observaciones, que son obligatorias.</summary>
public sealed record RecibirConReservasRequest(Guid? EntregaUsuarioId, Guid? RecibeUsuarioId, string? Firma, string? Reservas);

/// <summary>
/// Constancia de que una parte (<c>Parte</c>: <c>entrega</c> o <c>recibe</c>) se nego a firmar. La registra la otra parte y exige el motivo, un
/// testigo con usuario y la foto de la constancia.
/// </summary>
public sealed record RegistrarNoFirmaRequest(
    Guid? EntregaUsuarioId,
    Guid? RecibeUsuarioId,
    string? Parte,
    string? Motivo,
    Guid? TestigoUsuarioId,
    FotoEvidenciaRequest? Evidencia);

/// <summary>Un usuario del viaje que puede firmar una transferencia.</summary>
public sealed record FirmanteDto(Guid Id, string Nombre, string Rol);

/// <summary>
/// Filtros del listado tal como llegan en la consulta (texto); se validan en el servicio. <c>Pagina</c>, <c>Tamano</c> y <c>Orden</c> son el formato comun de los listados; la exportacion a Excel
/// los ignora.
/// </summary>
public sealed record FiltrosInspeccionesRequest(
    string? Vin,
    string? UsuarioId,
    string? ViajeId,
    string? Momento,
    string? Estado,
    string? Proceso,
    string? Operacion,
    string? Marca,
    string? ClienteId,
    string? OrigenId,
    string? DestinoId,
    string? Desde,
    string? Hasta,
    string? Pagina = null,
    string? Tamano = null,
    string? Orden = null);

/// <summary>Los filtros ya validados. <c>OrigenId</c> y <c>DestinoId</c> son los del viaje de la inspeccion; <c>Operacion</c>, <c>Marca</c> y <c>ClienteId</c>, los de su vehiculo.</summary>
public sealed record FiltrosInspecciones(
    string? Vin,
    Guid? UsuarioId,
    Guid? ViajeId,
    string? Momento,
    string? Estado,
    string? Proceso,
    string? Operacion,
    string? Marca,
    Guid? ClienteId,
    Guid? OrigenId,
    Guid? DestinoId,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta);

/// <summary>Fila del listado de inspecciones: los datos de la inspeccion mas el VIN y el proceso. La placa, el trailer y el conductor salen del viaje.</summary>
public sealed record InspeccionListadoDto(
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
    string Vin,
    string Proceso,
    DateTimeOffset IniciadaAt,
    DateTimeOffset? CerradaAt,
    int Version,
    Guid? CorrigeInspeccionId,
    string MomentoNombre);

/// <summary>Transferencia con nombres y firmas dibujadas, para la revision del informe en la web.</summary>
public sealed record TransferenciaDetalleDto(
    Guid Id,
    Guid InspeccionId,
    Guid? EntregaUsuarioId,
    Guid RecibeUsuarioId,
    bool Firmada,
    DateTimeOffset? FirmadaAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? EntregaNombre,
    string? RecibeNombre,
    string? FirmaEntrega,
    string? FirmaRecibe,
    string? TipoCierre,
    DateTimeOffset? EntregaFirmadaAt,
    DateTimeOffset? RecibeFirmadaAt,
    string? EntregaFirmaIp,
    string? RecibeFirmaIp,
    string? ReservasTexto,
    string? NoFirmaParte,
    string? MotivoNoFirma,
    Guid? TestigoUsuarioId,
    string? TestigoNombre,
    Guid? EvidenciaConstanciaId,
    IReadOnlyList<NotaAclaratoriaDto> NotasAclaratorias);

/// <summary>
/// Informe completo para revision: inspeccion con su contexto (VIN, proceso, lugar, inspector), hallazgos, evidencias con URL
/// y transferencia. La placa, el conductor y el trailer son los del viaje (en informes anteriores a los viajes, los capturados en la app).
/// </summary>
public sealed record InformeDetalleDto(
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
    string Vin,
    string Marca,
    string? Modelo,
    string Proceso,
    string? Lugar,
    bool RequiereInspeccion,
    string? InspectorNombre,
    string? Ninera,
    string? CodigoMtrack,
    string? Unidad,
    IReadOnlyList<HallazgoDto> Hallazgos,
    IReadOnlyList<EvidenciaDto> Evidencias,
    TransferenciaDetalleDto? Transferencia,
    DateTimeOffset IniciadaAt,
    DateTimeOffset? CerradaAt,
    int Version,
    Guid? CorrigeInspeccionId,
    string MomentoNombre,
    IReadOnlyList<NotaAclaratoriaDto> NotasAclaratorias,
    MotivoSinInspectorDto? MotivoSinInspector,
    IReadOnlyList<NovedadProcesoDto> NovedadesProceso,
    IReadOnlyList<ExtraDto> Extras,
    ConfirmacionSitioDto? ConfirmacionSitio = null);

/// <summary>
/// El resultado de confirmar «en sitio» la evidencia sellada de una inspeccion de Cargue o Descargue: <c>Resultado</c> (<c>confirmado_en_sitio</c>, <c>fuera_de_rango</c> o <c>sin_confirmacion_posible</c>),
/// su nombre, la distancia (en metros) de la foto al centro del lugar cuando se pudo calcular y la foto que lo determino. Es una marca: nunca bloquea nada.
/// </summary>
public sealed record ConfirmacionSitioDto(string Resultado, string Nombre, double? DistanciaM, Guid? EvidenciaId);

public sealed record ResumenCierreDto(int Evidencias, int Hallazgos);

public sealed record CerrarInspeccionResponse(InspeccionDto Inspeccion, ResumenCierreDto Resumen);
