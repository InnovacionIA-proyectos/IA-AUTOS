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
}
