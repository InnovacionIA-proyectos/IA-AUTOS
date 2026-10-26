using IAAutos.Aplicacion.Alertas;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Parametros;
using IAAutos.Dominio.Viajes;
using IAAutos.Dominio.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>Una fila de vwHallazgosVentanaUmbral: un hallazgo de una inspeccion cerrada con su cliente, zona, proceso y fecha de cierre.</summary>
internal sealed class clsHallazgoVentana
{
    public Guid HallazgoId { get; set; }

    public Guid ClienteId { get; set; }

    public int? Numero { get; set; }

    public string? Lado { get; set; }

    public string? ZonaCodigo { get; set; }

    public string Proceso { get; set; } = string.Empty;

    public DateTimeOffset Fecha { get; set; }
}

internal sealed class clsParametroRepository(clsAppDbContext contexto) : IParametroRepository
{
    public async Task<IReadOnlyList<clsParametro>> ListarAsync(CancellationToken ct = default) =>
        await contexto.Parametros.AsNoTracking().ToListAsync(ct);

    public Task<clsParametro?> ObtenerAsync(string clave, CancellationToken ct = default) =>
        contexto.Parametros.FirstOrDefaultAsync(p => p.Clave == clave, ct);
}

/// <summary>Lo que leen los procesos periodicos de alertas: conteos de patrones y las capturas y transferencias que siguen pendientes.</summary>
internal sealed class clsConsultasAlertas(clsAppDbContext contexto) : IConsultasAlertas
{
    public async Task<IReadOnlyList<clsConteoPatron>> ConteoPorZonaAsync(DateTimeOffset desde, CancellationToken ct = default)
    {
        var filas = await contexto.Set<clsHallazgoVentana>().AsNoTracking()
            .Where(h => h.Fecha >= desde && (h.Numero != null || h.ZonaCodigo != null))
            .GroupBy(h => new { h.ClienteId, h.Numero, h.Lado, h.ZonaCodigo })
            .Select(g => new { g.Key.ClienteId, g.Key.Numero, g.Key.Lado, g.Key.ZonaCodigo, Total = g.Count() })
            .ToListAsync(ct);
        return filas.Select(f =>
        {
            var clave = f.Numero is { } numero ? $"{f.Lado ?? "sin-lado"}-{numero}" : f.ZonaCodigo!;
            var nombre = f.Numero is { } n ? (f.Lado is { Length: > 0 } lado ? $"{char.ToUpperInvariant(lado[0])}{lado[1..]} {n}" : $"{n}") : f.ZonaCodigo!;
            return new clsConteoPatron(f.ClienteId, clave, nombre, f.Total);
        }).ToList();
    }

    public async Task<IReadOnlyList<clsConteoPatron>> ConteoPorProcesoAsync(DateTimeOffset desde, CancellationToken ct = default) =>
        (await contexto.Set<clsHallazgoVentana>().AsNoTracking()
            .Where(h => h.Fecha >= desde)
            .GroupBy(h => new { h.ClienteId, h.Proceso })
            .Select(g => new { g.Key.ClienteId, g.Key.Proceso, Total = g.Count() })
            .ToListAsync(ct))
        .Select(f => new clsConteoPatron(f.ClienteId, f.Proceso, f.Proceso, f.Total))
        .ToList();

    /// <summary>Una clase con propiedades de inicializacion (y no un registro con constructor): EF solo traduce los filtros que siguen a una proyeccion que arma asi.</summary>
    private sealed class FilaCaptura
    {
        public Guid InspeccionId { get; init; }

        public Guid EtapaId { get; init; }

        public Guid ViajeId { get; init; }

        public Guid VehiculoId { get; init; }

        public Guid ClienteId { get; init; }

        public string Vin { get; init; } = string.Empty;

        public string Proceso { get; init; } = string.Empty;

        public Guid UsuarioId { get; init; }

        public string RolCreador { get; init; } = string.Empty;

        public bool ConPersonalApoyo { get; init; }

        public DateTimeOffset IniciadaAt { get; init; }
    }

