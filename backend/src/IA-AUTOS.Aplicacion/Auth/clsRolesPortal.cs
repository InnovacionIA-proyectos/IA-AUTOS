using IAAutos.Dominio.Usuarios;

namespace IAAutos.Aplicacion.Auth;

/// <summary>
/// Traduce los roles del portal a un rol de IA Autos. Un rol del portal da acceso a IA Autos cuando su nombre termina en
/// IAAUTOS y lo que antecede es un rol de la aplicacion (AdminIAAUTOS -> admin, SuperAdminIAAUTOS -> super_admin,
/// InspectorIAAUTOS, CoordinadorCalidadIAAUTOS, ComercialIAAUTOS). La comparacion ignora mayusculas, tildes, espacios y
/// separadores. El conductor no entra por el portal, asi que no tiene rol equivalente.
/// </summary>
public static class clsRolesPortal
{
    public const string SUFIJO = "IAAUTOS";

    /// <summary>Prefijo del nombre del rol del portal (normalizado) y su rol interno. El ORDEN es la precedencia: gana el primero.</summary>
    private static readonly (string Prefijo, string Rol)[] PRECEDENCIA =
    [
        ("SUPERADMIN", clsRol.SUPER_ADMIN),
        ("ADMIN", clsRol.ADMIN),
        ("COORDINADORCALIDAD", clsRol.COORDINADOR_CALIDAD),
        ("COMERCIAL", clsRol.COMERCIAL),
        ("INSPECTOR", clsRol.INSPECTOR),
    ];

    /// <summary>Roles internos que corresponden a los roles del portal recibidos, del de mayor al de menor privilegio.</summary>
    public static IReadOnlyList<string> Coincidencias(IEnumerable<string?> rolesPortal)
    {
        var encontrados = new HashSet<string>();
        foreach (var descripcion in rolesPortal)
        {
            var normalizado = Normalizar(descripcion);
            if (!normalizado.EndsWith(SUFIJO, StringComparison.Ordinal))
            {
                continue;
            }

            var prefijo = normalizado[..^SUFIJO.Length];
            foreach (var (candidato, rol) in PRECEDENCIA)
            {
                if (prefijo == candidato)
                {
                    encontrados.Add(rol);
                }
            }
        }

        return [.. PRECEDENCIA.Select(p => p.Rol).Where(encontrados.Contains)];
    }

    /// <summary>El rol de mayor privilegio entre los roles del portal, o null si ninguno da acceso a IA Autos.</summary>
    public static string? Resolver(IEnumerable<string?> rolesPortal) => Coincidencias(rolesPortal).FirstOrDefault();

    internal static string Normalizar(string? texto) => IAAutos.Aplicacion.Comun.clsTextoComparable.Normalizar(texto);
}
