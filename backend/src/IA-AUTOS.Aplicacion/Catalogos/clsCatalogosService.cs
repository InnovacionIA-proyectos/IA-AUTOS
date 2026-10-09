using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Catalogos;

/// <summary>Opciones de los catalogos (seccion Catalogos de la configuracion).</summary>
public sealed class clsOpcionesCatalogos
{
    public const string SECCION = "Catalogos";

    /// <summary>
    /// Segundos que se conservan los catalogos en memoria. Cualquier cambio hecho en esta instancia los renueva al instante; si hay
    /// varias instancias de la API, las demas se enteran como maximo cuando vence este tiempo.
    /// </summary>
    public int CacheSegundos { get; set; } = 60;
}

/// <summary>Estado de todos los catalogos en un momento: cada uno con su version y una version global.</summary>
public sealed record InstantaneaCatalogos(string Version, IReadOnlyDictionary<string, CatalogoDto> Catalogos);

/// <summary>
/// Catalogos maestros: lectura con cache y version (ETag), y administracion (crear, editar, borrar) de cualquiera de ellos. Un valor
/// en uso no se borra (la base lo impide y la API responde 409): se inactiva. Cada cambio renueva el cache.
/// </summary>
public sealed class clsCatalogosService(
    IEnumerable<ICatalogo> catalogos,
    IMemoryCache cache,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IOptions<clsOpcionesCatalogos> opciones) : ICatalogosLectura
{
    private const string CLAVE_CACHE = "catalogos:instantanea";

    private readonly IReadOnlyDictionary<string, ICatalogo> _porNombre = catalogos.ToDictionary(c => c.Nombre);

    // ------------------------------------------------------------------------------------------------ lectura

    public async Task<InstantaneaCatalogos> ObtenerTodosAsync(CancellationToken ct = default) =>
        (await cache.GetOrCreateAsync(CLAVE_CACHE, async entrada =>
        {
            entrada.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(opciones.Value.CacheSegundos, 1));
            return await ConstruirAsync(ct);
        }))!;

    public async Task<CatalogoDto> ObtenerAsync(string nombre, CancellationToken ct = default)
    {
        Buscar(nombre);
        return (await ObtenerTodosAsync(ct)).Catalogos[nombre];
    }

    public async Task<IReadOnlySet<string>> CodigosAsync(string catalogo, bool soloActivos, CancellationToken ct = default)
    {
        var items = (await ObtenerAsync(catalogo, ct)).Items;
        return items.Where(i => !soloActivos || i.Activo).Select(i => i.Codigo).OfType<string>().ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<Guid>> EtapasDeLaRutaAsync(CancellationToken ct = default) =>
        (await ObtenerAsync(clsNombresCatalogo.ETAPAS, ct)).Items.Where(i => i.Activo).Select(i => i.Id).ToList();

    public async Task<bool> ExisteActivoAsync(string catalogo, Guid id, CancellationToken ct = default) =>
        (await ObtenerAsync(catalogo, ct)).Items.Any(i => i.Id == id && i.Activo);

    // ------------------------------------------------------------------------------------------- administracion

    public async Task<ValorCatalogo> CrearAsync(string nombre, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var valor = await Buscar(nombre).CrearAsync(campos, ct);
        auditor.Registrar($"catalogo.{nombre}.crear", nombre, valor.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        Invalidar();
        return valor;
    }

    public async Task<ValorCatalogo> ActualizarAsync(string nombre, Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var valor = await Buscar(nombre).ActualizarAsync(id, campos, ct);
        auditor.Registrar($"catalogo.{nombre}.actualizar", nombre, id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        Invalidar();
        return valor;
    }

    public async Task EliminarAsync(string nombre, Guid id, CancellationToken ct = default)
    {
        await Buscar(nombre).EliminarAsync(id, ct);
        auditor.Registrar($"catalogo.{nombre}.eliminar", nombre, id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        Invalidar();
    }

    // ----------------------------------------------------------------------------------------------- internos

    private ICatalogo Buscar(string nombre) =>
        _porNombre.TryGetValue(nombre, out var catalogo)
            ? catalogo
            : throw new clsEntidadNoEncontradaException($"Catálogo desconocido: {nombre}. Disponibles: {string.Join(", ", _porNombre.Keys.Order())}");

    private void Invalidar() => cache.Remove(CLAVE_CACHE);

    private async Task<InstantaneaCatalogos> ConstruirAsync(CancellationToken ct)
    {
        var resultado = new SortedDictionary<string, CatalogoDto>(StringComparer.Ordinal);
        foreach (var catalogo in _porNombre.Values)
        {
            var items = await catalogo.ListarAsync(ct);
            resultado[catalogo.Nombre] = new CatalogoDto(Huella(items), items);
        }

        return new InstantaneaCatalogos(Huella(resultado.Select(c => $"{c.Key}:{c.Value.Version}")), resultado);
    }

    /// <summary>Huella corta y estable del contenido: cambia si y solo si cambia algun dato del catalogo.</summary>
    private static string Huella<T>(T contenido) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(contenido)))).ToLowerInvariant()[..16];
}
