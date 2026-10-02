using System.Text.RegularExpressions;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>VIN: 17 caracteres alfanumericos en mayuscula, sin las letras I, O ni Q (ISO 3779).</summary>
public static partial class clsVin
{
    public static string Normalizar(string vin) => vin.Trim().ToUpperInvariant();

    public static bool EsFormatoIso(string vin) => FormatoIso().IsMatch(Normalizar(vin));

    [GeneratedRegex("^[A-HJ-NPR-Z0-9]{17}$")]
    private static partial Regex FormatoIso();
}
