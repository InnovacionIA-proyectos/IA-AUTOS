namespace IAAutos.Dominio.Comun;

/// <summary>Validaciones primitivas reutilizadas por las entidades.</summary>
public static class clsValidador
{
    public static string Requerido(string? valor, string mensaje)
    {
        var limpio = valor?.Trim();
        if (string.IsNullOrEmpty(limpio))
        {
            throw new clsValidacionException(mensaje);
        }

        return limpio;
    }

    public static string? Opcional(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrEmpty(limpio) ? null : limpio;
    }

    public static void Latitud(double? lat, string campo = "lat")
    {
        if (lat is < -90 or > 90)
        {
            throw new clsValidacionException($"{campo} fuera de rango (-90 a 90)");
        }
    }

    public static void Longitud(double? lng, string campo = "lng")
    {
        if (lng is < -180 or > 180)
        {
            throw new clsValidacionException($"{campo} fuera de rango (-180 a 180)");
        }
    }

    public static void EnCatalogo(string valor, IReadOnlySet<string> catalogo, string campo)
    {
        if (!catalogo.Contains(valor))
        {
            throw new clsValidacionException($"{campo} inválido: {valor}");
        }
    }
}
