using IAAutos.Aplicacion.Alertas;
using IAAutos.Dominio.Catalogos;
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
    private sealed class FilaFotoDeLugar
    {
        public Guid LugarId { get; init; }

        public string LugarNombre { get; init; } = string.Empty;

        public Guid ViajeId { get; init; }

        public Guid ClienteId { get; init; }

        public double Lat { get; init; }

        public double Lng { get; init; }
    }

    public async Task<IReadOnlyList<clsFotoDeLugar>> FotosPorLugarAsync(double valvulaPrecisionM, int limite, CancellationToken ct = default)
    {
        // Solo las fotos de vistas de Cargue y Descargue, selladas con ubicacion y con buena precision; el lugar es el de cargue del viaje (Cargue) o el destino de la unidad (Descargue).
        var baseFotos = from ev in contexto.Evidencias.AsNoTracking()
                        join i in contexto.Inspecciones.AsNoTracking() on ev.InspeccionId equals i.Id
                        join e in contexto.Etapas.AsNoTracking() on i.EtapaId equals e.Id
                        join p in contexto.EtapasCatalogo.AsNoTracking() on e.EtapaCatalogoId equals p.Id
                        join v in contexto.Vehiculos.AsNoTracking() on e.VehiculoId equals v.Id
                        where !ev.EsConstancia && !ev.EsSello && (ev.SelloLat != 0 || ev.SelloLng != 0) && ev.Precision != null && ev.Precision < valvulaPrecisionM
                        select new { ev, i, e, p, v };
        var cargue = await (from f in baseFotos
                            join vj in contexto.Viajes.AsNoTracking() on f.i.ViajeId equals vj.Id
                            join l in contexto.Lugares.AsNoTracking() on vj.LugarCargueId equals l.Id
                            where f.p.Codigo == clsCodigosEtapa.CARGUE && l.Activo && l.EstadoUbicacion != clsEstadoUbicacion.CONFIRMADA
                            select new FilaFotoDeLugar { LugarId = l.Id, LugarNombre = l.Nombre, ViajeId = vj.Id, ClienteId = f.v.ClienteId, Lat = f.ev.SelloLat, Lng = f.ev.SelloLng })
            .Take(limite).ToListAsync(ct);
        var descargue = await (from f in baseFotos
                               join vv in contexto.ViajeVehiculos.AsNoTracking() on new { ViajeId = f.i.ViajeId, VehiculoId = f.e.VehiculoId } equals new { vv.ViajeId, vv.VehiculoId }
                               join l in contexto.Lugares.AsNoTracking() on vv.LugarDestinoId equals l.Id
                               where f.p.Codigo == clsCodigosEtapa.DESCARGUE && l.Activo && l.EstadoUbicacion != clsEstadoUbicacion.CONFIRMADA
                               select new FilaFotoDeLugar { LugarId = l.Id, LugarNombre = l.Nombre, ViajeId = f.i.ViajeId, ClienteId = f.v.ClienteId, Lat = f.ev.SelloLat, Lng = f.ev.SelloLng })
            .Take(limite).ToListAsync(ct);
        return cargue.Concat(descargue).Select(f => new clsFotoDeLugar(f.LugarId, f.LugarNombre, f.ViajeId, f.ClienteId, f.Lat, f.Lng)).ToList();
    }

    private sealed class FilaCargueSinReclamar
    {
        public Guid ViajeId { get; init; }

        public string? CodigoMtrack { get; init; }

        public string? Placa { get; init; }

        public DateTimeOffset FechaProgramada { get; init; }

        public string? Lugar { get; init; }

        public Guid ClienteId { get; init; }
    }

    public async Task<IReadOnlyList<clsCargueSinReclamar>> CarguesSinReclamarAsync(DateTimeOffset programadosDesde, DateTimeOffset programadosAntesDe, int limite, CancellationToken ct = default)
    {
        var filas = await (from vj in contexto.Viajes.AsNoTracking()
                           join vv in contexto.ViajeVehiculos.AsNoTracking() on vj.Id equals vv.ViajeId
                           join v in contexto.Vehiculos.AsNoTracking() on vv.VehiculoId equals v.Id
                           join l in contexto.Lugares.AsNoTracking() on vj.LugarCargueId equals l.Id into lugares
                           from l in lugares.DefaultIfEmpty()
                           where vj.Estado == clsEstadoViaje.PROGRAMADO && vj.FechaProgramada != null && vj.FechaProgramada >= programadosDesde && vj.FechaProgramada < programadosAntesDe
                              && vv.EstadoUnidad == clsEstadoUnidad.ACTIVA
                              && !contexto.Reclamos.Any(r => r.ViajeId == vj.Id && r.Proceso == clsProcesoReclamo.CARGUE && r.LiberadoAt == null)
                              && !contexto.Inspecciones.Any(i => i.ViajeId == vj.Id)
                           orderby vj.FechaProgramada
                           select new FilaCargueSinReclamar
                           {
                               ViajeId = vj.Id,
                               CodigoMtrack = vj.CodigoMtrack,
                               Placa = vj.Placa,
                               FechaProgramada = vj.FechaProgramada!.Value,
                               Lugar = l != null ? l.Nombre : null,
                               ClienteId = v.ClienteId,
                           }).Distinct().Take(limite).ToListAsync(ct);
        return filas.Select(f => new clsCargueSinReclamar(f.ViajeId, f.CodigoMtrack, f.Placa, f.FechaProgramada, f.Lugar, f.ClienteId)).ToList();
    }

    public async Task<IReadOnlyList<clsReclamoSinActividad>> ReclamosSinActividadAsync(DateTimeOffset reclamadosAntesDe, int limite, CancellationToken ct = default) =>
        await (from r in contexto.Reclamos.AsNoTracking()
               join vj in contexto.Viajes.AsNoTracking() on r.ViajeId equals vj.Id
               join u in contexto.Usuarios.AsNoTracking() on r.InspectorId equals u.Id
               where r.LiberadoAt == null && r.ReclamadoAt < reclamadosAntesDe
                  && (vj.Estado == clsEstadoViaje.PROGRAMADO || vj.Estado == clsEstadoViaje.EN_CURSO)
                  && !contexto.Inspecciones.Any(i => i.ViajeId == r.ViajeId && (i.IniciadaAt > r.ReclamadoAt || i.CerradaAt > r.ReclamadoAt))
               orderby r.ReclamadoAt
               select new clsReclamoSinActividad(r.Id, vj.Id, vj.CodigoMtrack, vj.Placa, r.Proceso, r.InspectorId, u.Nombre, u.ClienteId, r.ReclamadoAt)).Take(limite).ToListAsync(ct);

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
                               IniciadaAt = i.IniciadaAt,
                           }).Take(limite).ToListAsync(ct);
        return filas.Select(f => new clsCapturaAbierta(
            f.InspeccionId, f.EtapaId, f.ViajeId, f.VehiculoId, f.ClienteId, f.Vin, f.Proceso, f.UsuarioId, f.RolCreador, f.IniciadaAt)).ToList();
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
