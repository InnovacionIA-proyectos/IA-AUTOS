namespace IAAutos.Aplicacion.Comun;

/// <summary>Reglas de seguridad configurables (seccion Seguridad de la configuracion).</summary>
public sealed class clsOpcionesSeguridad
{
    public const string SECCION = "Seguridad";

    /// <summary>Longitud minima de las contrasenas que asigna el administrador.</summary>
    public int PasswordMinimo { get; set; } = 8;

    /// <summary>Si es true, solo el creador de una inspeccion (o un administrador) puede agregarle datos.</summary>
    public bool ValidarPropiedadInspeccion { get; set; } = true;

    /// <summary>Intentos de login permitidos por minuto y por direccion IP.</summary>
    public int LoginMaxIntentosPorMinuto { get; set; } = 10;

    /// <summary>Intentos de login-portal por minuto y por IP. Mayor que el login directo: detras de un proxy corporativo muchas personas comparten IP.</summary>
    public int LoginPortalMaxIntentosPorMinuto { get; set; } = 30;

    /// <summary>Renovaciones de sesion (refresh) por minuto y por IP.</summary>
    public int RefreshMaxPorMinuto { get; set; } = 60;

    /// <summary>Intentos de login-conductor por minuto y por IP. Alto: en una bodega muchos conductores comparten la misma red.</summary>
    public int LoginConductorMaxIntentosPorMinuto { get; set; } = 60;

    /// <summary>PIN errados seguidos que bloquean la cuenta de una persona.</summary>
    public int PinMaxIntentos { get; set; } = 5;

    /// <summary>Minutos que dura el bloqueo por PIN errados.</summary>
    public int PinBloqueoMinutos { get; set; } = 15;
}
