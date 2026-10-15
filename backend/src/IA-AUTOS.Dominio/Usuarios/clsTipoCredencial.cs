namespace IAAutos.Dominio.Usuarios;

/// <summary>Como entra el usuario: con correo y contrasena, o con cedula y PIN.</summary>
public static class clsTipoCredencial
{
    public const string PASSWORD = "password";
    public const string PIN = "pin";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { PASSWORD, PIN };

    /// <summary>Roles que pueden entrar con cedula y PIN (el personal de campo).</summary>
    public static readonly IReadOnlySet<string> ROLES_CON_PIN = new HashSet<string> { clsRol.CONDUCTOR, clsRol.INSPECTOR };
}
