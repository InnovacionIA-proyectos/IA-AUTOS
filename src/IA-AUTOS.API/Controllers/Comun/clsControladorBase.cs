using System.Text.Json;
using IAAutos.API.Errores;
using IAAutos.Dominio.Comun;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Comun;

/// <summary>
/// Base de los controladores. Lee el cuerpo JSON con los mismos errores que la API original:
/// 400 "El cuerpo debe ser JSON válido" / "El cuerpo debe ser un objeto JSON" / "Datos inválidos".
/// </summary>
[ApiController]
public abstract class clsControladorBase : ControllerBase
{
    protected async Task<T> LeerCuerpoAsync<T>(CancellationToken ct)
    {
        JsonDocument documento;
        try
        {
            documento = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            throw new clsValidacionException("El cuerpo debe ser JSON válido");
        }

        using (documento)
        {
            if (documento.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new clsValidacionException("El cuerpo debe ser un objeto JSON");
            }

            try
            {
                return documento.RootElement.Deserialize<T>(clsJsonApi.OPCIONES)
                    ?? throw new clsValidacionException("El cuerpo debe ser un objeto JSON");
            }
            catch (JsonException ex)
            {
                var campo = (ex.Path ?? string.Empty).TrimStart('$', '.');
                var mensaje = ex.Message.Contains("ISO 8601", StringComparison.Ordinal)
                    ? "Debe ser una fecha/hora ISO 8601 válida"
                    : "Tipo de dato inválido";
                throw new clsValidacionException(
                    "Datos inválidos",
                    new Dictionary<string, object>
                    {
                        ["formErrors"] = Array.Empty<string>(),
                        ["fieldErrors"] = new Dictionary<string, string[]> { [campo] = [mensaje] },
                    });
            }
        }
    }

    protected ObjectResult Creado<T>(T valor) => StatusCode(StatusCodes.Status201Created, valor);
}
