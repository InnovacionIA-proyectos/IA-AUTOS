using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Catalogos;

/// <summary>
/// Lado de una zona. Que un cuadrante lleve lado (zonas izquierdo y derecho) o no (una sola zona) lo declara el cuadrante en el catalogo (<c>LlevaLado</c>): ninguna regla usa numeros.
/// «Izquierdo» es el lado del conductor, visto sentado al volante. La zona se guarda por lado + numero, no por el panel donde se toco.
/// </summary>
public static class clsLado
{
    public const string IZQUIERDO = "izquierdo";
    public const string DERECHO = "derecho";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { IZQUIERDO, DERECHO };
}

/// <summary>
/// Zona del dano a la que se atribuye un hallazgo: un cuadrante numerado con su lado (o sin lado si el cuadrante no lo lleva). Las crea la base de datos al crear el cuadrante (dos si lleva lado, una si no)
/// y las siembra el archivo del dibujo; aqui solo se leen. <see cref="NombreCompleto"/> y <see cref="AiagArea"/> son de la zona (por pieza y lado): el nombre completo lleva el lado
/// («Guardafango delantero izquierdo») y el codigo AIAG se trata como texto, sin validar su formato.
/// </summary>
public sealed class clsZonaDano : clsEntidadBase
{
    private clsZonaDano()
    {
    }

    public Guid CuadranteId { get; private set; }

    public int Numero { get; private set; }

    /// <summary>Copia de <c>LlevaLado</c> del cuadrante (la base impide que difieran).</summary>
    public bool LlevaLado { get; private set; }

    public string? Lado { get; private set; }

    public string? NombreCompleto { get; private set; }

    public string? AiagArea { get; private set; }

    public int Orden { get; private set; }

    public bool Activo { get; private set; }
}

/// <summary>
/// Panel del dibujo de danos: una de las imagenes de la ficha del vehiculo donde se toca la region del dano (costados, frontal, posterior, superior y diagonales), separado del catalogo de vistas
/// de foto. <see cref="ImagenPath"/> es el archivo de fondo en el almacen de imagenes (nulo hasta que se carga); <see cref="AnchoRef"/> y <see cref="AltoRef"/> son el tamano de referencia de esa
/// imagen y <see cref="Version"/> la version del archivo del dibujo con que se sembro. Una imagen distinta de la de su version desalinea los poligonos.
/// </summary>
public sealed class clsPanelDano : clsCatalogoCodificado
{
    public const int IMAGEN_PATH_MAX = 260;

    private clsPanelDano()
    {
    }

    public string? ImagenPath { get; private set; }

    public int? AnchoRef { get; private set; }

    public int? AltoRef { get; private set; }

    public int? Version { get; private set; }

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
/// Region tocable de un panel: el poligono de una zona en ese panel, con geometria normalizada (coordenadas entre 0 y 1 sobre la imagen del panel). Una zona se dibuja a lo sumo una vez por panel.
/// <see cref="Prioridad"/> ordena las regiones que se superponen al tocar.
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

    public int Prioridad { get; private set; }
}
