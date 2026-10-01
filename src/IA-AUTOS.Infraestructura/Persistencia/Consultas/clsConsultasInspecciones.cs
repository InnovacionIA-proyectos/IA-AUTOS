using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsConsultasInspecciones(clsAppDbContext contexto) : IConsultasInspecciones
{
    public async Task<IReadOnlyList<InspeccionListadoDto>> ListarAsync(FiltrosInspecciones f, int limite, CancellationToken ct = default) =>
        await (from i in contexto.Inspecciones.AsNoTracking()
               join e in contexto.Etapas on i.EtapaId equals e.Id
               join v in contexto.Vehiculos on e.VehiculoId equals v.Id
               join p in contexto.PuntosCustodia on e.PuntoCustodiaId equals p.Id
               where (f.Vin == null || v.Vin == f.Vin)
                  && (f.UsuarioId == null || i.UsuarioId == f.UsuarioId)
                  && (f.Momento == null || i.Momento == f.Momento)
                  && (f.Estado == null || i.Estado == f.Estado)
                  && (f.Proceso == null || p.Nombre == f.Proceso)
                  && (f.Desde == null || i.Fecha >= f.Desde)
                  && (f.Hasta == null || i.Fecha <= f.Hasta)
               orderby i.Fecha descending
               select new InspeccionListadoDto(
                   i.Id, i.EtapaId, i.UsuarioId, i.Momento, i.Fecha, i.Lat, i.Lng, i.Estado, i.ConductorNombre, i.Placa, i.Trailer, i.Notas,
                   i.CreatedAt, i.UpdatedAt, v.Vin, p.Nombre))
            .Take(limite)
            .ToListAsync(ct);

    public async Task<InformeDetalleDto?> ObtenerInformeAsync(Guid id, CancellationToken ct = default)
    {
        var fila = await (from i in contexto.Inspecciones.AsNoTracking()
                          join e in contexto.Etapas on i.EtapaId equals e.Id
                          join v in contexto.Vehiculos on e.VehiculoId equals v.Id
                          join p in contexto.PuntosCustodia on e.PuntoCustodiaId equals p.Id
                          join u in contexto.Usuarios on i.UsuarioId equals u.Id into usuarios
                          from u in usuarios.DefaultIfEmpty()
                          where i.Id == id
                          select new
                          {
                              Inspeccion = i,
                              VehiculoId = v.Id,
                              v.Vin,
                              v.Marca,
                              v.Modelo,
                              Proceso = p.Nombre,
                              e.RequiereInspeccion,
                              InspectorNombre = (string?)u.Nombre,
                          }).FirstOrDefaultAsync(ct);
        if (fila is null)
        {
            return null;
        }

        // Ultima asignacion del vehiculo: ninera (placa) y conductor asignados, como respaldo de lo capturado en la app.
        var asignacion = await contexto.Asignaciones.AsNoTracking()
            .Where(a => a.VehiculoId == fila.VehiculoId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.Ninera, a.ConductorId })
            .FirstOrDefaultAsync(ct);
        string? conductorAsignado = null;
        if (asignacion?.ConductorId is { } conductorId)
        {
            conductorAsignado = await contexto.Usuarios.Where(u => u.Id == conductorId).Select(u => u.Nombre).FirstOrDefaultAsync(ct);
        }

        var hallazgos = await contexto.Hallazgos.AsNoTracking().Where(h => h.InspeccionId == id).OrderBy(h => h.CreatedAt).ToListAsync(ct);
        var evidencias = await contexto.Evidencias.AsNoTracking().Where(e => e.InspeccionId == id).OrderBy(e => e.CreatedAt).ToListAsync(ct);
        var transferencia = await (from t in contexto.Transferencias.AsNoTracking()
                                   join ue in contexto.Usuarios on t.EntregaUsuarioId equals ue.Id into entregas
                                   from ue in entregas.DefaultIfEmpty()
                                   join ur in contexto.Usuarios on t.RecibeUsuarioId equals ur.Id into recibes
                                   from ur in recibes.DefaultIfEmpty()
                                   where t.InspeccionId == id
                                   select new { Transferencia = t, EntregaNombre = (string?)ue.Nombre, RecibeNombre = (string?)ur.Nombre })
            .FirstOrDefaultAsync(ct);

        var insp = fila.Inspeccion;
        return new InformeDetalleDto(
            insp.Id, insp.EtapaId, insp.UsuarioId, insp.Momento, insp.Fecha, insp.Lat, insp.Lng, insp.Estado,
            insp.ConductorNombre ?? conductorAsignado,
            insp.Placa,
            insp.Trailer,
            insp.Notas,
            insp.CreatedAt,
            insp.UpdatedAt,
            fila.Vin,
            fila.Marca,
            fila.Modelo,
            fila.Proceso,
            fila.RequiereInspeccion,
            fila.InspectorNombre,
            insp.Placa ?? asignacion?.Ninera,
            hallazgos.Select(clsMapeadorInspecciones.AHallazgo).ToList(),
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
                    transferencia.Transferencia.FirmaRecibe));
    }
}
