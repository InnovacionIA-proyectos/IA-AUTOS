using System.Text.RegularExpressions;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Usuarios;

/// <summary>Usuario del sistema (inspector, conductor, administrador, etc.). Admite baja logica.</summary>
public sealed partial class clsUsuario : clsEntidadBase
{
    private clsUsuario()
    {
    }

    public string Nombre { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string Rol { get; private set; } = string.Empty;

    public string? SubtipoInspector { get; private set; }

    /// <summary>Baja logica: false significa retirado (conserva historial, no puede ingresar).</summary>
    public bool Activo { get; private set; } = true;

    public string? PasswordHash { get; private set; }

    /// <summary>
    /// Id de la persona en el portal de administracion (tblSAPUsuariosApp.ID). Nulo en quien no entra por el portal
    /// (por ejemplo, los conductores). Es unico entre los usuarios que lo tienen.
    /// </summary>
    public long? PortalUsuarioId { get; private set; }

    public static clsUsuario Crear(
        string nombre,
        string email,
        string rol,
        string? subtipoInspector,
        string passwordHash,
        DateTimeOffset ahora)
    {
        var usuario = new clsUsuario
        {
            Nombre = clsValidador.Requerido(nombre, "El nombre es requerido"),
            Email = NormalizarEmail(email),
            Rol = ValidarRol(rol),
            SubtipoInspector = ValidarSubtipo(subtipoInspector),
            PasswordHash = passwordHash,
            Activo = true,
        };
        usuario.Inicializar(ahora);
        return usuario;
    }

    /// <summary>Actualiza solo los campos recibidos (los nulos se conservan).</summary>
    public void Actualizar(
        string? nombre,
        string? email,
        string? rol,
        string? subtipoInspector,
        string? nuevoPasswordHash,
        bool? activo,
        DateTimeOffset ahora)
    {
        if (nombre is not null)
        {
            Nombre = clsValidador.Requerido(nombre, "El nombre es requerido");
        }

        if (email is not null)
        {
            Email = NormalizarEmail(email);
        }

        if (rol is not null)
        {
            Rol = ValidarRol(rol);
        }

        if (subtipoInspector is not null)
        {
            SubtipoInspector = ValidarSubtipo(subtipoInspector);
        }

        if (nuevoPasswordHash is not null)
        {
            PasswordHash = nuevoPasswordHash;
        }

        if (activo is not null)
        {
            Activo = activo.Value;
        }

        Tocar(ahora);
    }

    /// <summary>Reemplaza el hash (por ejemplo, al actualizar un hash legado en el siguiente login).</summary>
    public void ReemplazarPasswordHash(string passwordHash, DateTimeOffset ahora)
    {
        PasswordHash = passwordHash;
        Tocar(ahora);
    }

    /// <summary>Vincula al usuario con su identidad en el portal (inicio de sesion unico).</summary>
    public void VincularPortal(long portalUsuarioId, DateTimeOffset ahora)
    {
        if (portalUsuarioId <= 0)
        {
            throw new clsValidacionException("El id del portal debe ser positivo");
        }

        PortalUsuarioId = portalUsuarioId;
        Tocar(ahora);
    }

    /// <summary>Baja logica: conserva el historial de operaciones del usuario.</summary>
    public void Retirar(DateTimeOffset ahora)
    {
        Activo = false;
        Tocar(ahora);
    }

    /// <summary>Un usuario no puede eliminarse a si mismo.</summary>
    public void AsegurarPuedeSerEliminadoPor(Guid actorId)
    {
        if (Id == actorId)
        {
            throw new clsConflictoException("No puedes eliminar tu propio usuario");
        }
    }

    private static string NormalizarEmail(string email)
    {
        var limpio = clsValidador.Requerido(email, "Email inválido").ToLowerInvariant();
        if (!FormatoEmail().IsMatch(limpio))
        {
            throw new clsValidacionException("Email inválido");
        }

        return limpio;
    }

    private static string ValidarRol(string rol)
    {
        clsValidador.EnCatalogo(rol, clsRol.VALORES, "rol");
        return rol;
    }

    private static string? ValidarSubtipo(string? subtipo)
    {
        if (subtipo is null)
        {
            return null;
        }

        clsValidador.EnCatalogo(subtipo, clsSubtipoInspector.VALORES, "subtipo_inspector");
        return subtipo;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex FormatoEmail();
}
