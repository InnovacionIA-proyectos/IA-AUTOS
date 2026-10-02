using IAAutos.Dominio.Comun;

namespace IAAutos.Aplicacion.Comun;

/// <summary>Usuario autenticado en la solicitud en curso (nulo en rutas publicas).</summary>
public interface IUsuarioActual
{
    Guid? Id { get; }

    string? Rol { get; }

    string? Email { get; }
}

public static class clsUsuarioActualExtensiones
{
    public static Guid IdObligatorio(this IUsuarioActual actual) =>
        actual.Id ?? throw new clsNoAutenticadoException("Sesión no válida");

    public static bool EsAdministrador(this IUsuarioActual actual) =>
        actual.Rol is IAAutos.Dominio.Usuarios.clsRol.ADMIN or IAAutos.Dominio.Usuarios.clsRol.SUPER_ADMIN;
}
