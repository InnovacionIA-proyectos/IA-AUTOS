using System.Text.Json.Serialization;

namespace IAAutos.Aplicacion.Auth.DTOAuth;

public sealed record LoginRequest(string? Email, string? Password);

/// <summary>Cuerpo de login-conductor: la cedula y el PIN de quien entra desde la app.</summary>
public sealed record LoginConductorRequest(string? Cedula, string? Pin);

/// <summary>Cuerpo de cambiar-pin: el PIN actual y el nuevo.</summary>
public sealed record CambiarPinRequest(string? PinActual, string? PinNuevo);

/// <summary>Quien inicio sesion. <c>DebeCambiarPin</c> avisa a la app que debe llevar a la persona a cambiar su PIN antes de seguir.</summary>
public sealed record UsuarioAutenticadoDto(Guid Id, string Nombre, string? Email, string Rol, string? Cedula = null, bool DebeCambiarPin = false);

public sealed record ClienteDto(Guid Id, string Codigo, string Nombre);

/// <summary>
/// Perfil del usuario autenticado (GET /api/auth/me): quien es, con que entra, su cliente y lo que puede hacer (<c>Capacidades</c>,
/// nombres estables para armar el menu).
/// </summary>
public sealed record PerfilDto(
    Guid Id,
    string Nombre,
    string? Email,
    string Rol,
    string? SubtipoInspector,
    string? Cedula,
    string TipoCredencial,
    bool DebeCambiarPin,
    ClienteDto? Cliente,
    IReadOnlyList<string> Capacidades);

public sealed record LoginResponse(string Token, UsuarioAutenticadoDto Usuario);

/// <summary>
/// Usuario del portal tal como el portal lo guarda en su sesion del navegador. EXCEPCION a la convencion de nombres de la API:
/// los campos van en camelCase y se declaran de forma explicita (la lectura distingue mayusculas y los numeros son estrictos).
/// Solo se usa el id: el nombre y los permisos los puede editar el cliente, asi que el servidor no los cree para autorizar y
/// revalida el id contra TI.
/// </summary>
public sealed record LoginPortalUsuarioDto(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("nombreUsuario")] string? NombreUsuario,
    [property: JsonPropertyName("permisos")] IReadOnlyList<LoginPortalPermisoDto>? Permisos);

/// <summary>
/// Permiso del portal. Todos sus campos son texto, incluido el id; y el campo se llama <c>idOcion</c> (sin la "p"), tal como lo
/// envia el portal.
/// </summary>
public sealed record LoginPortalPermisoDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("descripcion")] string? Descripcion,
    [property: JsonPropertyName("idOcion")] string? IdOcion,
    [property: JsonPropertyName("opcion")] string? Opcion);

/// <summary>Respuesta de login-portal: access y refresh en el cuerpo (mismo esquema de sesion que EmbarQA) y el usuario.</summary>
public sealed record LoginPortalResponse(string Access, string Refresh, UsuarioAutenticadoDto User);

/// <summary>Cuerpo de refresh y de logout.</summary>
public sealed record RefreshRequest(string? Refresh);

/// <summary>Respuesta de refresh: el par nuevo. El refresh anterior queda revocado.</summary>
public sealed record RefreshResponse(string Access, string Refresh);
