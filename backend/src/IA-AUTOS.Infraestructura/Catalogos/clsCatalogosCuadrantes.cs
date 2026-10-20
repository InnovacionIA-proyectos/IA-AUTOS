using System.Text.Json;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Catalogos;

/// <summary>
/// Cuadrantes: las zonas numeradas de la carroceria. Donde se dibujan en cada panel lo administra cuadrante-regiones. Junto a ellos se publican las
/// zonas provisionales (frente, llantas) con su codigo, sin numero y sin region; esas filas se leen aqui pero no se editan.
/// </summary>
internal sealed class clsCatalogoCuadrantes(clsAppDbContext contexto, IReloj reloj)
    : clsCatalogoEf<clsCuadrante>(contexto, reloj, clsNombresCatalogo.CUADRANTES)
{
    protected override IReadOnlyList<string> CamposPermitidos { get; } = ["tipo_vehiculo", "numero", "nombre", "orden", "activo"];

    public override async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await Conjunto.AsNoTracking().OrderBy(c => c.Orden).ThenBy(c => c.Numero).ToListAsync(ct);
        var provisionales = await Contexto.ZonasDano.AsNoTracking().Where(z => z.CuadranteId == null).OrderBy(z => z.Orden).ToListAsync(ct);
        return filas.Select(Mapear).Concat(provisionales.Select(MapearProvisional)).ToList();
    }

    private static ValorCatalogo MapearProvisional(clsZonaDano z) => new()
    {
        ["id"] = z.Id,
        ["codigo"] = z.Codigo,
        ["tipo_vehiculo"] = null,
        ["numero"] = null,
        ["nombre"] = z.Nombre,
        ["orden"] = z.Orden,
        ["activo"] = z.Activo,
    };

    protected override ValorCatalogo Mapear(clsCuadrante c) => new()
    {
        ["id"] = c.Id,
        ["codigo"] = null,
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
