using System.Globalization;
using System.Text.RegularExpressions;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// La fecha y hora programada del cargue. En la carga por Excel llega como texto («2026-11-03 06:00»): sin zona horaria es hora de Colombia (UTC-5, sin horario de verano) y, si trae su zona o
/// <c>Z</c>, se respeta. Al editar el viaje la fecha debe traer su zona horaria (ISO 8601).
/// </summary>
public static partial class clsFechaProgramada
{
    /// <summary>La zona de Colombia: el dia y la hora que escribe el coordinador.</summary>
    public static readonly TimeSpan COLOMBIA = TimeSpan.FromHours(-5);

    private static readonly string[] FORMATOS_SIN_ZONA =
    [
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFF", "yyyy/MM/dd HH:mm", "yyyy/MM/dd HH:mm:ss",
    ];

    /// <summary>Lee la fecha de la carga (con o sin zona horaria); nulo si el texto no es una fecha y hora valida.</summary>
    public static DateTimeOffset? DeCarga(string? texto)
    {
        var limpio = texto?.Trim();
        if (string.IsNullOrEmpty(limpio))
        {
            return null;
        }

        if (ConZona().IsMatch(limpio))
        {
            return DateTimeOffset.TryParse(limpio, CultureInfo.InvariantCulture, DateTimeStyles.None, out var conZona) ? conZona : null;
        }

        return DateTime.TryParseExact(limpio, FORMATOS_SIN_ZONA, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(local, COLOMBIA)
            : null;
    }

    /// <summary>Lee la fecha de la edicion del viaje: ISO 8601 con su zona horaria; nulo si no la trae o no es valida.</summary>
    public static DateTimeOffset? DeIso(string? texto)
    {
        var limpio = texto?.Trim();
        return !string.IsNullOrEmpty(limpio) && ConZona().IsMatch(limpio) && DateTimeOffset.TryParse(limpio, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) ? fecha : null;
    }

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex ConZona();
}
