using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Parametros;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Alertas;

/// <summary>Cuantos hallazgos tiene un cliente en una zona o en un proceso dentro de la ventana. <c>Clave</c> identifica la zona o el proceso y <c>Nombre</c> es como se muestra.</summary>
public sealed record clsConteoPatron(Guid ClienteId, string Clave, string Nombre, int Total, int? CuadranteNumero = null, string? Lado = null);

/// <summary>Una inspeccion en borrador de Cargue o Descargue que sigue abierta: quien la inicio, a que unidad y proceso, y si su creador es un conductor en un cargue sin personal de apoyo.</summary>
public sealed record clsCapturaAbierta(
    Guid InspeccionId, Guid EtapaId, Guid ViajeId, Guid VehiculoId, Guid ClienteId, string Vin, string Proceso, Guid UsuarioId, string RolCreador, DateTimeOffset IniciadaAt, string Momento = clsMomento.LLEGADA);

/// <summary>
/// Una llegada cerrada cuya transferencia no tiene tipo de cierre: a quien le falta firmar (si la transferencia existe) o, si no existe, el conductor y el inspector del viaje y el proceso de la
/// etapa que sigue.
/// </summary>
/// <summary>
/// La salida del cargue de un carro, cerrada antes del limite, cuya transferencia (la de la firma por niñera) no tiene la firma de una parte: <c>FaltaEntrega</c> es verdadero si falta la del inspector del viaje y
/// <c>FaltaRecibe</c> si falta la del conductor (sin transferencia faltan las dos).
/// </summary>
public sealed record clsFirmaCarguePendiente(Guid ViajeId, Guid ClienteId, Guid VehiculoId, string Vin, bool FaltaEntrega, bool FaltaRecibe, Guid? InspectorId, Guid? ConductorId);

public sealed record clsTransferenciaPendiente(
    Guid InspeccionId, Guid ViajeId, Guid VehiculoId, Guid ClienteId, string Vin, string Proceso, bool ExisteTransferencia,
    Guid? EntregaUsuarioId, bool FaltaEntrega, Guid? RecibeUsuarioId, bool FaltaRecibe, string? ProcesoSiguiente, Guid? ConductorId, Guid? InspectorId);

/// <summary>Un camion reclamado (reclamo activo) por un inspector que aun no inicio ni cerro ninguna inspeccion del viaje despues de reclamarlo.</summary>
public sealed record clsReclamoSinActividad(
    Guid ReclamoId, Guid ViajeId, string? CodigoMtrack, string? Placa, string Proceso, Guid InspectorId, string? InspectorNombre, Guid? ClienteId, DateTimeOffset ReclamadoAt, string? Parada = null);

/// <summary>Una foto sellada con ubicacion de una visita a un lugar sin ubicacion confirmada: de que lugar, de que viaje (la visita), de que cliente y sus coordenadas.</summary>
public sealed record clsFotoDeLugar(Guid LugarId, string LugarNombre, Guid ViajeId, Guid ClienteId, double Lat, double Lng);

/// <summary>Un viaje en curso, por cliente de sus unidades, y el momento de su ultimo movimiento (la ultima inspeccion creada o cerrada, novedad, llegada o firma).</summary>
public sealed record clsViajeSinMovimiento(Guid ViajeId, string? CodigoMtrack, Guid ClienteId, DateTimeOffset UltimoMovimientoAt);

/// <summary>Un cargue programado cuya hora ya paso y que nadie reclamo: el viaje, su lugar, su hora programada y el cliente de una de sus unidades.</summary>
public sealed record clsCargueSinReclamar(Guid ViajeId, string? CodigoMtrack, string? Placa, DateTimeOffset FechaProgramada, string? Lugar, Guid ClienteId);

/// <summary>Consultas de solo lectura de los procesos de alertas.</summary>
public interface IConsultasAlertas
{
    /// <summary>
    /// Las fotos selladas de Cargue y Descargue con ubicacion (no (0, 0)) y con precision menor que <paramref name="valvulaPrecisionM"/>, de los lugares activos cuya ubicacion no esta confirmada (el lugar de cargue
    /// del viaje y el destino de la unidad), para la autocalibracion.
    /// </summary>
    Task<IReadOnlyList<clsFotoDeLugar>> FotosPorLugarAsync(double valvulaPrecisionM, int limite, CancellationToken ct = default);

