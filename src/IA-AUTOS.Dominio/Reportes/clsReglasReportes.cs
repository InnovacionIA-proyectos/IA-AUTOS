namespace IAAutos.Dominio.Reportes;

/// <summary>
/// Reglas de negocio de movilizacion y rechazo. La misma definicion de "movilizado" se aplica en SQL
/// (consultas de Reportes y listados); esta version pura es la referencia verificable por pruebas unitarias.
/// </summary>
public static class clsReglasReportes
{
    /// <summary>Linea base de tasa de novedad por operacion (PRD, enero a abril).</summary>
    public static readonly IReadOnlyDictionary<string, decimal> LINEA_BASE = new Dictionary<string, decimal>
    {
        ["importacion"] = 8.81m,
        ["exportacion"] = 8.34m,
        ["nacional"] = 1.95m,
    };

    /// <summary>
    /// Un vehiculo esta movilizado cuando TODAS sus etapas (y al menos una) tienen su informe cerrado.
    /// Los VINs recien cargados, con la ruta creada pero sin informes, quedan como pendientes.
    /// </summary>
    public static bool EsMovilizado(IReadOnlyCollection<bool> etapasConInformeCerrado) =>
        etapasConInformeCerrado.Count > 0 && etapasConInformeCerrado.All(cerrada => cerrada);

    /// <summary>Porcentaje con dos decimales. Cero si no hay unidades.</summary>
    public static decimal Porcentaje(int parte, int total) =>
        total > 0 ? Math.Round((decimal)parte * 100m / total, 2, MidpointRounding.AwayFromZero) : 0m;

    public static decimal LineaBaseDe(string operacion) =>
        LINEA_BASE.TryGetValue(operacion, out var valor) ? valor : 0m;

    public static decimal Variacion(decimal tasaActual, decimal lineaBase) =>
        Math.Round(tasaActual - lineaBase, 2, MidpointRounding.AwayFromZero);
}
