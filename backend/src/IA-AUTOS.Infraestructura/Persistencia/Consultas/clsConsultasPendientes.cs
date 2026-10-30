using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>La lista de pendientes de un inspector: los cargues de hoy de sus puntos y las entregas de viajes en curso que llegan a ellos, con el estado de cada uno frente al reclamo.</summary>
internal sealed class clsConsultasPendientes(clsAppDbContext contexto) : IConsultasPendientes
{
    public async Task<IReadOnlyList<PendienteDto>> ListarAsync(Guid usuarioId, IReadOnlyCollection<Guid> lugares, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct = default)
    {
        var cargues = await (from v in contexto.Viajes.AsNoTracking()
                             join l in contexto.Lugares.AsNoTracking() on v.LugarCargueId equals l.Id
                             where (v.Estado == clsEstadoViaje.PROGRAMADO || v.Estado == clsEstadoViaje.EN_CURSO) && lugares.Contains(l.Id)
                                && v.FechaProgramada >= desde && v.FechaProgramada < hasta
                             select new { v.Id, v.CodigoMtrack, v.Placa, LugarId = l.Id, Lugar = l.Nombre, v.FechaProgramada }).ToListAsync(ct);

        // Las unidades activas que se entregan en un punto del inspector, de viajes en curso.
        var destinos = await (from vv in contexto.ViajeVehiculos.AsNoTracking()
                              join v in contexto.Viajes.AsNoTracking() on vv.ViajeId equals v.Id
                              join l in contexto.Lugares.AsNoTracking() on vv.LugarDestinoId equals l.Id
                              where vv.EstadoUnidad == clsEstadoUnidad.ACTIVA && v.Estado == clsEstadoViaje.EN_CURSO && lugares.Contains(l.Id)
                              select new { v.Id, v.CodigoMtrack, v.Placa, v.FechaProgramada, vv.VehiculoId, LugarId = l.Id, Lugar = l.Nombre }).ToListAsync(ct);
        var viajeIds = cargues.Select(c => c.Id).Concat(destinos.Select(d => d.Id)).Distinct().ToList();
        if (viajeIds.Count == 0)
        {
            return [];
        }

        // Las unidades que ya tienen cerrada la transferencia de su Descargue no estan pendientes de entrega.
        var etapasDescargue = await (from e in contexto.Etapas.AsNoTracking()
                                     join c in contexto.EtapasCatalogo.AsNoTracking() on e.EtapaCatalogoId equals c.Id
                                     where c.Codigo == clsCodigosEtapa.DESCARGUE && viajeIds.Contains(e.ViajeId)
                                     select new { e.Id, e.ViajeId, e.VehiculoId }).ToListAsync(ct);
        var etapaIds = etapasDescargue.Select(e => e.Id).ToList();
        var cerradas = (await (from t in contexto.Transferencias.AsNoTracking()
                               join i in contexto.Inspecciones.AsNoTracking() on t.InspeccionId equals i.Id
                               where t.TipoCierre != null && etapaIds.Contains(i.EtapaId)
                               select i.EtapaId).ToListAsync(ct)).ToHashSet();
        var entregadas = etapasDescargue.Where(e => cerradas.Contains(e.Id)).Select(e => (e.ViajeId, e.VehiculoId)).ToHashSet();

        var activos = (await contexto.ViajeVehiculos.AsNoTracking()
            .Where(vv => viajeIds.Contains(vv.ViajeId) && vv.EstadoUnidad == clsEstadoUnidad.ACTIVA)
            .GroupBy(vv => vv.ViajeId).Select(g => new { ViajeId = g.Key, Total = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.ViajeId, x => x.Total);

        var reclamosActivos = await (from r in contexto.Reclamos.AsNoTracking()
                                     join u in contexto.Usuarios.AsNoTracking() on r.InspectorId equals u.Id
                                     where r.LiberadoAt == null && viajeIds.Contains(r.ViajeId)
                                     select new { r.ViajeId, r.Proceso, r.InspectorId, u.Nombre, r.ReclamadoAt }).ToListAsync(ct);

        PendienteDto Armar(Guid viajeId, string? codigo, string? placa, string proceso, Guid lugarId, string lugar, DateTimeOffset? fecha, int unidades)
        {
            var reclamo = reclamosActivos.FirstOrDefault(r => r.ViajeId == viajeId && r.Proceso == proceso);
            var estado = reclamo is null ? clsEstadoPendiente.DISPONIBLE : reclamo.InspectorId == usuarioId ? clsEstadoPendiente.MIO : clsEstadoPendiente.EN_REVISION;
            return new PendienteDto(viajeId, codigo, placa, proceso, lugarId, lugar, fecha, estado, reclamo?.InspectorId, reclamo?.Nombre, reclamo?.ReclamadoAt, unidades);
        }

        var lista = cargues.Select(c => Armar(c.Id, c.CodigoMtrack, c.Placa, clsProcesoReclamo.CARGUE, c.LugarId, c.Lugar, c.FechaProgramada, activos.GetValueOrDefault(c.Id))).ToList();
        lista.AddRange(destinos
            .Where(d => !entregadas.Contains((d.Id, d.VehiculoId)))
            .GroupBy(d => new { d.Id, d.CodigoMtrack, d.Placa, d.FechaProgramada, d.LugarId, d.Lugar })
            .Select(g => Armar(g.Key.Id, g.Key.CodigoMtrack, g.Key.Placa, clsProcesoReclamo.ENTREGA, g.Key.LugarId, g.Key.Lugar, g.Key.FechaProgramada, g.Count())));
        return lista
            .OrderBy(p => p.Proceso, StringComparer.Ordinal)
            .ThenBy(p => p.FechaProgramada)
            .ThenBy(p => p.Placa, StringComparer.Ordinal)
            .ToList();
    }
}
