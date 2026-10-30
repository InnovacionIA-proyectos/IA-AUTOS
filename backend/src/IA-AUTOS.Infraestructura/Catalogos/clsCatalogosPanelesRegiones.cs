using System.Text.Json;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Catalogos;

/// <summary>
/// Region tocable de un panel del dibujo de danos: el poligono de una zona (cuadrante + lado, o sin lado en los cuadrantes que no lo llevan) en un panel, una sola vez por panel.
/// <c>prioridad</c> ordena las regiones que se superponen. El poligono se rellena con la regla par-impar (evenOdd): los huecos son anillos interiores. Solo lectura: el archivo del dibujo manda.
/// </summary>
internal sealed class clsCatalogoCuadranteRegiones(clsAppDbContext contexto) : ICatalogo
{
    public string Nombre => clsNombresCatalogo.CUADRANTE_REGIONES;

    public async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await (from r in contexto.RegionesDano.AsNoTracking()
                           join p in contexto.PanelesDano.AsNoTracking() on r.PanelId equals p.Id
                           join c in contexto.Cuadrantes.AsNoTracking() on r.CuadranteId equals c.Id
                           join z in contexto.ZonasDano.AsNoTracking() on r.ZonaId equals z.Id
                           orderby p.Orden, r.Prioridad, c.Orden, c.Numero, z.Lado
                           select new { Region = r, Panel = p.Codigo, c.Numero, c.Nombre, z.Lado }).ToListAsync(ct);
        return filas.Select(f => new ValorCatalogo
        {
            ["id"] = f.Region.Id,
            ["panel"] = f.Panel,
            ["cuadrante_id"] = f.Region.CuadranteId,
            ["numero"] = f.Numero,
            ["nombre"] = f.Nombre,
            ["lado"] = f.Lado,
            ["prioridad"] = f.Region.Prioridad,
            // La geometria sale como objeto JSON (no como texto) para que la app y el portal la lean directamente.
            ["geometria"] = JsonSerializer.Deserialize<JsonElement>(f.Region.Geometria),
        }).ToList();
    }

    public Task<ValorCatalogo> CrearAsync(IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default) => throw clsDibujoSoloLectura.Conflicto();

    public Task<ValorCatalogo> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default) => throw clsDibujoSoloLectura.Conflicto();

    public Task EliminarAsync(Guid id, CancellationToken ct = default) => throw clsDibujoSoloLectura.Conflicto();
}

/// <summary>
/// Los paneles del dibujo de danos con la URL de su imagen de fondo, su tamano de referencia (<c>ancho_ref</c>, <c>alto_ref</c>) y la <c>version</c> del archivo con que se sembraron. La imagen se cambia
/// con PUT .../imagen (debe ser la de la misma version del archivo: una imagen distinta desalinea los poligonos); el resto es de solo lectura.
/// </summary>
internal sealed class clsCatalogoPanelesDano(clsAppDbContext contexto) : ICatalogo
{
    public string Nombre => clsNombresCatalogo.PANELES_DANO;

    public async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default) =>
        (await contexto.PanelesDano.AsNoTracking().OrderBy(p => p.Orden).ThenBy(p => p.Nombre).ToListAsync(ct)).Select(p => new ValorCatalogo
        {
            ["id"] = p.Id,
            ["codigo"] = p.Codigo,
            ["nombre"] = p.Nombre,
            ["orden"] = p.Orden,
            ["activo"] = p.Activo,
            ["ancho_ref"] = p.AnchoRef,
            ["alto_ref"] = p.AltoRef,
            ["version"] = p.Version,
            // El nombre del archivo va en la URL para que la cache larga de la imagen se renueve cuando se cambia.
            ["imagen_url"] = p.ImagenPath is null
                ? null
                : $"/api/catalogos/{clsNombresCatalogo.PANELES_DANO}/{p.Codigo}/imagen?v={Path.GetFileNameWithoutExtension(p.ImagenPath)}",
        }).ToList();

    public Task<ValorCatalogo> CrearAsync(IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default) => throw clsDibujoSoloLectura.Conflicto();

    public Task<ValorCatalogo> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default) => throw clsDibujoSoloLectura.Conflicto();

    public Task EliminarAsync(Guid id, CancellationToken ct = default) => throw clsDibujoSoloLectura.Conflicto();
}

/// <summary>Lee y cambia el archivo de la imagen de fondo de un panel de danos.</summary>
internal sealed class clsImagenesPanelDano(clsAppDbContext contexto, IReloj reloj) : IImagenesPanelDano
{
    public async Task<string?> ObtenerNombreAsync(string codigoPanel, CancellationToken ct = default) =>
        (await BuscarAsync(codigoPanel, asNoTracking: true, ct)).ImagenPath;

    public async Task CambiarAsync(string codigoPanel, string archivo, CancellationToken ct = default) =>
        (await BuscarAsync(codigoPanel, asNoTracking: false, ct)).CambiarImagen(archivo, reloj.Ahora);

    private async Task<clsPanelDano> BuscarAsync(string codigoPanel, bool asNoTracking, CancellationToken ct)
    {
        var consulta = asNoTracking ? contexto.PanelesDano.AsNoTracking() : contexto.PanelesDano;
        return await consulta.FirstOrDefaultAsync(p => p.Codigo == codigoPanel, ct)
            ?? throw new clsEntidadNoEncontradaException($"No existe el panel {codigoPanel}");
    }
}
