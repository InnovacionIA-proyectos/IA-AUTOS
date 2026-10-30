using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Repositorios;

internal sealed class clsViajeRepository(clsAppDbContext contexto) : IViajeRepository
{
    public Task<clsViaje?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        contexto.Viajes.FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<bool> ExisteCodigoMtrackAsync(string codigo, Guid? exceptoViajeId, CancellationToken ct = default) =>
        // La colacion de la base hace la comparacion insensible a mayusculas, igual que el indice unico.
        contexto.Viajes.AsNoTracking().AnyAsync(v => v.CodigoMtrack == codigo && (exceptoViajeId == null || v.Id != exceptoViajeId), ct);

    public async Task<IReadOnlySet<string>> CodigosMtrackExistentesAsync(IReadOnlyCollection<string> codigos, CancellationToken ct = default)
    {
        var resultado = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lote in codigos.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(1000))
        {
            var encontrados = await contexto.Viajes.AsNoTracking()
                .Where(v => v.CodigoMtrack != null && lote.Contains(v.CodigoMtrack))
                .Select(v => v.CodigoMtrack!)
                .ToListAsync(ct);
            resultado.UnionWith(encontrados);
        }

        return resultado;
    }

    public async Task<IReadOnlyDictionary<Guid, clsViajeActivoDeVin>> ViajesActivosPorVehiculoAsync(
        IReadOnlyCollection<Guid> vehiculoIds,
        Guid? exceptoViajeId,
        CancellationToken ct = default)
    {
        var resultado = new Dictionary<Guid, clsViajeActivoDeVin>();
        foreach (var lote in vehiculoIds.Distinct().Chunk(1000))
        {
            var filas = await (from vv in contexto.ViajeVehiculos.AsNoTracking()
                               join v in contexto.Viajes.AsNoTracking() on vv.ViajeId equals v.Id
                               where lote.Contains(vv.VehiculoId)
                                  && (v.Estado == clsEstadoViaje.PROGRAMADO || v.Estado == clsEstadoViaje.EN_CURSO)
                                  && (exceptoViajeId == null || v.Id != exceptoViajeId)
                                  && vv.EstadoUnidad != clsEstadoUnidad.NO_SALE
                               select new { vv.VehiculoId, ViajeId = v.Id, v.CodigoMtrack }).ToListAsync(ct);
            foreach (var fila in filas)
            {
                resultado.TryAdd(fila.VehiculoId, new clsViajeActivoDeVin(fila.ViajeId, fila.CodigoMtrack));
            }
        }

        return resultado;
    }

    public async Task<IReadOnlyList<Guid>> HallazgosNoSaleSinRecuperarAsync(Guid vehiculoId, CancellationToken ct = default) =>
        await (from vv in contexto.ViajeVehiculos.AsNoTracking()
               join h in contexto.Hallazgos.AsNoTracking() on vv.NoSaleHallazgoId equals h.Id
               where vv.VehiculoId == vehiculoId && vv.EstadoUnidad == clsEstadoUnidad.NO_SALE && h.EstadoSeguimiento != clsEstadoSeguimiento.RECUPERADO
               select h.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<clsViajeVehiculo>> ObtenerVehiculosAsync(Guid viajeId, CancellationToken ct = default) =>
        await contexto.ViajeVehiculos.Where(v => v.ViajeId == viajeId).ToListAsync(ct);

    public Task<clsViajeVehiculo?> ObtenerVehiculoAsync(Guid viajeId, Guid vehiculoId, CancellationToken ct = default) =>
        contexto.ViajeVehiculos.FirstOrDefaultAsync(v => v.ViajeId == viajeId && v.VehiculoId == vehiculoId, ct);

    public async Task<IReadOnlyList<clsEtapa>> ObtenerEtapasAsync(Guid viajeId, Guid? vehiculoId, CancellationToken ct = default) =>
        await contexto.Etapas.Where(e => e.ViajeId == viajeId && (vehiculoId == null || e.VehiculoId == vehiculoId)).ToListAsync(ct);

    public Task<bool> TieneSegundaInspeccionDelCargueEnCursoAsync(Guid viajeId, CancellationToken ct = default) =>
        (from i in contexto.Inspecciones.AsNoTracking()
         join e in contexto.Etapas.AsNoTracking() on i.EtapaId equals e.Id
         join c in contexto.EtapasCatalogo.AsNoTracking() on e.EtapaCatalogoId equals c.Id
         where i.ViajeId == viajeId && i.Momento == IAAutos.Dominio.Inspecciones.clsMomento.SALIDA
            && i.Estado == IAAutos.Dominio.Inspecciones.clsEstadoInspeccion.BORRADOR && c.Codigo == IAAutos.Dominio.Vehiculos.clsCodigosEtapa.CARGUE
         select i.Id).AnyAsync(ct);

    public Task<bool> TieneInspeccionesAsync(Guid viajeId, Guid? vehiculoId, CancellationToken ct = default) =>
        contexto.Inspecciones.AsNoTracking().AnyAsync(
            i => i.ViajeId == viajeId
              && (vehiculoId == null || contexto.Etapas.Any(e => e.Id == i.EtapaId && e.VehiculoId == vehiculoId)),
            ct);

    public async Task<IReadOnlySet<Guid>> EtapasConInformeCerradoAsync(Guid viajeId, CancellationToken ct = default) =>
        (await contexto.Etapas.AsNoTracking()
            .Where(e => e.ViajeId == viajeId && contexto.Inspecciones.Any(i => i.EtapaId == e.Id && i.Estado == IAAutos.Dominio.Inspecciones.clsEstadoInspeccion.CERRADA))
            .Select(e => e.Id)
            .ToListAsync(ct)).ToHashSet();

    public async Task<Guid?> BuscarViajeParaOrdenAsync(Guid vehiculoId, string? codigoMtrack, CancellationToken ct = default)
    {
        // Entre los viajes del VIN (y del codigo, si la orden lo trae) se prefiere uno activo, y entre ellos el mas reciente.
        var id = await (from vv in contexto.ViajeVehiculos.AsNoTracking()
                        join v in contexto.Viajes.AsNoTracking() on vv.ViajeId equals v.Id
                        where vv.VehiculoId == vehiculoId
                           && v.Estado != clsEstadoViaje.CANCELADO
                           && (codigoMtrack == null || v.CodigoMtrack == codigoMtrack)
                        orderby (v.Estado == clsEstadoViaje.PROGRAMADO || v.Estado == clsEstadoViaje.EN_CURSO) ? 0 : 1, v.CreatedAt descending
                        select (Guid?)v.Id).FirstOrDefaultAsync(ct);
        return id;
    }

    public void Agregar(clsViaje viaje) => contexto.Viajes.Add(viaje);

    public void Agregar(clsViajeVehiculo vinEnViaje) => contexto.ViajeVehiculos.Add(vinEnViaje);

    public void Eliminar(clsViaje viaje) => contexto.Viajes.Remove(viaje);

    public void Eliminar(clsViajeVehiculo vinEnViaje) => contexto.ViajeVehiculos.Remove(vinEnViaje);

    public void Eliminar(IEnumerable<clsEtapa> etapas) => contexto.Etapas.RemoveRange(etapas);
}
