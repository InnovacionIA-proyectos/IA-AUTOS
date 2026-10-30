using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Notificaciones;

/// <summary>Lo que avisa una notificacion.</summary>
public static class clsTipoNotificacion
{
    /// <summary>El conductor registro una novedad en ruta.</summary>
    public const string NOVEDAD_CONDUCTOR = "novedad_conductor";

    /// <summary>Un coordinador respondio la novedad del conductor.</summary>
    public const string RESPUESTA_CONDUCTOR = "respuesta_conductor";

    public const string HALLAZGO_CRITICO = "hallazgo_critico";
    public const string UMBRAL_PATRON = "umbral_patron";
    public const string CAPTURA_INCOMPLETA = "captura_incompleta";
    public const string TRANSFERENCIA_PENDIENTE = "transferencia_pendiente";

    /// <summary>Un camion reclamado por un inspector lleva <c>reclamo_sin_actividad_minutos</c> sin ninguna inspeccion iniciada o cerrada: aviso al coordinador de calidad. El reclamo no se libera.</summary>
    public const string RECLAMO_SIN_ACTIVIDAD = "reclamo_sin_actividad";
    public const string REPORTE_DIARIO = "reporte_diario";

    /// <summary>Algo paso en el proceso y no es un dano (por ejemplo, un cargue sin inspector).</summary>
    public const string NOVEDAD_PROCESO = "novedad_proceso";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string>
    {
        NOVEDAD_CONDUCTOR, RESPUESTA_CONDUCTOR, HALLAZGO_CRITICO, UMBRAL_PATRON, CAPTURA_INCOMPLETA, TRANSFERENCIA_PENDIENTE, REPORTE_DIARIO, NOVEDAD_PROCESO, RECLAMO_SIN_ACTIVIDAD,
    };
}

public static class clsUrgenciaNotificacion
{
    public const string ALTA = "alta";
    public const string MEDIA = "media";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { ALTA, MEDIA };
}

/// <summary>Por donde llega: la bandeja del portal y de la app, el push al celular o el correo (solo el reporte diario).</summary>
public static class clsCanalNotificacion
{
    public const string BANDEJA = "bandeja";
    public const string PUSH = "push";
    public const string CORREO = "correo";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { BANDEJA, PUSH, CORREO };
}

/// <summary>Como atendio el coordinador una novedad del conductor.</summary>
public static class clsMedioRespuesta
{
    public const string APP = "app";
    public const string LLAMADA = "llamada";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { APP, LLAMADA };
}

/// <summary>
/// Una notificacion de un canal para un destinatario (la tabla guarda una fila por canal y por destinatario). Es append-only: solo se lee y se responde, y lo que se
/// intento enviar (cuando se envio o el error) se fija al crearla. <see cref="ClaveDedupe"/> evita repetir la misma alerta.
/// </summary>
public sealed class clsNotificacion
{
    public const int TITULO_MAX = 200;
    public const int MENSAJE_MAX = 1000;
    public const int ERROR_MAX = 500;
    public const int RESPUESTA_MAX = 1000;
    public const int CLAVE_MAX = 300;

    private clsNotificacion()
    {
    }

    public Guid Id { get; private set; }

    public string Tipo { get; private set; } = string.Empty;

    public string Urgencia { get; private set; } = string.Empty;

    /// <summary>El destinatario; nulo solo en el reporte diario.</summary>
    public Guid? UsuarioId { get; private set; }

    public Guid? ClienteId { get; private set; }

    public Guid? ViajeId { get; private set; }

    public Guid? VehiculoId { get; private set; }

    public Guid? HallazgoId { get; private set; }

    public Guid? InspeccionId { get; private set; }

    public string Titulo { get; private set; } = string.Empty;

    public string Mensaje { get; private set; } = string.Empty;

    public string Canal { get; private set; } = string.Empty;

