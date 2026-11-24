using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Repositorios;

internal sealed class clsRepositorioMapeosCargaViajes(clsAppDbContext contexto) : IMapeoCargaViajesRepository
{
    public Task<clsMapeoCargaViajes?> ObtenerAsync(Guid clienteId, string huella, CancellationToken ct = default) =>
        contexto.MapeosCargaViajes.FirstOrDefaultAsync(m => m.ClienteId == clienteId && m.Huella == huella, ct);

    public void Agregar(clsMapeoCargaViajes mapeo) => contexto.MapeosCargaViajes.Add(mapeo);
}
