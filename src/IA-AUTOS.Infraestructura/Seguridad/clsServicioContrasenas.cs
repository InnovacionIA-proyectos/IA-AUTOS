using System.Security.Cryptography;
using System.Text;
using IAAutos.Aplicacion.Auth.Services;
using Microsoft.Extensions.Options;

namespace IAAutos.Infraestructura.Seguridad;

public sealed class clsOpcionesContrasenas
{
    public const string SECCION = "Contrasenas";

    /// <summary>Iteraciones de PBKDF2-SHA256 para hashes nuevos (OWASP: 600000 o mas).</summary>
    public int Iteraciones { get; set; } = 600_000;
}

/// <summary>
/// Hashes nuevos: <c>pbkdf2-sha256$&lt;iteraciones&gt;$&lt;sal base64&gt;$&lt;hash base64&gt;</c>.
/// Hashes heredados del sistema anterior: <c>scrypt:&lt;sal hex&gt;:&lt;hash hex&gt;</c> (solo verificacion; se regeneran al ingresar).
/// </summary>
internal sealed class clsServicioContrasenas(IOptions<clsOpcionesContrasenas> opciones) : IServicioContrasenas
{
    private const string PREFIJO_PBKDF2 = "pbkdf2-sha256";
    private const string PREFIJO_SCRYPT = "scrypt";
    private const int LONGITUD_SAL = 16;
    private const int LONGITUD_HASH = 32;
    private const int SCRYPT_N = 16384;
    private const int SCRYPT_R = 8;
    private const int SCRYPT_P = 1;

    // Hash de relleno: se verifica contra el cuando el usuario no existe, para igualar el tiempo de respuesta.
    private readonly string _hashRelleno = Crear(opciones.Value.Iteraciones, "relleno-sin-usuario");

    public string Hashear(string password) => Crear(opciones.Value.Iteraciones, password);

    public bool Verificar(string password, string? hashAlmacenado)
    {
        if (string.IsNullOrEmpty(hashAlmacenado))
        {
            VerificarPbkdf2(password, _hashRelleno);
            return false;
        }

        if (hashAlmacenado.StartsWith(PREFIJO_PBKDF2 + "$", StringComparison.Ordinal))
        {
            return VerificarPbkdf2(password, hashAlmacenado);
        }

        if (hashAlmacenado.StartsWith(PREFIJO_SCRYPT + ":", StringComparison.Ordinal))
        {
            return VerificarScrypt(password, hashAlmacenado);
        }

        return false;
    }

    public bool RequiereActualizacion(string? hashAlmacenado) =>
        !string.IsNullOrEmpty(hashAlmacenado)
        && (hashAlmacenado.StartsWith(PREFIJO_SCRYPT + ":", StringComparison.Ordinal) || IteracionesInsuficientes(hashAlmacenado));

    private bool IteracionesInsuficientes(string hash)
    {
        var partes = hash.Split('$');
        return partes.Length == 4
            && int.TryParse(partes[1], out var iteraciones)
            && iteraciones < opciones.Value.Iteraciones;
    }

    private static string Crear(int iteraciones, string password)
    {
        var sal = RandomNumberGenerator.GetBytes(LONGITUD_SAL);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), sal, iteraciones, HashAlgorithmName.SHA256, LONGITUD_HASH);
        return $"{PREFIJO_PBKDF2}${iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerificarPbkdf2(string password, string almacenado)
    {
        var partes = almacenado.Split('$');
        if (partes.Length != 4 || !int.TryParse(partes[1], out var iteraciones) || iteraciones < 1)
        {
            return false;
        }

        try
        {
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerificarScrypt(string password, string almacenado)
    {
        var partes = almacenado.Split(':');
        if (partes.Length != 3 || partes[1].Length == 0 || partes[2].Length == 0)
        {
            return false;
        }

        try
        {
            var sal = Convert.FromHexString(partes[1]);
            var esperado = Convert.FromHexString(partes[2]);
            var calculado = clsScrypt.Derivar(Encoding.UTF8.GetBytes(password), sal, SCRYPT_N, SCRYPT_R, SCRYPT_P, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
