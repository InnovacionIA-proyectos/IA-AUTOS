using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>Los extras del vehiculo de varias inspecciones de una vez, con el nombre de su tipo y sus fotos selladas (para el informe, el inventario y las respuestas de los extras).</summary>
internal static class clsConsultasExtras
{
    /// <summary>Los extras por inspeccion, en el orden en que se agregaron; las inspecciones sin extras no aparecen.</summary>
    public static async Task<Dictionary<Guid, IReadOnlyList<ExtraDto>>> CargarAsync(clsAppDbContext contexto, IReadOnlyCollection<Guid> inspeccionIds, CancellationToken ct)
    {
        var ids = inspeccionIds.ToList();
        var extras = await contexto.InspeccionExtras.AsNoTracking().Where(e => ids.Contains(e.InspeccionId)).OrderBy(e => e.CreatedAt).ToListAsync(ct);
        if (extras.Count == 0)
        {
            return [];
        }

        var codigos = extras.Select(e => e.Tipo).Distinct().ToList();
        var nombres = await contexto.TiposExtra.AsNoTracking().Where(t => codigos.Contains(t.Codigo)).ToDictionaryAsync(t => t.Codigo, t => t.Nombre, ct);
        var extraIds = extras.Select(e => e.Id).ToList();
        var fotos = (await contexto.Evidencias.AsNoTracking().Where(e => e.ExtraId != null && extraIds.Contains(e.ExtraId.Value)).OrderBy(e => e.CreatedAt).ToListAsync(ct))
            .ToLookup(e => e.ExtraId!.Value);
        return extras
            .GroupBy(e => e.InspeccionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ExtraDto>)g.Select(e => clsMapeadorInspecciones.AExtra(e, nombres.GetValueOrDefault(e.Tipo) ?? e.Tipo, fotos[e.Id].Select(clsMapeadorInspecciones.AEvidencia).ToList())).ToList());
    }
}
