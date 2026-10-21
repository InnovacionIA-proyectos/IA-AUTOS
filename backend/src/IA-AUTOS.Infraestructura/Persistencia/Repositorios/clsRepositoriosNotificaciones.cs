using IAAutos.Dominio.Notificaciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Repositorios;

internal sealed class clsNotificacionRepository(clsAppDbContext contexto) : INotificacionRepository
{
    public Task<clsNotificacion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        contexto.Notificaciones.FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task<IReadOnlySet<string>> ClavesExistentesAsync(IReadOnlyCollection<string> claves, CancellationToken ct = default) =>
        (await contexto.Notificaciones.AsNoTracking().Where(n => claves.Contains(n.ClaveDedupe)).Select(n => n.ClaveDedupe).ToListAsync(ct)).ToHashSet();

    public Task<bool> NovedadRespondidaAsync(Guid hallazgoId, CancellationToken ct = default) =>
        contexto.Notificaciones.AsNoTracking().AnyAsync(
            n => n.HallazgoId == hallazgoId && n.Tipo == clsTipoNotificacion.NOVEDAD_CONDUCTOR && n.RespondidaAt != null, ct);

    public Task<int> ResponderNovedadAsync(Guid hallazgoId, string texto, string medio, Guid usuarioId, DateTimeOffset ahora, CancellationToken ct = default) =>
        contexto.Notificaciones
            .Where(n => n.HallazgoId == hallazgoId && n.Tipo == clsTipoNotificacion.NOVEDAD_CONDUCTOR && n.RespondidaAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(n => n.RespuestaTexto, texto)
                    .SetProperty(n => n.RespuestaMedio, medio)
                    .SetProperty(n => n.RespondidaPor, usuarioId)
                    .SetProperty(n => n.RespondidaAt, ahora),
                ct);

    public void Agregar(clsNotificacion notificacion) => contexto.Notificaciones.Add(notificacion);
}

internal sealed class clsDispositivoRepository(clsAppDbContext contexto) : IDispositivoRepository
{
    public Task<clsDispositivo?> ObtenerPorTokenAsync(string tokenFcm, CancellationToken ct = default) =>
        contexto.Dispositivos.FirstOrDefaultAsync(d => d.TokenFcm == tokenFcm, ct);

    public async Task<IReadOnlyList<clsDispositivo>> ObtenerActivosDeUsuariosAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default) =>
        await contexto.Dispositivos.Where(d => d.Activo && usuarioIds.Contains(d.UsuarioId)).ToListAsync(ct);

    public void Agregar(clsDispositivo dispositivo) => contexto.Dispositivos.Add(dispositivo);
}
