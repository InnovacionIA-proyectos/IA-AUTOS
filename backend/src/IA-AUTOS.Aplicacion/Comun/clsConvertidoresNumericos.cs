using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IAAutos.Aplicacion.Comun;

/// <summary>Acepta un entero como numero JSON o como texto numerico (equivalente al <c>coerce</c> del esquema original).</summary>
public sealed class clsConvertidorLongFlexible : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones)
    {
        switch (lector.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number when lector.TryGetInt64(out var numero):
                return numero;
            case JsonTokenType.String when long.TryParse(lector.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var texto):
                return texto;
            default:
                throw new JsonException("Debe ser un número entero");
        }
    }

    public override void Write(Utf8JsonWriter escritor, long? valor, JsonSerializerOptions opciones)
    {
        if (valor is null)
        {
            escritor.WriteNullValue();
            return;
        }

        escritor.WriteNumberValue(valor.Value);
    }
}

/// <summary>Acepta un decimal como numero JSON o como texto numerico.</summary>
public sealed class clsConvertidorDecimalFlexible : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones)
    {
        switch (lector.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number when lector.TryGetDecimal(out var numero):
                return numero;
            case JsonTokenType.String when decimal.TryParse(lector.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var texto):
                return texto;
            default:
                throw new JsonException("Debe ser un número");
        }
    }

    public override void Write(Utf8JsonWriter escritor, decimal? valor, JsonSerializerOptions opciones)
    {
        if (valor is null)
        {
            escritor.WriteNullValue();
            return;
        }

        escritor.WriteNumberValue(valor.Value);
    }
}
