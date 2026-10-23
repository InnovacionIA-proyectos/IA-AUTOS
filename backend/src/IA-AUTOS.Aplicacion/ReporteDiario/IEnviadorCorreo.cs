namespace IAAutos.Aplicacion.ReporteDiario;

/// <summary>Un archivo adjunto de un correo.</summary>
public sealed record clsAdjuntoCorreo(string Nombre, byte[] Contenido, string TipoContenido);

/// <summary>Un correo en HTML para varios destinatarios, con sus adjuntos. <c>IdCorrelacion</c> (opcional) viaja al servicio de correo para cruzar sus registros con los de la aplicacion.</summary>
public sealed record clsCorreo(IReadOnlyList<string> Destinatarios, string Asunto, string CuerpoHtml, IReadOnlyList<clsAdjuntoCorreo> Adjuntos, string? IdCorrelacion = null);

/// <summary>El resultado de enviar un correo: si salio hacia el servidor de correo y, si no, el motivo.</summary>
public sealed record clsResultadoCorreo(bool Enviado, string? Error = null)
{
    public static clsResultadoCorreo Exito { get; } = new(true);

    public static clsResultadoCorreo Fallo(string error) => new(false, error);
}

/// <summary>Envia correos (por el servicio interno MailSender API). Nunca lanza por un fallo del envio: lo informa en el resultado, para que el proceso que lo pidio lo registre y siga.</summary>
public interface IEnviadorCorreo
{
    Task<clsResultadoCorreo> EnviarAsync(clsCorreo correo, CancellationToken ct = default);
}
