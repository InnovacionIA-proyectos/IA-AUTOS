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
