namespace IAAutos.Aplicacion.Notificaciones;

/// <summary>Lo que se muestra en el celular y los datos con los que la app abre la pantalla de la alerta.</summary>
public sealed record clsMensajePush(string Titulo, string Mensaje, IReadOnlyDictionary<string, string> Datos);

/// <summary>El resultado de enviar un push a un dispositivo. <c>TokenInvalido</c> dice que FCM ya no reconoce el token: el dispositivo se desactiva.</summary>
public sealed record clsResultadoPush(bool Enviado, string? Error = null, bool TokenInvalido = false)
{
    public static clsResultadoPush Exito { get; } = new(true);

    public static clsResultadoPush Fallo(string error, bool tokenInvalido = false) => new(false, error, tokenInvalido);
}

/// <summary>Envia notificaciones push a los celulares (FCM). Nunca lanza por un fallo del envio: lo informa en el resultado, para no romper la peticion que lo origino.</summary>
public interface IEnviadorPush
{
    Task<clsResultadoPush> EnviarAsync(string tokenFcm, clsMensajePush mensaje, CancellationToken ct = default);
}
