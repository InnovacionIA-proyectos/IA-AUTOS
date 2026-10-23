using IAAutos.Aplicacion.Inspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsNotaAclaratoriaRepository(clsAppDbContext contexto) : IAAutos.Dominio.Inspecciones.INotaAclaratoriaRepository
{
    public void Agregar(IAAutos.Dominio.Inspecciones.clsNotaAclaratoria nota) => contexto.NotasAclaratorias.Add(nota);
}

/// <summary>Las notas aclaratorias con el nombre y el rol de su autor, de la mas antigua a la mas reciente.</summary>
internal sealed class clsConsultasNotas(clsAppDbContext contexto) : IConsultasNotas
{
    public async Task<IReadOnlyList<NotaAclaratoriaDto>> ListarAsync(string entidad, Guid entidadId, CancellationToken ct = default) =>
        (await ListarPorEntidadesAsync(entidad, [entidadId], ct)).GetValueOrDefault(entidadId) ?? [];

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<NotaAclaratoriaDto>>> ListarPorEntidadesAsync(
        string entidad, IReadOnlyCollection<Guid> entidadIds, CancellationToken ct = default)
    {
        if (entidadIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<NotaAclaratoriaDto>>();
        }

        var filas = await (from n in contexto.NotasAclaratorias.AsNoTracking()
                           join u in contexto.Usuarios on n.AutorId equals u.Id into autores
                           from u in autores.DefaultIfEmpty()
                           where n.Entidad == entidad && entidadIds.Contains(n.EntidadId)
                           orderby n.CreatedAt, n.Id
                           select new NotaAclaratoriaDto(n.Id, n.Entidad, n.EntidadId, n.AutorId, u.Nombre, u.Rol, n.Texto, n.CreatedAt)).ToListAsync(ct);
        return filas.GroupBy(f => f.EntidadId).ToDictionary(g => g.Key, g => (IReadOnlyList<NotaAclaratoriaDto>)g.ToList());
    }
}
