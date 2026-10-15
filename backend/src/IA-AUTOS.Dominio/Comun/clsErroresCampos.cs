namespace IAAutos.Dominio.Comun;

/// <summary>Arma errores de validacion asociados a un campo, con la misma forma que el resto de la API (details.fieldErrors).</summary>
public static class clsErroresCampos
{
    public static clsValidacionException De(string campo, string mensaje) =>
        Varios(new Dictionary<string, string[]> { [campo] = [mensaje] });

    /// <summary>Varios campos con error a la vez (por ejemplo, todas las referencias invalidas de un viaje).</summary>
    public static clsValidacionException Varios(IReadOnlyDictionary<string, string[]> porCampo) =>
        new(
            "Datos inválidos",
            new Dictionary<string, object>
            {
                ["formErrors"] = Array.Empty<string>(),
                ["fieldErrors"] = porCampo.ToDictionary(c => c.Key, c => c.Value),
            });
}
