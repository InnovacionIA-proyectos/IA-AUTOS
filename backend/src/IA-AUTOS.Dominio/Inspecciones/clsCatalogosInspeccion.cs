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

    /// <summary>Un borrador que su autor (o el coordinador de calidad) descarto: se conserva, pero ya no es el informe de su momento.</summary>
    public const string DESCARTADA = "descartada";

    /// <summary>El borrador de otra persona que dejo de ser el informe de su momento porque quien tomo el carro capturo ese momento de cero.</summary>
    public const string SUPERADA = "superada";

    /// <summary>Los estados con los que se crea, se lista y se filtra un informe; <see cref="DESCARTADA"/> y <see cref="SUPERADA"/> nunca salen en los listados.</summary>
    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { BORRADOR, CERRADA };

    /// <summary>Los estados en los que el informe ya no cuenta como el de su momento.</summary>
    public static readonly IReadOnlySet<string> FUERA_DE_JUEGO = new HashSet<string> { DESCARTADA, SUPERADA };
}
