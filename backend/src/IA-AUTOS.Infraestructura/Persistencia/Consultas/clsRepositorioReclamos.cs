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

    public void Agregar(clsReclamo reclamo) => contexto.Reclamos.Add(reclamo);
}
