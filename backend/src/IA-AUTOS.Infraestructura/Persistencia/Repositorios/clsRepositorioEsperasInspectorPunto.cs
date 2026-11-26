using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Repositorios;

internal sealed class clsRepositorioEsperasInspectorPunto(clsAppDbContext contexto) : IEsperaInspectorPuntoRepository
{
    public Task<clsEsperaInspectorPunto?> ObtenerAbiertaAsync(Guid viajeId, Guid lugarId, CancellationToken ct = default) =>
        contexto.EsperasInspectorPunto.FirstOrDefaultAsync(e => e.ViajeId == viajeId && e.LugarId == lugarId && e.Estado == clsEstadoEspera.ESPERANDO, ct);

    public Task<clsEsperaInspectorPunto?> ObtenerUltimaAsync(Guid viajeId, Guid lugarId, CancellationToken ct = default) =>
        contexto.EsperasInspectorPunto.Where(e => e.ViajeId == viajeId && e.LugarId == lugarId).OrderByDescending(e => e.ActivadaAt).ThenByDescending(e => e.CreatedAt).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<clsEsperaInspectorPunto>> ObtenerAbiertasDeViajeAsync(Guid viajeId, CancellationToken ct = default) =>
        await contexto.EsperasInspectorPunto.Where(e => e.ViajeId == viajeId && e.Estado == clsEstadoEspera.ESPERANDO).ToListAsync(ct);

    public async Task<IReadOnlyList<clsEsperaInspectorPunto>> ObtenerPorAvisarAsync(DateTimeOffset activadasHasta, int limite, CancellationToken ct = default) =>
        await contexto.EsperasInspectorPunto.Where(e => e.Estado == clsEstadoEspera.ESPERANDO && e.AvisoAt == null && e.ActivadaAt <= activadasHasta).OrderBy(e => e.ActivadaAt).Take(limite).ToListAsync(ct);

    public void Agregar(clsEsperaInspectorPunto espera) => contexto.EsperasInspectorPunto.Add(espera);
}
