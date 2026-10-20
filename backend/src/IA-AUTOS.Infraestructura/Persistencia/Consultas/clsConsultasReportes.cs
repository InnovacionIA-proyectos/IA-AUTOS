using IAAutos.Aplicacion.Inspecciones;
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
        var movilizados = contexto.Set<clsViajeCompleto>().Select(m => m.VehiculoId);
        var hallazgos = from h in contexto.Hallazgos.AsNoTracking()
                        join i in contexto.Inspecciones on h.InspeccionId equals i.Id
                        join e in contexto.Etapas on i.EtapaId equals e.Id
                        join p in contexto.EtapasCatalogo on e.EtapaCatalogoId equals p.Id
                        where movilizados.Contains(e.VehiculoId)
                        select new { Proceso = p.Codigo, h.ZonaId, h.ParteVehiculo };

        var porProceso = await hallazgos.GroupBy(x => x.Proceso)
            .Select(g => new { g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Key)
            .ToListAsync(ct);
        var porZonaProceso = await hallazgos.GroupBy(x => new { x.Proceso, x.ZonaId, x.ParteVehiculo })
            .Select(g => new { g.Key.Proceso, g.Key.ZonaId, g.Key.ParteVehiculo, Total = g.Count() })
            .ToListAsync(ct);

        // La zona se muestra como texto («Izquierdo 7 · Puerta delantera»); un hallazgo historico sin ubicar cuenta por la parte que se escribio entonces.
        var textos = await TextosDeZonasAsync(porZonaProceso.Where(x => x.ZonaId is not null).Select(x => x.ZonaId!.Value).Distinct().ToList(), ct);
        string Zona(Guid? zonaId, string? parte) => (zonaId is { } id ? textos.GetValueOrDefault(id) : null) ?? parte ?? "Sin ubicar";
        var zonaProceso = porZonaProceso
            .GroupBy(x => (x.Proceso, Zona: Zona(x.ZonaId, x.ParteVehiculo)))
            .Select(g => new { g.Key.Proceso, g.Key.Zona, Total = g.Sum(x => x.Total) })
            .ToList();
        var zonas = zonaProceso.GroupBy(x => x.Zona).Select(g => new { g.Key, Total = g.Sum(x => x.Total) })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();

        return (
            porProceso.Select(x => new ConteoProcesoDto(x.Key, x.Total)).ToList(),
            zonas.Select(x => new ConteoZonaDto(x.Key, x.Total)).ToList(),
            zonaProceso.OrderByDescending(x => x.Total).ThenBy(x => x.Proceso, StringComparer.Ordinal).ThenBy(x => x.Zona, StringComparer.Ordinal)
                .Select(x => new ConteoZonaProcesoDto(x.Proceso, x.Zona, x.Total)).ToList());
    }

    /// <summary>El texto para mostrar de cada zona (lado + numero + nombre del cuadrante, o el nombre de la zona provisional).</summary>
    private async Task<Dictionary<Guid, string>> TextosDeZonasAsync(IReadOnlyCollection<Guid> zonaIds, CancellationToken ct)
    {
        var zonas = await (from z in contexto.ZonasDano.AsNoTracking()
                           join c in contexto.Cuadrantes.AsNoTracking() on z.CuadranteId equals c.Id into cuadrantes
                           from c in cuadrantes.DefaultIfEmpty()
                           where zonaIds.Contains(z.Id)
                           select new { Zona = z, CuadranteNombre = (string?)c.Nombre }).ToListAsync(ct);
        return zonas.ToDictionary(x => x.Zona.Id, x => clsMapeadorInspecciones.TextoDeZona(x.Zona, x.CuadranteNombre) ?? string.Empty);
    }

    public async Task<(IReadOnlyList<ConteoClaseDto>, IReadOnlyList<ConteoTamanoDto>)> ObtenerAnalisisAsync(CancellationToken ct = default)
    {
        // A diferencia de los patrones, el analisis cuantifica los hallazgos de unidades movilizadas de cualquier proceso.
        var movilizados = contexto.Set<clsViajeCompleto>().Select(m => m.VehiculoId);
        var hallazgos = from h in contexto.Hallazgos.AsNoTracking()
                        join i in contexto.Inspecciones on h.InspeccionId equals i.Id
                        join e in contexto.Etapas on i.EtapaId equals e.Id
                        where movilizados.Contains(e.VehiculoId)
                        select h;

        // Una novedad del conductor aun sin revisar no tiene clase y no cuenta en el analisis por clase.
        var porClase = await hallazgos.Where(h => h.ClaseDano != null).GroupBy(h => h.ClaseDano)
            .Select(g => new { g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Key)
            .ToListAsync(ct);
        var porTamano = await hallazgos.GroupBy(h => h.TamanoDano)
            .Select(g => new { g.Key, Total = g.Count() })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Key)
            .ToListAsync(ct);

        return (
            porClase.Select(x => new ConteoClaseDto(x.Key!, x.Total)).ToList(),
            porTamano.Select(x => new ConteoTamanoDto(x.Key, x.Total)).ToList());
    }

    public async Task<ConteosRechazo> ObtenerConteosRechazoAsync(CancellationToken ct = default)
    {
        var movilizados = contexto.Set<clsViajeCompleto>().Select(m => m.VehiculoId);
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
