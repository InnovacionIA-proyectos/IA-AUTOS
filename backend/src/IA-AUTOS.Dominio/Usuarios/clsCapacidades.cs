namespace IAAutos.Dominio.Usuarios;

/// <summary>
/// Lo que puede hacer cada rol, en nombres estables que la web y la app usan para armar el menu. Sale de las mismas listas de roles
/// que autorizan los endpoints, asi que menu y permisos no se desalinean.
/// </summary>
public static class clsCapacidades
{
    public const string VER_INSPECCIONES = "ver_inspecciones";
    public const string REGISTRAR_INSPECCIONES = "registrar_inspecciones";
    public const string VER_VIAJES = "ver_viajes";
    public const string GESTIONAR_VIAJES = "gestionar_viajes";
    public const string GESTIONAR_USUARIOS = "gestionar_usuarios";
    public const string ADMINISTRAR_CATALOGOS = "administrar_catalogos";
    public const string CARGAR_VEHICULOS = "cargar_vehiculos";
    public const string VER_REPORTES = "ver_reportes";
    public const string GESTIONAR_SEGUIMIENTO = "gestionar_seguimiento";
    public const string VER_ALERTAS = "ver_alertas";
    public const string VER_AUDITORIA = "ver_auditoria";

    public static IReadOnlyList<string> De(string? rol)
    {
        if (rol is null)
        {
            return [];
        }

        var capacidades = new List<string> { VER_INSPECCIONES, VER_VIAJES, VER_REPORTES };
        AgregarSi(capacidades, clsRol.ESCRITURA, rol, REGISTRAR_INSPECCIONES);
        AgregarSi(capacidades, clsRol.GESTION_VIAJES, rol, GESTIONAR_VIAJES);
        AgregarSi(capacidades, clsRol.GESTION_USUARIOS, rol, GESTIONAR_USUARIOS);
        AgregarSi(capacidades, clsRol.ADMINISTRACION_CATALOGOS, rol, ADMINISTRAR_CATALOGOS);
        AgregarSi(capacidades, clsRol.ADMINISTRACION, rol, CARGAR_VEHICULOS);
        AgregarSi(capacidades, clsRol.GESTION_SEGUIMIENTO, rol, GESTIONAR_SEGUIMIENTO);
        AgregarSi(capacidades, clsRol.VER_ALERTAS, rol, VER_ALERTAS);
        AgregarSi(capacidades, clsRol.ADMINISTRACION, rol, VER_AUDITORIA);
        return capacidades;
    }

    private static void AgregarSi(List<string> capacidades, string[] roles, string rol, string capacidad)
    {
        if (roles.Contains(rol))
        {
            capacidades.Add(capacidad);
        }
    }
}
