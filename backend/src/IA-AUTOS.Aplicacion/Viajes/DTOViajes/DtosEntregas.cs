namespace IAAutos.Aplicacion.Viajes.DTOViajes;

/// <summary>La hora y el lugar del momento en que una parte firmo (no cuando la firma llego al servidor). <c>SinUbicacion</c> es verdadero cuando la firma no trajo GPS.</summary>
public sealed record MomentoDeFirmaDto(DateTimeOffset? Momento, double? Lat, double? Lng, double? PrecisionM, bool SinUbicacion);

/// <summary>
/// El rechazo de un carro en su parada: el dano de su revision de llegada que lo motivo (<c>HallazgoId</c>) con su foto (<c>EvidenciaId</c>), quien lo marco y el motivo fijo <c>rechazado_por_novedad</c>.
/// </summary>
public sealed record RechazoDeEntregaDto(Guid HallazgoId, Guid? EvidenciaId, Guid MarcadoPor, string? MarcadoPorNombre, string Motivo);

/// <summary>
/// Una unidad en la entrega de una parada. <c>Estado</c>: <c>sin_inspeccion</c> (aun no tiene la inspeccion de llegada del Descargue, donde se firma), <c>pendiente</c> (se puede firmar o esta abierta), <c>cerrada</c> (su
/// transferencia ya tiene cierre: <c>firmada</c>, <c>con_reservas</c>, <c>no_firma</c> o <c>recibo_sin_entrega</c>) o <c>rechazado</c> (no se entrego: se rechazo por una novedad de su revision de llegada; sigue en el camion a cargo
/// del conductor y trae su <c>Rechazo</c>). Cada unidad conserva su propia transferencia.
/// <c>MotivoCierre</c> es el motivo escrito del cierre con una sola firma (<c>recibo_sin_entrega</c>) y es nulo en cualquier otro caso; <c>MotivoCodigo</c> es su motivo codificado (<c>se_nego_a_firmar</c>, <c>no_habia_nadie</c>,
/// <c>sin_inspector_del_punto</c> u <c>otro</c> en los cierres anteriores a que se codificara); <c>FotosDeCierre</c> cuenta las fotos de ese cierre y <c>TestigoNombre</c> es el testigo sin usuario (nunca su cedula);
/// <c>NoFirmaParte</c> dice cual parte no firmo (la otra es la que firmo). <c>Reservas</c> son las observaciones de quien recibe sobre este carro. <c>EntregaMomento</c> y <c>RecibeMomento</c> son la hora y la ubicacion del momento
/// de cada firma, aparte de <c>EntregaFirmadaAt</c> y <c>RecibeFirmadaAt</c>, que son cuando llegaron al servidor. <c>DanosNuevos</c> cuenta los danos nuevos de su revision de llegada.
/// <c>RecibeExternoNombre</c> y <c>RecibeExternoCedula</c> son los de la persona que recibio cuando no es un usuario (el punto no tiene inspector) y son nulos si recibio un usuario o si nadie recibio.
/// <c>EntregadaSinInspectorDelPunto</c> es verdadero en el carro que se entrego en una parada con inspectores sin que el inspector del punto estuviera (el conductor cerro la parada con el plan B); un carro rechazado nunca la lleva.
/// </summary>
public sealed record EntregaUnidadDto(
    Guid VehiculoId,
    string Vin,
    string Estado,
    Guid? InspeccionId,
    string? TipoCierre,
    DateTimeOffset? EntregaFirmadaAt,
    DateTimeOffset? RecibeFirmadaAt,
    string? NoFirmaParte,
    string? MotivoCierre = null,
    string? RecibeExternoNombre = null,
    string? RecibeExternoCedula = null,
    string? MotivoCodigo = null,
    string? Reservas = null,
    int FotosDeCierre = 0,
    string? TestigoNombre = null,
    MomentoDeFirmaDto? EntregaMomento = null,
    MomentoDeFirmaDto? RecibeMomento = null,
    int DanosNuevos = 0,
    RechazoDeEntregaDto? Rechazo = null,
    bool EntregadaSinInspectorDelPunto = false);

