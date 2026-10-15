using System.Security.Cryptography;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Usuarios;

/// <summary>PIN numerico del personal de campo: formato, validacion y generacion de PIN temporales.</summary>
public static class clsPin
{
    public const int LONGITUD_MIN = 4;
    public const int LONGITUD_MAX = 8;
    public const int LONGITUD_GENERADO = 6;

    /// <summary>Valida el formato (solo digitos, de 4 a 8) y rechaza los triviales (todos iguales o en secuencia). Devuelve el PIN limpio.</summary>
    public static string Validar(string? pin, string campo = "pin")
    {
        var limpio = pin?.Trim();
        if (string.IsNullOrEmpty(limpio))
        {
            throw clsErroresCampos.De(campo, "El PIN es requerido");
        }

        if (limpio.Length is < LONGITUD_MIN or > LONGITUD_MAX || !limpio.All(char.IsAsciiDigit))
        {
            throw clsErroresCampos.De(campo, $"El PIN debe tener solo números, de {LONGITUD_MIN} a {LONGITUD_MAX} dígitos");
        }

        if (EsTrivial(limpio))
        {
            throw clsErroresCampos.De(campo, "El PIN es demasiado simple: no use dígitos repetidos ni en secuencia");
        }

        return limpio;
    }

    /// <summary>PIN aleatorio de 6 digitos que no es trivial.</summary>
    public static string Generar()
    {
        while (true)
        {
            var pin = string.Concat(Enumerable.Range(0, LONGITUD_GENERADO).Select(_ => RandomNumberGenerator.GetInt32(10).ToString()));
            if (!EsTrivial(pin))
            {
                return pin;
            }
        }
    }

    private static bool EsTrivial(string pin)
    {
        if (pin.All(c => c == pin[0]))
        {
            return true;
        }

        var pasos = pin.Zip(pin.Skip(1), (a, b) => b - a).Distinct().ToList();
        return pasos.Count == 1 && pasos[0] is 1 or -1;
    }
}