    public async Task<IReadOnlyList<clsCapturaAbierta>> CapturasAbiertasAsync(DateTimeOffset iniciadasAntesDe, int limite, CancellationToken ct = default)
    {
        var filas = await (from i in contexto.Inspecciones.AsNoTracking()
                           join e in contexto.Etapas on i.EtapaId equals e.Id
                           join p in contexto.EtapasCatalogo on e.EtapaCatalogoId equals p.Id
                           join v in contexto.Vehiculos on e.VehiculoId equals v.Id
                           join vj in contexto.Viajes on i.ViajeId equals vj.Id
                           join u in contexto.Usuarios on i.UsuarioId equals u.Id
                           where i.Estado == clsEstadoInspeccion.BORRADOR && i.IniciadaAt < iniciadasAntesDe && u.Activo
                              && (p.Codigo == clsCodigosEtapa.CARGUE || p.Codigo == clsCodigosEtapa.DESCARGUE)
                              && (vj.Estado == clsEstadoViaje.PROGRAMADO || vj.Estado == clsEstadoViaje.EN_CURSO)
                           orderby i.IniciadaAt
                           select new FilaCaptura
                           {
                               InspeccionId = i.Id,
                               EtapaId = e.Id,
                               ViajeId = i.ViajeId,
                               VehiculoId = v.Id,
                               ClienteId = v.ClienteId,
                               Vin = v.Vin,
                               Proceso = p.Codigo,
                               UsuarioId = u.Id,
                               RolCreador = u.Rol,
                               ConPersonalApoyo = e.ConPersonalApoyo,
                               IniciadaAt = i.IniciadaAt,
                           }).Take(limite).ToListAsync(ct);
        return filas.Select(f => new clsCapturaAbierta(
            f.InspeccionId, f.EtapaId, f.ViajeId, f.VehiculoId, f.ClienteId, f.Vin, f.Proceso, f.UsuarioId, f.RolCreador, f.ConPersonalApoyo, f.IniciadaAt)).ToList();
    }

    private sealed class FilaTransferencia
    {
        public Guid InspeccionId { get; init; }

        public Guid ViajeId { get; init; }

        public Guid VehiculoId { get; init; }

        public Guid ClienteId { get; init; }

        public string Vin { get; init; } = string.Empty;

        public string Proceso { get; init; } = string.Empty;

        public bool ExisteTransferencia { get; init; }

        public Guid? EntregaUsuarioId { get; init; }

        public bool FaltaEntrega { get; init; }

        public Guid? RecibeUsuarioId { get; init; }

        public bool FaltaRecibe { get; init; }

        public string? ProcesoSiguiente { get; init; }

        public Guid? ConductorId { get; init; }

        public Guid? InspectorId { get; init; }
    }

    public async Task<IReadOnlyList<clsTransferenciaPendiente>> TransferenciasPendientesAsync(DateTimeOffset cerradasAntesDe, int limite, CancellationToken ct = default)
    {
        var filas = await (from i in contexto.Inspecciones.AsNoTracking()
                           join e in contexto.Etapas on i.EtapaId equals e.Id
                           join p in contexto.EtapasCatalogo on e.EtapaCatalogoId equals p.Id
                           join v in contexto.Vehiculos on e.VehiculoId equals v.Id
                           join vj in contexto.Viajes on i.ViajeId equals vj.Id
                           join vv in contexto.ViajeVehiculos on new { ViajeId = vj.Id, VehiculoId = v.Id } equals new { vv.ViajeId, vv.VehiculoId }
                           join t in contexto.Transferencias on i.Id equals t.InspeccionId into transferencias
                           from t in transferencias.DefaultIfEmpty()
                           where i.Momento == clsMomento.LLEGADA && i.Estado == clsEstadoInspeccion.CERRADA && i.CerradaAt < cerradasAntesDe
                              && (t == null || t.TipoCierre == null)
                              && vv.EstadoUnidad == clsEstadoUnidad.ACTIVA
                              && (vj.Estado == clsEstadoViaje.PROGRAMADO || vj.Estado == clsEstadoViaje.EN_CURSO)
                              && !contexto.Inspecciones.Any(c => c.CorrigeInspeccionId == i.Id)
                           orderby i.CerradaAt
                           select new FilaTransferencia
                           {
                               InspeccionId = i.Id,
                               ViajeId = vj.Id,
                               VehiculoId = v.Id,
                               ClienteId = v.ClienteId,
                               Vin = v.Vin,
                               Proceso = p.Codigo,
                               ExisteTransferencia = t != null,
                               EntregaUsuarioId = t != null ? t.EntregaUsuarioId : null,
                               FaltaEntrega = t != null && t.FirmaEntrega == null,
                               RecibeUsuarioId = t != null ? t.RecibeUsuarioId : null,
                               FaltaRecibe = t != null && t.FirmaRecibe == null,
                               ProcesoSiguiente = (from e2 in contexto.Etapas
                                                   join p2 in contexto.EtapasCatalogo on e2.EtapaCatalogoId equals p2.Id
                                                   where e2.ViajeId == e.ViajeId && e2.VehiculoId == e.VehiculoId && e2.Orden > e.Orden
                                                   orderby e2.Orden
                                                   select p2.Codigo).FirstOrDefault(),
                               ConductorId = vj.ConductorId,
                               InspectorId = vj.InspectorId,
                           }).Take(limite).ToListAsync(ct);
        return filas.Select(f => new clsTransferenciaPendiente(
            f.InspeccionId, f.ViajeId, f.VehiculoId, f.ClienteId, f.Vin, f.Proceso, f.ExisteTransferencia, f.EntregaUsuarioId, f.FaltaEntrega, f.RecibeUsuarioId, f.FaltaRecibe,
            f.ProcesoSiguiente, f.ConductorId, f.InspectorId)).ToList();
    }
}