/// <summary>
/// Las unidades del viaje que se entregan en una parada (un lugar de destino), con lo que falta de cada una. <c>Entregada</c> es verdadero cuando ninguna esta pendiente y al menos una se entrego.
/// <c>LugarTieneInspector</c> dice si el punto de destino tiene al menos un inspector activo asignado (la misma regla con la que el servidor decide quien firma la entrega); nunca trae quienes son.
/// <c>EstadoParada</c>: <c>pendiente</c>, <c>entregada</c>, <c>entregada_con_rechazos</c> o <c>firmada_todos_rechazados</c> (la parada se firmo como constancia del rechazo de cada carro y ninguno se entrego).
/// <c>Receptor</c> es quien recibe: <c>inspector_del_punto</c> (el que tomo la entrega de esta parada), <c>persona_del_lugar</c> (el punto no tiene inspectores) o <c>sin_receptor</c> (el punto tiene inspectores y nadie
/// tomo la entrega: no se puede firmar), con su <c>ReceptorNombre</c> cuando lo hay. <c>ConductorLlego</c> dice si el conductor ya marco «Llegue» en la parada (algun carro de la parada tiene su llegada de Transporte
/// confirmada): la firma del inspector solo se activa entonces.
/// <c>Espera</c> es la ultima espera del inspector del punto de la parada (nula si el conductor nunca activo «No hay inspector» en ella) y <c>EsperaAvisoMinutos</c> y <c>EsperaPlanBMinutos</c> son X e Y vigentes, para que la app
/// cuente los plazos sin senal. <c>DanosPorCalificar</c> son los danos sin nivel que el conductor marco en las revisiones de llegada de la parada (los que el inspector que la tomo califica antes de firmar); va vacia si no hay.
/// </summary>
public sealed record EntregaPuntoDto(
    Guid ViajeId,
    string ViajeEstado,
    Guid LugarId,
    string Lugar,
    string? Ciudad,
    int UnidadesTotal,
    int UnidadesPendientes,
    bool Entregada,
    IReadOnlyList<EntregaUnidadDto> Unidades,
    bool LugarTieneInspector = false,
    string EstadoParada = "pendiente",
    string Receptor = "sin_receptor",
    string? ReceptorNombre = null,
    bool ConductorLlego = false,
    EsperaDelPuntoDto? Espera = null,
    int EsperaAvisoMinutos = 15,
    int EsperaPlanBMinutos = 30,
    IReadOnlyList<DanoPorCalificarDto>? DanosPorCalificar = null);

/// <summary>
/// La espera del inspector del punto de una parada. <c>Estado</c>: <c>esperando</c>, <c>inspector_llego</c> (con el inspector y la hora), <c>cerrada_sin_inspector_del_punto</c> (con la hora del cierre) o <c>anulada</c>.
/// <c>ActivadaAt</c> es la hora del celular del conductor y <c>RecibidaAt</c> cuando llego al servidor; <c>AvisoDesde</c> y <c>PlanBDesde</c> son cuando se avisa al coordinador (X minutos) y cuando se habilita el plan B (X mas Y
/// minutos), con los parametros vigentes; <c>AvisoAt</c> es cuando se aviso (nulo si aun no). <c>PlanBHabilitado</c> es verdadero con la espera abierta y el plazo cumplido.
/// </summary>
public sealed record EsperaDelPuntoDto(
    Guid Id,
    Guid ViajeId,
    Guid LugarId,
    string Estado,
    Guid ConductorId,
    string MotivoSinInspector,
    string? MotivoSinInspectorTexto,
    DateTimeOffset ActivadaAt,
    DateTimeOffset RecibidaAt,
    double? Lat,
    double? Lng,
    double? PrecisionM,
    DateTimeOffset AvisoDesde,
    DateTimeOffset? AvisoAt,
    DateTimeOffset PlanBDesde,
    bool PlanBHabilitado,
    Guid? InspectorId,
    string? InspectorNombre,
    DateTimeOffset? InspectorLlegoAt,
    DateTimeOffset? CerradaAt);

/// <summary>Un dano sin nivel que el conductor marco en la revision de llegada de un carro de la parada: el <c>HallazgoId</c>, el <c>Vin</c>, la parte del carro, su clase y su primera foto.</summary>
public sealed record DanoPorCalificarDto(Guid HallazgoId, string Vin, string? Parte, string? ClaseDano, Guid? EvidenciaId);

