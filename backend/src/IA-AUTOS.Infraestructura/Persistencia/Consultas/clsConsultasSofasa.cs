using IAAutos.Aplicacion.Sofasa;
using IAAutos.Aplicacion.Sofasa.DTOSofasa;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsConsultasSofasa(clsAppDbContext contexto) : IConsultasSofasa
{
    public async Task<IReadOnlyList<OrdenSofasaDto>> ListarAsync(FiltrosOrdenesSofasa f, int limite, CancellationToken ct = default)
    {
        var consulta = contexto.OrdenesSofasa.AsNoTracking().AsQueryable();
        if (f.Vin is not null)
        {
            consulta = consulta.Where(o => o.Vin != null && o.Vin.Contains(f.Vin));
        }

        if (f.Estado is not null)
        {
            consulta = consulta.Where(o => o.StatusCode == f.Estado);
        }

        if (f.Ciudad is not null)
        {
            consulta = consulta.Where(o => o.DestinationCity != null && o.DestinationCity.Contains(f.Ciudad));
        }

        if (f.Desde is not null)
        {
            consulta = consulta.Where(o => o.DatetimeEvent >= f.Desde);
        }

        if (f.Hasta is not null)
        {
            consulta = consulta.Where(o => o.DatetimeEvent <= f.Hasta);
        }

        return await consulta
            .OrderBy(o => o.DatetimeEvent == null)
            .ThenByDescending(o => o.DatetimeEvent)
            .Take(limite)
            .Select(o => new OrdenSofasaDto(
                o.Id, o.SourceId, o.StatusCode, o.DatetimeEvent, o.OrdenNumber, o.OrdenSap, o.OriginCedi, o.Vin, o.Make,
                o.ModelDescription, o.DealerCode, o.DestinationCity, o.DestinationAddress, o.LoadingDate, o.Appointment,
                o.LoadingDock, o.Weight, o.VehiculoId, o.SyncedAt, o.CreatedAt, o.UpdatedAt))
            .ToListAsync(ct);
    }
}
