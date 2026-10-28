using System.Text.Json;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Catalogos;

/// <summary>Nombres de los catalogos tal como se usan en la ruta (/api/catalogos/{nombre}) y como claves del conjunto completo.</summary>
public static class clsNombresCatalogo
{
    public const string ETAPAS = "etapas";
    public const string PUNTOS_CUSTODIA = "puntos-custodia";
    public const string TIPOS_UNIDAD = "tipos-unidad";
    public const string CLASES_DANO = "clases-dano";
    public const string TAMANOS_DANO = "tamanos-dano";
    public const string NIVELES_SEVERIDAD = "niveles-severidad";
    public const string VISTAS_FOTO = "vistas-foto";
    public const string CUADRANTES = "cuadrantes";

    public const string PANELES_DANO = "paneles-dano";
    public const string CUADRANTE_REGIONES = "cuadrante-regiones";
    public const string CLIENTES = "clientes";
    public const string ESTADOS_SOFASA = "estados-sofasa";
    public const string MOTIVOS_SIN_INSPECTOR = "motivos-sin-inspector";
    public const string LUGARES = "lugares";
}

/// <summary>Un valor de catalogo tal como lo ve el cliente: solo los campos propios de ese catalogo (snake_case), con id, codigo, nombre, orden y activo.</summary>
public sealed class ValorCatalogo : Dictionary<string, object?>
{
    public ValorCatalogo()
    {
    }

    public ValorCatalogo(IDictionary<string, object?> campos)
        : base(campos)
    {
    }

    public Guid Id => (Guid)this["id"]!;

    public bool Activo => this["activo"] is true;

    public string? Codigo => TryGetValue("codigo", out var codigo) ? codigo as string : null;
}

/// <summary>Un catalogo administrable. Lo implementa la infraestructura sobre su tabla; el servicio de catalogos lo orquesta.</summary>
public interface ICatalogo
{
    /// <summary>Nombre en la ruta (por ejemplo "clases-dano").</summary>
    string Nombre { get; }

    /// <summary>Todos los valores (activos e inactivos), ordenados por su orden.</summary>
    Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default);

    /// <summary>Agrega un valor al contexto (no confirma). Valida los campos recibidos.</summary>
    Task<ValorCatalogo> CrearAsync(IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default);

    /// <summary>Cambia solo los campos recibidos (no confirma).</summary>
    Task<ValorCatalogo> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default);

    /// <summary>Marca el valor para borrarlo (no confirma). La base impide borrarlo si algo lo usa.</summary>
    Task EliminarAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Cuerpo de cambiar la imagen de fondo de un panel de danos: el nombre del archivo que devolvio la subida de imagenes.</summary>
public sealed record CambiarImagenPanelRequest(string? Archivo);

/// <summary>Imagen de fondo de los paneles del dibujo de danos (el archivo vive en el almacen de imagenes).</summary>
public interface IImagenesPanelDano
{
    /// <summary>Nombre del archivo de la imagen del panel; nulo si aun no tiene. 404 si el panel no existe.</summary>
    Task<string?> ObtenerNombreAsync(string codigoPanel, CancellationToken ct = default);

    /// <summary>Fija el archivo de la imagen del panel (no confirma). 404 si el panel no existe.</summary>
    Task CambiarAsync(string codigoPanel, string archivo, CancellationToken ct = default);
}

/// <summary>Un catalogo con su version (huella de su contenido) para cache y validacion con ETag.</summary>
public sealed record CatalogoDto(string Version, IReadOnlyList<ValorCatalogo> Items);

/// <summary>Todos los catalogos con una version global; sirve para que la app detecte si algo cambio con una sola comparacion.</summary>
public sealed record CatalogosDto(string Version, IReadOnlyDictionary<string, IReadOnlyList<ValorCatalogo>> Catalogos);

/// <summary>Cuerpo de crear o editar un valor. Cada catalogo acepta solo los campos que le corresponden (ver CONTRATO.md).</summary>
public sealed record CatalogoValorRequest(
    string? Codigo,
    string? Nombre,
    int? Orden,
    bool? Activo,
    string? Accion,
    bool? Obligatoria,
    string? TipoVehiculo,
    Guid? CuadranteId,
    Guid? VistaFotoId,
    int? Numero,
    GeometriaCuadranteDto? Geometria,
    string? Panel,
    string? Lado);

/// <summary>Poligono normalizado de un cuadrante en una vista: cada punto es [x, y] con x e y entre 0 y 1 sobre la imagen de la vista.</summary>
public sealed record GeometriaCuadranteDto(IReadOnlyList<IReadOnlyList<double>> Puntos);

/// <summary>Lectura de los catalogos para el resto de la aplicacion (validar entradas, armar la ruta de un vehiculo). Con cache.</summary>
public interface ICatalogosLectura
{
    /// <summary>Codigos del catalogo; <paramref name="soloActivos"/> excluye los inactivos.</summary>
    Task<IReadOnlySet<string>> CodigosAsync(string catalogo, bool soloActivos, CancellationToken ct = default);

    /// <summary>Etapas activas del catalogo (id y codigo), en orden: la ruta estandar de cada VIN de un viaje.</summary>
    Task<IReadOnlyList<clsEtapaDeRuta>> RutaEstandarAsync(CancellationToken ct = default);

    /// <summary>Todos los valores del catalogo (activos e inactivos) tal como los sirve la API: id, codigo, nombre, orden, activo...</summary>
    Task<IReadOnlyList<ValorCatalogo>> ValoresAsync(string catalogo, CancellationToken ct = default);

    /// <summary>True si el valor existe en el catalogo y esta activo (por ejemplo, para crear una etapa con un lugar valido).</summary>
    Task<bool> ExisteActivoAsync(string catalogo, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Id del cliente activo con ese codigo; si no existe o no se indica, el primer cliente activo por orden. Es el cliente que se asigna a
    /// lo que se crea sin un cliente explicito (por ejemplo, los vehiculos de la ingesta).
    /// </summary>
    Task<Guid> ClienteAsync(string? codigoPreferido, CancellationToken ct = default);
}
