namespace IAAutos.Dominio.Vehiculos;

public static class clsOperacion
{
    public const string IMPORTACION = "importacion";
    public const string EXPORTACION = "exportacion";
    public const string NACIONAL = "nacional";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { IMPORTACION, EXPORTACION, NACIONAL };
}
