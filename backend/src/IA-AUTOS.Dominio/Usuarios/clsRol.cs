namespace IAAutos.Dominio.Usuarios;

public static class clsRol
{
    public const string INSPECTOR = "inspector";
    public const string CONDUCTOR = "conductor";
    public const string COORDINADOR_CALIDAD = "coordinador_calidad";
    public const string COMERCIAL = "comercial";
    public const string ADMIN = "admin";
    public const string SUPER_ADMIN = "super_admin";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string>
    {
        INSPECTOR, CONDUCTOR, COORDINADOR_CALIDAD, COMERCIAL, ADMIN, SUPER_ADMIN,
    };

    /// <summary>Roles que registran informes en campo (inspecciones, evidencias, hallazgos, firmas, fotos).</summary>
    public static readonly string[] ESCRITURA = [INSPECTOR, CONDUCTOR, ADMIN];

    /// <summary>Administracion de datos maestros (carga de VINs, asignaciones, etapas). super_admin incluido.</summary>
    public static readonly string[] ADMINISTRACION = [ADMIN, SUPER_ADMIN];

    /// <summary>Gestion de usuarios.</summary>
    public static readonly string[] GESTION_USUARIOS = [SUPER_ADMIN];

    public static bool EsAdministrador(string rol) => rol is ADMIN or SUPER_ADMIN;
}

public static class clsSubtipoInspector
{
    public static readonly IReadOnlySet<string> VALORES = new HashSet<string>
    {
        "importacion", "nacional", "export_preembarque", "export_recibido",
    };
}
