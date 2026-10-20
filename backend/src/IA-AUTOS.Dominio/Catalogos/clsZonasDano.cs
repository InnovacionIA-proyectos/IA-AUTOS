using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Catalogos;

/// <summary>
/// Lado de una zona numerada. Los numeros del 1 al 25 llevan lado (izquierdo o derecho); del 26 en adelante la zona no tiene lado. La zona se
/// guarda por lado + numero, no por el panel donde se toco.
/// </summary>
public static class clsLado
{
    public const string IZQUIERDO = "izquierdo";
    public const string DERECHO = "derecho";

    /// <summary>Ultimo numero de zona que lleva lado.</summary>
    public const int ULTIMO_NUMERO_CON_LADO = 25;

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { IZQUIERDO, DERECHO };

    /// <summary>True si una zona con ese numero lleva lado (del 1 al 25).</summary>
    public static bool LlevaLado(int numero) => numero <= ULTIMO_NUMERO_CON_LADO;
}

/// <summary>Zonas provisionales: sin numero, lado ni dibujo, se eligen de una lista. El dia que el negocio les de numero se migran.</summary>
public static class clsZonaProvisional
{
    public const string FRENTE = "frente";
    public const string LLANTA_DELANTERA_IZQUIERDA = "llanta_delantera_izquierda";
    public const string LLANTA_DELANTERA_DERECHA = "llanta_delantera_derecha";
    public const string LLANTA_TRASERA_IZQUIERDA = "llanta_trasera_izquierda";
    public const string LLANTA_TRASERA_DERECHA = "llanta_trasera_derecha";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string>
    {
        FRENTE, LLANTA_DELANTERA_IZQUIERDA, LLANTA_DELANTERA_DERECHA, LLANTA_TRASERA_IZQUIERDA, LLANTA_TRASERA_DERECHA,
    };
}

/// <summary>
/// Zona del dano a la que se atribuye un hallazgo: un cuadrante numerado con su lado, o una zona provisional (con codigo). Las zonas numeradas las crea
/// la base de datos al crear el cuadrante (una por lado del 1 al 25, una sola del 26 en adelante); aqui solo se leen.
/// </summary>
public sealed class clsZonaDano : clsEntidadBase
{
    private clsZonaDano()
    {
    }

    public Guid? CuadranteId { get; private set; }

    public int? Numero { get; private set; }

    public string? Lado { get; private set; }

    /// <summary>Codigo de una zona provisional; nulo en las numeradas.</summary>
    public string? Codigo { get; private set; }

    /// <summary>Nombre de una zona provisional; en las numeradas el nombre es el de su cuadrante.</summary>
    public string? Nombre { get; private set; }

    public int Orden { get; private set; }

    public bool Activo { get; private set; }

    public bool EsProvisional => CuadranteId is null;
}

/// <summary>
/// Panel del dibujo de danos: una de las cinco imagenes de la ficha del vehiculo donde se toca la region del dano (superior, perfiles posteriores y
/// costados), separado del catalogo de vistas de foto. <see cref="ImagenPath"/> es el archivo de fondo en el almacen de imagenes (nulo hasta que se carga).
/// </summary>
public sealed class clsPanelDano : clsCatalogoCodificado
{
    public const int IMAGEN_PATH_MAX = 260;

    private clsPanelDano()
    {
    }

    public string? ImagenPath { get; private set; }

    /// <summary>Lado que corresponde a las regiones de este panel: el de los costados y perfiles; nulo en el panel superior (alli el lado lo da la region).</summary>
    public string? LadoDelPanel =>
        Codigo.EndsWith("_izquierdo", StringComparison.Ordinal) ? clsLado.IZQUIERDO
        : Codigo.EndsWith("_derecho", StringComparison.Ordinal) ? clsLado.DERECHO
        : null;

    public static clsPanelDano Crear(string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var panel = new clsPanelDano();
        panel.Iniciar(codigo, nombre, orden, activo, ahora);
        return panel;
    }

    /// <summary>Fija el archivo de la imagen de fondo (el nombre que devolvio el almacen).</summary>
    public void CambiarImagen(string imagenPath, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(imagenPath) || imagenPath.Length > IMAGEN_PATH_MAX)
        {
            throw clsErroresCampos.De("imagen", $"La ruta de la imagen es requerida y admite máximo {IMAGEN_PATH_MAX} caracteres");
        }

        ImagenPath = imagenPath;
        Tocar(ahora);
    }
}

/// <summary>
/// Region tocable de un panel: el poligono de una zona numerada en ese panel, con geometria normalizada (coordenadas entre 0 y 1 sobre la imagen del
/// panel). Una zona se dibuja a lo sumo una vez por panel. El lado de la region es el de su zona: en un costado o un perfil debe coincidir con el del panel.
/// </summary>
public sealed class clsRegionDano : clsEntidadBase
{
    private clsRegionDano()
    {
    }

    public Guid PanelId { get; private set; }

    public Guid ZonaId { get; private set; }

    public Guid CuadranteId { get; private set; }

    /// <summary>Poligono en JSON: {"puntos":[[x,y],...]}.</summary>
    public string Geometria { get; private set; } = string.Empty;

    public static clsRegionDano Crear(Guid panelId, Guid zonaId, Guid cuadranteId, string? geometria, DateTimeOffset ahora)
    {
        var region = new clsRegionDano { PanelId = panelId, ZonaId = zonaId, CuadranteId = cuadranteId, Geometria = clsCuadranteVista.GeometriaValida(geometria) };
        region.Inicializar(ahora);
        return region;
    }

    public void CambiarGeometria(string? geometria, DateTimeOffset ahora)
    {
        Geometria = clsCuadranteVista.GeometriaValida(geometria);
        Tocar(ahora);
    }
}
