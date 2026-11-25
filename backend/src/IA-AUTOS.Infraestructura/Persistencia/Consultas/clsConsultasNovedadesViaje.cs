using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsRepositorioNovedadesViaje(clsAppDbContext contexto) : INovedadViajeRepository
{
    public void Agregar(clsNovedadViaje novedad) => contexto.NovedadesViaje.Add(novedad);

    public Task<clsNovedadViaje?> ObtenerPorEnvioAsync(Guid viajeId, Guid idEnvio, CancellationToken ct = default) =>
        contexto.NovedadesViaje.AsNoTracking().FirstOrDefaultAsync(n => n.ViajeId == viajeId && n.IdEnvio == idEnvio, ct);
}

/// <summary>Las novedades del viaje con el nombre y la urgencia de su tipo, quien las reporto, la parada y su foto.</summary>
internal sealed class clsConsultasNovedadesViaje(clsAppDbContext contexto) : IConsultasNovedadesViaje
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<NovedadViajeDto>>> DeViajesAsync(IReadOnlyCollection<Guid> viajeIds, CancellationToken ct = default)
    {
        if (viajeIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<NovedadViajeDto>>();
        }

        var filas = await Filas(contexto.NovedadesViaje.AsNoTracking().Where(n => viajeIds.Contains(n.ViajeId))).ToListAsync(ct);
        return filas.GroupBy(f => f.Novedad.ViajeId).ToDictionary(g => g.Key, g => (IReadOnlyList<NovedadViajeDto>)g.Select(ADto).ToList());
    }

    public async Task<NovedadViajeDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var fila = (await Filas(contexto.NovedadesViaje.AsNoTracking().Where(n => n.Id == id)).ToListAsync(ct)).FirstOrDefault();
        return fila is null ? null : ADto(fila);
    }

    private sealed record Fila(clsNovedadViaje Novedad, string TipoNombre, string Urgencia, string? Usuario, string? Lugar);

    private IQueryable<Fila> Filas(IQueryable<clsNovedadViaje> novedades) =>
        from n in novedades
        join t in contexto.TiposNovedadViaje.AsNoTracking() on n.TipoCodigo equals t.Codigo
        join u in contexto.Usuarios.AsNoTracking() on n.UsuarioId equals u.Id into usuarios
        from u in usuarios.DefaultIfEmpty()
        join l in contexto.Lugares.AsNoTracking() on n.LugarId equals l.Id into lugares
        from l in lugares.DefaultIfEmpty()
        orderby n.Fecha, n.CreatedAt
        select new Fila(n, t.Nombre, t.Urgencia, u.Nombre, l.Nombre);

    private static NovedadViajeDto ADto(Fila f)
    {
        var n = f.Novedad;
        var foto = n.FotoStoragePath is null
            ? null
            : new FotoNovedadViajeDto(
                n.FotoSelloFecha!.Value, n.FotoSelloLat!.Value, n.FotoSelloLng!.Value, clsUrlsArchivos.ParaStoragePath(n.FotoStoragePath), null, n.FotoHashSha256, n.FotoPrecision, n.FotoDispositivoId,
                n.FotoSelloLat == 0 && n.FotoSelloLng == 0, n.FotoStoragePath);
        return new NovedadViajeDto(n.Id, n.TipoCodigo, f.TipoNombre, f.Urgencia, n.Rol, n.Proceso, n.LugarId, f.Lugar, n.Texto, f.Usuario, n.Fecha, n.Lat, n.Lng, n.SinUbicacion, foto, n.CreatedAt);
    }
}
