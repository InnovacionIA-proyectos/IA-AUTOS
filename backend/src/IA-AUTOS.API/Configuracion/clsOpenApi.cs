using System.Text.Json.Nodes;
using IAAutos.API.Errores;
using IAAutos.API.Seguridad;
using IAAutos.Dominio.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

namespace IAAutos.API.Configuracion;

/// <summary>Declara el tipo del cuerpo JSON de una accion (el cuerpo se lee a mano, asi que no aparece solo en el contrato).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CuerpoSolicitudAttribute(Type tipo) : Attribute
{
    public Type Tipo { get; } = tipo;

    /// <summary>True si el cuerpo es un arreglo JSON de <see cref="Tipo"/> (por ejemplo, login-portal).</summary>
    public bool EsArreglo { get; init; }

    /// <summary>True si la accion acepta tambien multipart/form-data con un archivo en el campo "archivo" (subida de fotos).</summary>
    public bool TambienMultipart { get; init; }
}

/// <summary>
/// Un codigo de error propio de la accion (por ejemplo <c>ETAPA_PREVIA_SIN_CIERRE</c>), ademas de los comunes que el contrato deduce de la ruta (400, 401, 403, 404, 409, 429). Aparece en las respuestas de
/// error de esa operacion del contrato OpenAPI.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CodigoErrorAttribute(string codigo, string descripcion) : Attribute
{
    public string Codigo { get; } = codigo;

    public string Descripcion { get; } = descripcion;
}

/// <summary>Forma comun de los errores de la API (ver docs/api.md): <c>details</c> es opcional (<c>fieldErrors</c> en una validacion, <c>etapa_previa</c> en una etapa previa sin cierre...).</summary>
public sealed record ErrorApiDto(string Error, string Code, string Message, string Timestamp, object? Details = null);

/// <summary>
/// Completa el contrato OpenAPI que consumen los clientes (Angular y Flutter): cuerpos de solicitud, esquema Bearer,
/// formato de error, etiquetas por modulo e identificadores de operacion estables.
/// </summary>
internal static class clsOpenApi
{
    public const string ESQUEMA_BEARER = "bearerAuth";
    public const string VERSION = "2.7.0";

    /// <summary>Lo que se publica en <c>info.description</c>: la regla de nombres y sus excepciones.</summary>
    public const string DESCRIPCION =
        "Traza Autos 360 / Inventario Visual por VIN. Autenticacion con JWT Bearer. Todo va en snake_case; las unicas excepciones en camelCase son el cuerpo de login-portal (lo fija el portal), "
        + "la respuesta de la ingesta Sofasa y el endpoint /api/metricas/rechazo. La guia para pasar de la version 1 a la 2 esta en docs/migracion-v1-v2.md.";

    public static void Configurar(OpenApiOptions opciones)
    {
        // Cada tipo del contrato lleva un ejemplo con sus nombres reales (snake_case) y valores de muestra.
        opciones.AddSchemaTransformer((esquema, contexto, ct) =>
        {
            // Un tipo complejo lleva su ejemplo donde se defina su esquema (tambien cuando su primer uso es como propiedad de otro tipo); las listas y los objetos vacios solo se ejemplifican como tipo propio.
            if (esquema.Examples is null or { Count: 0 } && clsEjemplosOpenApi.De(contexto.JsonTypeInfo.Type, contexto.JsonTypeInfo.Options) is { } ejemplo
                && ((ejemplo is JsonObject { Count: > 0 }) || (contexto.JsonPropertyInfo is null && ejemplo is JsonArray)))
            {
                esquema.Examples = [ejemplo];
            }

            return Task.CompletedTask;
        });

        opciones.AddDocumentTransformer((documento, contexto, ct) =>
        {
            documento.Info = new OpenApiInfo
            {
                Title = "IA-AUTOS API",
                Version = VERSION,
                Description = DESCRIPCION,
            };
            documento.Servers = [];
            documento.Components ??= new OpenApiComponents();
            documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            documento.Components.SecuritySchemes[ESQUEMA_BEARER] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
            };
            return Task.CompletedTask;
        });

        opciones.AddOperationTransformer(async (operacion, contexto, ct) =>
        {
            var descriptor = contexto.Description.ActionDescriptor as ControllerActionDescriptor;

            if (descriptor is not null)
            {
                var controlador = descriptor.ControllerName.StartsWith("cls", StringComparison.Ordinal)
                    ? descriptor.ControllerName[3..]
                    : descriptor.ControllerName;
                operacion.Tags = new HashSet<OpenApiTagReference> { new(controlador) };
                operacion.OperationId = $"{controlador}_{descriptor.ActionName}";

                // Una ruta marcada [Obsolete] se publica como deprecated: se retirara (y responde el encabezado Deprecation).
                if (descriptor.MethodInfo.IsDefined(typeof(ObsoleteAttribute), false))
                {
                    operacion.Deprecated = true;
                }

                var cuerpo = descriptor.MethodInfo.GetCustomAttributes(typeof(CuerpoSolicitudAttribute), false)
                    .Cast<CuerpoSolicitudAttribute>()
                    .FirstOrDefault();
                if (cuerpo is not null)
                {
                    var esquema = await contexto.GetOrCreateSchemaAsync(cuerpo.Tipo, null, ct);
                    // Se registra como componente con nombre para que los clientes generados tengan un tipo (y no uno anonimo).
                    contexto.Document!.Components ??= new OpenApiComponents();
                    contexto.Document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
                    contexto.Document.Components.Schemas[cuerpo.Tipo.Name] = esquema;
                    var contenido = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = cuerpo.EsArreglo
                                ? new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchemaReference(cuerpo.Tipo.Name) }
                                : new OpenApiSchemaReference(cuerpo.Tipo.Name),
                        },
                    };
                    if (cuerpo.TambienMultipart)
                    {
                        contenido["multipart/form-data"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["archivo"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                                },
                                Required = new HashSet<string> { "archivo" },
                            },
                        };
                    }

