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
    public const string RUTA_SWAGGER = "/swagger";

    public static IServiceCollection AddApi(this IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.AddHttpContextAccessor();
        servicios.Configure<clsOpcionesEtl>(configuracion.GetSection(clsOpcionesEtl.SECCION));
        servicios.AddScoped<IUsuarioActual, clsUsuarioActualHttp>();

        servicios
            // Todo controlador exige autenticacion salvo [AllowAnonymous] (login y health). Un filtro global, a diferencia de una
            // politica de respaldo, deja que las rutas inexistentes respondan 404 y los metodos incorrectos 405.
            .AddControllers(o =>
            {
                o.Filters.Add(new AuthorizeFilter());
                o.Filters.Add(new clsFiltroCambioPin());
            })
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
                LimitePorIp(contexto, s => s.LoginMaxIntentosPorMinuto));
            o.AddPolicy(clsAuthController.POLITICA_LIMITE_LOGIN_CONDUCTOR, contexto =>
                LimitePorIp(contexto, s => s.LoginConductorMaxIntentosPorMinuto));
            o.AddPolicy(clsAuthController.POLITICA_LIMITE_LOGIN_PORTAL, contexto =>
                LimitePorIp(contexto, s => s.LoginPortalMaxIntentosPorMinuto));
            o.AddPolicy(clsAuthController.POLITICA_LIMITE_REFRESH, contexto =>
                LimitePorIp(contexto, s => s.RefreshMaxPorMinuto));
        });

        servicios.AddCors(o =>
        {
            var origenes = LeerOrigenes(configuracion);
            if (origenes.Length == 0)
            {
                return;
            }

            o.AddPolicy(POLITICA_CORS, politica => politica
                .WithOrigins(origenes)
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                .WithHeaders("Content-Type", "Authorization")
                .WithExposedHeaders("Content-Disposition")
                .SetPreflightMaxAge(TimeSpan.FromSeconds(86400)));
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

        // El generador del contrato usa las opciones JSON de Http (no las de MVC): se alinean para que el contrato refleje el JSON real.
        servicios.ConfigureHttpJsonOptions(o => clsJsonApi.Configurar(o.SerializerOptions));
        servicios.AddOpenApi(clsOpenApi.Configurar);
        return servicios;
    }

    private static RateLimitPartition<string> LimitePorIp(HttpContext contexto, Func<clsOpcionesSeguridad, int> maximo)
    {
        var permitidos = maximo(contexto.RequestServices.GetRequiredService<IOptions<clsOpcionesSeguridad>>().Value);
        var ip = contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(permitidos, 1),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
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

        if (OrigenesConfigurados(app.Configuration).Contains("*"))
        {
            // Un origen comodin deja a cualquier sitio llamar a la API desde un navegador: se ignora y hay que listar los origenes exactos.
            app.Logger.LogWarning("Cors:Origins contiene \"*\": se ignora. Liste los origenes exactos de la web (Cors__Origins=https://portal.ejemplo.com,...).");
        }

        var documentacionHabilitada = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>(CLAVE_DOCUMENTACION);

        app.Use(async (contexto, siguiente) =>
        {
            var cabeceras = contexto.Response.Headers;
            cabeceras.XContentTypeOptions = "nosniff";
            cabeceras.XFrameOptions = "DENY";
            cabeceras["Referrer-Policy"] = "no-referrer";
            // La API solo devuelve JSON: no necesita cargar nada. La interfaz de documentacion (/swagger) si necesita sus
            // propios scripts y estilos, asi que solo esa ruta (y solo si la documentacion esta habilitada) tiene una politica mas amplia.
            cabeceras.ContentSecurityPolicy = documentacionHabilitada && contexto.Request.Path.StartsWithSegments(RUTA_SWAGGER)
                ? "default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'"
                : "default-src 'none'; frame-ancestors 'none'";
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

        if (documentacionHabilitada)
        {
            app.MapOpenApi();

            // Interfaz Swagger sobre el contrato que ya genera la API: /swagger (el boton Authorize acepta el token Bearer).
            app.UseSwaggerUI(o =>
            {
                o.RoutePrefix = RUTA_SWAGGER.TrimStart('/');
                o.DocumentTitle = "IA-AUTOS API";
                o.SwaggerEndpoint("/openapi/v1.json", "IA-AUTOS API v1");
                o.EnablePersistAuthorization();
            });
        }

        return app;
    }

    /// <summary>Lee Cors:Origins como lista (appsettings) o como texto separado por comas (variable Cors__Origins).</summary>
    internal static string[] LeerOrigenes(IConfiguration configuracion) =>
        [.. OrigenesConfigurados(configuracion).Where(o => o != "*")];

    private static string[] OrigenesConfigurados(IConfiguration configuracion)
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
