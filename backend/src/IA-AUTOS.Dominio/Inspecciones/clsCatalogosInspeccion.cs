namespace IAAutos.Dominio.Inspecciones;

/// <summary>
/// Momento de una inspeccion dentro de una etapa: la que se hace al salir (entrega) y la que se hace al llegar (recibo). En pantalla se
/// llaman «inspección de salida» e «inspección de llegada».
/// </summary>
public static class clsMomento
{
    public const string SALIDA = "salida";
    public const string LLEGADA = "llegada";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { SALIDA, LLEGADA };

    /// <summary>Nombre para mostrar: «Inspección de salida» o «Inspección de llegada».</summary>
    public static string Nombre(string momento) => momento switch
    {
        SALIDA => "Inspección de salida",
        LLEGADA => "Inspección de llegada",
        _ => momento,
    };
}

public static class clsEstadoInspeccion
{
    public const string BORRADOR = "borrador";
    public const string CERRADA = "cerrada";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { BORRADOR, CERRADA };
}