    /// <summary>
    /// Los viajes en curso (uno por cliente de sus unidades) cuyo ultimo movimiento fue antes de <paramref name="sinMovimientoDesde"/>: el movimiento es lo mas reciente entre una inspeccion creada o cerrada, un dano o novedad de
    /// una inspeccion, una novedad de proceso y una firma de entrega o de recibo (la llegada de una unidad es una inspeccion cerrada); sin ninguno, la ultima vez que se toco el viaje.
    /// </summary>
    Task<IReadOnlyList<clsViajeSinMovimiento>> ViajesSinMovimientoAsync(DateTimeOffset sinMovimientoDesde, int limite, CancellationToken ct = default);

    /// <summary>Los viajes programados con fecha programada entre <paramref name="programadosDesde"/> y <paramref name="programadosAntesDe"/>, sin ningun reclamo activo del cargue ni ninguna inspeccion iniciada.</summary>
    Task<IReadOnlyList<clsCargueSinReclamar>> CarguesSinReclamarAsync(DateTimeOffset programadosDesde, DateTimeOffset programadosAntesDe, int limite, CancellationToken ct = default);

    /// <summary>
    /// Los reclamos activos hechos antes de <paramref name="reclamadosAntesDe"/>, de viajes activos, sin ninguna inspeccion del viaje iniciada o cerrada despues del reclamo (la «actividad»).
    /// </summary>
    Task<IReadOnlyList<clsReclamoSinActividad>> ReclamosSinActividadAsync(DateTimeOffset reclamadosAntesDe, int limite, CancellationToken ct = default);

    /// <summary>Hallazgos de inspecciones cerradas desde <paramref name="desde"/>, contados por cliente y zona (lado + numero).</summary>
    Task<IReadOnlyList<clsConteoPatron>> ConteoPorZonaAsync(DateTimeOffset desde, CancellationToken ct = default);

    /// <summary>Hallazgos de inspecciones cerradas desde <paramref name="desde"/>, contados por cliente y proceso.</summary>
    Task<IReadOnlyList<clsConteoPatron>> ConteoPorProcesoAsync(DateTimeOffset desde, CancellationToken ct = default);

    /// <summary>Inspecciones en borrador de Cargue o Descargue, de viajes activos, iniciadas antes de <paramref name="iniciadasAntesDe"/>.</summary>
    Task<IReadOnlyList<clsCapturaAbierta>> CapturasAbiertasAsync(DateTimeOffset iniciadasAntesDe, int limite, CancellationToken ct = default);

    /// <summary>Llegadas cerradas antes de <paramref name="cerradasAntesDe"/>, de unidades que salen de viajes activos, con la transferencia sin tipo de cierre (o sin transferencia).</summary>
    Task<IReadOnlyList<clsTransferenciaPendiente>> TransferenciasPendientesAsync(DateTimeOffset cerradasAntesDe, int limite, CancellationToken ct = default);

    /// <summary>
    /// Los carros activos de viajes activos cuya salida del cargue (ultima version, sin correccion) se cerro antes de <paramref name="cerradasAntesDe"/> y cuyo cargue sigue sin entregarse cerrado (sin ninguna transferencia con
    /// tipo de cierre en la etapa): con lo que falta firmar de cada uno.
    /// </summary>
    Task<IReadOnlyList<clsFirmaCarguePendiente>> FirmasDelCarguePendientesAsync(DateTimeOffset cerradasAntesDe, int limite, CancellationToken ct = default);
}

