using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using IAAutos.Dominio.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsConsultasReportes(clsAppDbContext contexto) : IConsultasReportes
{
    /// <summary>Las unidades movilizadas (vista de viajes completos); con <paramref name="soloDeCliente"/>, solo las de ese cliente.</summary>
    private IQueryable<Guid> Movilizados(Guid? soloDeCliente)
    {
        var movilizadas = contexto.Set<clsViajeCompleto>().AsQueryable();
        if (soloDeCliente is { } clienteId)
        {
            movilizadas = movilizadas.Where(m => contexto.Vehiculos.Any(v => v.Id == m.VehiculoId && v.ClienteId == clienteId));
        }

        return movilizadas.Select(m => m.VehiculoId);
    }

    public async Task<ConteosRechazo> ObtenerConteosRechazoAsync(Guid? soloDeCliente, CancellationToken ct = default)
    {
        var movilizados = Movilizados(soloDeCliente);
        var vehiculosDelAlcance = soloDeCliente is { } clienteId ? contexto.Vehiculos.Where(v => v.ClienteId == clienteId) : contexto.Vehiculos;
        var conHallazgos = (from e in contexto.Etapas
                            join i in contexto.Inspecciones on e.Id equals i.EtapaId
                            join h in contexto.Hallazgos on i.Id equals h.InspeccionId
                            select e.VehiculoId).Distinct();

        var totalVehiculos = await vehiculosDelAlcance.CountAsync(ct);
        var totalMovilizadas = await movilizados.CountAsync(ct);
        var totalConNovedad = await vehiculosDelAlcance.CountAsync(v => conHallazgos.Contains(v.Id), ct);

        // Dos consultas simples (en lugar de un agregado con subconsulta, que SQL Server no admite) y se unen en memoria.
        var totales = await contexto.Vehiculos.AsNoTracking()
            .Where(v => movilizados.Contains(v.Id))
            .GroupBy(v => v.Operacion)
            .Select(g => new { Operacion = g.Key, Total = g.Count() })
            .ToListAsync(ct);
        var conNovedad = await contexto.Vehiculos.AsNoTracking()
            .Where(v => movilizados.Contains(v.Id) && conHallazgos.Contains(v.Id))
            .GroupBy(v => v.Operacion)
            .Select(g => new { Operacion = g.Key, Total = g.Count() })
            .ToListAsync(ct);

        var porOperacion = totales
            .Select(t => new ConteoOperacion(t.Operacion, t.Total, conNovedad.FirstOrDefault(c => c.Operacion == t.Operacion)?.Total ?? 0))
            .ToList();
        return new ConteosRechazo(totalVehiculos, totalMovilizadas, totalConNovedad, porOperacion);
    }
}
