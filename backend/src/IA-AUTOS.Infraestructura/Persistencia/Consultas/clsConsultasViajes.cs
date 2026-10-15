using IAAutos.Aplicacion.Viajes;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>Vehiculo que ya completo su ruta en un viaje segun la vista vwViajesCompletos (unica definicion de "movilizado").</summary>
internal sealed class clsViajeCompleto
{
    public Guid ViajeId { get; set; }

    public Guid VehiculoId { get; set; }
}

/// <summary>
/// Alcance de un usuario que solo ve lo de sus viajes (inspector o conductor): los viajes no cancelados en los que es conductor o inspector,
/// y los VIN de esos viajes. Se arman como subconsultas para componerlas dentro de otras consultas.
/// </summary>
internal static class clsAlcanceViajes
{
    public static IQueryable<Guid> Viajes(clsAppDbContext contexto, Guid usuarioId) =>
        contexto.Viajes
            .Where(v => v.Estado != clsEstadoViaje.CANCELADO && (v.ConductorId == usuarioId || v.InspectorId == usuarioId))
            .Select(v => v.Id);

    public static IQueryable<Guid> Vehiculos(clsAppDbContext contexto, Guid usuarioId)
    {
        var viajes = Viajes(contexto, usuarioId);
        return contexto.ViajeVehiculos.Where(vv => viajes.Contains(vv.ViajeId)).Select(vv => vv.VehiculoId);
    }
}

/// <summary>Placa, trailer y conductor de un viaje: es lo que las inspecciones muestran en lugar de texto capturado a mano.</summary>
internal sealed record clsDatosDeViaje(string? Placa, string? Trailer, string? Conductor, string? CodigoMtrack, string? Unidad)
{
    public static async Task<Dictionary<Guid, clsDatosDeViaje>> CargarAsync(clsAppDbContext contexto, IReadOnlyCollection<Guid> viajeIds, CancellationToken ct)
    {
        var filas = await (from v in contexto.Viajes.AsNoTracking()
                           join c in contexto.Usuarios on v.ConductorId equals c.Id into conductores
                           from c in conductores.DefaultIfEmpty()
                           join t in contexto.TiposUnidad on v.TipoUnidadId equals t.Id into tipos
                           from t in tipos.DefaultIfEmpty()
                           where viajeIds.Contains(v.Id)
                           select new { v.Id, v.Placa, v.Trailer, Conductor = (string?)c.Nombre, v.CodigoMtrack, Unidad = (string?)t.Nombre }).ToListAsync(ct);
        return filas.ToDictionary(f => f.Id, f => new clsDatosDeViaje(f.Placa, f.Trailer, f.Conductor, f.CodigoMtrack, f.Unidad));
    }
}

internal sealed class clsConsultasViajes(clsAppDbContext contexto) : IConsultasViajes
{
    /// <summary>El viaje con los nombres que se muestran (unidad, conductor, inspector, origen, destino) y sus conteos.</summary>
    private sealed record FilaViaje(
        clsViaje Viaje,
        string? Unidad,
        string? Conductor,
        string? Inspector,
        string? Origen,
        string? Destino,
        bool ConPersonalApoyo,
        int TotalVehiculos,
        int VehiculosCompletos);

    public async Task<IReadOnlyList<ViajeResumenDto>> ListarAsync(FiltrosViajes f, Guid? soloDeUsuario, int limite, CancellationToken ct = default)
    {
        var consulta = contexto.Viajes.AsNoTracking().AsQueryable();
        if (soloDeUsuario is { } usuarioId)
        {
            var propios = clsAlcanceViajes.Viajes(contexto, usuarioId);
            consulta = consulta.Where(v => propios.Contains(v.Id));
        }

        if (f.Estado is not null)
        {
            consulta = consulta.Where(v => v.Estado == f.Estado);
        }

        if (f.CodigoMtrack is not null)
        {
            consulta = consulta.Where(v => v.CodigoMtrack != null && v.CodigoMtrack.Contains(f.CodigoMtrack));
        }

        if (f.Vin is not null)
        {
            consulta = consulta.Where(v => contexto.ViajeVehiculos.Any(vv => vv.ViajeId == v.Id
                && contexto.Vehiculos.Any(h => h.Id == vv.VehiculoId && h.Vin.Contains(f.Vin))));
        }

        if (f.ConductorId is not null)
        {
            consulta = consulta.Where(v => v.ConductorId == f.ConductorId);
        }

        if (f.Desde is not null)
        {
            consulta = consulta.Where(v => v.CreatedAt >= f.Desde);
        }

        if (f.Hasta is not null)
        {
            consulta = consulta.Where(v => v.CreatedAt <= f.Hasta);
        }

        var filas = await ProyectarAsync(consulta.OrderByDescending(v => v.CreatedAt).Take(limite), ct);
        return filas.Select(AResumen).ToList();
    }

    public async Task<IReadOnlyList<ViajeDto>> MiosAsync(Guid usuarioId, IReadOnlyCollection<string> estados, CancellationToken ct = default)
    {
        var consulta = contexto.Viajes.AsNoTracking()
            .Where(v => (v.ConductorId == usuarioId || v.InspectorId == usuarioId) && estados.Contains(v.Estado))
            .OrderByDescending(v => v.CreatedAt);
        return await ConVehiculosAsync(await ProyectarAsync(consulta, ct), ct);
    }

