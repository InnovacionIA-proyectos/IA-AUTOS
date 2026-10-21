using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Inspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsConsultasInspecciones(clsAppDbContext contexto) : IConsultasInspecciones
{
    public async Task<HallazgoDto?> ObtenerHallazgoAsync(Guid hallazgoId, CancellationToken ct = default)
    {
        var hallazgo = await contexto.Hallazgos.AsNoTracking().FirstOrDefaultAsync(h => h.Id == hallazgoId, ct);
        return hallazgo is null ? null : (await new clsDescriptorHallazgos(contexto).DescribirAsync([hallazgo], ct))[0];
    }

    public async Task<IReadOnlyList<SeguimientoDto>> ObtenerHistorialAsync(Guid hallazgoId, CancellationToken ct = default)
    {
        var filas = await (from s in contexto.HallazgosSeguimiento.AsNoTracking()
                           join u in contexto.Usuarios on s.UsuarioId equals u.Id into usuarios
                           from u in usuarios.DefaultIfEmpty()
                           join e in contexto.Evidencias on s.EvidenciaSelloId equals e.Id into sellos
                           from e in sellos.DefaultIfEmpty()
                           where s.HallazgoId == hallazgoId
                           orderby s.CreatedAt, s.Id
                           select new { Fila = s, UsuarioNombre = (string?)u.Nombre, Sello = e }).ToListAsync(ct);
        return filas.Select(f => new SeguimientoDto(
            f.Fila.Id, f.Fila.Accion, f.Fila.EstadoAnterior, f.Fila.EstadoNuevo, f.Fila.UsuarioId, f.UsuarioNombre, f.Fila.Nota,
            f.Sello is null ? null : clsMapeadorInspecciones.AEvidencia(f.Sello),
            f.Fila.Detalle is null ? null : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(f.Fila.Detalle),
            f.Fila.CreatedAt)).ToList();
    }

    public Task<bool> EsDeClienteAsync(Guid inspeccionId, Guid clienteId, CancellationToken ct = default) =>
        (from i in contexto.Inspecciones.AsNoTracking()
         join e in contexto.Etapas on i.EtapaId equals e.Id
         join v in contexto.Vehiculos on e.VehiculoId equals v.Id
         where i.Id == inspeccionId && v.ClienteId == clienteId
         select i.Id).AnyAsync(ct);

    public async Task<IReadOnlyList<InspeccionListadoDto>> ListarAsync(FiltrosInspecciones f, Guid? soloDeUsuario, Guid? soloDeCliente, int limite, CancellationToken ct = default)
    {
        var consulta = from i in contexto.Inspecciones.AsNoTracking()
                       join e in contexto.Etapas on i.EtapaId equals e.Id
                       join v in contexto.Vehiculos on e.VehiculoId equals v.Id
                       join p in contexto.EtapasCatalogo on e.EtapaCatalogoId equals p.Id
                       join vj in contexto.Viajes on i.ViajeId equals vj.Id
                       join c in contexto.Usuarios on vj.ConductorId equals c.Id into conductores
                       from c in conductores.DefaultIfEmpty()
                       where (f.Vin == null || v.Vin == f.Vin)
                          && (soloDeCliente == null || v.ClienteId == soloDeCliente)
                          && (f.UsuarioId == null || i.UsuarioId == f.UsuarioId)
                          && (f.ViajeId == null || i.ViajeId == f.ViajeId)
                          && (f.Momento == null || i.Momento == f.Momento)
                          && (f.Estado == null || i.Estado == f.Estado)
                          && (f.Proceso == null || p.Codigo == f.Proceso)
                          && (f.Desde == null || i.Fecha >= f.Desde)
                          && (f.Hasta == null || i.Fecha <= f.Hasta)
                       select new { Inspeccion = i, v.Vin, Proceso = p.Codigo, vj.Placa, vj.Trailer, Conductor = (string?)c.Nombre };
        if (soloDeUsuario is { } usuarioId)
        {
            var viajesPropios = clsAlcanceViajes.Viajes(contexto, usuarioId);
            consulta = consulta.Where(x => viajesPropios.Contains(x.Inspeccion.ViajeId));
        }

        // La placa, el trailer y el conductor son los del viaje; lo capturado a mano antes de los viajes tiene prioridad.
        return await consulta
            .OrderByDescending(x => x.Inspeccion.Fecha)
            .Take(limite)
            .Select(x => new InspeccionListadoDto(
                x.Inspeccion.Id, x.Inspeccion.EtapaId, x.Inspeccion.ViajeId, x.Inspeccion.UsuarioId, x.Inspeccion.Momento, x.Inspeccion.Fecha,
                x.Inspeccion.Lat, x.Inspeccion.Lng, x.Inspeccion.Estado,
                x.Inspeccion.ConductorNombre ?? x.Conductor, x.Inspeccion.Placa ?? x.Placa, x.Inspeccion.Trailer ?? x.Trailer, x.Inspeccion.Notas,
                x.Inspeccion.CreatedAt, x.Inspeccion.UpdatedAt, x.Vin, x.Proceso,
                x.Inspeccion.IniciadaAt, x.Inspeccion.CerradaAt, x.Inspeccion.Version, x.Inspeccion.CorrigeInspeccionId,
                x.Inspeccion.Momento == clsMomento.SALIDA ? "Inspección de salida" : x.Inspeccion.Momento == clsMomento.LLEGADA ? "Inspección de llegada" : x.Inspeccion.Momento))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<HallazgoPrevioDto>> ObtenerHallazgosPreviosAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        var actual = await (from i in contexto.Inspecciones.AsNoTracking()
                            join e in contexto.Etapas on i.EtapaId equals e.Id
                            where i.Id == inspeccionId
                            select new { i.IniciadaAt, i.CorrigeInspeccionId, e.VehiculoId }).FirstOrDefaultAsync(ct);
        if (actual is null)
        {
            return [];
        }

        // Cerradas antes de que empezara esta, del mismo vehiculo; sin la version que esta corrige ni las que ya reemplazo una correccion cerrada.
        var previas = await (from i in contexto.Inspecciones.AsNoTracking()
                             join e in contexto.Etapas on i.EtapaId equals e.Id
                             join p in contexto.EtapasCatalogo on e.EtapaCatalogoId equals p.Id
                             join vj in contexto.Viajes on i.ViajeId equals vj.Id
                             where e.VehiculoId == actual.VehiculoId
                                && i.Id != inspeccionId
                                && i.Id != actual.CorrigeInspeccionId
                                && i.Estado == clsEstadoInspeccion.CERRADA
                                && i.CerradaAt < actual.IniciadaAt
                                && !contexto.Inspecciones.Any(c => c.CorrigeInspeccionId == i.Id && c.Estado == clsEstadoInspeccion.CERRADA)
                             select new { i.Id, i.Momento, CerradaAt = i.CerradaAt!.Value, i.ViajeId, vj.CodigoMtrack, Proceso = p.Codigo }).ToListAsync(ct);
        if (previas.Count == 0)
        {
            return [];
        }

        var ids = previas.Select(x => x.Id).ToList();
        var hallazgos = await contexto.Hallazgos.AsNoTracking().Where(h => ids.Contains(h.InspeccionId)).ToListAsync(ct);
        var dtos = (await new clsDescriptorHallazgos(contexto).DescribirAsync(hallazgos, ct)).ToDictionary(h => h.Id);
        var hallazgoIds = hallazgos.Select(h => h.Id).ToList();
        var fotos = (await contexto.Evidencias.AsNoTracking().Where(e => e.HallazgoId != null && hallazgoIds.Contains(e.HallazgoId.Value))
            .OrderBy(e => e.CreatedAt).ToListAsync(ct)).ToLookup(e => e.HallazgoId!.Value);
        var origenes = previas.ToDictionary(x => x.Id);

        return hallazgos
            .Select(h => (Hallazgo: h, Origen: origenes[h.InspeccionId]))
            .OrderByDescending(x => x.Origen.CerradaAt).ThenByDescending(x => x.Hallazgo.CreatedAt)
            .Select(x => new HallazgoPrevioDto(
                dtos[x.Hallazgo.Id], x.Origen.ViajeId, x.Origen.CodigoMtrack, x.Origen.Proceso, x.Origen.Momento, x.Origen.CerradaAt,
                fotos[x.Hallazgo.Id].Select(clsMapeadorInspecciones.AEvidencia).ToList()))
            .ToList();
    }

    public async Task<InformeDetalleDto?> ObtenerInformeAsync(Guid id, CancellationToken ct = default)
    {
        var fila = await (from i in contexto.Inspecciones.AsNoTracking()
                          join e in contexto.Etapas on i.EtapaId equals e.Id
                          join v in contexto.Vehiculos on e.VehiculoId equals v.Id
                          join p in contexto.EtapasCatalogo on e.EtapaCatalogoId equals p.Id
                          join l in contexto.PuntosCustodia on e.PuntoCustodiaId equals l.Id into lugares
                          from l in lugares.DefaultIfEmpty()
                          join u in contexto.Usuarios on i.UsuarioId equals u.Id into usuarios
                          from u in usuarios.DefaultIfEmpty()
                          where i.Id == id
                          select new
                          {
                              Inspeccion = i,
                              v.Vin,
                              v.Marca,
                              v.Modelo,
                              Proceso = p.Codigo,
                              Lugar = (string?)l.Nombre,
                              e.RequiereInspeccion,
                              InspectorNombre = (string?)u.Nombre,
                          }).FirstOrDefaultAsync(ct);
        if (fila is null)
        {
            return null;
        }

        var insp = fila.Inspeccion;
        var viaje = (await clsDatosDeViaje.CargarAsync(contexto, [insp.ViajeId], ct)).GetValueOrDefault(insp.ViajeId);

        var hallazgos = await contexto.Hallazgos.AsNoTracking().Where(h => h.InspeccionId == id).OrderBy(h => h.CreatedAt).ToListAsync(ct);
        var hallazgosDto = await new clsDescriptorHallazgos(contexto).DescribirAsync(hallazgos, ct);
        var evidencias = await contexto.Evidencias.AsNoTracking().Where(e => e.InspeccionId == id).OrderBy(e => e.CreatedAt).ToListAsync(ct);
        var transferencia = await (from t in contexto.Transferencias.AsNoTracking()
                                   join ue in contexto.Usuarios on t.EntregaUsuarioId equals ue.Id into entregas
                                   from ue in entregas.DefaultIfEmpty()
                                   join ur in contexto.Usuarios on t.RecibeUsuarioId equals ur.Id into recibes
                                   from ur in recibes.DefaultIfEmpty()
                                   join ut in contexto.Usuarios on t.TestigoUsuarioId equals ut.Id into testigos
                                   from ut in testigos.DefaultIfEmpty()
                                   where t.InspeccionId == id
                                   select new { Transferencia = t, EntregaNombre = (string?)ue.Nombre, RecibeNombre = (string?)ur.Nombre, TestigoNombre = (string?)ut.Nombre })
            .FirstOrDefaultAsync(ct);

        return new InformeDetalleDto(
            insp.Id, insp.EtapaId, insp.ViajeId, insp.UsuarioId, insp.Momento, insp.Fecha, insp.Lat, insp.Lng, insp.Estado,
            insp.ConductorNombre ?? viaje?.Conductor,
            insp.Placa ?? viaje?.Placa,
            insp.Trailer ?? viaje?.Trailer,
            insp.Notas,
            insp.CreatedAt,
            insp.UpdatedAt,
            fila.Vin,
            fila.Marca,
            fila.Modelo,
            fila.Proceso,
            fila.Lugar,
            fila.RequiereInspeccion,
            fila.InspectorNombre,
            insp.Placa ?? viaje?.Placa,
            viaje?.CodigoMtrack,
            viaje?.Unidad,
            hallazgosDto,
            evidencias.Select(clsMapeadorInspecciones.AEvidencia).ToList(),
            transferencia is null
                ? null
                : new TransferenciaDetalleDto(
                    transferencia.Transferencia.Id,
                    transferencia.Transferencia.InspeccionId,
                    transferencia.Transferencia.EntregaUsuarioId,
                    transferencia.Transferencia.RecibeUsuarioId,
                    transferencia.Transferencia.EstaCompleta,
                    transferencia.Transferencia.FirmadaAt,
                    transferencia.Transferencia.CreatedAt,
                    transferencia.Transferencia.UpdatedAt,
                    transferencia.EntregaNombre,
                    transferencia.RecibeNombre,
                    transferencia.Transferencia.FirmaEntrega,
                    transferencia.Transferencia.FirmaRecibe,
                    transferencia.Transferencia.TipoCierre,
                    transferencia.Transferencia.FirmaEntregaAt,
                    transferencia.Transferencia.FirmaRecibeAt,
                    transferencia.Transferencia.FirmaEntregaIp,
                    transferencia.Transferencia.FirmaRecibeIp,
                    transferencia.Transferencia.ReservasTexto,
                    transferencia.Transferencia.NoFirmaParte,
                    transferencia.Transferencia.MotivoNoFirma,
                    transferencia.Transferencia.TestigoUsuarioId,
                    transferencia.TestigoNombre,
                    transferencia.Transferencia.EvidenciaConstanciaId),
            insp.IniciadaAt,
            insp.CerradaAt,
            insp.Version,
            insp.CorrigeInspeccionId,
            clsMomento.Nombre(insp.Momento));
    }
}
