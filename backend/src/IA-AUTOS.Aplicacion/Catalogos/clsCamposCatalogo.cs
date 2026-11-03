using System.Text.Json;
using IAAutos.Dominio.Comun;

namespace IAAutos.Aplicacion.Catalogos;

/// <summary>
/// Lee los campos del cuerpo de crear o editar un valor de catalogo. Distingue "no enviado" de "enviado en nulo" (importante al editar)
/// y rechaza tipos incorrectos y campos que ese catalogo no admite, para que un error de escritura no se pierda en silencio.
/// </summary>
public sealed class clsCamposCatalogo(IReadOnlyDictionary<string, JsonElement> campos)
{
    /// <summary>Campos comunes de todos los catalogos.</summary>
    public static readonly string[] COMUNES = ["codigo", "nombre", "orden", "activo"];

    public bool Tiene(string campo) => campos.ContainsKey(campo);

    public string? Texto(string campo)
    {
        if (!campos.TryGetValue(campo, out var valor) || valor.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (valor.ValueKind != JsonValueKind.String)
        {
            throw TipoInvalido(campo, "texto");
        }

        return Leer(campo, valor.GetString);
    }

    public int? Entero(string campo)
    {
        if (!campos.TryGetValue(campo, out var valor) || valor.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return valor.ValueKind == JsonValueKind.Number && valor.TryGetInt32(out var entero) ? entero : throw TipoInvalido(campo, "entero");
    }

    public double? Numero(string campo)
    {
        if (!campos.TryGetValue(campo, out var valor) || valor.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return valor.ValueKind == JsonValueKind.Number && valor.TryGetDouble(out var numero) && double.IsFinite(numero) ? numero : throw TipoInvalido(campo, "número");
    }

    public bool? Booleano(string campo)
    {
        if (!campos.TryGetValue(campo, out var valor) || valor.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return valor.ValueKind is JsonValueKind.True or JsonValueKind.False ? valor.GetBoolean() : throw TipoInvalido(campo, "booleano");
    }

    public Guid? Identificador(string campo)
    {
        var texto = Texto(campo);
        if (texto is null)
        {
            return null;
        }

        return Guid.TryParse(texto, out var id) ? id : throw clsErroresCampos.De(campo, "Debe ser un UUID válido");
    }

    /// <summary>La geometria llega como objeto JSON (o null para borrarla); se devuelve su texto JSON.</summary>
    public string? Json(string campo)
    {
        if (!campos.TryGetValue(campo, out var valor) || valor.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (valor.ValueKind != JsonValueKind.Object)
        {
            throw TipoInvalido(campo, "objeto JSON");
        }

        return Leer(campo, valor.GetRawText);
    }

    /// <summary>Falla si llego algun campo que no se admite en <paramref name="donde"/> (por defecto, el catalogo).</summary>
    public void SoloPermitidos(IEnumerable<string> permitidos, string donde = "este catálogo")
    {
        var admitidos = permitidos.ToHashSet();
        var sobrantes = campos.Keys.Where(c => !admitidos.Contains(c)).Order().ToList();
        if (sobrantes.Count > 0)
        {
            throw clsErroresCampos.De(sobrantes[0], $"Campo no admitido en {donde} (admite: {string.Join(", ", admitidos.Order())})");
        }
    }

    /// <summary>Un cuerpo con bytes que no son UTF-8 valido (por ejemplo un acento en otra codificacion) es un error del cliente, no del servidor.</summary>
    private static string? Leer(string campo, Func<string?> lectura)
    {
        try
        {
            return lectura();
        }
        catch (InvalidOperationException)
        {
            throw clsErroresCampos.De(campo, "El texto no es UTF-8 válido");
        }
    }

    private static clsValidacionException TipoInvalido(string campo, string esperado) =>
        clsErroresCampos.De(campo, $"Debe ser {esperado}");
}