/// <summary>
/// Los procesos periodicos de alertas. Cada alerta lleva su clave de dedupe, asi que correr un proceso otra vez (o dos instancias a la vez) no repite lo ya avisado. Los umbrales y los minutos de
/// espera se leen de los parametros en cada ejecucion. Una alerta preventiva (patron) va al coordinador de calidad y a los administradores del cliente; un aviso operativo (captura incompleta y
/// transferencia pendiente) va a quien tiene la tarea pendiente. Todo por bandeja y push, nunca por correo.
/// </summary>
public sealed class clsAlertasPeriodicas(
    IConsultasAlertas consultas,
    IParametros parametros,
    IUsuarioRepository usuarios,
    IEtapaRepository etapas,
    IInspeccionRepository inspecciones,
    IPropuestaUbicacionRepository propuestas,
    IUnidadDeTrabajo unidadDeTrabajo,
    clsCalculadoraVistas calculadoraVistas,
    clsEmisorNotificaciones emisor,
    IReloj reloj,
    ILogger<clsAlertasPeriodicas> registro,
    IAAutos.Aplicacion.Viajes.clsPermisosSinInspectorService permisosSinInspector,
    IAAutos.Aplicacion.Viajes.clsEntregasService entregas)
{
    public const int UMBRAL_POR_DEFECTO = 10;
    public const int VENTANA_DIAS_POR_DEFECTO = 30;
    public const int CAPTURA_MINUTOS_POR_DEFECTO = 30;
    public const int TRANSFERENCIA_MINUTOS_POR_DEFECTO = 240;

    /// <summary>Minutos por defecto (si el parametro <c>firma_cargue_pendiente_minutos</c> no existe) que la salida del cargue espera sin la firma de una parte antes de avisarle.</summary>
    public const int FIRMA_CARGUE_MINUTOS_POR_DEFECTO = 30;
    public const int RECLAMO_MINUTOS_POR_DEFECTO = 30;
    public const int CARGUE_SIN_RECLAMAR_MINUTOS_POR_DEFECTO = 30;
    public const int VIAJE_SIN_MOVIMIENTO_HORAS_POR_DEFECTO = 24;
    public const int VALVULA_PRECISION_POR_DEFECTO_M = 500;

    /// <summary>Un cargue sin reclamar solo se avisa mientras su hora programada sea de las ultimas horas: un viaje programado hace dias que nadie reclamo ya no es un aviso operativo.</summary>
    private const int VENTANA_CARGUE_SIN_RECLAMAR_HORAS = 24;
    private const int LIMITE_FOTOS_AUTOCALIBRACION = 20000;
    private const int LIMITE_POR_EJECUCION = 500;

    /// <summary>
    /// Cuenta los hallazgos de la ventana (los ultimos <c>umbral_patron_ventana_dias</c>) por cliente y por zona (lado + numero) y por proceso; cuando una cuenta llega a <c>umbral_patron_cantidad</c>
    /// avisa a los coordinadores de calidad y administradores del cliente, una sola vez por zona o proceso y ventana (la ventana es un bloque de esos dias contado desde el 1 de enero de 2000).
    /// Devuelve cuantas notificaciones nuevas intento crear.
    /// </summary>
    public async Task<int> EjecutarUmbralesAsync(CancellationToken ct = default)
    {
        var ahora = reloj.Ahora;
        var umbral = Math.Max(await parametros.EnteroAsync(clsClavesParametros.UMBRAL_PATRON_CANTIDAD, UMBRAL_POR_DEFECTO, ct), 1);
        var dias = Math.Max(await parametros.EnteroAsync(clsClavesParametros.UMBRAL_PATRON_VENTANA_DIAS, VENTANA_DIAS_POR_DEFECTO, ct), 1);
        var bloque = (int)((ahora.UtcDateTime.Date - new DateTime(2000, 1, 1)).TotalDays / dias);
        var desde = ahora.AddDays(-dias);

        var superados = (await consultas.ConteoPorZonaAsync(desde, ct)).Select(c => (Conteo: c, Tipo: "zona"))
            .Concat((await consultas.ConteoPorProcesoAsync(desde, ct)).Select(c => (Conteo: c, Tipo: "proceso")))
            .Where(x => x.Conteo.Total >= umbral)
            .ToList();

        var emitidas = 0;
        foreach (var porCliente in superados.GroupBy(x => x.Conteo.ClienteId))
        {
            var destinatarios = (await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], porCliente.Key, ct)).Select(u => u.Id).ToList();
            foreach (var (conteo, tipo) in porCliente)
            {
                var que = tipo == "zona" ? $"La zona {conteo.Nombre}" : $"El proceso {conteo.Nombre}";
                await emisor.EmitirAsync(
                    new clsAlerta(
                        clsTipoNotificacion.UMBRAL_PATRON,
                        clsUrgenciaNotificacion.MEDIA,
                        destinatarios,
                        porCliente.Key,
                        null,
                        null,
                        null,
                        null,
                        $"Patrón de hallazgos: {conteo.Nombre}",
                        $"{que} acumula {conteo.Total} hallazgos en los últimos {dias} días (umbral {umbral}).",
                        $"{clsTipoNotificacion.UMBRAL_PATRON}:{porCliente.Key}:{tipo}:{conteo.Clave}:{bloque}",
                        // Los campos propios del aviso: la zona (numero de cuadrante y lado) o el proceso que superaron el umbral, para abrir el tablero ya filtrado.
                        new Dictionary<string, object?>
                        {
                            ["tipo_umbral"] = tipo,
                            ["cuadrante_numero"] = tipo == "zona" ? conteo.CuadranteNumero : null,
                            ["lado"] = tipo == "zona" ? conteo.Lado : null,
                            ["proceso"] = tipo == "proceso" ? conteo.Clave : null,
                            ["total"] = conteo.Total,
                            ["umbral"] = umbral,
                            ["ventana_dias"] = dias,
                        }),
                    ct);
                emitidas += destinatarios.Count;
            }
        }

        return emitidas;
    }

    /// <summary>
    /// Concede por tiempo los permisos de la excepcion «No hay inspector» que cumplieron 10 minutos sin respuesta (nunca antes) y avisa al conductor y al coordinador. Corre cada minuto, aparte de los demas avisos, para que
    /// la espera no dependa de que alguien abra una pantalla. Devuelve cuantos concedio.
    /// </summary>
    public Task<int> EjecutarPermisosSinInspectorAsync(CancellationToken ct = default) => permisosSinInspector.ConcederVencidosAsync(ct);

    /// <summary>
    /// Avisa al coordinador de calidad de las esperas del inspector del punto que cumplieron los minutos del parametro <c>espera_inspector_aviso_minutos</c> (15 por defecto) sin que nadie tomara la entrega, una vez por espera.
    /// Corre cada minuto, aparte de los demas avisos, para que no dependa de que alguien abra una pantalla. Devuelve cuantos avisos intento.
    /// </summary>
    public Task<int> EjecutarEsperasDelDestinoAsync(CancellationToken ct = default) => entregas.AvisarEsperasVencidasAsync(ct);

    /// <summary>Corre los avisos operativos: captura incompleta, transferencia pendiente y camion reclamado sin actividad. Devuelve cuantos avisos intento crear.</summary>
    public async Task<int> EjecutarAvisosAsync(CancellationToken ct = default) =>
        await EjecutarCapturasIncompletasAsync(ct) + await EjecutarTransferenciasPendientesAsync(ct) + await EjecutarReclamosSinActividadAsync(ct) + await EjecutarCarguesSinReclamarAsync(ct)
        + await EjecutarFirmasDelCarguePendientesAsync(ct) + await EjecutarViajesSinMovimientoAsync(ct);

    /// <summary>
    /// Avisa a los coordinadores de calidad del cliente de un viaje en curso que lleva <c>viaje_sin_movimiento_horas</c> (24 por defecto, ajustable sin desplegar) sin ningun movimiento: ni una inspeccion creada o cerrada, ni una
    /// novedad, ni una llegada, ni una firma. Un solo aviso por viaje y ultimo movimiento (urgencia media, solo bandeja): no se repite mientras no haya un movimiento nuevo y nunca avisa de un viaje finalizado o cancelado.
    /// </summary>
    public async Task<int> EjecutarViajesSinMovimientoAsync(CancellationToken ct = default)
    {
        var horas = Math.Max(await parametros.EnteroAsync(clsClavesParametros.VIAJE_SIN_MOVIMIENTO_HORAS, VIAJE_SIN_MOVIMIENTO_HORAS_POR_DEFECTO, ct), 1);
        var ahora = reloj.Ahora;
        var emitidas = 0;
        foreach (var viaje in await consultas.ViajesSinMovimientoAsync(ahora.AddHours(-horas), LIMITE_POR_EJECUCION, ct))
        {
            var destinatarios = (await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD], viaje.ClienteId, ct)).Select(u => u.Id).ToList();
            if (destinatarios.Count == 0)
            {
                continue;
            }

            var transcurridas = (int)Math.Floor((ahora - viaje.UltimoMovimientoAt).TotalHours);
            var codigo = viaje.CodigoMtrack ?? "sin código";
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.VIAJE_SIN_MOVIMIENTO,
                    clsUrgenciaNotificacion.MEDIA,
                    destinatarios,
                    viaje.ClienteId,
                    viaje.ViajeId,
                    null,
                    null,
                    null,
                    $"Viaje sin movimiento: {codigo}",
                    $"El viaje {codigo} lleva {transcurridas} horas sin movimiento: su último movimiento fue hace {transcurridas} horas.",
                    $"{clsTipoNotificacion.VIAJE_SIN_MOVIMIENTO}:{viaje.ViajeId}:{viaje.UltimoMovimientoAt.ToUnixTimeMilliseconds()}",
                    new Dictionary<string, object?>
                    {
                        ["viaje_id"] = viaje.ViajeId,
                        ["codigo_mtrack"] = viaje.CodigoMtrack,
                        ["ultimo_movimiento_at"] = viaje.UltimoMovimientoAt,
                        ["horas_sin_movimiento"] = transcurridas,
                    }),
                ct);
            emitidas += destinatarios.Count;
        }

        return emitidas;
    }

    /// <summary>
    /// Avisa a la parte de la ninera a la que le falta firmar el cargue: cuando la salida del cargue de un carro lleva cerrada <c>firma_cargue_pendiente_minutos</c> (30 por defecto, ajustable sin desplegar) sin que su
    /// transferencia tenga la firma de una parte, esa parte (el inspector del viaje si falta quien entrega, el conductor si falta quien recibe) recibe un aviso de urgencia media con los carros que faltan. Un solo aviso por viaje y
    /// parte: las corridas siguientes no lo repiten.
    /// </summary>
    public async Task<int> EjecutarFirmasDelCarguePendientesAsync(CancellationToken ct = default)
    {
        var minutos = Math.Max(await parametros.EnteroAsync(clsClavesParametros.FIRMA_CARGUE_PENDIENTE_MINUTOS, FIRMA_CARGUE_MINUTOS_POR_DEFECTO, ct), 1);
        var emitidas = 0;
        foreach (var delViaje in (await consultas.FirmasDelCarguePendientesAsync(reloj.Ahora.AddMinutes(-minutos), LIMITE_POR_EJECUCION, ct)).GroupBy(f => f.ViajeId))
        {
            foreach (var (parte, destinatario, vines) in new[]
                     {
                         (clsParteFirmante.ENTREGA, delViaje.First().InspectorId, delViaje.Where(f => f.FaltaEntrega).Select(f => f.Vin).Order(StringComparer.Ordinal).ToList()),
                         (clsParteFirmante.RECIBE, delViaje.First().ConductorId, delViaje.Where(f => f.FaltaRecibe).Select(f => f.Vin).Order(StringComparer.Ordinal).ToList()),
                     })
            {
                if (destinatario is not { } usuarioId || vines.Count == 0)
                {
                    continue;
                }

                await emisor.EmitirAsync(
                    new clsAlerta(
                        clsTipoNotificacion.FIRMA_CARGUE_PENDIENTE,
                        clsUrgenciaNotificacion.MEDIA,
                        [usuarioId],
                        delViaje.First().ClienteId,
                        delViaje.Key,
                        null,
                        null,
                        null,
                        "Firma del cargue pendiente",
                        $"La salida del cargue de {vines.Count} carro(s) lleva más de {minutos} minutos cerrada y falta su firma de la niñera ({(parte == clsParteFirmante.ENTREGA ? "entrega" : "recibo")}): {string.Join(", ", vines)}.",
                        $"{clsTipoNotificacion.FIRMA_CARGUE_PENDIENTE}:{delViaje.Key}:{parte}",
                        new Dictionary<string, object?> { ["viaje_id"] = delViaje.Key, ["vines"] = vines, ["parte_que_falta"] = parte }),
                    ct);
                emitidas++;
            }
        }

        return emitidas;
    }

    /// <summary>
    /// Avisa a los coordinadores de calidad del cliente de un cargue programado que lleva <c>cargue_sin_reclamar_minutos</c> (30 por defecto, ajustable sin desplegar) pasada su hora programada sin que ningun inspector lo
    /// haya reclamado ni iniciado. Un aviso por viaje: las corridas siguientes no lo repiten (urgencia media, bandeja y push).
    /// </summary>
    public async Task<int> EjecutarCarguesSinReclamarAsync(CancellationToken ct = default)
    {
        var minutos = Math.Max(await parametros.EnteroAsync(clsClavesParametros.CARGUE_SIN_RECLAMAR_MINUTOS, CARGUE_SIN_RECLAMAR_MINUTOS_POR_DEFECTO, ct), 1);
        var ahora = reloj.Ahora;
        var emitidas = 0;
        foreach (var cargue in await consultas.CarguesSinReclamarAsync(ahora.AddHours(-VENTANA_CARGUE_SIN_RECLAMAR_HORAS), ahora.AddMinutes(-minutos), LIMITE_POR_EJECUCION, ct))
        {
            var destinatarios = (await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD], cargue.ClienteId, ct)).Select(u => u.Id).ToList();
            if (destinatarios.Count == 0)
            {
                continue;
            }

            var hora = cargue.FechaProgramada.ToOffset(TimeSpan.FromHours(-5)).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var viaje = cargue.CodigoMtrack ?? cargue.Placa ?? "sin código";
            var lugar = cargue.Lugar is { } nombre ? $" en {nombre}" : string.Empty;
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.CARGUE_SIN_RECLAMAR,
                    clsUrgenciaNotificacion.MEDIA,
                    destinatarios,
                    cargue.ClienteId,
                    cargue.ViajeId,
                    null,
                    null,
                    null,
                    $"Cargue sin reclamar: {viaje}",
                    $"El cargue del viaje {viaje}{lugar} estaba programado para las {hora} y pasaron más de {minutos} minutos sin que ningún inspector lo reclame.",
                    $"{clsTipoNotificacion.CARGUE_SIN_RECLAMAR}:{cargue.ViajeId}"),
                ct);
            emitidas += destinatarios.Count;
        }

        return emitidas;
    }

    /// <summary>
    /// La autocalibracion de la ubicacion de los lugares: con las fotos selladas con ubicacion de al menos <c>propuesta_ubicacion_visitas</c> visitas distintas (3 por defecto, ajustable sin desplegar) a un lugar sin ubicacion confirmada
    /// propone el centro (mediana de las coordenadas) y el radio (segun su dispersion), crea la propuesta pendiente y avisa al coordinador de calidad. El lugar NO cambia mientras la propuesta siga pendiente. No
    /// repite una propuesta pendiente ni vuelve a proponer sin fotos nuevas despues de una decision. Devuelve cuantos avisos intento crear.
    /// </summary>
    public async Task<int> EjecutarAutocalibracionAsync(CancellationToken ct = default)
    {
        var valvula = await parametros.EnteroAsync(clsClavesParametros.VALVULA_PRECISION_M, VALVULA_PRECISION_POR_DEFECTO_M, ct);
        var visitasMinimas = Math.Max(await parametros.EnteroAsync(clsClavesParametros.PROPUESTA_UBICACION_VISITAS, clsAutocalibracion.VISITAS_MINIMAS, ct), 1);
        var porLugar = (await consultas.FotosPorLugarAsync(valvula, LIMITE_FOTOS_AUTOCALIBRACION, ct)).GroupBy(f => f.LugarId).ToList();
        if (porLugar.Count == 0)
        {
            return 0;
        }

        var previas = await propuestas.UltimaPorLugarAsync(porLugar.Select(g => g.Key).ToList(), ct);
        var emitidas = 0;
        foreach (var fotos in porLugar)
        {
            var calibracion = clsAutocalibracion.Proponer([.. fotos.Select(f => (f.Lat, f.Lng, f.ViajeId))], visitasMinimas);
            if (calibracion is null || (previas.TryGetValue(fotos.Key, out var previa) && (previa.EstaPendiente || calibracion.Fotos <= previa.Fotos)))
            {
                continue;
            }

            var propuesta = clsPropuestaUbicacion.Crear(fotos.Key, calibracion.CentroLat, calibracion.CentroLng, calibracion.RadioMetros, calibracion.Fotos, calibracion.Visitas, reloj.Ahora);
            propuestas.Agregar(propuesta);
            try
            {
                await unidadDeTrabajo.GuardarCambiosAsync(ct);
            }
            catch (Exception ex) when (ex is clsConflictoException)
            {
                // Otra instancia ya creo la propuesta pendiente de este lugar.
                unidadDeTrabajo.DescartarCambios();
                continue;
            }

            var lugar = fotos.First().LugarNombre;
            foreach (var cliente in fotos.Select(f => f.ClienteId).Distinct())
            {
                var destinatarios = (await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD], cliente, ct)).Select(u => u.Id).ToList();
                if (destinatarios.Count == 0)
                {
                    continue;
                }

                await emisor.EmitirAsync(
                    new clsAlerta(
                        clsTipoNotificacion.PROPUESTA_UBICACION,
                        clsUrgenciaNotificacion.MEDIA,
                        destinatarios,
                        cliente,
                        null,
                        null,
                        null,
                        null,
                        $"Propuesta de ubicación: {lugar}",
                        $"La autocalibración propone para {lugar} el centro ({calibracion.CentroLat:0.00000}, {calibracion.CentroLng:0.00000}) y un radio de {calibracion.RadioMetros} m, con {calibracion.Fotos} fotos de {calibracion.Visitas} visitas. Confírmela o descártela: el lugar no cambia hasta que decida.",
                        $"{clsTipoNotificacion.PROPUESTA_UBICACION}:{propuesta.Id}"),
                    ct);
                emitidas += destinatarios.Count;
            }
        }

        return emitidas;
    }

    /// <summary>
    /// Avisa a los coordinadores de calidad del cliente del inspector de un camion reclamado que lleva <c>reclamo_sin_actividad_minutos</c> sin ninguna inspeccion iniciada o cerrada del viaje despues del reclamo (urgencia
    /// media). Un aviso por reclamo: el reclamo NO se libera, solo lo libera una persona.
    /// </summary>
    public async Task<int> EjecutarReclamosSinActividadAsync(CancellationToken ct = default)
    {
        var minutos = Math.Max(await parametros.EnteroAsync(clsClavesParametros.RECLAMO_SIN_ACTIVIDAD_MINUTOS, RECLAMO_MINUTOS_POR_DEFECTO, ct), 1);
        var emitidas = 0;
        foreach (var reclamo in await consultas.ReclamosSinActividadAsync(reloj.Ahora.AddMinutes(-minutos), LIMITE_POR_EJECUCION, ct))
        {
            if (reclamo.ClienteId is not { } clienteId)
            {
                continue;
            }

            var destinatarios = (await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD], clienteId, ct)).Select(u => u.Id).ToList();
            if (destinatarios.Count == 0)
            {
                continue;
            }

            var camion = reclamo.Placa is { } placa ? $"el camión {placa}" : "el camión";
            var viaje = reclamo.CodigoMtrack is { } codigo ? $" (viaje {codigo})" : string.Empty;
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.RECLAMO_SIN_ACTIVIDAD,
                    clsUrgenciaNotificacion.MEDIA,
                    destinatarios,
                    clienteId,
                    reclamo.ViajeId,
                    null,
                    null,
                    null,
                    $"Camión reclamado sin actividad: {reclamo.Placa ?? reclamo.CodigoMtrack ?? "sin placa"}",
                    $"{reclamo.InspectorNombre ?? "Un inspector"} reclamó {camion}{viaje} ({reclamo.Proceso}{(reclamo.Parada is { } parada ? $" en {parada}" : string.Empty)}) hace más de {minutos} minutos y no ha iniciado ninguna inspección. El reclamo sigue vigente.",
                    $"{clsTipoNotificacion.RECLAMO_SIN_ACTIVIDAD}:{reclamo.ReclamoId}"),
                ct);
            emitidas += destinatarios.Count;
        }

        return emitidas;
    }

    /// <summary>
    /// Avisa a quien inicio la revision de llegada de un Cargue o de un Descargue que sigue en borrador, sin todas las vistas obligatorias, pasados los <c>captura_incompleta_minutos</c>: el inspector o, solo en un cargue
    /// sin personal de apoyo, el conductor. La acomodacion (la salida del Cargue) no cuenta: no exige vistas. Un aviso por inspeccion.
    /// </summary>
    public async Task<int> EjecutarCapturasIncompletasAsync(CancellationToken ct = default)
    {
        var minutos = Math.Max(await parametros.EnteroAsync(clsClavesParametros.CAPTURA_INCOMPLETA_MINUTOS, CAPTURA_MINUTOS_POR_DEFECTO, ct), 1);
        var emitidas = 0;
        foreach (var captura in await consultas.CapturasAbiertasAsync(reloj.Ahora.AddMinutes(-minutos), LIMITE_POR_EJECUCION, ct))
        {
            // Solo las llegadas del cargue y del descargue reclaman las vistas: la acomodacion (la salida del cargue) y las salidas que quedaron abiertas del descargue no.
            if (!clsEtapaCatalogo.ExigeVistas(captura.Proceso, captura.Momento))
            {
                continue;
            }

            // El conductor solo recibe el aviso de la salida de un cargue que el inicio (la unica que hace, con la excepcion «No hay inspector»).
            if (captura.RolCreador == clsRol.CONDUCTOR && captura.Proceso != clsCodigosEtapa.CARGUE)
            {
                continue;
            }

            try
            {
                var etapa = await etapas.ObtenerPorIdAsync(captura.EtapaId, ct);
                if (etapa is null)
                {
                    continue;
                }

                var faltantes = await calculadoraVistas.FaltantesAsync(etapa, await inspecciones.ObtenerResumenCierreAsync(captura.InspeccionId, ct), ct);
                if (faltantes.Count == 0)
                {
                    continue;
                }

                await emisor.EmitirAsync(
                    new clsAlerta(
                        clsTipoNotificacion.CAPTURA_INCOMPLETA,
                        clsUrgenciaNotificacion.MEDIA,
                        [captura.UsuarioId],
                        captura.ClienteId,
                        captura.ViajeId,
                        captura.VehiculoId,
                        null,
                        captura.InspeccionId,
                        $"Captura incompleta: {captura.Vin}",
                        $"La inspección de {captura.Proceso} de la unidad {captura.Vin} lleva más de {minutos} minutos abierta y le faltan fotos: {string.Join(", ", faltantes)}.",
                        $"{clsTipoNotificacion.CAPTURA_INCOMPLETA}:{captura.InspeccionId}"),
                    ct);
                emitidas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registro.LogError(ex, "No se pudo avisar de la captura incompleta de la inspeccion {Inspeccion}", captura.InspeccionId);
            }
        }

        return emitidas;
    }

    /// <summary>
    /// Avisa de las llegadas cerradas hace mas de <c>transferencia_pendiente_minutos</c> cuya transferencia no tiene tipo de cierre: al firmante que falta o, si la transferencia no existe, al conductor
    /// del viaje (la etapa siguiente es Transporte) o al inspector del viaje, o al conductor si no hay inspector (la etapa siguiente es Descargue). Un aviso por inspeccion y destinatario.
    /// </summary>
    public async Task<int> EjecutarTransferenciasPendientesAsync(CancellationToken ct = default)
    {
        var minutos = Math.Max(await parametros.EnteroAsync(clsClavesParametros.TRANSFERENCIA_PENDIENTE_MINUTOS, TRANSFERENCIA_MINUTOS_POR_DEFECTO, ct), 1);
        var emitidas = 0;
        foreach (var pendiente in await consultas.TransferenciasPendientesAsync(reloj.Ahora.AddMinutes(-minutos), LIMITE_POR_EJECUCION, ct))
        {
            var destinatarios = DestinatariosDe(pendiente);
            if (destinatarios.Count == 0)
            {
                continue;
            }

            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.TRANSFERENCIA_PENDIENTE,
                    clsUrgenciaNotificacion.MEDIA,
                    destinatarios,
                    pendiente.ClienteId,
                    pendiente.ViajeId,
                    pendiente.VehiculoId,
                    null,
                    pendiente.InspeccionId,
                    $"Transferencia pendiente: {pendiente.Vin}",
                    $"La llegada de {pendiente.Proceso} de la unidad {pendiente.Vin} lleva más de {minutos} minutos cerrada y su transferencia de custodia no tiene tipo de cierre: falta la firma.",
                    $"{clsTipoNotificacion.TRANSFERENCIA_PENDIENTE}:{pendiente.InspeccionId}"),
                ct);
            emitidas += destinatarios.Count;
        }

        return emitidas;
    }

    /// <summary>
    /// Con transferencia: quien le falta firmar. Sin ella: el conductor si lo que sigue es el Transporte y el inspector (o el conductor si no hay) si lo que sigue es el Descargue; si no hay etapa siguiente no
    /// hay a quien avisar.
    /// </summary>
    private static IReadOnlyCollection<Guid> DestinatariosDe(clsTransferenciaPendiente p)
    {
        if (p.ExisteTransferencia)
        {
            var faltantes = new List<Guid>();
            if (p.FaltaEntrega && p.EntregaUsuarioId is { } entrega)
            {
                faltantes.Add(entrega);
            }

            if (p.FaltaRecibe && p.RecibeUsuarioId is { } recibe)
            {
                faltantes.Add(recibe);
            }

            return faltantes;
        }

        var destinatario = p.ProcesoSiguiente switch
        {
            clsCodigosEtapa.TRANSPORTE => p.ConductorId,
            clsCodigosEtapa.DESCARGUE => p.InspectorId ?? p.ConductorId,
            _ => null,
        };
        return destinatario is { } id ? [id] : [];
    }
}
