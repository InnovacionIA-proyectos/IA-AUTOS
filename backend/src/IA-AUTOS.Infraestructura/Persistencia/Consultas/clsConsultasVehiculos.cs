using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Dominio.Inspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>Vehiculo movilizado segun la vista vwVehiculosMovilizados (unica definicion de "movilizado").</summary>
internal sealed class clsVehiculoMovilizado
{
    public Guid VehiculoId { get; set; }
}

internal sealed class clsConsultasVehiculos(clsAppDbContext contexto) : IConsultasVehiculos
{
    private const int MAXIMO_BUSQUEDA = 8;

    public async Task<IReadOnlyList<VehiculoBusquedaDto>> BuscarAsync(string termino, CancellationToken ct = default) =>
        await contexto.Vehiculos
            .AsNoTracking()
            .Where(v => v.Vin.Contains(termino) || v.Marca.Contains(termino))
            .OrderBy(v => v.Vin)
            .Take(MAXIMO_BUSQUEDA)
            .Select(v => new VehiculoBusquedaDto(v.Vin, v.Marca, v.Modelo, v.Color, v.Operacion))
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<ListadoVehiculoDto> Filas, int Total)> ListarAsync(
        string tipo,
        string? vin,
        int limite,
        int desplazamiento,
        CancellationToken ct = default)
    {
        var movilizados = contexto.Set<clsVehiculoMovilizado>().Select(m => m.VehiculoId);
        var consulta = contexto.Vehiculos.AsNoTracking().AsQueryable();
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

    public async Task<InventarioDto?> ObtenerInventarioAsync(string vin, CancellationToken ct = default)
    {
        var vehiculo = await contexto.Vehiculos.AsNoTracking().FirstOrDefaultAsync(v => v.Vin == vin, ct);
        if (vehiculo is null)
        {
            return null;
        }

        var etapas = await (from e in contexto.Etapas.AsNoTracking()
                            join p in contexto.PuntosCustodia on e.PuntoCustodiaId equals p.Id
                            where e.VehiculoId == vehiculo.Id
                            orderby e.Orden
                            select new { Etapa = e, Proceso = p.Nombre }).ToListAsync(ct);
        var etapaIds = etapas.Select(e => e.Etapa.Id).ToList();

        var inspecciones = await contexto.Inspecciones.AsNoTracking()
            .Where(i => etapaIds.Contains(i.EtapaId)).OrderBy(i => i.Fecha).ToListAsync(ct);
        var inspeccionIds = inspecciones.Select(i => i.Id).ToList();

        var hallazgos = await contexto.Hallazgos.AsNoTracking()
            .Where(h => inspeccionIds.Contains(h.InspeccionId)).OrderBy(h => h.CreatedAt).ToListAsync(ct);
        var evidencias = await contexto.Evidencias.AsNoTracking()
            .Where(e => inspeccionIds.Contains(e.InspeccionId)).OrderBy(e => e.CreatedAt).ToListAsync(ct);
        var transferencias = await contexto.Transferencias.AsNoTracking()
            .Where(t => inspeccionIds.Contains(t.InspeccionId)).ToListAsync(ct);

        var etapasDto = etapas.Select(e => new InventarioEtapaDto(
            e.Etapa.Id,
            e.Etapa.Orden,
            e.Proceso,
            e.Etapa.RequiereInspeccion,
            inspecciones.Where(i => i.EtapaId == e.Etapa.Id).Select(i => new InventarioInspeccionDto(
                i.Id, i.EtapaId, i.UsuarioId, i.Momento, i.Fecha, i.Lat, i.Lng, i.Estado, i.ConductorNombre, i.Placa, i.Trailer, i.Notas,
                i.CreatedAt, i.UpdatedAt,
                hallazgos.Where(h => h.InspeccionId == i.Id).Select(clsMapeadorInspecciones.AHallazgo).ToList(),
                evidencias.Where(ev => ev.InspeccionId == i.Id).Select(clsMapeadorInspecciones.AEvidencia).ToList(),
                transferencias.Where(t => t.InspeccionId == i.Id).Select(clsMapeadorInspecciones.ATransferencia).FirstOrDefault())).ToList()))
            .ToList();

        return new InventarioDto(
            vehiculo.Id, vehiculo.Vin, vehiculo.Marca, vehiculo.TipoVehiculo, vehiculo.Modelo, vehiculo.Chasis,
            vehiculo.Color, vehiculo.Operacion, vehiculo.CreatedAt, vehiculo.UpdatedAt, etapasDto);
    }
}
