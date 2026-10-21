namespace IAAutos.Dominio.Usuarios;

public interface IUsuarioRepository
{
    Task<clsUsuario?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Busca por correo sin distinguir mayusculas.</summary>
    Task<clsUsuario?> ObtenerPorEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Busca por cedula (ya normalizada).</summary>
    Task<clsUsuario?> ObtenerPorCedulaAsync(string cedula, CancellationToken ct = default);

    /// <summary>Busca por el id del portal (inicio de sesion unico).</summary>
    Task<clsUsuario?> ObtenerPorPortalIdAsync(long portalUsuarioId, CancellationToken ct = default);

    /// <summary>Usuarios por cedula (ya normalizada): cedula -> usuario. Sirve para identificar al conductor en la carga de viajes.</summary>
    Task<IReadOnlyDictionary<string, clsUsuario>> ObtenerPorCedulasAsync(IReadOnlyCollection<string> cedulas, CancellationToken ct = default);

    /// <summary>Usuarios ordenados por nombre, con <paramref name="clienteId"/> solo los de ese cliente (selector de entrega y recibo).</summary>
    Task<IReadOnlyList<clsUsuario>> ListarAsync(Guid? clienteId = null, CancellationToken ct = default);

    /// <summary>Los usuarios activos de ese cliente con alguno de esos roles (a quienes llegan las alertas del cliente), ordenados por nombre.</summary>
    Task<IReadOnlyList<clsUsuario>> ObtenerActivosPorRolesAsync(IReadOnlyCollection<string> roles, Guid clienteId, CancellationToken ct = default);

    /// <summary>Busqueda paginada para la gestion web, ordenada por nombre.</summary>
    Task<(IReadOnlyList<clsUsuario> Filas, int Total)> BuscarAsync(clsFiltroUsuarios filtro, int saltar, int tomar, CancellationToken ct = default);

    /// <summary>Correos ya registrados entre los indicados (ya normalizados).</summary>
    Task<IReadOnlySet<string>> EmailsExistentesAsync(IReadOnlyCollection<string> emails, CancellationToken ct = default);

    /// <summary>Cedulas ya registradas entre las indicadas (ya normalizadas, sin distinguir mayusculas).</summary>
    Task<IReadOnlySet<string>> CedulasExistentesAsync(IReadOnlyCollection<string> cedulas, CancellationToken ct = default);

    /// <summary>True si el usuario tiene operaciones asociadas (inspecciones, transferencias, viajes, auditoria).</summary>
    Task<bool> TieneHistorialAsync(Guid id, CancellationToken ct = default);

    void Agregar(clsUsuario usuario);

    void Eliminar(clsUsuario usuario);
}

/// <summary>Filtros de la busqueda de usuarios. <c>Texto</c> busca en nombre, correo y cedula.</summary>
public sealed record clsFiltroUsuarios(string? Texto, string? Rol, bool? Activo, Guid? ClienteId);