    /// <summary>Cuando se envio por su canal. Nulo si el envio fallo (<see cref="ErrorEnvio"/>).</summary>
    public DateTimeOffset? EnviadaAt { get; private set; }

    public string? ErrorEnvio { get; private set; }

    public DateTimeOffset? LeidaAt { get; private set; }

    public string? RespuestaTexto { get; private set; }

    public string? RespuestaMedio { get; private set; }

    public Guid? RespondidaPor { get; private set; }

    public DateTimeOffset? RespondidaAt { get; private set; }

    public string ClaveDedupe { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool EstaLeida => LeidaAt is not null;

    public bool EstaRespondida => RespondidaAt is not null;

    /// <summary>
    /// Crea la notificacion de un canal. <paramref name="enviadaAt"/> es cuando salio por su canal (la bandeja se entrega al crearla) y <paramref name="errorEnvio"/> por que no
    /// salio; no pueden venir los dos. La respuesta nunca se fija al crearla.
    /// </summary>
    public static clsNotificacion Crear(
        string tipo,
        string urgencia,
        Guid? usuarioId,
        Guid? clienteId,
        Guid? viajeId,
        Guid? vehiculoId,
        Guid? hallazgoId,
        Guid? inspeccionId,
        string titulo,
        string mensaje,
        string canal,
        DateTimeOffset? enviadaAt,
        string? errorEnvio,
        string claveDedupe,
        DateTimeOffset ahora)
    {
        clsValidador.EnCatalogo(tipo, clsTipoNotificacion.VALORES, "tipo");
        clsValidador.EnCatalogo(urgencia, clsUrgenciaNotificacion.VALORES, "urgencia");
        clsValidador.EnCatalogo(canal, clsCanalNotificacion.VALORES, "canal");
        if (usuarioId is null && tipo != clsTipoNotificacion.REPORTE_DIARIO)
        {
            throw new clsValidacionException("Solo el reporte diario no tiene destinatario");
        }

        if (canal == clsCanalNotificacion.CORREO && tipo != clsTipoNotificacion.REPORTE_DIARIO)
        {
            throw new clsValidacionException("El correo solo se usa para el reporte diario");
        }

        if (enviadaAt is not null && errorEnvio is not null)
        {
            throw new clsValidacionException("Una notificacion enviada no puede tener error de envio");
        }

        return new clsNotificacion
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Urgencia = urgencia,
            UsuarioId = usuarioId,
            ClienteId = clienteId,
            ViajeId = viajeId,
            VehiculoId = vehiculoId,
            HallazgoId = hallazgoId,
            InspeccionId = inspeccionId,
            Titulo = Recortar(clsValidador.Requerido(titulo, "El titulo es requerido"), TITULO_MAX),
            Mensaje = Recortar(clsValidador.Requerido(mensaje, "El mensaje es requerido"), MENSAJE_MAX),
            Canal = canal,
            EnviadaAt = enviadaAt,
            ErrorEnvio = errorEnvio is null ? null : Recortar(errorEnvio, ERROR_MAX),
            ClaveDedupe = Recortar(clsValidador.Requerido(claveDedupe, "La clave de la alerta es requerida"), CLAVE_MAX),
            CreatedAt = ahora,
        };
    }

    /// <summary>El destinatario la leyo. Leerla otra vez no cambia la primera lectura.</summary>
    public void MarcarLeida(DateTimeOffset ahora) => LeidaAt ??= ahora;

    /// <summary>Texto de respuesta valido (no vacio y de maximo <see cref="RESPUESTA_MAX"/> caracteres), ya sin espacios sobrantes.</summary>
    public static string TextoDeRespuesta(string? texto)
    {
        var limpio = clsValidador.Opcional(texto) ?? throw clsErroresCampos.De("texto", "El texto de la respuesta es requerido");
        return limpio.Length <= RESPUESTA_MAX ? limpio : throw clsErroresCampos.De("texto", $"El texto admite máximo {RESPUESTA_MAX} caracteres");
    }

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
