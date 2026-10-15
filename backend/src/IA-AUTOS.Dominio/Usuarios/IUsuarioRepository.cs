namespace IAAutos.Dominio.Usuarios;

public interface IUsuarioRepository
{
    Task<clsUsuario?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Busca por correo sin distinguir mayusculas.</summary>
    Task<clsUsuario?> ObtenerPorEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Busca por el id del portal (inicio de sesion unico).</summary>
    Task<clsUsuario?> ObtenerPorPortalIdAsync(long portalUsuarioId, CancellationToken ct = default);

    /// <summary>Usuarios por cedula (ya normalizada): cedula -> usuario. Sirve para identificar al conductor en la carga de viajes.</summary>
    Task<IReadOnlyDictionary<string, clsUsuario>> ObtenerPorCedulasAsync(IReadOnlyCollection<string> cedulas, CancellationToken ct = default);

    /// <summary>Todos los usuarios ordenados por nombre (gestion web y selector de entrega y recibo).</summary>
    Task<IReadOnlyList<clsUsuario>> ListarAsync(CancellationToken ct = default);

    /// <summary>True si el usuario tiene operaciones asociadas (inspecciones, transferencias, viajes, auditoria).</summary>
    Task<bool> TieneHistorialAsync(Guid id, CancellationToken ct = default);

    void Agregar(clsUsuario usuario);

    void Eliminar(clsUsuario usuario);
}
