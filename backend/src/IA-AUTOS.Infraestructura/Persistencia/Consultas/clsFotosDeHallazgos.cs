using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>Cuales hallazgos tienen su foto propia. El dano de un extra no lleva foto propia: su foto es la de su extra.</summary>
internal static class clsFotosDeHallazgos
{
    /// <summary>
    /// Los hallazgos de <paramref name="hallazgoIds"/> con foto propia (sin contar la del sello de quien valida ni la constancia): la evidencia con su <c>HallazgoId</c> o, en el dano de un extra, cualquier foto
    /// del extra del que nacio.
    /// </summary>
    public static async Task<HashSet<Guid>> ConFotoAsync(clsAppDbContext contexto, IReadOnlyCollection<Guid> hallazgoIds, CancellationToken ct)
    {
        var propias = (await contexto.Evidencias.AsNoTracking()
            .Where(e => e.HallazgoId != null && hallazgoIds.Contains(e.HallazgoId.Value) && !e.EsSello && !e.EsConstancia)
            .Select(e => e.HallazgoId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var deExtras = await (from h in contexto.Hallazgos.AsNoTracking()
                              where hallazgoIds.Contains(h.Id) && h.ExtraId != null
                                 && contexto.Evidencias.Any(e => e.ExtraId == h.ExtraId && !e.EsSello && !e.EsConstancia)
                              select h.Id).ToListAsync(ct);
        propias.UnionWith(deExtras);
        return propias;
    }
}
