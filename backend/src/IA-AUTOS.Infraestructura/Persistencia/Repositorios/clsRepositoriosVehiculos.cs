using IAAutos.Dominio.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Repositorios;

internal sealed class clsVehiculoRepository(clsAppDbContext contexto) : IVehiculoRepository
{
    public Task<clsVehiculo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        contexto.Vehiculos.FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<clsVehiculo?> ObtenerPorVinAsync(string vin, CancellationToken ct = default)
    {
        var normalizado = clsVin.Normalizar(vin);
        return contexto.Vehiculos.FirstOrDefaultAsync(v => v.Vin == normalizado, ct);
    }

    public async Task<IReadOnlyDictionary<string, clsVehiculo>> ObtenerPorVinesAsync(
        IReadOnlyCollection<string> vines,
        CancellationToken ct = default)
    {
        var normalizados = vines.Select(clsVin.Normalizar).Distinct().ToList();
        var resultado = new Dictionary<string, clsVehiculo>(StringComparer.OrdinalIgnoreCase);
        foreach (var lote in normalizados.Chunk(1000))
        {
            var encontrados = await contexto.Vehiculos.Where(v => lote.Contains(v.Vin)).ToListAsync(ct);
            foreach (var vehiculo in encontrados)
            {
                resultado[vehiculo.Vin] = vehiculo;
            }
        }

        return resultado;
    }

    public Task<bool> ExisteAsync(Guid id, CancellationToken ct = default) =>
        contexto.Vehiculos.AnyAsync(v => v.Id == id, ct);

    public void Agregar(clsVehiculo vehiculo) => contexto.Vehiculos.Add(vehiculo);
}

internal sealed class clsEtapaRepository(clsAppDbContext contexto) : IEtapaRepository
{
    public Task<clsEtapa?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        contexto.Etapas.FirstOrDefaultAsync(e => e.Id == id, ct);

    public void Agregar(clsEtapa etapa) => contexto.Etapas.Add(etapa);
}
