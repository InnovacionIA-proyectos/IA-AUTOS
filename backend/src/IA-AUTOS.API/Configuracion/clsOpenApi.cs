using IAAutos.API.Errores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace IAAutos.API.Configuracion;

/// <summary>Declara el tipo del cuerpo JSON de una accion (el cuerpo se lee a mano, asi que no aparece solo en el contrato).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CuerpoSolicitudAttribute(Type tipo) : Attribute
{
    public Type Tipo { get; } = tipo;
}

/// <summary>Forma comun de los errores de la API (ver docs/api.md).</summary>
public sealed record ErrorApiDto(string Error, string Code, string Message, string Timestamp, object? Details);

/// <summary>
/// Completa el contrato OpenAPI que consumen los clientes (Angular y Flutter): cuerpos de solicitud, esquema Bearer,
/// formato de error, etiquetas por modulo e identificadores de operacion estables.
/// </summary>
internal static class clsOpenApi
{
    public const string ESQUEMA_BEARER = "bearerAuth";

    public static void Configurar(OpenApiOptions opciones)
    {
        opciones.AddDocumentTransformer((documento, contexto, ct) =>
        {
            documento.Info = new OpenApiInfo
            {
                Title = "IA-AUTOS API",
                Version = "1.0.0",
                Description = "Traza Autos 360 / Inventario Visual por VIN. Autenticacion con JWT Bearer. Los campos van en snake_case, salvo Reportes y la respuesta de la ingesta, en camelCase.",
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
                    operacion.RequestBody = new OpenApiRequestBody
                    {
                        Required = true,
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType { Schema = new OpenApiSchemaReference(cuerpo.Tipo.Name) },
                        },
                    };
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
            operacion.Responses["default"] = new OpenApiResponse
            {
                Description = "Error (ver docs/api.md): VALIDATION_ERROR, UNAUTHORIZED, FORBIDDEN, NOT_FOUND, CONFLICT, RATE_LIMITED, INTERNAL_ERROR",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType { Schema = new OpenApiSchemaReference(nameof(ErrorApiDto)) },
                },
            };
        });
    }
}
