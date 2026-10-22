using System.Globalization;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Exportaciones;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Parametros;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.ReporteDiario;

/// <summary>Lo que paso en una ejecucion del reporte diario.</summary>
public enum EstadoReporteDiario
{
    /// <summary>Todavia no es la hora configurada: no se hizo nada.</summary>
    AunNoEsLaHora,

    /// <summary>El reporte de ese dia ya estaba registrado (enviado, sin destinatarios o con todos sus intentos fallidos).</summary>
    YaProcesado,

    /// <summary>El correo salio hacia el servidor de correo y quedo registrado.</summary>
    Enviado,

    /// <summary>La lista de destinatarios esta vacia: no se envio y quedo registrado.</summary>
    SinDestinatarios,

    /// <summary>El envio (o el calculo) fallo y quedo registrado con su error; se reintenta mientras queden intentos.</summary>
    Fallo,
}

/// <summary>El resultado de una ejecucion: el dia que se reporto y si ya no hay nada mas por hacer para ese dia.</summary>
public sealed record clsResultadoReporteDiario(EstadoReporteDiario Estado, DateOnly Dia, bool Terminado);

/// <summary>
/// El reporte diario por correo. A la hora configurada (<c>reporte_diario_hora</c>, hora de Colombia) calcula el dia anterior con los mismos reportes del tablero (indicador diario y tiempo de
/// captura), arma la tabla en el cuerpo del correo, adjunta el Excel de hallazgos de ese dia y lo envia a <c>reporte_diario_destinatarios</c>. Cada ejecucion que hace algo queda registrada en
/// tblNotificaciones como <c>reporte_diario</c> por <c>correo</c>, con su fecha de envio o su error; sin destinatarios queda registrado que no se envio. Una notificacion no se modifica, asi que un
/// envio fallido deja su fila (con su clave de intento) y el reintento crea otra; el dia queda cerrado con la fila de la clave del dia o con el ultimo intento.
/// </summary>
public sealed class clsReporteDiarioService(
    clsReportesTableroService reportes,
    clsExportacionesService exportaciones,
    IParametros parametros,
    INotificacionRepository notificaciones,
    IEnviadorCorreo correo,
    IUnidadDeTrabajo unidadDeTrabajo,
    IOptions<clsOpcionesReporteDiario> opciones,
    IReloj reloj,
    ILogger<clsReporteDiarioService> registro)
{
    public const string HORA_POR_DEFECTO = "06:00";
    public const string SIN_DESTINATARIOS = "No se envió: el parámetro reporte_diario_destinatarios no tiene destinatarios";

    private static readonly TimeSpan ZONA_COLOMBIA = TimeSpan.FromHours(-5);

    /// <summary>El dia que reporta una ejecucion hecha en <paramref name="ahora"/>: el anterior, en hora de Colombia.</summary>
    public static DateOnly DiaReportado(DateTimeOffset ahora) => DateOnly.FromDateTime(ahora.ToOffset(ZONA_COLOMBIA).Date).AddDays(-1);

    /// <summary>La clave del reporte de un dia que ya no se repite (enviado, o registrado sin destinatarios).</summary>
    public static string ClaveDelDia(DateOnly dia) => $"reporte_diario:{clsCorreoReporteDiario.Fecha(dia)}";

    /// <summary>La clave del intento fallido numero <paramref name="intento"/> del reporte de un dia.</summary>
    public static string ClaveDeIntento(DateOnly dia, int intento) => $"{ClaveDelDia(dia)}:intento-{intento.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Hace lo que toque ahora: nada si aun no es la hora, nada si el dia ya esta registrado y, si no, calcula, envia y registra. Un fallo al calcular o al enviar se registra en la notificacion; un
    /// fallo al guardar la notificacion sube a quien lo llama (el proceso lo reintenta en su siguiente latido).
    /// </summary>
    public async Task<clsResultadoReporteDiario> EjecutarAsync(CancellationToken ct = default)
    {
        var ahora = reloj.Ahora;
        var dia = DiaReportado(ahora);
        if (ahora.ToOffset(ZONA_COLOMBIA).TimeOfDay < await HoraConfiguradaAsync(ct))
        {
            return new clsResultadoReporteDiario(EstadoReporteDiario.AunNoEsLaHora, dia, false);
        }

        var maximo = Math.Max(opciones.Value.IntentosMaximos, 1);
        var claves = new List<string> { ClaveDelDia(dia) };
        claves.AddRange(Enumerable.Range(1, maximo).Select(i => ClaveDeIntento(dia, i)));
        var existentes = await notificaciones.ClavesExistentesAsync(claves, ct);
        if (existentes.Contains(ClaveDelDia(dia)) || existentes.Contains(ClaveDeIntento(dia, maximo)))
        {
            return new clsResultadoReporteDiario(EstadoReporteDiario.YaProcesado, dia, true);
        }

        var intento = Enumerable.Range(1, maximo).First(i => !existentes.Contains(ClaveDeIntento(dia, i)));
        var destinatarios = await parametros.ListaAsync(clsClavesParametros.REPORTE_DIARIO_DESTINATARIOS, [], ct);
        if (destinatarios.Count == 0)
        {
            await RegistrarAsync(dia, "Reporte diario IA Autos: no se envió", $"El reporte del {clsCorreoReporteDiario.Fecha(dia)} no se envió porque no hay destinatarios configurados.", null, SIN_DESTINATARIOS, ClaveDelDia(dia), ct);
            return new clsResultadoReporteDiario(EstadoReporteDiario.SinDestinatarios, dia, true);
        }

        string asunto = clsCorreoReporteDiario.Asunto(dia);
        string resumen;
        clsResultadoCorreo resultado;
        try
        {
            var texto = clsCorreoReporteDiario.Fecha(dia);
            var indicador = await reportes.IndicadorDiarioAsync(new FiltrosReporteRequest(null, null, null, null, null, null, null, null, null, texto), ct);
            var tiempo = await reportes.TiempoDeCapturaAsync(new FiltrosReporteRequest(texto, texto, null, null, null, null, null, null, null), ct);
            var excel = await exportaciones.ExportarHallazgosAsync(
                new FiltrosHallazgosRequest(null, null, null, null, null, null, null, null, null, null, null, $"{texto}T00:00:00-05:00", $"{texto}T23:59:59.999-05:00", null, null, null), ct);
            resumen = clsCorreoReporteDiario.Resumen(indicador, tiempo);
            resultado = await correo.EnviarAsync(
                new clsCorreo(destinatarios, asunto, clsCorreoReporteDiario.Construir(indicador, tiempo), [new clsAdjuntoCorreo($"hallazgos-{texto}.xlsx", excel.Contenido, excel.TipoContenido)], $"ia-autos-reporte-diario-{texto}-intento-{intento.ToString(CultureInfo.InvariantCulture)}"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogError(ex, "No se pudo armar el reporte diario del {Dia}", dia);
            resumen = $"No se pudo armar el reporte del {clsCorreoReporteDiario.Fecha(dia)}.";
            resultado = clsResultadoCorreo.Fallo("No se pudo armar el reporte: " + ex.Message);
        }

        if (resultado.Enviado)
        {
            await RegistrarAsync(dia, asunto, $"{resumen} Enviado a {string.Join(", ", destinatarios)}.", reloj.Ahora, null, ClaveDelDia(dia), ct);
            return new clsResultadoReporteDiario(EstadoReporteDiario.Enviado, dia, true);
        }

        await RegistrarAsync(dia, asunto, resumen, null, resultado.Error ?? "El correo no se pudo enviar", ClaveDeIntento(dia, intento), ct);
        registro.LogWarning("El reporte diario del {Dia} no se envió (intento {Intento} de {Maximo}): {Error}", dia, intento, maximo, resultado.Error);
        return new clsResultadoReporteDiario(EstadoReporteDiario.Fallo, dia, intento >= maximo);
    }

    /// <summary>La hora (de Colombia) a la que se envia el reporte: el parametro, o 06:00 si no tiene un valor valido.</summary>
    private async Task<TimeSpan> HoraConfiguradaAsync(CancellationToken ct)
    {
        var texto = await parametros.ValorAsync(clsClavesParametros.REPORTE_DIARIO_HORA, ct);
        return TimeSpan.TryParseExact(texto, "hh\\:mm", CultureInfo.InvariantCulture, out var hora) ? hora : TimeSpan.FromHours(6);
    }

    private async Task RegistrarAsync(DateOnly dia, string titulo, string mensaje, DateTimeOffset? enviadaAt, string? error, string clave, CancellationToken ct)
    {
        notificaciones.Agregar(clsNotificacion.Crear(
            clsTipoNotificacion.REPORTE_DIARIO, clsUrgenciaNotificacion.MEDIA, null, null, null, null, null, null, titulo, mensaje, clsCanalNotificacion.CORREO, enviadaAt, error, clave, reloj.Ahora));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
    }
}
