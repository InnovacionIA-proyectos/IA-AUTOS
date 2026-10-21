using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Inspecciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>Listado y detalle de los hallazgos con el contexto de su inspeccion, su viaje y su VIN.</summary>
internal sealed class clsConsultasHallazgos(clsAppDbContext contexto) : IConsultasHallazgos
{
    public async Task<(IReadOnlyList<HallazgoListadoDto> Filas, int Total)> ListarAsync(
        FiltrosHallazgos f, Guid? soloDeUsuario, Guid? clienteId, int desplazamiento, int tamano, CancellationToken ct = default)
    {
        var consulta = Base();
        if (soloDeUsuario is { } usuarioId)
        {
            var viajesPropios = clsAlcanceViajes.Viajes(contexto, usuarioId);
            consulta = consulta.Where(x => viajesPropios.Contains(x.Inspeccion.ViajeId));
        }

        if (clienteId is { } cliente)
        {
            consulta = consulta.Where(x => x.ClienteId == cliente);
        }

        if (f.EstadoSeguimiento is not null)
        {
            consulta = consulta.Where(x => x.Hallazgo.EstadoSeguimiento == f.EstadoSeguimiento);
        }

        if (f.Origen is not null)
        {
            consulta = consulta.Where(x => x.Hallazgo.Origen == f.Origen);
        }

        // «Revisado» solo aplica a las novedades del conductor: un inspector las revisa y queda la fecha; el hallazgo del inspector nace revisado y no entra en este filtro.
        if (f.Revisado is { } revisado)
        {
            consulta = consulta.Where(x => x.Hallazgo.Origen == clsOrigenHallazgo.CONDUCTOR && (x.Hallazgo.RevisadoAt != null) == revisado);
        }

        if (f.ViajeId is { } viajeId)
        {
            consulta = consulta.Where(x => x.Inspeccion.ViajeId == viajeId);
        }

        if (f.Vin is not null)
        {
            consulta = consulta.Where(x => x.Vin == f.Vin);
        }

        if (f.InspeccionId is { } inspeccionId)
        {
            consulta = consulta.Where(x => x.Hallazgo.InspeccionId == inspeccionId);
        }

        if (f.Nivel is not null)
        {
            consulta = consulta.Where(x => x.Hallazgo.NivelSeveridad == f.Nivel);
        }

        if (f.CuadranteNumero is { } numero)
        {
            consulta = consulta.Where(x => x.ZonaNumero == numero);
        }

        if (f.Lado is not null)
        {
            consulta = consulta.Where(x => x.ZonaLado == f.Lado);
        }

        if (f.Proceso is not null)
        {
            consulta = consulta.Where(x => x.Proceso == f.Proceso);
        }

        // Desde y hasta son la fecha de cierre de la inspeccion: un hallazgo de una inspeccion en borrador no tiene fecha de cierre y no entra en un rango.
        if (f.Desde is { } desde)
        {
            consulta = consulta.Where(x => x.Inspeccion.CerradaAt >= desde);
        }

        if (f.Hasta is { } hasta)
        {
            consulta = consulta.Where(x => x.Inspeccion.CerradaAt <= hasta);
        }

        var total = await consulta.CountAsync(ct);
        var ordenada = f.Orden switch
        {
            clsOrdenHallazgos.ANTIGUOS => consulta.OrderBy(x => x.Inspeccion.CerradaAt ?? x.Hallazgo.CreatedAt).ThenBy(x => x.Hallazgo.Id),
            clsOrdenHallazgos.SEVERIDAD => consulta
                .OrderBy(x => x.Hallazgo.NivelSeveridad == null ? 1 : 0)
                .ThenBy(x => x.Hallazgo.NivelSeveridad == clsCodigosNivel.V1_PLUS ? 0 : x.Hallazgo.NivelSeveridad == clsCodigosNivel.V1 ? 1 : x.Hallazgo.NivelSeveridad == clsCodigosNivel.V2 ? 2 : 3)
                .ThenByDescending(x => x.Inspeccion.CerradaAt ?? x.Hallazgo.CreatedAt)
                .ThenBy(x => x.Hallazgo.Id),
            _ => consulta.OrderByDescending(x => x.Inspeccion.CerradaAt ?? x.Hallazgo.CreatedAt).ThenBy(x => x.Hallazgo.Id),
        };
        var pagina = await ordenada.Skip(desplazamiento).Take(tamano).ToListAsync(ct);
        return (await ArmarAsync(pagina, ct), total);
    }

