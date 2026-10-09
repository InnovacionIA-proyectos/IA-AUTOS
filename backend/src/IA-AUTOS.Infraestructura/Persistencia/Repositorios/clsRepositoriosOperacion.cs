using IAAutos.Dominio.Asignaciones;
using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Sofasa;
using IAAutos.Dominio.Usuarios;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Repositorios;

internal sealed class clsUsuarioRepository(clsAppDbContext contexto) : IUsuarioRepository
{
    public Task<clsUsuario?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        contexto.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<clsUsuario?> ObtenerPorEmailAsync(string email, CancellationToken ct = default)
    {
        // La colacion de la base (Modern_Spanish_CI_AS) hace la comparacion insensible a mayusculas.
        var normalizado = email.Trim().ToLowerInvariant();
        return contexto.Usuarios.FirstOrDefaultAsync(u => u.Email == normalizado, ct);
    }

    public Task<clsUsuario?> ObtenerPorPortalIdAsync(long portalUsuarioId, CancellationToken ct = default) =>
        contexto.Usuarios.FirstOrDefaultAsync(u => u.PortalUsuarioId == portalUsuarioId, ct);

    public async Task<IReadOnlyList<clsUsuario>> ListarAsync(CancellationToken ct = default) =>
        await contexto.Usuarios.AsNoTracking().OrderBy(u => u.Nombre).ToListAsync(ct);

    public async Task<bool> TieneHistorialAsync(Guid id, CancellationToken ct = default) =>
        await contexto.Inspecciones.AnyAsync(i => i.UsuarioId == id, ct)
        || await contexto.Transferencias.AnyAsync(t => t.EntregaUsuarioId == id || t.RecibeUsuarioId == id, ct)
        || await contexto.Asignaciones.AnyAsync(a => a.AsignadoPor == id, ct)
        || await contexto.AuditLog.AnyAsync(a => a.UsuarioId == id, ct);

    public void Agregar(clsUsuario usuario) => contexto.Usuarios.Add(usuario);

    public void Eliminar(clsUsuario usuario) => contexto.Usuarios.Remove(usuario);
}

internal sealed class clsInspeccionRepository(clsAppDbContext contexto) : IInspeccionRepository
{
    public Task<clsInspeccion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        contexto.Inspecciones.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<clsResumenCierre> ObtenerResumenCierreAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        var evidencias = contexto.Evidencias.Where(e => e.InspeccionId == inspeccionId);
        var totalEvidencias = await evidencias.CountAsync(ct);
        var sinGeo = await evidencias.CountAsync(e => e.SelloLat == null || e.SelloLng == null, ct);
        var totalHallazgos = await contexto.Hallazgos.CountAsync(h => h.InspeccionId == inspeccionId, ct);
        return new clsResumenCierre(totalEvidencias, sinGeo, totalHallazgos);
    }

    public Task<bool> ExisteHallazgoEnInspeccionAsync(Guid hallazgoId, Guid inspeccionId, CancellationToken ct = default) =>
        contexto.Hallazgos.AnyAsync(h => h.Id == hallazgoId && h.InspeccionId == inspeccionId, ct);

    public void Agregar(clsInspeccion inspeccion) => contexto.Inspecciones.Add(inspeccion);

    public void Agregar(clsHallazgo hallazgo) => contexto.Hallazgos.Add(hallazgo);

    public void Agregar(clsEvidencia evidencia) => contexto.Evidencias.Add(evidencia);
}

internal sealed class clsTransferenciaCustodiaRepository(clsAppDbContext contexto) : ITransferenciaCustodiaRepository
{
    public Task<clsTransferenciaCustodia?> ObtenerPorInspeccionAsync(Guid inspeccionId, CancellationToken ct = default) =>
        contexto.Transferencias.FirstOrDefaultAsync(t => t.InspeccionId == inspeccionId, ct);

    public void Agregar(clsTransferenciaCustodia transferencia) => contexto.Transferencias.Add(transferencia);
}

internal sealed class clsAsignacionRepository(clsAppDbContext contexto) : IAsignacionRepository
{
    public void Agregar(clsAsignacion asignacion) => contexto.Asignaciones.Add(asignacion);
}

internal sealed class clsOrdenSofasaRepository(clsAppDbContext contexto) : IOrdenSofasaRepository
{
    public Task<clsOrdenSofasa?> ObtenerPorSourceIdAsync(long sourceId, CancellationToken ct = default) =>
        contexto.OrdenesSofasa.FirstOrDefaultAsync(o => o.SourceId == sourceId, ct);

    public void Agregar(clsOrdenSofasa orden) => contexto.OrdenesSofasa.Add(orden);
}

internal sealed class clsAuditoriaRepository(clsAppDbContext contexto) : IAuditoriaRepository
{
    public void Agregar(clsAuditLog registro) => contexto.AuditLog.Add(registro);
}

internal sealed class clsRefreshTokenRepository(clsAppDbContext contexto) : IRefreshTokenRepository
{
    public Task<clsRefreshToken?> ObtenerPorHashAsync(string tokenHash, CancellationToken ct = default) =>
        contexto.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public void Agregar(clsRefreshToken token) => contexto.RefreshTokens.Add(token);

    public Task<bool> FamiliaActivaAsync(Guid familiaId, DateTimeOffset ahora, CancellationToken ct = default) =>
        contexto.RefreshTokens.AsNoTracking().AnyAsync(t => t.FamiliaId == familiaId && t.RevocadoAt == null && t.ExpiraAt > ahora, ct);

    public Task RevocarFamiliaAsync(Guid familiaId, DateTimeOffset ahora, CancellationToken ct = default) =>
        contexto.RefreshTokens
            .Where(t => t.FamiliaId == familiaId && t.RevocadoAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevocadoAt, ahora).SetProperty(t => t.UpdatedAt, ahora), ct);
}
