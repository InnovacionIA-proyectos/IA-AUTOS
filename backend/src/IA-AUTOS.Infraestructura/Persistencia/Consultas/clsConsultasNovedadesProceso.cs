using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Inspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsNovedadProcesoRepository(clsAppDbContext contexto) : INovedadProcesoRepository
{
    public void Agregar(clsNovedadProceso novedad) => contexto.NovedadesProceso.Add(novedad);

    public Task<bool> ExisteCargueSinInspectorAsync(Guid viajeId, CancellationToken ct = default) =>
        contexto.NovedadesProceso.AnyAsync(n => n.ViajeId == viajeId && n.Tipo == clsTipoNovedadProceso.CARGUE_SIN_INSPECTOR, ct);
}

/// <summary>El motivo que guarda cada inspeccion (con el nombre del catalogo) y sus novedades de proceso, de la mas antigua a la mas reciente.</summary>
internal sealed class clsConsultasNovedadesProceso(clsAppDbContext contexto) : IConsultasNovedadesProceso
{
    public async Task<IReadOnlyDictionary<Guid, NovedadesDeInspeccionDto>> DeInspeccionesAsync(IReadOnlyCollection<Guid> inspeccionIds, CancellationToken ct = default)
    {
        if (inspeccionIds.Count == 0)
        {
            return new Dictionary<Guid, NovedadesDeInspeccionDto>();
        }

        var motivos = await (from i in contexto.Inspecciones.AsNoTracking()
                             join m in contexto.MotivosSinInspector.AsNoTracking() on i.MotivoSinInspector equals m.Codigo
                             where inspeccionIds.Contains(i.Id)
                             select new { i.Id, m.Codigo, m.Nombre, i.MotivoSinInspectorTexto }).ToListAsync(ct);
        var novedades = await (from n in contexto.NovedadesProceso.AsNoTracking()
                               join u in contexto.Usuarios.AsNoTracking() on n.UsuarioId equals u.Id into usuarios
                               from u in usuarios.DefaultIfEmpty()
                               join m in contexto.MotivosSinInspector.AsNoTracking() on n.MotivoCodigo equals m.Codigo into motivosDeNovedad
                               from m in motivosDeNovedad.DefaultIfEmpty()
                               join p in contexto.PermisosSinInspector.AsNoTracking() on n.PermisoId equals p.Id into permisos
                               from p in permisos.DefaultIfEmpty()
                               join pc in contexto.Usuarios.AsNoTracking() on p.ConductorId equals pc.Id into conductores
                               from pc in conductores.DefaultIfEmpty()
                               join pq in contexto.Usuarios.AsNoTracking() on p.ConcedidoPor equals pq.Id into concedentes
                               from pq in concedentes.DefaultIfEmpty()
                               where inspeccionIds.Contains(n.InspeccionId)
                               orderby n.Fecha, n.CreatedAt
                               select new
                               {
                                   n.InspeccionId, n.Tipo, n.MotivoCodigo, Motivo = (string?)m.Nombre, n.MotivoTexto, Usuario = (string?)u.Nombre, n.Fecha,
                                   TienePermiso = p != null, PedidoPor = (string?)pc.Nombre, PedidoAt = (DateTimeOffset?)p.CreatedAt, PedidoLat = p.Lat, PedidoLng = p.Lng,
                                   ConcedidoPorNombre = (string?)pq.Nombre, ConcedidoAt = p.ConcedidoAt, PorTiempo = p != null && p.PorTiempo,
                               }).ToListAsync(ct);

        var motivoPorInspeccion = motivos.ToDictionary(m => m.Id, m => new MotivoSinInspectorDto(m.Codigo, m.Nombre, m.MotivoSinInspectorTexto));
        var novedadesPorInspeccion = novedades.ToLookup(n => n.InspeccionId);
        return motivoPorInspeccion.Keys.Concat(novedadesPorInspeccion.Select(g => g.Key)).Distinct()
            .ToDictionary(
                id => id,
                id => new NovedadesDeInspeccionDto(
                    motivoPorInspeccion.GetValueOrDefault(id),
                    novedadesPorInspeccion[id].Select(n => new NovedadProcesoDto(
                        n.Tipo, n.MotivoCodigo, n.Motivo, n.MotivoTexto, n.Usuario, n.Fecha,
                        n.TienePermiso ? new PermisoDeLaNovedadDto(n.PedidoPor, n.PedidoAt!.Value, n.PedidoLat, n.PedidoLng, n.ConcedidoPorNombre, n.ConcedidoAt, n.PorTiempo) : null)).ToList()));
    }
}
