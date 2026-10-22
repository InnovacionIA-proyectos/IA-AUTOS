using IAAutos.Aplicacion.Comun;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsOrigenAuditoria(clsAppDbContext contexto) : IOrigenAuditoria
{
    public async Task<Guid?> ClienteDelViajeAsync(Guid viajeId, CancellationToken ct = default) =>
        await (from vv in contexto.ViajeVehiculos.AsNoTracking()
               join v in contexto.Vehiculos.AsNoTracking() on vv.VehiculoId equals v.Id
               where vv.ViajeId == viajeId
               orderby vv.CreatedAt
               select (Guid?)v.ClienteId).FirstOrDefaultAsync(ct);
}
