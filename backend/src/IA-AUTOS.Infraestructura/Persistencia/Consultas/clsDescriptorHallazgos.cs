using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Inspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>
/// Arma el DTO de los hallazgos leyendo, en pocas consultas, lo que el hallazgo solo referencia: su zona y el cuadrante de la zona, su nivel de severidad
/// y lo que le falta a su evidencia (su foto propia y el cierre de la transferencia de su inspeccion).
/// </summary>
internal sealed class clsDescriptorHallazgos(clsAppDbContext contexto) : IDescriptorHallazgos
{
    public async Task<IReadOnlyList<HallazgoDto>> DescribirAsync(IReadOnlyCollection<clsHallazgo> hallazgos, CancellationToken ct = default)
    {
        if (hallazgos.Count == 0)
        {
            return [];
        }

        var zonaIds = hallazgos.Where(h => h.ZonaId is not null).Select(h => h.ZonaId!.Value).Distinct().ToList();
        var zonas = await contexto.ZonasDano.AsNoTracking().Where(z => zonaIds.Contains(z.Id)).ToDictionaryAsync(z => z.Id, ct);
        var cuadranteIds = zonas.Values.Where(z => z.CuadranteId is not null).Select(z => z.CuadranteId!.Value).Distinct().ToList();
        var cuadrantes = await contexto.Cuadrantes.AsNoTracking().Where(c => cuadranteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);
        var codigosNivel = hallazgos.Where(h => h.NivelSeveridad is not null).Select(h => h.NivelSeveridad!).Distinct().ToList();
        var niveles = await contexto.NivelesSeveridad.AsNoTracking().Where(n => codigosNivel.Contains(n.Codigo)).ToDictionaryAsync(n => n.Codigo, ct);
        var ids = hallazgos.Select(h => h.Id).ToList();
        var conFoto = (await contexto.Evidencias.AsNoTracking()
            .Where(e => e.HallazgoId != null && ids.Contains(e.HallazgoId.Value) && !e.EsSello && !e.EsConstancia)
            .Select(e => e.HallazgoId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var inspeccionIds = hallazgos.Select(h => h.InspeccionId).Distinct().ToList();
        var cierres = await contexto.Transferencias.AsNoTracking().Where(t => inspeccionIds.Contains(t.InspeccionId))
            .ToDictionaryAsync(t => t.InspeccionId, t => t.TipoCierre, ct);

        return hallazgos.Select(h =>
        {
            var zona = h.ZonaId is { } zonaId ? zonas.GetValueOrDefault(zonaId) : null;
            var cuadranteNombre = zona?.CuadranteId is { } cuadranteId ? cuadrantes.GetValueOrDefault(cuadranteId) : null;
            var nivel = h.NivelSeveridad is { } codigo ? niveles.GetValueOrDefault(codigo) : null;
            var faltante = clsEvidenciaHallazgo.Faltante(conFoto.Contains(h.Id), cierres.GetValueOrDefault(h.InspeccionId));
            return clsMapeadorInspecciones.AHallazgo(h, zona, cuadranteNombre, nivel, faltante);
        }).ToList();
    }
}
