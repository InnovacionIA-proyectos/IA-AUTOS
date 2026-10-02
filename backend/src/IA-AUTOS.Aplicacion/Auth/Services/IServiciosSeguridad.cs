using IAAutos.Aplicacion.Auth.DTOAuth;

namespace IAAutos.Aplicacion.Auth.Services;

public interface IServicioContrasenas
{
    /// <summary>Genera el hash para almacenar una contrasena nueva.</summary>
    string Hashear(string password);

    /// <summary>
    /// Verifica la contrasena en tiempo constante. Si no hay hash almacenado hace el mismo trabajo contra un
    /// hash de relleno (no revela si el usuario existe por el tiempo de respuesta) y devuelve false.
    /// </summary>
    bool Verificar(string password, string? hashAlmacenado);

    /// <summary>True si el hash esta en un formato anterior (por ejemplo scrypt) y conviene regenerarlo.</summary>
    bool RequiereActualizacion(string? hashAlmacenado);
}

public interface IEmisorToken
{
    /// <summary>Emite el JWT (HS256) con los claims sub, email y user_role.</summary>
    string Emitir(UsuarioAutenticadoDto usuario);
}
