using System.Text;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Comun;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IAAutos.Infraestructura.Seguridad;

public sealed class clsOpcionesJwt
{
    public const string SECCION = "Jwt";
    public const int SECRETO_MINIMO = 32;

    /// <summary>Secreto HS256. Debe ser el mismo que firmo los tokens vigentes mientras existan sesiones antiguas.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Vigencia del token emitido en el login (dias).</summary>
    public int ExpiracionDias { get; set; } = 30;

    /// <summary>
    /// Emisor del token (claim iss). Si se configura, todos los tokens nuevos lo llevan y la API EXIGE que coincida; los
    /// tokens emitidos antes sin emisor dejan de valer y las personas deben iniciar sesion de nuevo. Vacio = no se exige.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>Destinatario del token (claim aud), con la misma regla que <see cref="Issuer"/>.</summary>
    public string? Audience { get; set; }

    public bool ExigeEmisor => !string.IsNullOrWhiteSpace(Issuer);

    public bool ExigeDestinatario => !string.IsNullOrWhiteSpace(Audience);

    public SymmetricSecurityKey ClaveDeFirma()
    {
        if (string.IsNullOrWhiteSpace(Secret) || Secret.Length < SECRETO_MINIMO)
        {
            throw new InvalidOperationException(
                $"Jwt:Secret es obligatorio y debe tener al menos {SECRETO_MINIMO} caracteres (variable de entorno Jwt__Secret).");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
    }
}

/// <summary>Emite el JWT HS256 con los mismos claims que el sistema anterior (sub, email, user_role).</summary>
internal sealed class clsEmisorTokenJwt(IOptions<clsOpcionesJwt> opciones, IReloj reloj) : IEmisorToken
{
    public const string CLAIM_ROL = "user_role";

    /// <summary>Identifica la sesion (la familia del refresh token) para revocar el access token al cerrar sesion.</summary>
    public const string CLAIM_SESION = "sid";

    public string Emitir(UsuarioAutenticadoDto usuario) =>
        Crear(usuario, TimeSpan.FromDays(opciones.Value.ExpiracionDias), familiaId: null);

    public string EmitirSesion(UsuarioAutenticadoDto usuario, Guid familiaId, TimeSpan vigencia) =>
        Crear(usuario, vigencia, familiaId);

    private string Crear(UsuarioAutenticadoDto usuario, TimeSpan vigencia, Guid? familiaId)
    {
        var configuracion = opciones.Value;
        var ahora = reloj.Ahora;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = usuario.Id.ToString(),
            [JwtRegisteredClaimNames.Email] = usuario.Email,
            [CLAIM_ROL] = usuario.Rol,
        };
        if (familiaId is not null)
        {
            claims[JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N");
            claims[CLAIM_SESION] = familiaId.Value.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            Issuer = configuracion.ExigeEmisor ? configuracion.Issuer : null,
            Audience = configuracion.ExigeDestinatario ? configuracion.Audience : null,
            IssuedAt = ahora.UtcDateTime,
            NotBefore = ahora.UtcDateTime,
            Expires = ahora.UtcDateTime.Add(vigencia),
            SigningCredentials = new SigningCredentials(configuracion.ClaveDeFirma(), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
