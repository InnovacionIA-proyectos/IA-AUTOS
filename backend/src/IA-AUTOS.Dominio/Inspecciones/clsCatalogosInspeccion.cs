namespace IAAutos.Dominio.Inspecciones;

public static class clsMomento
{
    public const string CHECK_IN = "check_in";
    public const string CHECK_OUT = "check_out";
    public const string INFORME = "informe";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { CHECK_IN, CHECK_OUT, INFORME };
}

public static class clsEstadoInspeccion
{
    public const string BORRADOR = "borrador";
    public const string CERRADA = "cerrada";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { BORRADOR, CERRADA };
}

public static class clsClaseDano
{
    public static readonly IReadOnlySet<string> VALORES = new HashSet<string>
    {
        "deformacion", "rotura", "cortado", "perforacion", "golpe",
        "mancha", "raya_pintar", "desconchado", "particulas", "faltante",
    };
}

public static class clsTamanoDano
{
    public const string REQUIERE_REEMPLAZO = "requiere_reemplazo";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string>
    {
        "1_5cm", "5_10cm", "10_20cm", "mas_20cm", REQUIERE_REEMPLAZO, "mas_10_particulas",
    };
}
