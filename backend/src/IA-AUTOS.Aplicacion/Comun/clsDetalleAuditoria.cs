using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IAAutos.Aplicacion.Comun;

/// <summary>
/// Lo que cambio una accion auditada: el estado <c>Antes</c> y el estado <c>Despues</c> del registro (cualquiera puede faltar: una alta no tiene
/// <c>Antes</c> y una baja no tiene <c>Despues</c>). Se guarda como JSON en snake_case y nunca incluye contrasenas, PIN ni tokens.
/// </summary>
public sealed record DetalleAuditoria(object? Antes, object? Despues)
{
    private static readonly JsonSerializerOptions OPCIONES = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Nombres de campo que jamas se guardan en la auditoria, aunque el llamador los incluya por descuido.</summary>
    private static readonly Regex CAMPO_SECRETO = new(
        "^(.*(password|contrasena|secret|secreto|clave|token).*|pin|pin_.*|.*_pin|.*_pin_.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>Campos cuyo nombre parece secreto pero solo son una marca (si debe cambiar el PIN), no el PIN.</summary>
    private static readonly HashSet<string> PERMITIDOS = new(StringComparer.OrdinalIgnoreCase) { "debe_cambiar_pin" };

    /// <summary>El JSON <c>{"antes": ..., "despues": ...}</c> sin los campos secretos.</summary>
    public string ATexto()
    {
        var raiz = new JsonObject
        {
            ["antes"] = Convertir(Antes),
            ["despues"] = Convertir(Despues),
        };
        return raiz.ToJsonString();
    }

    private static JsonNode? Convertir(object? valor)
    {
        if (valor is null)
        {
            return null;
        }

        var nodo = JsonSerializer.SerializeToNode(valor, OPCIONES);
        Depurar(nodo);
        return nodo;
    }

    private static void Depurar(JsonNode? nodo)
    {
        switch (nodo)
        {
            case JsonObject objeto:
                foreach (var nombre in objeto.Select(p => p.Key).ToList())
                {
                    if (!PERMITIDOS.Contains(nombre) && CAMPO_SECRETO.IsMatch(nombre))
                    {
                        objeto.Remove(nombre);
                    }
                    else
                    {
                        Depurar(objeto[nombre]);
                    }
                }

                break;
            case JsonArray arreglo:
                foreach (var elemento in arreglo)
                {
                    Depurar(elemento);
                }

                break;
        }
    }
}
