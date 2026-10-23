using System.Globalization;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Comun;

/// <summary>Lectura de filtros de consulta (texto) que acumula los errores por campo para responder todos juntos.</summary>
public static class clsParseoFiltros
{
    /// <summary>Valor dentro de un conjunto permitido; vacio = sin filtro. Un valor fuera del conjunto agrega el error del campo.</summary>
    public static string? Catalogo(string? valor, IReadOnlySet<string> catalogo, string campo, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        if (catalogo.Contains(valor))
        {
            return valor;
        }

        errores[campo] = [$"Valor inválido. Permitidos: {string.Join(", ", catalogo.Order())}"];
        return null;
    }

    /// <summary>Fecha ISO 8601 con zona horaria explicita (Z o desfase), como el resto de la API.</summary>
    public static DateTimeOffset? Fecha(string? texto, string campo, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var t = texto.Trim();
        var conZona = t.EndsWith('Z') || t.EndsWith('z') || (t.Length > 6 && (t[^6] is '+' or '-') && t[^3] == ':');
        if (conZona && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            return fecha;
        }

        errores[campo] = [$"{campo} debe ser fecha ISO"];
        return null;
    }

    /// <summary>UUID; vacio = sin filtro.</summary>
    public static Guid? Uuid(string? texto, string campo, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (Guid.TryParse(texto, out var id))
        {
            return id;
        }

        errores[campo] = [$"{campo} debe ser UUID"];
        return null;
    }

    /// <summary>
    /// El VIN de un filtro, normalizado (mayusculas, sin espacios en los bordes); vacio = sin filtro. Aplica la regla unica del VIN: 17 caracteres ISO (sin I, O ni Q) o un chasis de 6 a 20 alfanumericos;
    /// otro valor agrega el motivo al campo.
    /// </summary>
    public static string? Vin(string? texto, string campo, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (clsVin.Validar(texto) is { } motivo)
        {
            errores[campo] = [motivo];
            return null;
        }

        return clsVin.Normalizar(texto);
    }

    /// <summary>true o false; vacio = sin filtro.</summary>
    public static bool? Booleano(string? texto, string campo, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (bool.TryParse(texto.Trim(), out var valor))
        {
            return valor;
        }

        errores[campo] = [$"{campo} debe ser true o false"];
        return null;
    }
}
