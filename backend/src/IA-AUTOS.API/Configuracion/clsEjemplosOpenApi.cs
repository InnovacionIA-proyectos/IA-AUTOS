using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace IAAutos.API.Configuracion;

/// <summary>
/// Arma el ejemplo de un tipo del contrato a partir de su metadata JSON (los mismos nombres en snake_case que viajan por la API): cada campo lleva un valor de muestra segun su tipo y su nombre
/// (un VIN valido, un UUID, una fecha ISO con zona, un correo...), las listas llevan un elemento y la profundidad se limita para no repetir tipos que se contienen a si mismos.
/// </summary>
internal static class clsEjemplosOpenApi
{
    private const int PROFUNDIDAD_MAXIMA = 4;
    private const string UUID = "3fa85f64-5717-4562-b3fc-2c963f66afa6";
    private const string FECHA_HORA = "2026-10-22T14:30:00.000Z";

    public static JsonNode? De(Type tipo, JsonSerializerOptions opciones) => Valor(tipo, opciones, null, 0, []);

    private static JsonNode? Valor(Type declarado, JsonSerializerOptions opciones, string? nombre, int profundidad, HashSet<Type> enCurso)
    {
        var tipo = Nullable.GetUnderlyingType(declarado) ?? declarado;

        if (tipo == typeof(string))
        {
            return JsonValue.Create(Texto(nombre));
        }

        if (tipo == typeof(Guid))
        {
            return JsonValue.Create(UUID);
        }

        if (tipo == typeof(DateTimeOffset) || tipo == typeof(DateTime))
        {
            return JsonValue.Create(FECHA_HORA);
        }

        if (tipo == typeof(DateOnly))
        {
            return JsonValue.Create("2026-10-21");
        }

        if (tipo == typeof(bool))
        {
            return JsonValue.Create(true);
        }

        if (tipo == typeof(int) || tipo == typeof(long) || tipo == typeof(short) || tipo == typeof(byte))
        {
            return JsonValue.Create(1);
        }

        if (tipo == typeof(double) || tipo == typeof(float) || tipo == typeof(decimal))
        {
            return JsonValue.Create(Decimal(nombre));
        }

        if (tipo.IsEnum)
        {
            return JsonSerializer.SerializeToNode(Enum.GetValues(tipo).GetValue(0), tipo, opciones);
        }

        if (tipo == typeof(JsonElement) || tipo == typeof(JsonNode) || tipo == typeof(JsonObject) || tipo == typeof(object))
        {
            return new JsonObject();
        }

        if (tipo == typeof(byte[]) || tipo == typeof(Stream))
        {
            return null;
        }

        if (profundidad >= PROFUNDIDAD_MAXIMA || !enCurso.Add(tipo))
        {
            return null;
        }

        try
        {
            if (typeof(IDictionary).IsAssignableFrom(tipo) || EsDiccionarioGenerico(tipo))
            {
                return new JsonObject();
            }

            if (ElementoDe(tipo) is { } elemento)
            {
                var lista = new JsonArray();
                if (Valor(elemento, opciones, nombre, profundidad + 1, enCurso) is { } muestra)
                {
                    lista.Add(muestra);
                }

                return lista;
            }

            var objeto = new JsonObject();
            var info = opciones.GetTypeInfo(tipo);
            if (info.Kind != JsonTypeInfoKind.Object)
            {
                return null;
            }

            foreach (var propiedad in info.Properties)
            {
                objeto[propiedad.Name] = Valor(propiedad.PropertyType, opciones, propiedad.Name, profundidad + 1, enCurso);
            }

            return objeto;
        }
        finally
        {
            enCurso.Remove(tipo);
        }
    }

    private static bool EsDiccionarioGenerico(Type tipo) =>
        tipo.GetInterfaces().Append(tipo).Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

    /// <summary>El tipo de los elementos si es una lista o un arreglo; nulo en cualquier otro caso.</summary>
    private static Type? ElementoDe(Type tipo)
    {
        if (tipo.IsArray)
        {
            return tipo.GetElementType();
        }

        if (tipo == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(tipo))
        {
            return null;
        }

        var enumerable = tipo.GetInterfaces().Append(tipo).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0];
    }

    private static decimal Decimal(string? nombre) => nombre switch
    {
        "lat" => 4.6097m,
        "lng" => -74.0817m,
        _ when nombre?.StartsWith("porcentaje", StringComparison.Ordinal) == true || nombre?.StartsWith("tasa", StringComparison.Ordinal) == true => 1.25m,
        _ => 1.5m,
    };

    private static string Texto(string? nombre)
    {
        if (nombre is null)
        {
            return "texto";
        }

        if (nombre.EndsWith("_id", StringComparison.Ordinal) || nombre == "id")
        {
            return UUID;
        }

        if (nombre.EndsWith("_at", StringComparison.Ordinal) || nombre.StartsWith("fecha", StringComparison.Ordinal))
        {
            return FECHA_HORA;
        }

        return nombre switch
        {
            "vin" or "chasis" => "9FBHSR4E8PM000001",
            "email" or "correo" => "usuario@ejemplo.com",
            "password" => "Clave-Segura-1",
            "pin" or "pin_nuevo" or "pin_actual" or "pin_temporal" => "4821",
            "cedula" => "1032456789",
            "token" or "access" or "refresh" => "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.ejemplo.firma",
            "url" or "imagen_url" => "/api/uploads/ejemplo.jpg?exp=1789570000&firma=abc123",
            "archivo" or "storage_path" => "ejemplo.jpg",
            "placa" => "ABC123",
            "trailer" => "TR-1",
            "codigo_mtrack" => "MTK-0001",
            "marca" => "Renault",
            "modelo" => "Duster",
            "color" => "Gris",
            "operacion" => "nacional",
            "rol" => "inspector",
            "momento" => "salida",
            "estado" => "borrador",
            "proceso" => "cargue",
            "clase_dano" => "golpe",
            "tamano_dano" => "5_10cm",
            "nivel_severidad" or "nivel" => "v1",
            "lado" => "izquierdo",
            "codigo" => "codigo_de_ejemplo",
            "nombre" or "nombre_usuario" => "Nombre de ejemplo",
            "ip" => "203.0.113.7",
            "sha256" or "hash_sha256" => "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "mensaje" or "message" or "error" or "nota" or "motivo" => "Mensaje de ejemplo",
            _ => "texto",
        };
    }

    /// <summary>Un cuerpo de error de ejemplo con el codigo indicado, con la misma forma que devuelve la API.</summary>
    public static JsonObject Error(string codigo, string mensaje, JsonNode? detalles = null)
    {
        var cuerpo = new JsonObject
        {
            ["error"] = mensaje,
            ["code"] = codigo,
            ["message"] = mensaje,
            ["timestamp"] = FECHA_HORA,
        };
        if (detalles is not null)
        {
            cuerpo["details"] = detalles;
        }

        return cuerpo;
    }
}
