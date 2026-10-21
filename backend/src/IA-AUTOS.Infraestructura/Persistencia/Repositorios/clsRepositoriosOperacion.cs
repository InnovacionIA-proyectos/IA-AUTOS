using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Catalogos;
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

    public Task<clsUsuario?> ObtenerPorCedulaAsync(string cedula, CancellationToken ct = default) =>
        string.IsNullOrEmpty(cedula) ? Task.FromResult<clsUsuario?>(null) : contexto.Usuarios.FirstOrDefaultAsync(u => u.Cedula == cedula, ct);

    public Task<clsUsuario?> ObtenerPorPortalIdAsync(long portalUsuarioId, CancellationToken ct = default) =>
        contexto.Usuarios.FirstOrDefaultAsync(u => u.PortalUsuarioId == portalUsuarioId, ct);

    public async Task<IReadOnlyDictionary<string, clsUsuario>> ObtenerPorCedulasAsync(IReadOnlyCollection<string> cedulas, CancellationToken ct = default)
    {
        var resultado = new Dictionary<string, clsUsuario>(StringComparer.OrdinalIgnoreCase);
        foreach (var lote in cedulas.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(1000))
        {
            foreach (var usuario in await contexto.Usuarios.AsNoTracking().Where(u => u.Cedula != null && lote.Contains(u.Cedula)).ToListAsync(ct))
            {
                resultado[usuario.Cedula!] = usuario;
            }
        }

        return resultado;
    }

    public async Task<IReadOnlyList<clsUsuario>> ListarAsync(Guid? clienteId = null, CancellationToken ct = default) =>
        await contexto.Usuarios.AsNoTracking()
            .Where(u => clienteId == null || u.ClienteId == clienteId)
            .OrderBy(u => u.Nombre)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<clsUsuario> Filas, int Total)> BuscarAsync(clsFiltroUsuarios filtro, int saltar, int tomar, CancellationToken ct = default)
    {
        var consulta = contexto.Usuarios.AsNoTracking().AsQueryable();
        if (filtro.ClienteId is { } clienteId)
        {
            consulta = consulta.Where(u => u.ClienteId == clienteId);
        }

        if (filtro.Rol is not null)
        {
            consulta = consulta.Where(u => u.Rol == filtro.Rol);
        }

        if (filtro.Activo is { } activo)
        {
            consulta = consulta.Where(u => u.Activo == activo);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Texto))
        {
            var texto = filtro.Texto.Trim();
            consulta = consulta.Where(u => u.Nombre.Contains(texto) || (u.Email != null && u.Email.Contains(texto)) || (u.Cedula != null && u.Cedula.Contains(texto)));
        }

        var total = await consulta.CountAsync(ct);
        var filas = await consulta.OrderBy(u => u.Nombre).ThenBy(u => u.Id).Skip(saltar).Take(tomar).ToListAsync(ct);
        return (filas, total);
    }

    public async Task<IReadOnlySet<string>> EmailsExistentesAsync(IReadOnlyCollection<string> emails, CancellationToken ct = default)
    {
        var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lote in emails.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(1000))
        {
            existentes.UnionWith(await contexto.Usuarios.AsNoTracking().Where(u => u.Email != null && lote.Contains(u.Email)).Select(u => u.Email!).ToListAsync(ct));
        }

        return existentes;
    }

    public async Task<IReadOnlySet<string>> CedulasExistentesAsync(IReadOnlyCollection<string> cedulas, CancellationToken ct = default)
    {
        var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lote in cedulas.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(1000))
        {
            existentes.UnionWith(await contexto.Usuarios.AsNoTracking().Where(u => u.Cedula != null && lote.Contains(u.Cedula)).Select(u => u.Cedula!).ToListAsync(ct));
        }

        return existentes;
    }

    public async Task<bool> TieneHistorialAsync(Guid id, CancellationToken ct = default) =>
        await contexto.Inspecciones.AnyAsync(i => i.UsuarioId == id, ct)
        || await contexto.Transferencias.AnyAsync(t => t.EntregaUsuarioId == id || t.RecibeUsuarioId == id, ct)
        || await contexto.Viajes.AnyAsync(v => v.ConductorId == id || v.InspectorId == id || v.CreadoPor == id, ct)
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
        var evidencias = await contexto.Evidencias.AsNoTracking()
            .Where(e => e.InspeccionId == inspeccionId && !e.EsConstancia && !e.EsSello)
            .Select(e => new { e.Id, e.VistaId, e.HallazgoId, e.SelloLat, e.SelloLng, e.SelloServidorAt })
            .ToListAsync(ct);
        var hallazgos = await contexto.Hallazgos.AsNoTracking().Where(h => h.InspeccionId == inspeccionId).Select(h => h.Id).ToListAsync(ct);

        // Una foto repetida reemplaza a la anterior de la misma vista (o del mismo hallazgo): lo que cuenta para la ubicacion es la ultima.
        // Las fotos anteriores quedan guardadas (son evidencia) pero ya no bloquean el cierre.
        var ultimas = evidencias
            .GroupBy(e => e.VistaId is { } vista ? $"v:{vista}" : e.HallazgoId is { } hallazgo ? $"h:{hallazgo}" : $"f:{e.Id}")
            .Select(g => g.OrderByDescending(e => e.SelloServidorAt).First())
            .ToList();
        var sinUbicacion = ultimas.Count(e => e.SelloLat == 0 && e.SelloLng == 0);
        var conFoto = evidencias.Where(e => e.HallazgoId is not null).Select(e => e.HallazgoId!.Value).ToHashSet();
        var vistas = evidencias.Where(e => e.VistaId is not null).Select(e => e.VistaId!.Value).Distinct().ToList();
        return new clsResumenCierre(evidencias.Count, sinUbicacion, hallazgos.Count, hallazgos.Count(h => !conFoto.Contains(h)), vistas);
    }

    public Task<bool> TieneLlegadaCerradaAsync(Guid etapaId, CancellationToken ct = default) =>
        contexto.Inspecciones.AnyAsync(i => i.EtapaId == etapaId && i.Momento == clsMomento.LLEGADA && i.Estado == clsEstadoInspeccion.CERRADA, ct);

    public Task<bool> TieneTransferenciaSinCierreAsync(Guid etapaId, CancellationToken ct = default) =>
        (from t in contexto.Transferencias
         join i in contexto.Inspecciones on t.InspeccionId equals i.Id
         where i.EtapaId == etapaId && t.TipoCierre == null
         select t.Id).AnyAsync(ct);

    public Task<clsEvidencia?> ObtenerEvidenciaAsync(Guid id, CancellationToken ct = default) =>
        contexto.Evidencias.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Guid?> ObtenerCorreccionIdAsync(Guid inspeccionId, CancellationToken ct = default) =>
        contexto.Inspecciones.AsNoTracking().Where(i => i.CorrigeInspeccionId == inspeccionId).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);

    public Task<bool> ExisteHallazgoEnInspeccionAsync(Guid hallazgoId, Guid inspeccionId, CancellationToken ct = default) =>
        contexto.Hallazgos.AnyAsync(h => h.Id == hallazgoId && h.InspeccionId == inspeccionId, ct);

    public async Task<clsZonaDano?> ObtenerZonaNumeradaAsync(int numero, string? lado, string? tipoVehiculo, CancellationToken ct = default)
    {
        var candidatas = await (from z in contexto.ZonasDano.AsNoTracking()
                                join c in contexto.Cuadrantes.AsNoTracking() on z.CuadranteId equals c.Id
                                where z.Activo && c.Activo && c.Numero == numero && z.Lado == lado
                                    && (c.TipoVehiculo == null || c.TipoVehiculo == tipoVehiculo)
                                select new { Zona = z, c.TipoVehiculo }).ToListAsync(ct);
        // El cuadrante propio del tipo de vehiculo gana sobre el generico.
        return candidatas.OrderBy(x => x.TipoVehiculo is null ? 1 : 0).Select(x => x.Zona).FirstOrDefault();
    }

    public Task<clsZonaDano?> ObtenerZonaProvisionalAsync(string codigo, CancellationToken ct = default) =>
        contexto.ZonasDano.AsNoTracking().Where(z => z.CuadranteId == null && z.Activo && z.Codigo == codigo).FirstOrDefaultAsync(ct);

    public Task<string?> ObtenerTipoVehiculoAsync(Guid inspeccionId, CancellationToken ct = default) =>
        (from i in contexto.Inspecciones.AsNoTracking()
         join e in contexto.Etapas on i.EtapaId equals e.Id
         join v in contexto.Vehiculos on e.VehiculoId equals v.Id
         where i.Id == inspeccionId
         select v.TipoVehiculo).FirstOrDefaultAsync(ct);

    public Task<bool> EsHallazgoPrevioDelVehiculoAsync(Guid hallazgoOrigenId, Guid inspeccionId, Guid zonaId, string claseDano, CancellationToken ct = default) =>
        (from actual in contexto.Inspecciones.AsNoTracking()
         join ea in contexto.Etapas on actual.EtapaId equals ea.Id
         join h in contexto.Hallazgos.AsNoTracking() on hallazgoOrigenId equals h.Id
         join ip in contexto.Inspecciones on h.InspeccionId equals ip.Id
         join ep in contexto.Etapas on ip.EtapaId equals ep.Id
         where actual.Id == inspeccionId && ip.Id != inspeccionId && ip.Estado == clsEstadoInspeccion.CERRADA && ep.VehiculoId == ea.VehiculoId && h.ZonaId == zonaId && h.ClaseDano == claseDano
         select h.Id).AnyAsync(ct);

    public Task<clsHallazgo?> ObtenerHallazgoAsync(Guid id, CancellationToken ct = default) => contexto.Hallazgos.FirstOrDefaultAsync(h => h.Id == id, ct);

    public async Task<IReadOnlyList<clsHallazgo>> ObtenerHallazgosDeInspeccionAsync(Guid inspeccionId, CancellationToken ct = default) =>
        await contexto.Hallazgos.Where(h => h.InspeccionId == inspeccionId).ToListAsync(ct);

    public async Task<IReadOnlySet<Guid>> HallazgosConFotoAsync(IReadOnlyCollection<Guid> hallazgoIds, CancellationToken ct = default) =>
        (await contexto.Evidencias.AsNoTracking()
            .Where(e => e.HallazgoId != null && hallazgoIds.Contains(e.HallazgoId.Value) && !e.EsSello && !e.EsConstancia)
            .Select(e => e.HallazgoId!.Value).Distinct().ToListAsync(ct)).ToHashSet();

    public void Agregar(clsInspeccion inspeccion) => contexto.Inspecciones.Add(inspeccion);

    public void Agregar(clsHallazgo hallazgo) => contexto.Hallazgos.Add(hallazgo);

    public void Agregar(clsEvidencia evidencia) => contexto.Evidencias.Add(evidencia);

    public void Agregar(clsHallazgoSeguimiento seguimiento) => contexto.HallazgosSeguimiento.Add(seguimiento);
}

