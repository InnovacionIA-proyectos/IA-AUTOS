using System.Text.Json;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Catalogos;

/// <summary>
/// Region tocable de un panel del dibujo de danos: el poligono de una zona numerada (cuadrante + lado) en un panel, una sola vez por panel. En los
/// costados y perfiles el lado es el del panel; en el panel superior el lado se envia (del cuadrante 1 al 25) y del 26 en adelante no lleva.
/// El panel, el cuadrante y el lado no se cambian: para moverla se crea en el otro panel y se borra esta.
/// </summary>
internal sealed class clsCatalogoCuadranteRegiones(clsAppDbContext contexto, IReloj reloj) : ICatalogo
{
    private static readonly string[] CAMPOS_PERMITIDOS = ["panel", "cuadrante_id", "lado", "geometria"];

    public string Nombre => clsNombresCatalogo.CUADRANTE_REGIONES;

    public async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await (from r in contexto.RegionesDano.AsNoTracking()
                           join p in contexto.PanelesDano.AsNoTracking() on r.PanelId equals p.Id
                           join c in contexto.Cuadrantes.AsNoTracking() on r.CuadranteId equals c.Id
                           join z in contexto.ZonasDano.AsNoTracking() on r.ZonaId equals z.Id
                           orderby p.Orden, c.Orden, c.Numero, z.Lado
                           select new { Region = r, Panel = p.Codigo, c.Numero, c.Nombre, z.Lado }).ToListAsync(ct);
        return filas.Select(f => Mapear(f.Region, f.Panel, f.Numero, f.Nombre, f.Lado)).ToList();
    }

    public async Task<ValorCatalogo> CrearAsync(IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var lector = new clsCamposCatalogo(campos);
        lector.SoloPermitidos(CAMPOS_PERMITIDOS);
        var codigoPanel = lector.Texto("panel") ?? throw clsErroresCampos.De("panel", "El panel es requerido");
        var cuadranteId = lector.Identificador("cuadrante_id") ?? throw clsErroresCampos.De("cuadrante_id", "El cuadrante es requerido");
        var panel = await contexto.PanelesDano.AsNoTracking().FirstOrDefaultAsync(p => p.Codigo == codigoPanel, ct)
            ?? throw clsErroresCampos.De("panel", "El panel no existe");
        var cuadrante = await contexto.Cuadrantes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cuadranteId, ct)
            ?? throw clsErroresCampos.De("cuadrante_id", "El cuadrante no existe");

        var lado = LadoDeLaRegion(panel, cuadrante.Numero, lector.Texto("lado"));
        var zona = await contexto.ZonasDano.AsNoTracking().FirstOrDefaultAsync(z => z.CuadranteId == cuadranteId && z.Lado == lado, ct)
            ?? throw clsErroresCampos.De("cuadrante_id", "El cuadrante no tiene una zona para ese lado");
        if (await contexto.RegionesDano.AnyAsync(r => r.PanelId == panel.Id && r.ZonaId == zona.Id, ct))
        {
            throw new clsConflictoException(
                $"El panel {panel.Codigo} ya tiene una región del cuadrante {cuadrante.Numero}" + (lado is null ? string.Empty : $" lado {lado}"));
        }

        var region = clsRegionDano.Crear(panel.Id, zona.Id, cuadranteId, lector.Json("geometria"), reloj.Ahora);
        contexto.RegionesDano.Add(region);
        return Mapear(region, panel.Codigo, cuadrante.Numero, cuadrante.Nombre, lado);
    }

    public async Task<ValorCatalogo> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var lector = new clsCamposCatalogo(campos);
        lector.SoloPermitidos(CAMPOS_PERMITIDOS);
        var region = await contexto.RegionesDano.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new clsEntidadNoEncontradaException($"No existe el valor {id} en el catálogo {Nombre}");
        var panel = await contexto.PanelesDano.AsNoTracking().FirstAsync(p => p.Id == region.PanelId, ct);
        var cuadrante = await contexto.Cuadrantes.AsNoTracking().FirstAsync(c => c.Id == region.CuadranteId, ct);
        var lado = await contexto.ZonasDano.AsNoTracking().Where(z => z.Id == region.ZonaId).Select(z => z.Lado).FirstAsync(ct);

        const string FIJO = "no se puede cambiar: cree la región en el otro panel y borre esta";
        if (lector.Tiene("panel") && lector.Texto("panel") != panel.Codigo)
        {
            throw clsErroresCampos.De("panel", $"El panel {FIJO}");
        }

        if (lector.Tiene("cuadrante_id") && lector.Identificador("cuadrante_id") != region.CuadranteId)
        {
            throw clsErroresCampos.De("cuadrante_id", $"El cuadrante {FIJO}");
        }

        if (lector.Tiene("lado") && lector.Texto("lado") != lado)
        {
            throw clsErroresCampos.De("lado", $"El lado {FIJO}");
        }

        if (lector.Tiene("geometria"))
        {
            region.CambiarGeometria(lector.Json("geometria"), reloj.Ahora);
        }

        return Mapear(region, panel.Codigo, cuadrante.Numero, cuadrante.Nombre, lado);
    }

    public async Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var region = await contexto.RegionesDano.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new clsEntidadNoEncontradaException($"No existe el valor {id} en el catálogo {Nombre}");
        contexto.RegionesDano.Remove(region);
    }

    /// <summary>
    /// El lado de la zona a la que pertenece la region: del cuadrante 26 en adelante no hay lado; del 1 al 25 sale del panel (costados y perfiles) o debe
    /// enviarse (panel superior). Un lado que no coincide con el del panel o con el numero responde 400 en el campo lado.
    /// </summary>
    private static string? LadoDeLaRegion(clsPanelDano panel, int numero, string? lado)
    {
        if (!clsLado.LlevaLado(numero))
        {
            return lado is null ? null : throw clsErroresCampos.De("lado", "Del cuadrante 26 en adelante la región no lleva lado");
        }

        if (lado is not null && !clsLado.VALORES.Contains(lado))
        {
            throw clsErroresCampos.De("lado", "El lado debe ser izquierdo o derecho");
        }

        if (panel.LadoDelPanel is { } ladoPanel)
        {
            return lado is null || lado == ladoPanel
                ? ladoPanel
                : throw clsErroresCampos.De("lado", $"El lado no coincide con el del panel {panel.Codigo} ({ladoPanel})");
        }

        return lado ?? throw clsErroresCampos.De("lado", "El lado es requerido del cuadrante 1 al 25 (izquierdo o derecho)");
    }

    private static ValorCatalogo Mapear(clsRegionDano r, string panel, int numero, string nombre, string? lado) => new()
    {
        ["id"] = r.Id,
        ["panel"] = panel,
        ["cuadrante_id"] = r.CuadranteId,
        ["numero"] = numero,
        ["nombre"] = nombre,
        ["lado"] = lado,
        // La geometria sale como objeto JSON (no como texto) para que la app y el portal la lean directamente.
        ["geometria"] = JsonSerializer.Deserialize<JsonElement>(r.Geometria),
    };
}

/// <summary>Los cinco paneles del dibujo de danos. Se publican con la URL de su imagen de fondo; la imagen se cambia con PUT .../imagen.</summary>
internal sealed class clsCatalogoPanelesDano(clsAppDbContext contexto, IReloj reloj)
    : clsCatalogoEf<clsPanelDano>(contexto, reloj, clsNombresCatalogo.PANELES_DANO)
{
    protected override IReadOnlyList<string> CamposPermitidos => clsCamposCatalogo.COMUNES;

    protected override ValorCatalogo Mapear(clsPanelDano p)
    {
        var valor = Comunes(p);
        // El nombre del archivo va en la URL para que la cache larga de la imagen se renueve cuando se cambia.
        valor["imagen_url"] = p.ImagenPath is null
            ? null
            : $"/api/catalogos/{clsNombresCatalogo.PANELES_DANO}/{p.Codigo}/imagen?v={Path.GetFileNameWithoutExtension(p.ImagenPath)}";
        return valor;
    }

    protected override Task<clsPanelDano> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct) =>
        Task.FromResult(clsPanelDano.Crear(campos.Texto("codigo"), campos.Texto("nombre"), campos.Entero("orden"), campos.Booleano("activo"), Reloj.Ahora));
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
