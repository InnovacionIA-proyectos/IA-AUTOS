using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsRepositorioReclamos(clsAppDbContext contexto) : IReclamoRepository
{
    public Task<clsReclamo?> ObtenerActivoAsync(Guid viajeId, string proceso, CancellationToken ct = default) =>
        contexto.Reclamos.FirstOrDefaultAsync(r => r.ViajeId == viajeId && r.Proceso == proceso && r.LiberadoAt == null, ct);

    public async Task<IReadOnlyList<clsReclamo>> ObtenerDeViajeAsync(Guid viajeId, CancellationToken ct = default) =>
        await contexto.Reclamos.Where(r => r.ViajeId == viajeId).OrderByDescending(r => r.ReclamadoAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> LugaresDelInspectorAsync(Guid usuarioId, CancellationToken ct = default) =>
        await contexto.InspectorLugares.AsNoTracking().Where(p => p.UsuarioId == usuarioId).Select(p => p.LugarId).ToListAsync(ct);

    public async Task<IReadOnlyList<clsInspectorLugar>> ObtenerPuntosAsync(Guid usuarioId, CancellationToken ct = default) =>
        await contexto.InspectorLugares.Where(p => p.UsuarioId == usuarioId).OrderBy(p => p.CreatedAt).ToListAsync(ct);

    public void AgregarPunto(clsInspectorLugar punto) => contexto.InspectorLugares.Add(punto);

    public void QuitarPunto(clsInspectorLugar punto) => contexto.InspectorLugares.Remove(punto);

    public async Task<IReadOnlyList<Guid>> InspectoresDelLugarAsync(Guid lugarId, CancellationToken ct = default) =>
        await (from p in contexto.InspectorLugares.AsNoTracking()
               join u in contexto.Usuarios.AsNoTracking() on p.UsuarioId equals u.Id
               where p.LugarId == lugarId && u.Activo
               select u.Id).Distinct().ToListAsync(ct);

    public async Task<bool> LugarTieneInspectoresAsync(Guid lugarId, CancellationToken ct = default) =>
        await clsReglaInspectorDelPunto.LugaresConInspector(contexto).AnyAsync(l => l == lugarId, ct);

    public async Task<IReadOnlySet<Guid>> LugaresConInspectoresAsync(IReadOnlyCollection<Guid> lugarIds, CancellationToken ct = default)
    {
        var ids = lugarIds.Distinct().ToList();
        return ids.Count == 0 ? new HashSet<Guid>() : (await clsReglaInspectorDelPunto.LugaresConInspector(contexto).Where(l => ids.Contains(l)).Distinct().ToListAsync(ct)).ToHashSet();
    }

    public void Agregar(clsReclamo reclamo) => contexto.Reclamos.Add(reclamo);
}
