namespace IAAutos.Aplicacion.Auth.DTOAuth;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record UsuarioAutenticadoDto(Guid Id, string Nombre, string Email, string Rol);

public sealed record LoginResponse(string Token, UsuarioAutenticadoDto Usuario);