/// <summary>
/// El conductor activa «No hay inspector» en una parada con inspectores donde nadie tomo la entrega: el <c>MotivoSinInspector</c> (codigo activo de <c>motivos-sin-inspector</c>), su texto si el motivo lo pide (hasta 300), y la
/// hora (<c>FechaMomento</c>, ISO 8601 con zona) y el GPS (<c>Lat</c>, <c>Lng</c>, <c>PrecisionM</c>) del celular en el momento en que la activo.
/// </summary>
public sealed record ActivarSinInspectorRequest(
    string? MotivoSinInspector,
    string? MotivoSinInspectorTexto = null,
    string? FechaMomento = null,
    double? Lat = null,
    double? Lng = null,
    double? PrecisionM = null);

/// <summary>Todas las entregas del viaje, una por lugar de destino. <c>UnidadesSinDestino</c> cuenta las unidades activas de viajes anteriores al destino por unidad (sin lugar).</summary>
public sealed record EntregasDelViajeDto(Guid ViajeId, string ViajeEstado, int UnidadesSinDestino, IReadOnlyList<EntregaPuntoDto> Puntos);

/// <summary>El carro que se rechaza en su parada: su <c>Vin</c> y el <c>HallazgoId</c> del dano de su revision de llegada que lo motiva (con foto).</summary>
public sealed record RechazoDeEntregaRequest(string? Vin, Guid? HallazgoId);

/// <summary>
/// La firma de una parte para todas las unidades pendientes de una parada. Quien entrega es el conductor del viaje y quien recibe, el inspector que tomo la entrega de ESA parada o, donde el punto no tiene inspectores,
/// la persona del lugar: <c>RecibeNombre</c>, <c>RecibeCedula</c> y <c>FirmaRecibe</c> (su firma) son obligatorios, <c>Firma</c> es la de quien entrega y la entrega queda cerrada como «firmada». En un punto CON inspectores
/// esos tres campos se rechazan: la firma el inspector. <c>Reservas</c> son las observaciones de quien recibe por carro (de 1 a 1000 caracteres; con persona del lugar las deja quien entrega en su nombre) y <c>Rechazos</c> los carros
/// que no se entregan por una novedad de su revision de llegada (solo los manda el inspector que tomo la entrega o, donde no hay inspectores, el conductor). <c>FechaMomento</c> (ISO 8601 con zona), <c>Lat</c>, <c>Lng</c> y
/// <c>PrecisionM</c> son la hora y el GPS del momento en que se firmo: se guardan aparte de cuando llega la firma al servidor, y sin GPS la firma queda «sin ubicacion».
/// </summary>
public sealed record FirmarEntregaRequest(
    string? Firma,
    string? RecibeNombre = null,
    string? RecibeCedula = null,
    string? FirmaRecibe = null,
    IReadOnlyList<ReservaDelCargueRequest>? Reservas = null,
    IReadOnlyList<RechazoDeEntregaRequest>? Rechazos = null,
    string? FechaMomento = null,
    double? Lat = null,
    double? Lng = null,
    double? PrecisionM = null);

/// <summary>Una foto del cierre de la entrega con una sola firma: la foto sellada (como la de una constancia) y el <c>Vin</c> del carro al que corresponde.</summary>
public sealed record FotoDeCierreRequest(
    string? Vin,
    string? StoragePath,
    DateTimeOffset? SelloFecha,
    double? SelloLat,
    double? SelloLng,
    double? Precision,
    string? DispositivoId,
    string? Nota,
    string? HashSha256);

/// <summary>
/// Cierra la entrega de una parada con una sola firma: el <c>MotivoCodigo</c> es obligatorio (<c>se_nego_a_firmar</c>, <c>no_habia_nadie</c> o <c>sin_inspector_del_punto</c>), con su <c>MotivoTexto</c> opcional (hasta 300
/// caracteres), las <c>Fotos</c> (selladas, cada una con el VIN de su carro; <c>se_nego_a_firmar</c> exige al menos una), un testigo sin usuario (<c>TestigoNombre</c> y <c>TestigoCedula</c>, juntos y opcionales) y la hora y el GPS
/// del momento. La <c>Firma</c> solo hace falta si quien cierra aun no habia firmado. Quien cierra es quien entrega o quien recibe; la otra parte no puede haber firmado. Cierra todos los carros pendientes de la parada.
/// </summary>
public sealed record CerrarEntregaUnaFirmaRequest(
    string? Firma,
    string? MotivoCodigo,
    string? MotivoTexto = null,
    IReadOnlyList<FotoDeCierreRequest>? Fotos = null,
    string? TestigoNombre = null,
    string? TestigoCedula = null,
    string? FechaMomento = null,
    double? Lat = null,
    double? Lng = null,
    double? PrecisionM = null);
