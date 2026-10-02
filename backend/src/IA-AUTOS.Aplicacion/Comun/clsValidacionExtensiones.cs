using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using IAAutos.Dominio.Comun;

namespace IAAutos.Aplicacion.Comun;

public static class clsValidacionExtensiones
{
    private const string MENSAJE_GENERAL = "Datos inválidos";

    /// <summary>
    /// Valida la solicitud y, si hay errores, lanza una validacion con el mismo formato de detalle de la API original:
    /// { formErrors: [...], fieldErrors: { campo: [...] } }. Los nombres de campo salen en snake_case, como en el JSON.
    /// Cuando el unico error es general (sin campo) su texto pasa a ser el mensaje de la respuesta.
    /// </summary>
    public static async Task ValidarOLanzarAsync<T>(this IValidator<T> validador, T solicitud, CancellationToken ct = default)
    {
        ValidationResult resultado = await validador.ValidateAsync(solicitud, ct);
        if (resultado.IsValid)
        {
            return;
        }

        var generales = resultado.Errors.Where(e => string.IsNullOrEmpty(e.PropertyName)).Select(e => e.ErrorMessage).ToList();
        var porCampo = resultado.Errors
            .Where(e => !string.IsNullOrEmpty(e.PropertyName))
            .GroupBy(e => JsonNamingPolicy.SnakeCaseLower.ConvertName(CampoRaiz(e.PropertyName)))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        if (generales.Count == 1 && porCampo.Count == 0)
        {
            throw new clsValidacionException(generales[0]);
        }

        throw new clsValidacionException(
            MENSAJE_GENERAL,
            new Dictionary<string, object> { ["formErrors"] = generales, ["fieldErrors"] = porCampo });
    }

    /// <summary>Los errores anidados (por ejemplo "Items[3].Vin") se agrupan bajo su campo raiz ("Items"), como el esquema original.</summary>
    private static string CampoRaiz(string propiedad)
    {
        var indice = propiedad.IndexOfAny(['.', '[']);
        return indice < 0 ? propiedad : propiedad[..indice];
    }
}
