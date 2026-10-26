using System.Text.RegularExpressions;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>
/// El VIN. La regla unica de toda la API (carga, busqueda exacta y filtros): un VIN de 17 caracteres alfanumericos en mayuscula, sin las letras I, O ni Q (ISO 3779), o un chasis de Sofasa de 6 a 20
/// caracteres alfanumericos.
/// </summary>
public static partial class clsVin
{
    public const int LONGITUD_ISO = 17;
    public const int CHASIS_MINIMO = 6;
    public const int CHASIS_MAXIMO = 20;

    public const string MOTIVO_ISO = "VIN inválido: los de 17 caracteres son alfanuméricos y no llevan las letras I, O ni Q";
    public const string MOTIVO_GENERAL = "VIN o chasis inválido: use un VIN de 17 caracteres alfanuméricos, sin las letras I, O ni Q, o un chasis de 6 a 20 caracteres alfanuméricos";

    public static string Normalizar(string vin) => vin.Trim().ToUpperInvariant();

    public static bool EsFormatoIso(string vin) => FormatoIso().IsMatch(Normalizar(vin));

    /// <summary>El motivo por el que el texto no es un VIN ni un chasis valido, o nulo si lo es (un VIN de 17 caracteres debe cumplir el formato ISO; cualquier otro, ser un chasis de 6 a 20 alfanumericos).</summary>
    public static string? Validar(string? texto)
    {
        var vin = Normalizar(texto ?? string.Empty);
        if (vin.Length == LONGITUD_ISO)
        {
            return FormatoIso().IsMatch(vin) ? null : MOTIVO_ISO;
        }

        return Chasis().IsMatch(vin) ? null : MOTIVO_GENERAL;
    }

    /// <summary>True si el texto parece un VIN completo (17 caracteres alfanumericos, sin guiones ni espacios): ese si debe cumplir el formato ISO aunque se use para buscar por fragmento.</summary>
    public static bool ParecePesquisaDeVinCompleto(string texto) => texto.Trim().Length == LONGITUD_ISO && Alfanumerico().IsMatch(texto.Trim());

    [GeneratedRegex("^[A-HJ-NPR-Z0-9]{17}$")]
    private static partial Regex FormatoIso();

    [GeneratedRegex("^[A-Z0-9]{6,20}$")]
    private static partial Regex Chasis();

    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex Alfanumerico();
}
