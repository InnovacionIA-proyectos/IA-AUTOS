using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using IAAutos.Dominio.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsConsultasReportes(clsAppDbContext contexto) : IConsultasReportes
{
    // Las agrupaciones se ordenan en la base de datos sobre una proyeccion anonima y se convierten a DTO en memoria
    // (EF no traduce un OrderBy sobre propiedades de un DTO construido despues del Select).

    public async Task<(IReadOnlyList<ConteoProcesoDto>, IReadOnlyList<ConteoZonaDto>, IReadOnlyList<ConteoZonaProcesoDto>)> ObtenerPatronesAsync(CancellationToken ct = default)
    {
        var procesos = clsPuntoCustodiaNombre.PROCESOS_ANALISIS;
        var movilizados = contexto.Set<clsVehiculoMovilizado>().Select(m => m.VehiculoId);
        var hallazgos = from h in contexto.Hallazgos.AsNoTracking()
                        join i in contexto.Inspecciones on h.InspeccionId equals i.Id
                        join e in contexto.Etapas on i.EtapaId equals e.Id
                        join p in contexto.PuntosCustodia on e.PuntoCustodiaId equals p.Id
                        where movilizados.Contains(e.VehiculoId) && procesos.Contains(p.Nombre)
                        select new { Proceso = p.Nombre, Parte = h.ParteVehiculo };

        var porProceso = await hallazgos.GroupBy(x => x.Proceso)
            .Select(g => new { g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Key)
            .ToListAsync(ct);
        var porZona = await hallazgos.GroupBy(x => x.Parte)
            .Select(g => new { g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Key)
            .ToListAsync(ct);
        var porZonaProceso = await hallazgos.GroupBy(x => new { x.Proceso, x.Parte })
            .Select(g => new { g.Key.Proceso, g.Key.Parte, Total = g.Count() })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Proceso).ThenBy(x => x.Parte)
            .ToListAsync(ct);

        return (
            porProceso.Select(x => new ConteoProcesoDto(x.Key, x.Total)).ToList(),
            porZona.Select(x => new ConteoZonaDto(x.Key, x.Total)).ToList(),
            porZonaProceso.Select(x => new ConteoZonaProcesoDto(x.Proceso, x.Parte, x.Total)).ToList());
    }

    public async Task<(IReadOnlyList<ConteoClaseDto>, IReadOnlyList<ConteoTamanoDto>)> ObtenerAnalisisAsync(CancellationToken ct = default)
    {
        // A diferencia de los patrones, el analisis cuantifica los hallazgos de unidades movilizadas de cualquier proceso.
        var movilizados = contexto.Set<clsVehiculoMovilizado>().Select(m => m.VehiculoId);
        var hallazgos = from h in contexto.Hallazgos.AsNoTracking()
                        join i in contexto.Inspecciones on h.InspeccionId equals i.Id
                        join e in contexto.Etapas on i.EtapaId equals e.Id
                        where movilizados.Contains(e.VehiculoId)
                        select h;

        var porClase = await hallazgos.GroupBy(h => h.ClaseDano)
            .Select(g => new { g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Key)
            .ToListAsync(ct);
        var porTamano = await hallazgos.GroupBy(h => h.TamanoDano)
            .Select(g => new { g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Key)
            .ToListAsync(ct);

        return (
            porClase.Select(x => new ConteoClaseDto(x.Key, x.Total)).ToList(),
            porTamano.Select(x => new ConteoTamanoDto(x.Key, x.Total)).ToList());
    }

    public async Task<ConteosRechazo> ObtenerConteosRechazoAsync(CancellationToken ct = default)
    {
        var movilizados = contexto.Set<clsVehiculoMovilizado>().Select(m => m.VehiculoId);
        var conHallazgos = (from e in contexto.Etapas
                            join i in contexto.Inspecciones on e.Id equals i.EtapaId
                            join h in contexto.Hallazgos on i.Id equals h.InspeccionId
                            select e.VehiculoId).Distinct();

        var totalVehiculos = await contexto.Vehiculos.CountAsync(ct);
        var totalMovilizadas = await movilizados.CountAsync(ct);
        var totalConNovedad = await contexto.Vehiculos.CountAsync(v => conHallazgos.Contains(v.Id), ct);

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