    public async Task<(HallazgoListadoDto Fila, Guid ViajeId)?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var fila = await Base().Where(x => x.Hallazgo.Id == id).FirstOrDefaultAsync(ct);
        return fila is null ? null : ((await ArmarAsync([fila], ct))[0], fila.Inspeccion.ViajeId);
    }

    public Task<bool> UsuarioVeElViajeAsync(Guid viajeId, Guid usuarioId, CancellationToken ct = default) =>
        clsAlcanceViajes.Viajes(contexto, usuarioId).Where(v => v == viajeId).AnyAsync(ct);

    private IQueryable<FilaHallazgo> Base() =>
        from h in contexto.Hallazgos.AsNoTracking()
        join i in contexto.Inspecciones on h.InspeccionId equals i.Id
        join e in contexto.Etapas on i.EtapaId equals e.Id
        join v in contexto.Vehiculos on e.VehiculoId equals v.Id
        join p in contexto.EtapasCatalogo on e.EtapaCatalogoId equals p.Id
        join vj in contexto.Viajes on i.ViajeId equals vj.Id
        join z in contexto.ZonasDano on h.ZonaId equals z.Id into zonas
        from z in zonas.DefaultIfEmpty()
        select new FilaHallazgo { Hallazgo = h, Inspeccion = i, Vin = v.Vin, ClienteId = v.ClienteId, Proceso = p.Codigo, CodigoMtrack = vj.CodigoMtrack, ZonaNumero = z.Numero, ZonaLado = z.Lado };

    /// <summary>Convierte las filas en DTO: el hallazgo con su zona y nivel, y su foto propia (la primera que se tomo).</summary>
    private async Task<IReadOnlyList<HallazgoListadoDto>> ArmarAsync(IReadOnlyList<FilaHallazgo> filas, CancellationToken ct)
    {
        if (filas.Count == 0)
        {
            return [];
        }

        var dtos = (await new clsDescriptorHallazgos(contexto).DescribirAsync(filas.Select(f => f.Hallazgo).ToList(), ct)).ToDictionary(h => h.Id);
        var ids = filas.Select(f => f.Hallazgo.Id).ToList();
        var fotos = (await contexto.Evidencias.AsNoTracking()
            .Where(e => e.HallazgoId != null && ids.Contains(e.HallazgoId.Value) && !e.EsSello && !e.EsConstancia)
            .OrderBy(e => e.CreatedAt).ToListAsync(ct)).GroupBy(e => e.HallazgoId!.Value).ToDictionary(g => g.Key, g => g.First());

        return filas.Select(f => new HallazgoListadoDto(
            dtos[f.Hallazgo.Id], f.Vin, f.Inspeccion.ViajeId, f.CodigoMtrack, f.Proceso, f.Inspeccion.Momento, f.Inspeccion.Estado, f.Inspeccion.CerradaAt,
            fotos.TryGetValue(f.Hallazgo.Id, out var foto) ? clsMapeadorInspecciones.AEvidencia(foto) : null)).ToList();
    }

    /// <summary>Una fila de la consulta; se proyecta con inicializadores (y no con constructor) para que EF pueda seguir filtrando y ordenando sobre sus miembros.</summary>
    private sealed class FilaHallazgo
    {
        public required clsHallazgo Hallazgo { get; init; }

        public required clsInspeccion Inspeccion { get; init; }

        public required string Vin { get; init; }

        public required Guid ClienteId { get; init; }

        public required string Proceso { get; init; }

        public string? CodigoMtrack { get; init; }

        public int? ZonaNumero { get; init; }

        public string? ZonaLado { get; init; }
    }
}
