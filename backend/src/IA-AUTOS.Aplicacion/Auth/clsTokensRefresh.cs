using System.Security.Cryptography;
using System.Text;

namespace IAAutos.Aplicacion.Auth;

/// <summary>
/// Generacion y hash del refresh token. El valor que recibe el navegador es aleatorio (256 bits) y opaco; en la base solo
/// se guarda su SHA-256, asi que una filtracion de la tabla no permite usar los tokens.
/// </summary>
public static class clsTokensRefresh
{
    public static (string Plano, string Hash) Generar()
    {
        var plano = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (plano, Hash(plano));
    }

    public static string Hash(string plano) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plano))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
