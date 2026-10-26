using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Archivos.DTOArchivos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Vehiculos;
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
    IImagenesPanelDano imagenesPaneles,
    clsArchivosService archivos,
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

    public async Task<IReadOnlyList<clsEtapaDeRuta>> RutaEstandarAsync(CancellationToken ct = default) =>
        (await ObtenerAsync(clsNombresCatalogo.ETAPAS, ct)).Items.Where(i => i.Activo).Select(i => new clsEtapaDeRuta(i.Id, i.Codigo ?? string.Empty)).ToList();

    public async Task<IReadOnlyList<ValorCatalogo>> ValoresAsync(string catalogo, CancellationToken ct = default) =>
        (await ObtenerAsync(catalogo, ct)).Items;

    public async Task<bool> ExisteActivoAsync(string catalogo, Guid id, CancellationToken ct = default) =>
        (await ObtenerAsync(catalogo, ct)).Items.Any(i => i.Id == id && i.Activo);

    public async Task<Guid> ClienteAsync(string? codigoPreferido, CancellationToken ct = default)
    {
        var activos = (await ObtenerAsync(clsNombresCatalogo.CLIENTES, ct)).Items.Where(i => i.Activo).ToList();
        var elegido = (codigoPreferido is null ? null : activos.FirstOrDefault(c => c.Codigo == codigoPreferido)) ?? activos.FirstOrDefault();
        return elegido?.Id ?? throw new clsErrorInternoException("No hay clientes activos en el catálogo de clientes");
    }

    // ------------------------------------------------------------------------------------------- administracion

    public async Task<ValorCatalogo> CrearAsync(string nombre, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var valor = await Buscar(nombre).CrearAsync(campos, ct);
        auditor.Registrar($"catalogo.{nombre}.crear", nombre, valor.Id, detalle: new DetalleAuditoria(null, valor), clienteId: ClienteDelValor(nombre, valor.Id));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        Invalidar();
        return valor;
    }

    public async Task<ValorCatalogo> ActualizarAsync(string nombre, Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var catalogo = Buscar(nombre);
        var antes = (await catalogo.ListarAsync(ct)).FirstOrDefault(v => v.Id == id);
        var valor = await catalogo.ActualizarAsync(id, campos, ct);
        auditor.Registrar($"catalogo.{nombre}.actualizar", nombre, id, detalle: new DetalleAuditoria(antes, valor), clienteId: ClienteDelValor(nombre, id));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        Invalidar();
        return valor;
    }

    public async Task EliminarAsync(string nombre, Guid id, CancellationToken ct = default)
    {
        var catalogo = Buscar(nombre);
        var antes = (await catalogo.ListarAsync(ct)).FirstOrDefault(v => v.Id == id);
        await catalogo.EliminarAsync(id, ct);
        auditor.Registrar($"catalogo.{nombre}.eliminar", nombre, id, detalle: new DetalleAuditoria(antes, null), clienteId: ClienteDelValor(nombre, id));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        Invalidar();
    }

    // ------------------------------------------------------------------------------------- imagen de los paneles

    /// <summary>La imagen de fondo de un panel de danos (404 si el panel no existe o aun no tiene imagen).</summary>
    public async Task<ArchivoLeidoDto> ObtenerImagenPanelAsync(string codigoPanel, CancellationToken ct = default)
    {
        var nombre = await imagenesPaneles.ObtenerNombreAsync(codigoPanel, ct)
            ?? throw new clsEntidadNoEncontradaException($"El panel {codigoPanel} aún no tiene imagen");
        return await archivos.LeerAsync(nombre, ct);
    }

    /// <summary>Fija la imagen del panel con un archivo ya subido (400 si no existe en el almacen).</summary>
    public async Task CambiarImagenPanelAsync(string codigoPanel, CambiarImagenPanelRequest solicitud, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(solicitud.Archivo))
        {
            throw clsErroresCampos.De("archivo", "El nombre del archivo es requerido: es el que devolvió la subida de imágenes");
        }

        var archivo = solicitud.Archivo.Trim();
        try
        {
            await archivos.LeerAsync(archivo, ct);
        }
        catch (clsEntidadNoEncontradaException)
        {
            throw clsErroresCampos.De("archivo", "El archivo no existe: súbalo primero con POST /api/uploads");
        }

        var anterior = await imagenesPaneles.ObtenerNombreAsync(codigoPanel, ct);
        await imagenesPaneles.CambiarAsync(codigoPanel, archivo, ct);
        auditor.Registrar(
            $"catalogo.{clsNombresCatalogo.PANELES_DANO}.imagen",
            clsNombresCatalogo.PANELES_DANO,
            detalle: new DetalleAuditoria(new { panel = codigoPanel, archivo = anterior }, new { panel = codigoPanel, archivo }));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        Invalidar();
    }

    // ----------------------------------------------------------------------------------------------- internos

    /// <summary>Un cliente del catalogo de clientes es su propio cliente en la auditoria; el resto de los catalogos no pertenece a uno.</summary>
    private static Guid? ClienteDelValor(string catalogo, Guid id) => catalogo == clsNombresCatalogo.CLIENTES ? id : null;

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
