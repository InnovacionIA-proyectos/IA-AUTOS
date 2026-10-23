using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using IAAutos.Dominio.Reportes;

namespace IAAutos.Aplicacion.Reportes;

/// <summary>
/// Consulta de la tasa de rechazo (lectura dedicada, sin CQRS general). Considera solo unidades movilizadas, con la misma
/// definicion que los listados: la vista vwViajesCompletos. Con <c>soloDeCliente</c> cuenta solo las unidades de ese cliente.
/// </summary>
public interface IConsultasReportes
{
    Task<ConteosRechazo> ObtenerConteosRechazoAsync(Guid? soloDeCliente, CancellationToken ct = default);
}

public sealed class clsReportesService(IConsultasReportes consultas, IUsuarioActual actual)
{
    /// <summary>
    /// Tasa de rechazo por operacion (unidades movilizadas con al menos un hallazgo) frente a la linea base del PRD.
    /// Las unidades pendientes se reportan aparte y no afectan la tasa.
    /// </summary>
    public async Task<TasaRechazoDto> ObtenerTasaRechazoAsync(CancellationToken ct = default)
    {
        var conteos = await consultas.ObtenerConteosRechazoAsync(actual.ClienteRestringido(), ct);

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
