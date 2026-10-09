namespace IAAutos.Aplicacion.Comun;

/// <summary>
/// Sesion del portal (seccion Sesion de la configuracion): vigencias del access y del refresh token. Los tiempos por defecto son
/// los del portal.
/// </summary>
public sealed class clsOpcionesSesion
{
    public const string SECCION = "Sesion";

    /// <summary>Vigencia del access token de la sesion del portal (minutos).</summary>
    public int AccessMinutos { get; set; } = 60;

    /// <summary>Vigencia del refresh token (dias).</summary>
    public int RefreshDias { get; set; } = 30;

    /// <summary>
    /// Segundos durante los que un refresh recien rotado, presentado de nuevo, NO se toma como robo. Cubre dos pestañas
    /// que refrescan a la vez con el mismo refresh token. Pasado ese margen, un reuso revoca toda la sesion. 0 lo desactiva.
    /// </summary>
    public int RefreshGraciaSegundos { get; set; } = 10;
}

/// <summary>Verificacion de la identidad contra el portal (seccion PortalTi): lo que necesita la capa de Aplicacion.</summary>
public sealed class clsOpcionesPortalAplicacion
{
    public const string SECCION = "PortalTi";

    /// <summary>
    /// Dominio de correo (sin @) para crear la cuenta cuando el portal no tiene correo de la persona. Vacio = en ese caso
    /// no se crea la cuenta y se responde acceso no habilitado.
    /// </summary>
    public string? DominioCorreo { get; set; }
}
