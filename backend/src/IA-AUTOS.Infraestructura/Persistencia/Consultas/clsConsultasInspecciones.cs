using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Inspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsConsultasInspecciones(clsAppDbContext contexto) : IConsultasInspecciones
{
    public async Task<IReadOnlyList<InspeccionListadoDto>> ListarAsync(FiltrosInspecciones f, Guid? soloDeUsuario, int limite, CancellationToken ct = default)
    {
        var consulta = from i in contexto.Inspecciones.AsNoTracking()
                       join e in contexto.Etapas on i.EtapaId equals e.Id
                       join v in contexto.Vehiculos on e.VehiculoId equals v.Id
                       join p in contexto.EtapasCatalogo on e.EtapaCatalogoId equals p.Id
                       join vj in contexto.Viajes on i.ViajeId equals vj.Id
                       join c in contexto.Usuarios on vj.ConductorId equals c.Id into conductores
                       from c in conductores.DefaultIfEmpty()
                       where (f.Vin == null || v.Vin == f.Vin)
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
