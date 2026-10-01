using FluentValidation;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Aplicacion.Auth;

/// <summary>Inicio de sesion por correo y contrasena, y consulta del usuario actual.</summary>
public sealed class clsAuthService(
    IUsuarioRepository usuarios,
    IServicioContrasenas contrasenas,
    IEmisorToken emisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IReloj reloj,
    IValidator<LoginRequest> validador)
{
    public async Task<LoginResponse> LoginAsync(LoginRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var email = solicitud.Email!.Trim();
        var password = solicitud.Password!;

        var usuario = await usuarios.ObtenerPorEmailAsync(email, ct);
        var correcta = contrasenas.Verificar(password, usuario?.PasswordHash);

        // Mensaje generico ante cualquier fallo: no revela si el correo existe.
        if (usuario is null || !correcta)
        {
            throw new clsNoAutenticadoException("Correo o contraseña incorrectos");
        }

        if (!usuario.Activo)
        {
            throw new clsNoAutenticadoException("Usuario retirado: no tiene acceso. Contacta al administrador.");
        }

        // Los hashes en formato anterior (scrypt) se regeneran en el formato vigente al primer ingreso exitoso.
        if (contrasenas.RequiereActualizacion(usuario.PasswordHash))
        {
            usuario.ReemplazarPasswordHash(contrasenas.Hashear(password), reloj.Ahora);
        }

        auditor.Registrar("auth.login", "usuarios", usuario.Id, usuario.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);

        var dto = new UsuarioAutenticadoDto(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol);
        return new LoginResponse(emisor.Emitir(dto), dto);
    }

    public async Task<UsuarioAutenticadoDto> ObtenerPerfilAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await usuarios.ObtenerPorIdAsync(usuarioId, ct)
            ?? throw new clsNoAutenticadoException("Sesión no válida");
        return new UsuarioAutenticadoDto(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol);
    }
}
