using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Parametros;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Alertas;

/// <summary>Cuantos hallazgos tiene un cliente en una zona o en un proceso dentro de la ventana. <c>Clave</c> identifica la zona o el proceso y <c>Nombre</c> es como se muestra.</summary>
public sealed record clsConteoPatron(Guid ClienteId, string Clave, string Nombre, int Total);

/// <summary>Una inspeccion en borrador de Cargue o Descargue que sigue abierta: quien la inicio, a que unidad y proceso, y si su creador es un conductor en un cargue sin personal de apoyo.</summary>
public sealed record clsCapturaAbierta(
    Guid InspeccionId, Guid EtapaId, Guid ViajeId, Guid VehiculoId, Guid ClienteId, string Vin, string Proceso, Guid UsuarioId, string RolCreador, DateTimeOffset IniciadaAt);

/// <summary>
/// Una llegada cerrada cuya transferencia no tiene tipo de cierre: a quien le falta firmar (si la transferencia existe) o, si no existe, el conductor y el inspector del viaje y el proceso de la
/// etapa que sigue.
/// </summary>
public sealed record clsTransferenciaPendiente(
    Guid InspeccionId, Guid ViajeId, Guid VehiculoId, Guid ClienteId, string Vin, string Proceso, bool ExisteTransferencia,
    Guid? EntregaUsuarioId, bool FaltaEntrega, Guid? RecibeUsuarioId, bool FaltaRecibe, string? ProcesoSiguiente, Guid? ConductorId, Guid? InspectorId);

/// <summary>Un camion reclamado (reclamo activo) por un inspector que aun no inicio ni cerro ninguna inspeccion del viaje despues de reclamarlo.</summary>
public sealed record clsReclamoSinActividad(
    Guid ReclamoId, Guid ViajeId, string? CodigoMtrack, string? Placa, string Proceso, Guid InspectorId, string? InspectorNombre, Guid? ClienteId, DateTimeOffset ReclamadoAt);

/// <summary>Consultas de solo lectura de los procesos de alertas.</summary>
public interface IConsultasAlertas
{
    /// <summary>
    /// Los reclamos activos hechos antes de <paramref name="reclamadosAntesDe"/>, de viajes activos, sin ninguna inspeccion del viaje iniciada o cerrada despues del reclamo (la «actividad»).
    /// </summary>
    Task<IReadOnlyList<clsReclamoSinActividad>> ReclamosSinActividadAsync(DateTimeOffset reclamadosAntesDe, int limite, CancellationToken ct = default);

    /// <summary>Hallazgos de inspecciones cerradas desde <paramref name="desde"/>, contados por cliente y zona (lado + numero, o zona provisional).</summary>
    Task<IReadOnlyList<clsConteoPatron>> ConteoPorZonaAsync(DateTimeOffset desde, CancellationToken ct = default);

    /// <summary>Hallazgos de inspecciones cerradas desde <paramref name="desde"/>, contados por cliente y proceso.</summary>
    Task<IReadOnlyList<clsConteoPatron>> ConteoPorProcesoAsync(DateTimeOffset desde, CancellationToken ct = default);

    /// <summary>Inspecciones en borrador de Cargue o Descargue, de viajes activos, iniciadas antes de <paramref name="iniciadasAntesDe"/>.</summary>
    Task<IReadOnlyList<clsCapturaAbierta>> CapturasAbiertasAsync(DateTimeOffset iniciadasAntesDe, int limite, CancellationToken ct = default);

    /// <summary>Llegadas cerradas antes de <paramref name="cerradasAntesDe"/>, de unidades que salen de viajes activos, con la transferencia sin tipo de cierre (o sin transferencia).</summary>
    Task<IReadOnlyList<clsTransferenciaPendiente>> TransferenciasPendientesAsync(DateTimeOffset cerradasAntesDe, int limite, CancellationToken ct = default);
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
    clsCalculadoraVistas calculadoraVistas,
    clsEmisorNotificaciones emisor,
    IReloj reloj,
    ILogger<clsAlertasPeriodicas> registro)
{
    public const int UMBRAL_POR_DEFECTO = 10;
    public const int VENTANA_DIAS_POR_DEFECTO = 30;
    public const int CAPTURA_MINUTOS_POR_DEFECTO = 30;
    public const int TRANSFERENCIA_MINUTOS_POR_DEFECTO = 240;
    public const int RECLAMO_MINUTOS_POR_DEFECTO = 30;
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
                        $"{clsTipoNotificacion.UMBRAL_PATRON}:{porCliente.Key}:{tipo}:{conteo.Clave}:{bloque}"),
                    ct);
                emitidas += destinatarios.Count;
            }
        }

        return emitidas;
    }

    /// <summary>Corre los avisos operativos: captura incompleta, transferencia pendiente y camion reclamado sin actividad. Devuelve cuantos avisos intento crear.</summary>
    public async Task<int> EjecutarAvisosAsync(CancellationToken ct = default) =>
        await EjecutarCapturasIncompletasAsync(ct) + await EjecutarTransferenciasPendientesAsync(ct) + await EjecutarReclamosSinActividadAsync(ct);

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
                    $"{reclamo.InspectorNombre ?? "Un inspector"} reclamó {camion}{viaje} ({reclamo.Proceso}) hace más de {minutos} minutos y no ha iniciado ninguna inspección. El reclamo sigue vigente.",
                    $"{clsTipoNotificacion.RECLAMO_SIN_ACTIVIDAD}:{reclamo.ReclamoId}"),
                ct);
            emitidas += destinatarios.Count;
        }

        return emitidas;
    }

    /// <summary>
    /// Avisa a quien inicio una inspeccion de Cargue o Descargue que sigue en borrador, sin todas las vistas obligatorias, pasados los <c>captura_incompleta_minutos</c>: el inspector o, solo en un cargue
    /// sin personal de apoyo, el conductor. Un aviso por inspeccion.
    /// </summary>
    public async Task<int> EjecutarCapturasIncompletasAsync(CancellationToken ct = default)
    {
        var minutos = Math.Max(await parametros.EnteroAsync(clsClavesParametros.CAPTURA_INCOMPLETA_MINUTOS, CAPTURA_MINUTOS_POR_DEFECTO, ct), 1);
        var emitidas = 0;
        foreach (var captura in await consultas.CapturasAbiertasAsync(reloj.Ahora.AddMinutes(-minutos), LIMITE_POR_EJECUCION, ct))
        {
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
