using System.Text.Json;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Catalogos;

/// <summary>El dibujo de danos (cuadrantes, paneles y regiones) es de solo lectura por la API: un dibujo distinto es una version nueva del archivo, que se siembra con un guion.</summary>
internal static class clsDibujoSoloLectura
{
    public const string MENSAJE = "El dibujo se cambia con una versión nueva del archivo";

    public static clsConflictoException Conflicto() => new(MENSAJE);
}

/// <summary>
/// Cuadrantes: las piezas numeradas de la carroceria. Cada uno declara si lleva lado (<c>lleva_lado</c>) y trae sus zonas: una por lado (izquierdo y derecho) o una sola sin lado, con el
/// <c>nombre_completo</c> de la pieza en ese lado y su codigo <c>aiag_area</c>. Donde se dibujan en cada panel lo dice cuadrante-regiones. Solo lectura: el archivo del dibujo manda.
/// </summary>
internal sealed class clsCatalogoCuadrantes(clsAppDbContext contexto) : ICatalogo
{
    public string Nombre => clsNombresCatalogo.CUADRANTES;

    public async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
    {
        var cuadrantes = await contexto.Cuadrantes.AsNoTracking().OrderBy(c => c.Orden).ThenBy(c => c.Numero).ToListAsync(ct);
        var zonas = (await contexto.ZonasDano.AsNoTracking().ToListAsync(ct)).ToLookup(z => z.CuadranteId);
        return cuadrantes.Select(c => new ValorCatalogo
        {
            ["id"] = c.Id,
            ["codigo"] = null,
            ["tipo_vehiculo"] = c.TipoVehiculo,
            ["numero"] = c.Numero,
            ["nombre"] = c.Nombre,
            ["lleva_lado"] = c.LlevaLado,
            ["zonas"] = zonas[c.Id].OrderBy(z => z.Lado == clsLado.DERECHO ? 1 : 0)
                .Select(z => new Dictionary<string, object?> { ["id"] = z.Id, ["lado"] = z.Lado, ["nombre_completo"] = z.NombreCompleto, ["aiag_area"] = z.AiagArea }).ToList(),
            ["orden"] = c.Orden,
            ["activo"] = c.Activo,
        }).ToList();
    }

    public Task<ValorCatalogo> CrearAsync(IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default) => throw clsDibujoSoloLectura.Conflicto();

    public Task<ValorCatalogo> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default) => throw clsDibujoSoloLectura.Conflicto();

    public Task EliminarAsync(Guid id, CancellationToken ct = default) => throw clsDibujoSoloLectura.Conflicto();
}
