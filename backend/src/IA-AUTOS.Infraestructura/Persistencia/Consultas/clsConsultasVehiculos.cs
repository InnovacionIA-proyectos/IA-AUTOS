using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Dominio.Inspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsConsultasVehiculos(clsAppDbContext contexto) : IConsultasVehiculos
{
    private const int MAXIMO_BUSQUEDA = 8;

    public async Task<IReadOnlyList<VehiculoBusquedaDto>> BuscarAsync(string termino, Guid? soloDeUsuario, CancellationToken ct = default)
    {
        var consulta = contexto.Vehiculos.AsNoTracking().Where(v => v.Vin.Contains(termino) || v.Marca.Contains(termino));
        if (soloDeUsuario is { } usuarioId)
        {
            var propios = clsAlcanceViajes.Vehiculos(contexto, usuarioId);
            consulta = consulta.Where(v => propios.Contains(v.Id));
        }

        return await consulta
            .OrderBy(v => v.Vin)
            .Take(MAXIMO_BUSQUEDA)
            .Select(v => new VehiculoBusquedaDto(v.Vin, v.Marca, v.Modelo, v.Color, v.Operacion))
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<ListadoVehiculoDto> Filas, int Total)> ListarAsync(
        string tipo,
        string? vin,
        Guid? soloDeUsuario,
        int limite,
        int desplazamiento,
        CancellationToken ct = default)
    {
        var movilizados = contexto.Set<clsViajeCompleto>().Select(m => m.VehiculoId);
        var consulta = contexto.Vehiculos.AsNoTracking().AsQueryable();
        if (soloDeUsuario is { } usuarioId)
        {
            var propios = clsAlcanceViajes.Vehiculos(contexto, usuarioId);
            consulta = consulta.Where(v => propios.Contains(v.Id));
        }

        if (vin is not null)
        {
            consulta = consulta.Where(v => v.Vin.Contains(vin));
        }

        consulta = tipo switch
        {
            clsTipoListado.MOVILIZADOS => consulta.Where(v => movilizados.Contains(v.Id)),
            clsTipoListado.PENDIENTES => consulta.Where(v => !movilizados.Contains(v.Id)),
            _ => consulta.Where(v => contexto.Etapas.Any(e => e.VehiculoId == v.Id
                && contexto.Inspecciones.Any(i => i.EtapaId == e.Id
                    && contexto.Hallazgos.Any(h => h.InspeccionId == i.Id)))),
        };

        var total = await consulta.CountAsync(ct);
        var filas = await consulta
            .OrderBy(v => v.Vin)
            .Skip(desplazamiento)
            .Take(limite)
            .Select(v => new ListadoVehiculoDto(
                v.Id,
                v.Vin,
                v.Marca,
                v.Modelo,
                v.Color,
                v.Operacion,
                contexto.Etapas.Count(e => e.VehiculoId == v.Id),
                contexto.Etapas.Count(e => e.VehiculoId == v.Id
                    && contexto.Inspecciones.Any(i => i.EtapaId == e.Id && i.Estado == clsEstadoInspeccion.CERRADA)),
                (from e in contexto.Etapas
                 join i in contexto.Inspecciones on e.Id equals i.EtapaId
                 join h in contexto.Hallazgos on i.Id equals h.InspeccionId
                 where e.VehiculoId == v.Id
                 select h.Id).Count()))
            .ToListAsync(ct);
        return (filas, total);
    }

    public async Task<InventarioDto?> ObtenerInventarioAsync(string vin, Guid? soloDeUsuario, CancellationToken ct = default)
    {
        var vehiculo = await contexto.Vehiculos.AsNoTracking().FirstOrDefaultAsync(v => v.Vin == vin, ct);
        if (vehiculo is null)
        {
            return null;
        }

        var consultaEtapas = from e in contexto.Etapas.AsNoTracking()
                             join p in contexto.EtapasCatalogo on e.EtapaCatalogoId equals p.Id
                             join l in contexto.PuntosCustodia on e.PuntoCustodiaId equals l.Id into lugares
                             from l in lugares.DefaultIfEmpty()
                             join vj in contexto.Viajes on e.ViajeId equals vj.Id
                             where e.VehiculoId == vehiculo.Id
                             select new { Etapa = e, Proceso = p.Codigo, Lugar = (string?)l.Nombre, vj.CodigoMtrack, ViajeCreado = vj.CreatedAt };
        if (soloDeUsuario is { } usuarioId)
        {
            var viajesPropios = clsAlcanceViajes.Viajes(contexto, usuarioId);
            consultaEtapas = consultaEtapas.Where(x => viajesPropios.Contains(x.Etapa.ViajeId));
        }

        var etapas = await consultaEtapas.OrderBy(x => x.ViajeCreado).ThenBy(x => x.Etapa.Orden).ToListAsync(ct);
        var etapaIds = etapas.Select(e => e.Etapa.Id).ToList();

        var inspecciones = await contexto.Inspecciones.AsNoTracking()
            .Where(i => etapaIds.Contains(i.EtapaId)).OrderBy(i => i.Fecha).ToListAsync(ct);
        var inspeccionIds = inspecciones.Select(i => i.Id).ToList();
        var datosDeViaje = await clsDatosDeViaje.CargarAsync(contexto, etapas.Select(e => e.Etapa.ViajeId).Distinct().ToList(), ct);

        var hallazgos = await contexto.Hallazgos.AsNoTracking()
            .Where(h => inspeccionIds.Contains(h.InspeccionId)).OrderBy(h => h.CreatedAt).ToListAsync(ct);
        var evidencias = await contexto.Evidencias.AsNoTracking()
            .Where(e => inspeccionIds.Contains(e.InspeccionId)).OrderBy(e => e.CreatedAt).ToListAsync(ct);
        var transferencias = await contexto.Transferencias.AsNoTracking()
            .Where(t => inspeccionIds.Contains(t.InspeccionId)).ToListAsync(ct);

        // La placa, el trailer y el conductor son los del viaje; lo capturado a mano antes de los viajes tiene prioridad.
        InventarioInspeccionDto AInspeccion(clsInspeccion i)
        {
            var viaje = datosDeViaje.GetValueOrDefault(i.ViajeId);
            return new InventarioInspeccionDto(
                i.Id, i.EtapaId, i.ViajeId, i.UsuarioId, i.Momento, i.Fecha, i.Lat, i.Lng, i.Estado,
                i.ConductorNombre ?? viaje?.Conductor, i.Placa ?? viaje?.Placa, i.Trailer ?? viaje?.Trailer, i.Notas,
                i.CreatedAt, i.UpdatedAt,
                i.IniciadaAt, i.CerradaAt, i.Version, i.CorrigeInspeccionId, clsMomento.Nombre(i.Momento),
                hallazgos.Where(h => h.InspeccionId == i.Id).Select(clsMapeadorInspecciones.AHallazgo).ToList(),
                evidencias.Where(ev => ev.InspeccionId == i.Id).Select(clsMapeadorInspecciones.AEvidencia).ToList(),
                transferencias.Where(t => t.InspeccionId == i.Id).Select(clsMapeadorInspecciones.ATransferencia).FirstOrDefault());
        }

        var etapasDto = etapas.Select(e => new InventarioEtapaDto(
            e.Etapa.Id,
            e.Etapa.Orden,
            e.Proceso,
            e.Lugar,
            e.Etapa.RequiereInspeccion,
            e.Etapa.ViajeId,
            e.CodigoMtrack,
            e.Etapa.ConPersonalApoyo,
            inspecciones.Where(i => i.EtapaId == e.Etapa.Id).Select(AInspeccion).ToList()))
            .ToList();

        return new InventarioDto(
            vehiculo.Id, vehiculo.Vin, vehiculo.Marca, vehiculo.TipoVehiculo, vehiculo.Modelo, vehiculo.Chasis,
            vehiculo.Color, vehiculo.Operacion, vehiculo.CreatedAt, vehiculo.UpdatedAt, etapasDto);
    }
}
