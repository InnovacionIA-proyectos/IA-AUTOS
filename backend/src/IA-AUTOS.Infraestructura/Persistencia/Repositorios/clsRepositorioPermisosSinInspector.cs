using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Repositorios;

internal sealed class clsRepositorioPermisosSinInspector(clsAppDbContext contexto) : IPermisoSinInspectorRepository
{
    public Task<clsPermisoSinInspector?> ObtenerDeViajeAsync(Guid viajeId, CancellationToken ct = default) =>
        contexto.PermisosSinInspector.FirstOrDefaultAsync(p => p.ViajeId == viajeId, ct);

    public async Task<IReadOnlyList<clsPermisoSinInspector>> ObtenerPendientesAsync(DateTimeOffset pedidasHasta, int limite, CancellationToken ct = default) =>
        await contexto.PermisosSinInspector.Where(p => p.Estado == clsEstadoPermiso.PENDIENTE && p.CreatedAt <= pedidasHasta).OrderBy(p => p.CreatedAt).Take(limite).ToListAsync(ct);

    public void Agregar(clsPermisoSinInspector permiso) => contexto.PermisosSinInspector.Add(permiso);
}