    public async Task<ViajeDto?> ObtenerAsync(Guid id, CancellationToken ct = default) =>
        (await ConVehiculosAsync(await ProyectarAsync(contexto.Viajes.AsNoTracking().Where(v => v.Id == id), ct), ct)).FirstOrDefault();

    public async Task<bool> UsuarioVeVinAsync(string vin, Guid usuarioId, CancellationToken ct = default)
    {
        var propios = clsAlcanceViajes.Viajes(contexto, usuarioId);
        return await (from vv in contexto.ViajeVehiculos.AsNoTracking()
                      join v in contexto.Vehiculos on vv.VehiculoId equals v.Id
                      where v.Vin == vin && propios.Contains(vv.ViajeId)
                      select vv.Id).AnyAsync(ct);
    }

    private async Task<List<FilaViaje>> ProyectarAsync(IQueryable<clsViaje> consulta, CancellationToken ct) =>
        (await (from v in consulta
                join t in contexto.TiposUnidad on v.TipoUnidadId equals t.Id into tipos
                from t in tipos.DefaultIfEmpty()
                join c in contexto.Usuarios on v.ConductorId equals c.Id into conductores
                from c in conductores.DefaultIfEmpty()
                join i in contexto.Usuarios on v.InspectorId equals i.Id into inspectores
                from i in inspectores.DefaultIfEmpty()
                join o in contexto.PuntosCustodia on v.OrigenId equals o.Id into origenes
                from o in origenes.DefaultIfEmpty()
                join d in contexto.PuntosCustodia on v.DestinoId equals d.Id into destinos
                from d in destinos.DefaultIfEmpty()
                select new
                {
                    Viaje = v,
                    Unidad = (string?)t.Nombre,
                    Conductor = (string?)c.Nombre,
                    Inspector = (string?)i.Nombre,
                    Origen = (string?)o.Nombre,
                    Destino = (string?)d.Nombre,
                    ConPersonalApoyo = contexto.Etapas.Any(e => e.ViajeId == v.Id && e.ConPersonalApoyo),
                    TotalVehiculos = contexto.ViajeVehiculos.Count(vv => vv.ViajeId == v.Id),
                    VehiculosCompletos = contexto.Set<clsViajeCompleto>().Count(x => x.ViajeId == v.Id),
                }).ToListAsync(ct))
        .Select(f => new FilaViaje(f.Viaje, f.Unidad, f.Conductor, f.Inspector, f.Origen, f.Destino, f.ConPersonalApoyo, f.TotalVehiculos, f.VehiculosCompletos))
        .OrderByDescending(f => f.Viaje.CreatedAt)
        .ToList();

    private async Task<IReadOnlyList<ViajeDto>> ConVehiculosAsync(List<FilaViaje> filas, CancellationToken ct)
    {
        var ids = filas.Select(f => f.Viaje.Id).ToList();
        var vehiculos = await (from vv in contexto.ViajeVehiculos.AsNoTracking()
                               join h in contexto.Vehiculos on vv.VehiculoId equals h.Id
                               where ids.Contains(vv.ViajeId)
                               orderby h.Vin
                               select new
                               {
                                   vv.ViajeId,
                                   Dto = new ViajeVehiculoDto(
                                       h.Id,
                                       h.Vin,
                                       h.Marca,
                                       h.Modelo,
                                       h.Color,
                                       h.Operacion,
                                       contexto.Etapas.Count(e => e.ViajeId == vv.ViajeId && e.VehiculoId == h.Id),
                                       contexto.Etapas.Count(e => e.ViajeId == vv.ViajeId && e.VehiculoId == h.Id
                                           && contexto.Inspecciones.Any(x => x.EtapaId == e.Id && x.Estado == clsEstadoInspeccion.CERRADA))),
                               }).ToListAsync(ct);
        var porViaje = vehiculos.ToLookup(v => v.ViajeId, v => v.Dto);
        return filas.Select(f => ADto(f, porViaje[f.Viaje.Id].ToList())).ToList();
    }

    private static ViajeResumenDto AResumen(FilaViaje f) => new(
        f.Viaje.Id, f.Viaje.CodigoMtrack, f.Viaje.TipoUnidadId, f.Unidad, f.Viaje.Placa, f.Viaje.Trailer,
        f.Viaje.ConductorId, f.Conductor, f.Viaje.InspectorId, f.Inspector, f.Viaje.OrigenId, f.Origen, f.Viaje.DestinoId, f.Destino,
        f.Viaje.Estado, f.ConPersonalApoyo, f.TotalVehiculos, f.VehiculosCompletos, f.Viaje.CreatedAt, f.Viaje.UpdatedAt);

    private static ViajeDto ADto(FilaViaje f, IReadOnlyList<ViajeVehiculoDto> vehiculos) => new(
        f.Viaje.Id, f.Viaje.CodigoMtrack, f.Viaje.TipoUnidadId, f.Unidad, f.Viaje.Placa, f.Viaje.Trailer,
        f.Viaje.ConductorId, f.Conductor, f.Viaje.InspectorId, f.Inspector, f.Viaje.OrigenId, f.Origen, f.Viaje.DestinoId, f.Destino,
        f.Viaje.Estado, f.ConPersonalApoyo, vehiculos, f.Viaje.CreatedAt, f.Viaje.UpdatedAt);
}
