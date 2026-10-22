using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Sofasa;
using IAAutos.Aplicacion.Sofasa.DTOSofasa;
using IAAutos.Dominio.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsConsultasSofasa(clsAppDbContext contexto) : IConsultasSofasa
{
    public async Task<(IReadOnlyList<OrdenSofasaDto> Filas, int Total)> ListarAsync(
        FiltrosOrdenesSofasa f, Guid? soloDeUsuario, Guid? soloDeCliente, PaginaSolicitada pagina, OrdenSolicitado? orden, CancellationToken ct = default)
    {
        var vinExacto = f.Vin?.Length == clsVin.LONGITUD_ISO;
        var consulta = contexto.OrdenesSofasa.AsNoTracking().AsQueryable();
        if (soloDeCliente is { } clienteId)
        {
            consulta = consulta.Where(o => o.Vin != null && contexto.Vehiculos.Any(v => v.Vin == o.Vin && v.ClienteId == clienteId));
        }

        if (soloDeUsuario is { } usuarioId)
        {
            var viajesPropios = clsAlcanceViajes.Viajes(contexto, usuarioId);
            consulta = consulta.Where(o => o.ViajeId != null && viajesPropios.Contains(o.ViajeId.Value));
        }

        if (f.Vin is not null)
        {
            consulta = consulta.Where(o => o.Vin != null && (vinExacto ? o.Vin == f.Vin : o.Vin.Contains(f.Vin)));
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

        var total = await consulta.CountAsync(ct);
        var ordenada = clsOrdenamiento.De(consulta)
            .Campo("fecha", o => o.DatetimeEvent)
            .Campo("vin", o => o.Vin)
            .Campo("estado", o => o.StatusCode)
            .Campo("ciudad", o => o.DestinationCity)
            .Campo("orden", o => o.OrdenNumber)
            .Campo("marca", o => o.Make)
            .Campo("created_at", o => o.CreatedAt)
            .Aplicar(orden, q => q.OrderBy(o => o.DatetimeEvent == null).ThenByDescending(o => o.DatetimeEvent), q => q.ThenBy(o => o.Id));
        var filas = await ordenada
            .Skip(pagina.Desplazamiento)
            .Take(pagina.Tamano)
            .Select(o => new OrdenSofasaDto(
                o.Id, o.SourceId, o.StatusCode, o.DatetimeEvent, o.OrdenNumber, o.OrdenSap, o.OriginCedi, o.Vin, o.Make,
                o.ModelDescription, o.DealerCode, o.DestinationCity, o.DestinationAddress, o.LoadingDate, o.Appointment,
                o.LoadingDock, o.Weight, o.VehiculoId, o.CodigoMtrack, o.ViajeId, o.SyncedAt, o.CreatedAt, o.UpdatedAt))
            .ToListAsync(ct);
        return (filas, total);
    }
}
