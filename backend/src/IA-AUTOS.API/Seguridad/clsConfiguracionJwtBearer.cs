using System.Security.Claims;
using IAAutos.API.Errores;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Infraestructura.Seguridad;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IAAutos.API.Seguridad;

/// <summary>Falla de autenticacion con un mensaje seguro para el cliente.</summary>
internal sealed class clsFalloAutenticacionException(string mensaje) : Exception(mensaje);

/// <summary>
/// Valida el JWT HS256 (compatible con los tokens del sistema anterior) y ademas consulta la base de datos:
/// un usuario retirado o eliminado pierde el acceso de inmediato y el rol efectivo es el vigente en la base.
/// </summary>
internal sealed class clsConfiguracionJwtBearer(IOptions<clsOpcionesJwt> opcionesJwt) : IConfigureNamedOptions<JwtBearerOptions>
{
    private const string MENSAJE_SIN_TOKEN = "Falta el token de autenticación (Bearer)";
    private const string MENSAJE_TOKEN_INVALIDO = "Token de autenticación inválido o expirado";

    public void Configure(JwtBearerOptions opciones) => Configure(JwtBearerDefaults.AuthenticationScheme, opciones);

    public void Configure(string? nombre, JwtBearerOptions opciones)
    {
        opciones.MapInboundClaims = false;
        opciones.RequireHttpsMetadata = false;
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = opcionesJwt.Value.ExigeEmisor,
            ValidIssuer = opcionesJwt.Value.Issuer,
            ValidateAudience = opcionesJwt.Value.ExigeDestinatario,
            ValidAudience = opcionesJwt.Value.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = opcionesJwt.Value.ClaveDeFirma(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = clsUsuarioActualHttp.CLAIM_ROL,
        };
        opciones.Events = new JwtBearerEvents
        {
            OnTokenValidated = ValidarContraBaseDeDatosAsync,
            OnChallenge = ResponderNoAutenticadoAsync,
        };
    }

    private static async Task ValidarContraBaseDeDatosAsync(TokenValidatedContext contexto)
    {
        var sub = contexto.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(sub, out var usuarioId))
        {
            contexto.Fail(new clsFalloAutenticacionException(MENSAJE_TOKEN_INVALIDO));
            return;
        }

        var usuarios = contexto.HttpContext.RequestServices.GetRequiredService<IUsuarioRepository>();
        var usuario = await usuarios.ObtenerPorIdAsync(usuarioId, contexto.HttpContext.RequestAborted);
        if (usuario is null)
        {
            contexto.Fail(new clsFalloAutenticacionException("Sesión no válida"));
            return;
        }

        if (!usuario.Activo)
        {
            contexto.Fail(new clsFalloAutenticacionException("Usuario retirado: no tiene acceso. Contacta al administrador."));
            return;
        }

        // Los access tokens de la sesion del portal llevan sid (la familia del refresh token): si la sesion se cerro (logout)
        // o se revoco por reuso, el access token deja de servir aunque todavia no haya vencido.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(clsUsuarioActualHttp.CLAIM_ROL, usuario.Rol),
        };
        if (usuario.Email is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, usuario.Email));
        }

        if (usuario.ClienteId is { } clienteId)
        {
            claims.Add(new Claim(clsUsuarioActualHttp.CLAIM_CLIENTE, clienteId.ToString()));
        }

        // El PIN temporal se marca aqui desde la base (no desde el token): el filtro global bloquea el resto de la API hasta que lo cambie.
        if (usuario.DebeCambiarPin)
        {
            claims.Add(new Claim(clsUsuarioActualHttp.CLAIM_CAMBIAR_PIN, "1"));
        }

        var sid = contexto.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sid);
        if (sid is not null)
        {
            if (!Guid.TryParse(sid, out var familiaId))
            {
                contexto.Fail(new clsFalloAutenticacionException(MENSAJE_TOKEN_INVALIDO));
                return;
            }

            var refrescos = contexto.HttpContext.RequestServices.GetRequiredService<IRefreshTokenRepository>();
            var reloj = contexto.HttpContext.RequestServices.GetRequiredService<IReloj>();
            if (!await refrescos.FamiliaActivaAsync(familiaId, reloj.Ahora, contexto.HttpContext.RequestAborted))
            {
                contexto.Fail(new clsFalloAutenticacionException("La sesión se cerró. Inicia sesión de nuevo."));
                return;
            }

            claims.Add(new Claim(JwtRegisteredClaimNames.Sid, sid));
        }

        // Los claims efectivos salen de la base de datos, no del token.
        var identidad = new ClaimsIdentity(
            claims,
            JwtBearerDefaults.AuthenticationScheme,
            JwtRegisteredClaimNames.Sub,
            clsUsuarioActualHttp.CLAIM_ROL);
        contexto.Principal = new ClaimsPrincipal(identidad);
    }

    private static async Task ResponderNoAutenticadoAsync(JwtBearerChallengeContext contexto)
    {
        contexto.HandleResponse();
        var mensaje = contexto.AuthenticateFailure switch
        {
            clsFalloAutenticacionException propio => propio.Message,
            not null => MENSAJE_TOKEN_INVALIDO,
            _ => MENSAJE_SIN_TOKEN,
        };
        contexto.Response.Headers.WWWAuthenticate = "Bearer";
        await clsCuerpoError.EscribirAsync(contexto.HttpContext, clsCodigosError.UNAUTHORIZED, mensaje);
    }
}
