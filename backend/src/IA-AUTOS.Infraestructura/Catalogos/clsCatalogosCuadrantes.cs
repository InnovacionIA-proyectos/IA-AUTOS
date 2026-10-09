using System.Text.Json;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Catalogos;

/// <summary>Cuadrantes: las zonas numeradas de la carroceria. Donde se dibujan en cada vista lo administra cuadrante-vistas.</summary>
internal sealed class clsCatalogoCuadrantes(clsAppDbContext contexto, IReloj reloj)
    : clsCatalogoEf<clsCuadrante>(contexto, reloj, clsNombresCatalogo.CUADRANTES)
{
    protected override IReadOnlyList<string> CamposPermitidos { get; } = ["tipo_vehiculo", "numero", "nombre", "orden", "activo"];

    public override async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await Conjunto.AsNoTracking().OrderBy(c => c.Orden).ThenBy(c => c.Numero).ToListAsync(ct);
        return filas.Select(Mapear).ToList();
    }

    protected override ValorCatalogo Mapear(clsCuadrante c) => new()
    {
        ["id"] = c.Id,
        ["tipo_vehiculo"] = c.TipoVehiculo,
        ["numero"] = c.Numero,
        ["nombre"] = c.Nombre,
        ["orden"] = c.Orden,
        ["activo"] = c.Activo,
    };

    protected override Task<clsCuadrante> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct) =>
        Task.FromResult(clsCuadrante.Crear(
            campos.Texto("tipo_vehiculo"),
            campos.Entero("numero"),
            campos.Texto("nombre"),
            campos.Entero("orden"),
            campos.Booleano("activo"),
            Reloj.Ahora));

    protected override Task AplicarAsync(clsCuadrante entidad, clsCamposCatalogo campos, CancellationToken ct)
    {
        entidad.Cambiar(campos.Texto("tipo_vehiculo"), campos.Tiene("tipo_vehiculo"), campos.Entero("numero"), Reloj.Ahora);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Poligono de un cuadrante en una vista: un mismo cuadrante se dibuja en varias vistas (la puerta se ve de lado y en diagonal),
/// una vez por vista. Solo las vistas obligatorias y activas admiten poligonos.
/// </summary>
internal sealed class clsCatalogoCuadranteVistas(clsAppDbContext contexto, IReloj reloj) : ICatalogo
{
    private static readonly string[] CAMPOS_PERMITIDOS = ["cuadrante_id", "vista_foto_id", "geometria"];

    public string Nombre => clsNombresCatalogo.CUADRANTE_VISTAS;

    public async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await (from p in contexto.CuadranteVistas.AsNoTracking()
                           join c in contexto.Cuadrantes.AsNoTracking() on p.CuadranteId equals c.Id
                           join v in contexto.VistasFoto.AsNoTracking() on p.VistaFotoId equals v.Id
                           orderby c.Orden, c.Numero, v.Orden
                           select new { Poligono = p, c.Numero, Vista = v.Codigo }).ToListAsync(ct);
        return filas.Select(f => Mapear(f.Poligono, f.Numero, f.Vista)).ToList();
    }

    public async Task<ValorCatalogo> CrearAsync(IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var lector = new clsCamposCatalogo(campos);
        lector.SoloPermitidos(CAMPOS_PERMITIDOS);
        var cuadranteId = lector.Identificador("cuadrante_id") ?? throw clsErroresCampos.De("cuadrante_id", "El cuadrante es requerido");
        var vistaId = lector.Identificador("vista_foto_id") ?? throw clsErroresCampos.De("vista_foto_id", "La vista es requerida");
        if (!await contexto.Cuadrantes.AnyAsync(c => c.Id == cuadranteId, ct))
        {
            throw clsErroresCampos.De("cuadrante_id", "El cuadrante no existe");
        }

        await AsegurarVistaConCuadrantesAsync(vistaId, ct);
        var poligono = clsCuadranteVista.Crear(cuadranteId, vistaId, lector.Json("geometria"), reloj.Ahora);
        contexto.CuadranteVistas.Add(poligono);
        return await MapearAsync(poligono, ct);
    }

    public async Task<ValorCatalogo> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var lector = new clsCamposCatalogo(campos);
        lector.SoloPermitidos(CAMPOS_PERMITIDOS);
        var poligono = await contexto.CuadranteVistas.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new clsEntidadNoEncontradaException($"No existe el valor {id} en el catálogo {Nombre}");

        if (lector.Tiene("cuadrante_id") && lector.Identificador("cuadrante_id") != poligono.CuadranteId)
        {
            throw clsErroresCampos.De("cuadrante_id", "El cuadrante no se puede cambiar: cree el polígono en el otro cuadrante y borre este");
        }

        var vistaId = lector.Identificador("vista_foto_id");
        if (vistaId is not null)
        {
            await AsegurarVistaConCuadrantesAsync(vistaId.Value, ct);
        }

        poligono.Cambiar(vistaId, lector.Json("geometria"), lector.Tiene("geometria"), reloj.Ahora);
        return await MapearAsync(poligono, ct);
    }

    public async Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var poligono = await contexto.CuadranteVistas.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new clsEntidadNoEncontradaException($"No existe el valor {id} en el catálogo {Nombre}");
        contexto.CuadranteVistas.Remove(poligono);
    }

    private async Task<ValorCatalogo> MapearAsync(clsCuadranteVista p, CancellationToken ct)
    {
        var numero = await contexto.Cuadrantes.Where(c => c.Id == p.CuadranteId).Select(c => (int?)c.Numero).FirstOrDefaultAsync(ct);
        var vista = await contexto.VistasFoto.Where(v => v.Id == p.VistaFotoId).Select(v => v.Codigo).FirstOrDefaultAsync(ct);
        return Mapear(p, numero, vista);
    }

    private static ValorCatalogo Mapear(clsCuadranteVista p, int? numero, string? vista) => new()
    {
        ["id"] = p.Id,
        ["cuadrante_id"] = p.CuadranteId,
        ["numero"] = numero,
        ["vista_foto_id"] = p.VistaFotoId,
        ["vista"] = vista,
        // La geometria sale como objeto JSON (no como texto) para que la app y el portal la lean directamente.
        ["geometria"] = JsonSerializer.Deserialize<JsonElement>(p.Geometria),
    };

    /// <summary>La vista debe existir, estar activa y ser obligatoria: las vistas adicionales (baul, cojineria) no tienen cuadrantes.</summary>
    private async Task AsegurarVistaConCuadrantesAsync(Guid vistaId, CancellationToken ct)
    {
        var vista = await contexto.VistasFoto.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vistaId, ct)
            ?? throw clsErroresCampos.De("vista_foto_id", "La vista no existe");
        if (!vista.Obligatoria)
        {
            throw clsErroresCampos.De("vista_foto_id", "Las vistas adicionales no tienen cuadrantes");
        }

        if (!vista.Activo)
        {
            throw clsErroresCampos.De("vista_foto_id", "La vista está inactiva");
        }
    }
}
