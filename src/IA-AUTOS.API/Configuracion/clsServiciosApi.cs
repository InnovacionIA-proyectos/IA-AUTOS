using System.Threading.RateLimiting;
using IAAutos.API.Controllers.Auth;
using IAAutos.API.Controllers.Sofasa;
using IAAutos.API.Errores;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Infraestructura.Seguridad;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace IAAutos.API.Configuracion;

public static class clsServiciosApi
{
    public const string POLITICA_CORS = "api";
    public const string CLAVE_PROXY = "Proxy:ConfiarEnEncabezadosReenviados";
    public const string CLAVE_DOCUMENTACION = "Documentacion:Habilitada";

    public static IServiceCollection AddApi(this IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AddHttpContextAccessor();
        servicios.Configure<clsOpcionesEtl>(configuracion.GetSection(clsOpcionesEtl.SECCION));
        servicios.AddScoped<IUsuarioActual, clsUsuarioActualHttp>();

        servicios
            // Todo controlador exige autenticacion salvo [AllowAnonymous] (login y health). Un filtro global, a diferencia de una
            // politica de respaldo, deja que las rutas inexistentes respondan 404 y los metodos incorrectos 405.
            .AddControllers(o => o.Filters.Add(new AuthorizeFilter()))
            .AddJsonOptions(o => clsJsonApi.Configurar(o.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(o => o.SuppressModelStateInvalidFilter = true);

        servicios.AddProblemDetails();
        servicios.AddExceptionHandler<clsManejadorExcepciones>();

        // Falla al iniciar (no en la primera peticion) si el secreto JWT falta o es corto.
        servicios.AddOptions<clsOpcionesJwt>()
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Secret) && o.Secret.Length >= clsOpcionesJwt.SECRETO_MINIMO,
                $"Jwt:Secret es obligatorio y debe tener al menos {clsOpcionesJwt.SECRETO_MINIMO} caracteres (variable Jwt__Secret).")
            .ValidateOnStart();

        servicios.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        servicios.ConfigureOptions<clsConfiguracionJwtBearer>();
        servicios.AddAuthorization();

        servicios.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (contexto, ct) =>
            {
                contexto.HttpContext.Response.Headers.RetryAfter = "60";
                await clsCuerpoError.EscribirAsync(
                    contexto.HttpContext,
                    clsCodigosError.RATE_LIMITED,
                    "Demasiados intentos. Intenta de nuevo en un minuto.");
            };
            o.AddPolicy(clsAuthController.POLITICA_LIMITE_LOGIN, contexto =>
            {
                var maximo = contexto.RequestServices.GetRequiredService<IOptions<clsOpcionesSeguridad>>().Value.LoginMaxIntentosPorMinuto;
                var ip = contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(maximo, 1),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });

        servicios.AddCors(o =>
        {
            var origenes = LeerOrigenes(configuracion);
            if (origenes.Length == 0)
            {
                return;
            }

            o.AddPolicy(POLITICA_CORS, politica =>
            {
                if (origenes.Contains("*"))
                {
                    politica.AllowAnyOrigin();
                }
                else
                {
                    politica.WithOrigins(origenes);
                }

                politica
                    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                    .WithHeaders("Content-Type", "Authorization")
                    .SetPreflightMaxAge(TimeSpan.FromSeconds(86400));
            });
        });

        // Detras de un proxy inverso: solo se confia en X-Forwarded-* cuando se habilita de forma explicita.
        if (configuracion.GetValue<bool>(CLAVE_PROXY))
        {
            servicios.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });
        }

        servicios.AddOpenApi();
        return servicios;
    }

    public static WebApplication UsarPipelineApi(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>(CLAVE_PROXY))
        {
            app.UseForwardedHeaders();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages(async contexto =>
        {
            var http = contexto.HttpContext;
            switch (http.Response.StatusCode)
            {
                case StatusCodes.Status404NotFound:
                    await clsCuerpoError.EscribirAsync(http, clsCodigosError.NOT_FOUND, $"Ruta no encontrada: {http.Request.Path}");
                    break;
                case StatusCodes.Status405MethodNotAllowed:
                    var permitidos = http.Response.Headers.Allow.ToString();
                    var sufijo = permitidos.Length > 0 ? $". Permitidos: {permitidos}" : string.Empty;
                    await clsCuerpoError.EscribirAsync(
                        http,
                        clsCodigosError.METHOD_NOT_ALLOWED,
                        $"Método {http.Request.Method} no permitido en {http.Request.Path}{sufijo}");
                    break;
            }
        });

        app.Use(async (contexto, siguiente) =>
        {
            var cabeceras = contexto.Response.Headers;
            cabeceras.XContentTypeOptions = "nosniff";
            cabeceras.XFrameOptions = "DENY";
            cabeceras["Referrer-Policy"] = "no-referrer";
            cabeceras.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            contexto.Response.OnStarting(() =>
            {
                if (!cabeceras.ContainsKey("Cache-Control"))
                {
                    cabeceras.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            await siguiente();
        });

        if (LeerOrigenes(app.Configuration).Length > 0)
        {
            app.UseCors(POLITICA_CORS);
        }

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>(CLAVE_DOCUMENTACION))
        {
            app.MapOpenApi();
        }

        return app;
    }

    /// <summary>Lee Cors:Origins como lista (appsettings) o como texto separado por comas (variable Cors__Origins).</summary>
    internal static string[] LeerOrigenes(IConfiguration configuracion)
    {
        var lista = configuracion.GetSection("Cors:Origins").Get<string[]>() ?? [];
        var plano = configuracion["Cors:Origins"];
        if (!string.IsNullOrWhiteSpace(plano))
        {
            lista = [.. lista, .. plano.Split(',')];
        }

        return lista.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToArray();
    }
}
