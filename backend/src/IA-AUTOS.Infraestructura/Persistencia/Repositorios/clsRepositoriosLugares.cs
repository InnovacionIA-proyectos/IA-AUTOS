using IAAutos.Dominio.Catalogos;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Repositorios;

internal sealed class clsRepositorioLugares(clsAppDbContext contexto) : ILugarRepository
{
    public Task<clsLugar?> ObtenerAsync(Guid id, CancellationToken ct = default) => contexto.Lugares.FirstOrDefaultAsync(l => l.Id == id, ct);
}

internal sealed class clsRepositorioPropuestasUbicacion(clsAppDbContext contexto) : IPropuestaUbicacionRepository
{
    public Task<clsPropuestaUbicacion?> ObtenerAsync(Guid id, CancellationToken ct = default) => contexto.PropuestasUbicacion.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<(clsPropuestaUbicacion Propuesta, string LugarNombre)>> ListarAsync(string? estado, CancellationToken ct = default)
    {
        var filas = await (from p in contexto.PropuestasUbicacion.AsNoTracking()
                           join l in contexto.Lugares.AsNoTracking() on p.LugarId equals l.Id
                           where estado == null || p.Estado == estado
                           orderby p.CreatedAt descending
                           select new { Propuesta = p, LugarNombre = l.Nombre }).ToListAsync(ct);
        return filas.Select(f => (f.Propuesta, f.LugarNombre)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, clsPropuestaUbicacion>> UltimaPorLugarAsync(IReadOnlyCollection<Guid> lugarIds, CancellationToken ct = default)
    {
        var ids = lugarIds.ToList();
        var propuestas = await contexto.PropuestasUbicacion.AsNoTracking().Where(p => ids.Contains(p.LugarId)).ToListAsync(ct);
        return propuestas.GroupBy(p => p.LugarId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());
    }

    public void Agregar(clsPropuestaUbicacion propuesta) => contexto.PropuestasUbicacion.Add(propuesta);
}
