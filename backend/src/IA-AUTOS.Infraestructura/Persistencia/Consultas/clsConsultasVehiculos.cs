using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Viajes;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

internal sealed class clsConsultasVehiculos(clsAppDbContext contexto) : IConsultasVehiculos
{
    private const int MAXIMO_BUSQUEDA = 8;

    public async Task<IReadOnlyList<VehiculoBusquedaDto>> BuscarAsync(string termino, Guid? soloDeUsuario, Guid? soloDeCliente, CancellationToken ct = default)
    {
        // Un vehiculo dado de baja ya no se ofrece al buscar (sigue en los listados, el inventario y los reportes).
        var consulta = contexto.Vehiculos.AsNoTracking().Where(v => v.Activo && (v.Vin.Contains(termino) || v.Marca.Contains(termino)));
        if (soloDeCliente is { } clienteId)
        {
            consulta = consulta.Where(v => v.ClienteId == clienteId);
        }

        if (soloDeUsuario is { } usuarioId)
        {
            var propios = clsAlcanceViajes.Vehiculos(contexto, usuarioId);
            consulta = consulta.Where(v => propios.Contains(v.Id));
        }

        return await consulta
            .OrderBy(v => v.Vin)
            .Take(MAXIMO_BUSQUEDA)
            .Select(v => new VehiculoBusquedaDto(v.Vin, v.Marca, v.Modelo, v.Color, v.Operacion, v.Activo))
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<ListadoVehiculoDto> Filas, int Total)> ListarAsync(
        string tipo,
        string? vin,
        Guid? soloDeUsuario,
        Guid? soloDeCliente,
        int limite,
        int desplazamiento,
        CancellationToken ct = default)
    {
        var movilizados = contexto.Set<clsViajeCompleto>().Select(m => m.VehiculoId);
        var consulta = contexto.Vehiculos.AsNoTracking().AsQueryable();
        if (soloDeCliente is { } clienteId)
        {
            consulta = consulta.Where(v => v.ClienteId == clienteId);
        }

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
                v.Activo,
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
                             join vv in contexto.ViajeVehiculos on new { e.ViajeId, e.VehiculoId } equals new { vv.ViajeId, vv.VehiculoId } into vinesEnViaje
                             from vv in vinesEnViaje.DefaultIfEmpty()
                             join um in contexto.Usuarios on vv.NoSaleMarcadoPor equals um.Id into marcadores
                             from um in marcadores.DefaultIfEmpty()
                             join r in contexto.Vehiculos on vv.ReemplazoVehiculoId equals r.Id into reemplazos
                             from r in reemplazos.DefaultIfEmpty()
                             where e.VehiculoId == vehiculo.Id
                             select new
                             {
                                 Etapa = e,
                                 Proceso = p.Codigo,
                                 Lugar = (string?)l.Nombre,
                                 vj.CodigoMtrack,
                                 ViajeCreado = vj.CreatedAt,
                                 EstadoUnidad = vv.EstadoUnidad ?? clsEstadoUnidad.ACTIVA,
                                 vv.NoSaleMotivo,
                                 vv.NoSaleHallazgoId,
                                 vv.NoSaleMarcadoPor,
                                 MarcadoPorNombre = (string?)um.Nombre,
                                 vv.NoSaleAt,
                                 VinReemplazo = (string?)r.Vin,
                             };
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
        var hallazgosDto = (await new clsDescriptorHallazgos(contexto).DescribirAsync(hallazgos, ct)).ToDictionary(h => h.Id);
        var evidencias = await contexto.Evidencias.AsNoTracking()
            .Where(e => inspeccionIds.Contains(e.InspeccionId)).OrderBy(e => e.CreatedAt).ToListAsync(ct);
        var transferencias = await contexto.Transferencias.AsNoTracking()
            .Where(t => inspeccionIds.Contains(t.InspeccionId)).ToListAsync(ct);
        var notas = await new clsConsultasNotas(contexto).ListarPorEntidadesAsync(clsEntidadNota.INSPECCION, inspeccionIds, ct);

        // La placa, el trailer y el conductor son los del viaje; lo capturado a mano antes de los viajes tiene prioridad.
        InventarioInspeccionDto AInspeccion(clsInspeccion i)
        {
            var viaje = datosDeViaje.GetValueOrDefault(i.ViajeId);
            return new InventarioInspeccionDto(
                i.Id, i.EtapaId, i.ViajeId, i.UsuarioId, i.Momento, i.Fecha, i.Lat, i.Lng, i.Estado,
                i.ConductorNombre ?? viaje?.Conductor, i.Placa ?? viaje?.Placa, i.Trailer ?? viaje?.Trailer, i.Notas,
                i.CreatedAt, i.UpdatedAt,
                i.IniciadaAt, i.CerradaAt, i.Version, i.CorrigeInspeccionId, clsMomento.Nombre(i.Momento),
                hallazgos.Where(h => h.InspeccionId == i.Id).Select(h => hallazgosDto[h.Id]).ToList(),
                evidencias.Where(ev => ev.InspeccionId == i.Id).Select(clsMapeadorInspecciones.AEvidencia).ToList(),
                transferencias.Where(t => t.InspeccionId == i.Id).Select(clsMapeadorInspecciones.ATransferencia).FirstOrDefault(),
                notas.GetValueOrDefault(i.Id) ?? []);
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
            e.EstadoUnidad,
            clsMapeadorViajes.ANoSale(e.EstadoUnidad, e.NoSaleMotivo, e.NoSaleHallazgoId, e.NoSaleMarcadoPor, e.MarcadoPorNombre, e.NoSaleAt, e.VinReemplazo),
            inspecciones.Where(i => i.EtapaId == e.Etapa.Id).Select(AInspeccion).ToList()))
            .ToList();

        return new InventarioDto(
            vehiculo.Id, vehiculo.Vin, vehiculo.Marca, vehiculo.TipoVehiculo, vehiculo.Modelo, vehiculo.Chasis,
            vehiculo.Color, vehiculo.Operacion, vehiculo.Activo, vehiculo.CreatedAt, vehiculo.UpdatedAt, etapasDto);
    }
}
