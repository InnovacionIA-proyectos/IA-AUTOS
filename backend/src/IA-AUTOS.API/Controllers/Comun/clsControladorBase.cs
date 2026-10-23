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
    /// <summary>
    /// Lee el cuerpo como <typeparamref name="T"/>. Con <paramref name="noAdmitidos"/>, un cuerpo que traiga alguno de esos campos responde 400 con
    /// «Campo no admitido» en ese campo (son los campos de un modelo anterior que ya no se aceptan).
    /// </summary>
    protected async Task<T> LeerCuerpoAsync<T>(CancellationToken ct, params string[] noAdmitidos)
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

            var presentes = noAdmitidos.Where(c => documento.RootElement.TryGetProperty(c, out _)).ToList();
            if (presentes.Count > 0)
            {
                throw new clsValidacionException(
                    "Datos inválidos",
                    new Dictionary<string, object>
                    {
                        ["formErrors"] = Array.Empty<string>(),
                        ["fieldErrors"] = presentes.ToDictionary(c => c, _ => new[] { "Campo no admitido" }),
                    });
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


    /// <summary>
    /// Lee un cuerpo objeto como diccionario campo -> valor JSON. Cada valor se clona porque el documento se libera al salir:
    /// sin el clon, los textos con escapes (ó) fallarian al leerse despues.
    /// </summary>
    protected async Task<Dictionary<string, JsonElement>> LeerCamposAsync(CancellationToken ct)
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

            var campos = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var propiedad in documento.RootElement.EnumerateObject())
            {
                campos[propiedad.Name] = propiedad.Value.Clone();
            }

            return campos;
        }
    }

    protected ObjectResult Creado<T>(T valor) => StatusCode(StatusCodes.Status201Created, valor);

    /// <summary>Lee un cuerpo que es un arreglo JSON (por ejemplo, el usuario del portal en login-portal).</summary>
    protected async Task<List<T>> LeerArregloAsync<T>(CancellationToken ct)
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
            if (documento.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new clsValidacionException("El cuerpo debe ser un arreglo JSON");
            }

            try
            {
                return documento.RootElement.Deserialize<List<T>>(clsJsonApi.OPCIONES)
                    ?? throw new clsValidacionException("El cuerpo debe ser un arreglo JSON");
            }
            catch (JsonException ex)
            {
                var campo = (ex.Path ?? string.Empty).TrimStart('$', '.');
                throw new clsValidacionException(
                    "Datos inválidos",
                    new Dictionary<string, object>
                    {
                        ["formErrors"] = Array.Empty<string>(),
                        ["fieldErrors"] = new Dictionary<string, string[]> { [campo] = ["Tipo de dato inválido"] },
                    });
            }
        }
    }
}
