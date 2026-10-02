namespace IAAutos.Dominio.Vehiculos;

public static class clsOperacion
{
    public const string IMPORTACION = "importacion";
    public const string EXPORTACION = "exportacion";
    public const string NACIONAL = "nacional";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { IMPORTACION, EXPORTACION, NACIONAL };
}

public static class clsPuntoCustodiaNombre
{
    public const string CARGUE = "cargue";
    public const string PUNTO_ORIGEN = "punto_origen";
    public const string CERTIFICADORA = "certificadora";
    public const string PUNTO_DESTINO = "punto_destino";
    public const string TRANSPORTE = "transporte";
    public const string DESCARGUE = "descargue";
    public const string CONCESIONARIO = "concesionario";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string>
    {
        CARGUE, PUNTO_ORIGEN, CERTIFICADORA, PUNTO_DESTINO, TRANSPORTE, DESCARGUE, CONCESIONARIO,
    };

    /// <summary>Ruta de custodia estandar que se crea al cargar un VIN: Cargue, Transporte, Descargue.</summary>
    public static readonly string[] RUTA_CARGA = [CARGUE, TRANSPORTE, DESCARGUE];

    /// <summary>Procesos que entran en el mapa de calor y la atribucion de responsabilidad.</summary>
    public static readonly string[] PROCESOS_ANALISIS = [CARGUE, TRANSPORTE, DESCARGUE];
}
