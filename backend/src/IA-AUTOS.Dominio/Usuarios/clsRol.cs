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

    /// <summary>Administracion de datos maestros (carga de VINs). super_admin incluido.</summary>
    public static readonly string[] ADMINISTRACION = [ADMIN, SUPER_ADMIN];

    /// <summary>Administracion de los catalogos maestros (etapas, lugares, clases y tamanos de dano, vistas, cuadrantes...).</summary>
    public static readonly string[] ADMINISTRACION_CATALOGOS = [ADMIN, SUPER_ADMIN, COORDINADOR_CALIDAD];

    /// <summary>Creacion y administracion de viajes (incluida la carga por Excel).</summary>
    public static readonly string[] GESTION_VIAJES = [ADMIN, SUPER_ADMIN, COORDINADOR_CALIDAD];

    /// <summary>Gestion de usuarios (crear, editar, desactivar, PIN). Solo el super administrador asigna o modifica el nivel super_admin.</summary>
    public static readonly string[] GESTION_USUARIOS = [ADMIN, SUPER_ADMIN, COORDINADOR_CALIDAD];

    /// <summary>Seguimiento de los hallazgos (validado, rechazado, recuperado, derogado) y su historial.</summary>
    public static readonly string[] GESTION_SEGUIMIENTO = [COORDINADOR_CALIDAD, COMERCIAL, ADMIN, SUPER_ADMIN];

    /// <summary>Marcar que una unidad con un hallazgo V1+ no sale del viaje: el inspector (del viaje) y el coordinador de calidad.</summary>
    public static readonly string[] MARCAR_NO_SALE = [INSPECTOR, COORDINADOR_CALIDAD];

    public static bool EsAdministrador(string rol) => rol is ADMIN or SUPER_ADMIN;

    /// <summary>Inspectores y conductores solo ven los VIN, viajes e informes de los viajes en los que participan.</summary>
    public static bool VeSoloSusViajes(string? rol) => rol is INSPECTOR or CONDUCTOR;
}

public static class clsSubtipoInspector
{
    public static readonly IReadOnlySet<string> VALORES = new HashSet<string>
    {
        "importacion", "nacional", "export_preembarque", "export_recibido",
    };
}
