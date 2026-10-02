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

    public string Emitir(UsuarioAutenticadoDto usuario)
    {
        var configuracion = opciones.Value;
        var ahora = reloj.Ahora;
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = usuario.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = usuario.Email,
                [CLAIM_ROL] = usuario.Rol,
            },
            IssuedAt = ahora.UtcDateTime,
            NotBefore = ahora.UtcDateTime,
            Expires = ahora.UtcDateTime.AddDays(configuracion.ExpiracionDias),
            SigningCredentials = new SigningCredentials(configuracion.ClaveDeFirma(), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
