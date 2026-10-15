using IAAutos.Dominio.Catalogos;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Reportes;

namespace IAAutos.Aplicacion.Reportes;

/// <summary>
/// Consultas analiticas (lecturas dedicadas, sin CQRS general). Todas consideran solo unidades movilizadas, con la misma
/// definicion que los listados: la vista vwViajesCompletos.
/// </summary>
public interface IConsultasReportes
{
    /// <summary>Hallazgos por proceso (solo cargue, transporte y descargue), por zona y por zona y proceso, de mayor a menor.</summary>
    Task<(IReadOnlyList<ConteoProcesoDto> PorProceso, IReadOnlyList<ConteoZonaDto> PorZona, IReadOnlyList<ConteoZonaProcesoDto> PorZonaProceso)> ObtenerPatronesAsync(CancellationToken ct = default);

    Task<(IReadOnlyList<ConteoClaseDto> PorClase, IReadOnlyList<ConteoTamanoDto> PorTamano)> ObtenerAnalisisAsync(CancellationToken ct = default);

    Task<ConteosRechazo> ObtenerConteosRechazoAsync(CancellationToken ct = default);
}

public sealed class clsReportesService(IConsultasReportes consultas)
{
    public async Task<PatronesDto> ObtenerPatronesAsync(CancellationToken ct = default)
    {
        var (porProceso, porZona, porZonaProceso) = await consultas.ObtenerPatronesAsync(ct);
        return new PatronesDto(porProceso.Sum(p => p.Total), porProceso, porZona, porZonaProceso);
    }

    /// <summary>Cuantifica clases y tamanos de dano y propone mejoras sencillas segun las categorias predominantes.</summary>
    public async Task<AnalisisDto> ObtenerAnalisisAsync(CancellationToken ct = default)
    {
        var (porClase, porTamano) = await consultas.ObtenerAnalisisAsync(ct);
        var sugerencias = new List<string>();

        if (porClase.Count > 0)
        {
            var principal = porClase[0];
            sugerencias.Add(
                $"La clase de daño más frecuente es \"{principal.ClaseDano}\" ({principal.Total}). Revisar el procedimiento de manipulación asociado.");
        }

        var reemplazos = porTamano.FirstOrDefault(t => t.TamanoDano == clsTamanoDano.REQUIERE_REEMPLAZO);
        if (reemplazos is { Total: > 0 })
        {
            sugerencias.Add($"{reemplazos.Total} hallazgo(s) requieren reemplazo de pieza: priorizar inspección reforzada en cargue.");
        }

        if (sugerencias.Count == 0)
        {
            sugerencias.Add("Sin hallazgos suficientes para sugerir mejoras.");
        }

        return new AnalisisDto(porClase, porTamano, sugerencias);
    }

    /// <summary>
    /// Tasa de rechazo por operacion (unidades movilizadas con al menos un hallazgo) frente a la linea base del PRD.
    /// Las unidades pendientes se reportan aparte y no afectan la tasa.
    /// </summary>
    public async Task<TasaRechazoDto> ObtenerTasaRechazoAsync(CancellationToken ct = default)
    {
        var conteos = await consultas.ObtenerConteosRechazoAsync(ct);

        var porOperacion = conteos.PorOperacion
            .OrderBy(o => o.Operacion, StringComparer.Ordinal)
            .Select(o =>
            {
                var tasaActual = clsReglasReportes.Porcentaje(o.ConNovedad, o.Total);
                var lineaBase = clsReglasReportes.LineaBaseDe(o.Operacion);
                return new TasaOperacionDto(o.Operacion, o.Total, o.ConNovedad, tasaActual, lineaBase, clsReglasReportes.Variacion(tasaActual, lineaBase));
            })
            .ToList();

        var totalNovedad = porOperacion.Sum(o => o.ConNovedad);
        return new TasaRechazoDto(
            clsReglasReportes.Porcentaje(totalNovedad, conteos.Movilizadas),
            conteos.Movilizadas,
            conteos.TotalVehiculos - conteos.Movilizadas,
            conteos.ConNovedad,
            totalNovedad,
            porOperacion);
    }
}
