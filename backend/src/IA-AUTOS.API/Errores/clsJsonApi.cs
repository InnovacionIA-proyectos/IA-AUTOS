using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IAAutos.API.Errores;

/// <summary>
/// Configuracion JSON de la API: propiedades en snake_case y fechas ISO 8601 UTC con milisegundos
/// (<c>2026-07-17T12:30:15.123Z</c>), igual que <c>toISOString()</c> de la API original.
/// </summary>
public static class clsJsonApi
{
    public static readonly JsonSerializerOptions OPCIONES = Crear();

    public static void Configurar(JsonSerializerOptions opciones)
    {
        opciones.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        opciones.DictionaryKeyPolicy = null;
        opciones.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        opciones.NumberHandling = JsonNumberHandling.Strict;
        opciones.Converters.Add(new clsConvertidorFecha());
        opciones.Converters.Add(new clsConvertidorFechaNula());
        // Acentos y simbolos salen como UTF-8 directo (como JSON.stringify del sistema anterior), sin secuencias de escape.
        opciones.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    }

    private static JsonSerializerOptions Crear()
    {
        var opciones = new JsonSerializerOptions();
        Configurar(opciones);
        return opciones;
    }
}

internal sealed class clsConvertidorFecha : JsonConverter<DateTimeOffset>
{
    private const string FORMATO = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones)
    {
        var texto = lector.GetString() ?? throw new JsonException("Debe ser una fecha/hora ISO 8601 válida");
        // Como el esquema original (zod datetime con offset): exige zona horaria explicita (Z o +hh:mm).
        var terminaEnZona = texto.EndsWith('Z') || texto.EndsWith('z')
            || (texto.Length > 6 && (texto[^6] is '+' or '-') && texto[^3] == ':');
        if (!terminaEnZona
            || !DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor))
        {
            throw new JsonException("Debe ser una fecha/hora ISO 8601 válida");
        }

        return valor;
    }

    public override void Write(Utf8JsonWriter escritor, DateTimeOffset valor, JsonSerializerOptions opciones) =>
        escritor.WriteStringValue(valor.UtcDateTime.ToString(FORMATO, CultureInfo.InvariantCulture));
}

internal sealed class clsConvertidorFechaNula : JsonConverter<DateTimeOffset?>
{
    private readonly clsConvertidorFecha _interno = new();

    public override DateTimeOffset? Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones) =>
        lector.TokenType == JsonTokenType.Null ? null : _interno.Read(ref lector, typeof(DateTimeOffset), opciones);

    public override void Write(Utf8JsonWriter escritor, DateTimeOffset? valor, JsonSerializerOptions opciones)
    {
        if (valor is null)
        {
            escritor.WriteNullValue();
            return;
        }

        _interno.Write(escritor, valor.Value, opciones);
    }
}
