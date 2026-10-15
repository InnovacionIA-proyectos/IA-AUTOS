using System.Globalization;
using System.Text;

namespace IAAutos.Aplicacion.Comun;

/// <summary>Texto para comparar sin importar mayusculas, tildes, espacios ni separadores ("Niñera" = "ninera" = "NI-ÑERA").</summary>
public static class clsTextoComparable
{
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return string.Empty;
        }

        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var resultado = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || !char.IsLetterOrDigit(c))
            {
                continue;
            }

            resultado.Append(char.ToUpperInvariant(c));
        }

        return resultado.ToString();
    }
}