                    operacion.RequestBody = new OpenApiRequestBody { Required = true, Content = contenido };
                }
            }

            // Seguridad: todo exige Bearer salvo los endpoints marcados como anonimos.
            var anonimo = contexto.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
            if (!anonimo)
            {
                operacion.Security =
                [
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(ESQUEMA_BEARER, null)] = [] },
                ];
            }

            var esquemaError = await contexto.GetOrCreateSchemaAsync(typeof(ErrorApiDto), null, ct);
            contexto.Document!.Components ??= new OpenApiComponents();
            contexto.Document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
            contexto.Document.Components.Schemas[nameof(ErrorApiDto)] = esquemaError;
            operacion.Responses ??= new OpenApiResponses();
            AgregarErrores(operacion, contexto.Description, descriptor, anonimo);
            operacion.Responses["default"] = RespuestaDeError("Error inesperado (INTERNAL_ERROR, 500) o indisponibilidad de la base (DB_UNAVAILABLE, 503). Ver docs/api.md.", [(clsCodigosError.INTERNAL_ERROR, "Error inesperado")]);
        });
    }

    /// <summary>
    /// Los errores propios de cada operacion, por codigo HTTP. Se deducen de la ruta (un cuerpo o parametros: 400; sin ser anonima: 401 y 403; con identificador: 404; las que escriben: 409; las que limitan
    /// intentos: 429) y de los <see cref="CodigoErrorAttribute"/> de la accion. Todos usan el cuerpo comun <see cref="ErrorApiDto"/>.
    /// </summary>
    private static void AgregarErrores(OpenApiOperation operacion, Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription descripcion, ControllerActionDescriptor? descriptor, bool anonimo)
    {
        var porEstado = new SortedDictionary<int, List<(string Codigo, string Descripcion)>>();

        void Agregar(string codigo, string texto)
        {
            var estado = clsCuerpoError.EstadoPorCodigo(codigo);
            if (!porEstado.TryGetValue(estado, out var lista))
            {
                porEstado[estado] = lista = [];
            }

            lista.Add((codigo, texto));
        }

        var metadata = descripcion.ActionDescriptor.EndpointMetadata;
        var tieneCuerpo = descriptor?.MethodInfo.IsDefined(typeof(CuerpoSolicitudAttribute), false) == true;
        var tieneRuta = descripcion.ParameterDescriptions.Any(p => p.Source == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Path);
        var tieneConsulta = descripcion.ParameterDescriptions.Any(p => p.Source == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Query);
        var escribe = descripcion.HttpMethod is "POST" or "PUT" or "PATCH" or "DELETE";
        var esAuth = descripcion.RelativePath?.StartsWith("api/auth/", StringComparison.Ordinal) == true;

        if (tieneCuerpo || tieneConsulta || tieneRuta)
        {
            Agregar(clsCodigosError.VALIDATION_ERROR, "Cuerpo o filtros invalidos; details.fieldErrors dice cada campo");
        }

        if (!anonimo || esAuth)
        {
            Agregar(clsCodigosError.UNAUTHORIZED, "Sin sesion, sesion vencida o credenciales incorrectas");
        }

        if (!anonimo)
        {
            Agregar(clsCodigosError.FORBIDDEN, "El rol no puede hacerlo o el dato es de otro cliente o de un viaje ajeno");
            if (!metadata.OfType<PermitirConCambioDePinAttribute>().Any())
            {
                Agregar(clsCodigosError.PIN_CHANGE_REQUIRED, "El PIN es temporal: hay que cambiarlo con POST /api/auth/cambiar-pin");
            }
        }

        if (tieneRuta)
        {
            Agregar(clsCodigosError.NOT_FOUND, "El recurso no existe");
        }

        if (escribe && (!anonimo || esAuth))
        {
            Agregar(clsCodigosError.CONFLICT, "El estado actual no admite la accion (duplicado, ya cerrado...)");
        }

        if (metadata.OfType<EnableRateLimitingAttribute>().Any())
        {
            Agregar(clsCodigosError.RATE_LIMITED, "Demasiados intentos desde la misma IP");
        }

        foreach (var propio in descriptor?.MethodInfo.GetCustomAttributes(typeof(CodigoErrorAttribute), false).Cast<CodigoErrorAttribute>() ?? [])
        {
            Agregar(propio.Codigo, propio.Descripcion);
        }

        foreach (var (estado, codigos) in porEstado)
        {
            operacion.Responses![estado.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                RespuestaDeError($"Error {estado}: {string.Join(", ", codigos.Select(c => c.Codigo))}", codigos);
        }
    }

    private static OpenApiResponse RespuestaDeError(string descripcion, IReadOnlyList<(string Codigo, string Descripcion)> codigos)
    {
        var ejemplos = new Dictionary<string, IOpenApiExample>();
        foreach (var (codigo, texto) in codigos)
        {
            ejemplos[codigo] = new OpenApiExample
            {
                Summary = texto,
                Value = clsEjemplosOpenApi.Error(codigo, texto, codigo == clsCodigosError.VALIDATION_ERROR ? new JsonObject { ["formErrors"] = new JsonArray(), ["fieldErrors"] = new JsonObject { ["campo"] = new JsonArray("mensaje") } } : null),
            };
        }

        return new OpenApiResponse
        {
            Description = descripcion,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = new OpenApiSchemaReference(nameof(ErrorApiDto)), Examples = ejemplos },
            },
        };
    }
}
