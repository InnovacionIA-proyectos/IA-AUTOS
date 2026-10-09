namespace IAAutos.Aplicacion.Auth.Services;

/// <summary>
/// Identidad de una persona en el portal de administracion (solo lectura, tablas de TI): quien es y los nombres de TODOS sus
/// roles y opciones de permiso de hoy. No incluye la contrasena del portal ni se lee nunca.
/// </summary>
public sealed record clsUsuarioPortal(
    long Id,
    string NombreUsuario,
    string? Email,
    string? DescripcionUsuario,
    IReadOnlyList<string> Roles);

/// <summary>Consulta de solo lectura a la base de usuarios del portal (TI). Nunca se escribe en ella.</summary>
public interface IPortalUsuarios
{
    /// <summary>
    /// False si la base de TI no esta configurada: en ese caso login-portal solo acepta ids ya vinculados a un usuario
    /// (por la carga inicial o por el administrador).
    /// </summary>
    bool Disponible { get; }

    /// <summary>La persona con ese id, o null si no existe en el portal. Lanza clsServicioNoDisponibleException si TI no responde.</summary>
    Task<clsUsuarioPortal?> ObtenerAsync(long id, CancellationToken ct = default);
}
