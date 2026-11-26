using IAAutos.Dominio.Custodia;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// La hora y el GPS del momento en que se firmo, tal como llegan en el cuerpo de una firma de la entrega: opcionales (una firma hecha sin senal se acepta y queda «sin ubicacion»), pero si vienen deben ser validos: la hora en
/// ISO 8601 con zona y la latitud y la longitud juntas y dentro de rango.
/// </summary>
internal static class clsMomentoDeFirmaOpcional
{
    public static MomentoDeFirma? Leer(string? fecha, double? lat, double? lng, double? precisionM, DateTimeOffset ahora, Dictionary<string, string[]> errores)
    {
        var momento = string.IsNullOrWhiteSpace(fecha) ? (DateTimeOffset?)null : clsFechaProgramada.DeIso(fecha);
        if (!string.IsNullOrWhiteSpace(fecha) && momento is null)
        {
            errores["fecha_momento"] = ["La hora del momento en que se firmó debe estar en formato ISO 8601 con su zona horaria (por ejemplo 2026-11-18T14:09:00-05:00)"];
        }

        if ((lat is null) != (lng is null))
        {
            errores["lat"] = ["La latitud y la longitud van juntas o ninguna"];
        }
        else if (lat is { } la && lng is { } lo)
        {
            if (la is < -90 or > 90 || !double.IsFinite(la))
            {
                errores["lat"] = ["La latitud debe estar entre -90 y 90"];
            }

            if (lo is < -180 or > 180 || !double.IsFinite(lo))
            {
                errores["lng"] = ["La longitud debe estar entre -180 y 180"];
            }
        }

        if (precisionM is < 0 || (precisionM is { } p && !double.IsFinite(p)))
        {
            errores["precision_m"] = ["La precisión debe ser un número en metros mayor o igual a 0"];
        }

        if (errores.ContainsKey("fecha_momento") || errores.ContainsKey("lat") || errores.ContainsKey("lng") || errores.ContainsKey("precision_m"))
        {
            return null;
        }

        if (momento is null && lat is null)
        {
            return null;
        }

        return new MomentoDeFirma(momento ?? ahora, lat, lng, lat is null ? null : precisionM);
    }
}
