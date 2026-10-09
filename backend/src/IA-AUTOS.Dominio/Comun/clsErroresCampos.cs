namespace IAAutos.Dominio.Comun;

/// <summary>Arma errores de validacion asociados a un campo, con la misma forma que el resto de la API (details.fieldErrors).</summary>
public static class clsErroresCampos
{
    public static clsValidacionException De(string campo, string mensaje) =>
        new(
            "Datos inválidos",
            new Dictionary<string, object>
            {
                ["formErrors"] = Array.Empty<string>(),
                ["fieldErrors"] = new Dictionary<string, string[]> { [campo] = [mensaje] },
            });
}