internal sealed class clsTransferenciaCustodiaRepository(clsAppDbContext contexto) : ITransferenciaCustodiaRepository
{
    public Task<clsTransferenciaCustodia?> ObtenerPorInspeccionAsync(Guid inspeccionId, CancellationToken ct = default) =>
        contexto.Transferencias.FirstOrDefaultAsync(t => t.InspeccionId == inspeccionId, ct);

    public void Agregar(clsTransferenciaCustodia transferencia) => contexto.Transferencias.Add(transferencia);
}

internal sealed class clsOrdenSofasaRepository(clsAppDbContext contexto) : IOrdenSofasaRepository
{
    public Task<clsOrdenSofasa?> ObtenerPorSourceIdAsync(long sourceId, CancellationToken ct = default) =>
        contexto.OrdenesSofasa.FirstOrDefaultAsync(o => o.SourceId == sourceId, ct);

    public async Task<IReadOnlyList<clsOrdenSofasa>> ObtenerSinViajePorVinesAsync(IReadOnlyCollection<string> vines, CancellationToken ct = default)
    {
        var resultado = new List<clsOrdenSofasa>();
        foreach (var lote in vines.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(1000))
        {
            resultado.AddRange(await contexto.OrdenesSofasa.Where(o => o.ViajeId == null && o.Vin != null && lote.Contains(o.Vin)).ToListAsync(ct));
        }

        return resultado;
    }

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
