namespace IAAutos.Aplicacion.Usuarios.DTOUsuarios;

public sealed record CrearUsuarioRequest(string? Nombre, string? Email, string? Rol, string? SubtipoInspector, string? Password);

public sealed record ActualizarUsuarioRequest(
    string? Nombre,
    string? Email,
    string? Rol,
    string? SubtipoInspector,
    string? Password,
    bool? Activo);

public sealed record UsuarioDto(
    Guid Id,
    string Nombre,
    string Email,
    string Rol,
    string? SubtipoInspector,
    bool Activo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Resultado de eliminar: "eliminado" (borrado real) o "retirado" (baja logica por tener historial).</summary>
public sealed record EliminarUsuarioResponse(string Modo);

/// <summary>Vista reducida para quien no administra usuarios (selector de entrega y recibo en la app): sin correos ni estado.</summary>
public sealed record UsuarioListaDto(Guid Id, string Nombre, string Rol);
