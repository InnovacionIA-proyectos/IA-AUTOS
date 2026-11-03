using IAAutos.Aplicacion.Exportaciones;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>
/// Lo que se exporta a Excel (viajes, inspecciones y hallazgos), con los mismos filtros de los listados. Se arma con proyecciones planas y se limita a un maximo de filas (se pide una de mas
/// para saber si se excedio).
/// </summary>
internal sealed class clsConsultasExportaciones(clsAppDbContext contexto) : IConsultasExportaciones
{
    // ------------------------------------------------------------------------------------------------ viajes

    /// <summary>Una etapa de una unidad en un viaje, con los datos de la unidad y del viaje.</summary>
    private sealed class FilaEtapa
    {
        public required Guid EtapaId { get; init; }

        public required Guid ViajeId { get; init; }

        public required string Vin { get; init; }

        public required string Marca { get; init; }

        public string? TipoVehiculo { get; init; }

        public required string Operacion { get; init; }

        public required Guid ClienteId { get; init; }

        public string? CodigoMtrack { get; init; }

        public string? Placa { get; init; }

        public string? Trailer { get; init; }

        public string? Unidad { get; init; }

        public string? ConductorNombre { get; init; }

        public string? ConductorCedula { get; init; }

        public required string Etapa { get; init; }

        public string? Lugar { get; init; }

        public required string EstadoViaje { get; init; }

        public required string EstadoUnidad { get; init; }

        public string? Origen { get; init; }

        public string? Destino { get; init; }
    }

    public async Task<ResultadoExportacion<FilaExportViaje>> ViajesAsync(FiltrosViajes f, Guid? soloDeCliente, int maximo, CancellationToken ct = default)
    {
        var codigo = f.CodigoMtrack;
        var vinParcial = f.Vin;
        var vinExacto = vinParcial?.Length == clsVin.LONGITUD_ISO;
        var consulta = from e in contexto.Etapas.AsNoTracking()
                       join vj in contexto.Viajes on e.ViajeId equals vj.Id
                       join v in contexto.Vehiculos on e.VehiculoId equals v.Id
                       join vv in contexto.ViajeVehiculos on new { ViajeId = vj.Id, VehiculoId = v.Id } equals new { vv.ViajeId, vv.VehiculoId }
                       join ec in contexto.EtapasCatalogo on e.EtapaCatalogoId equals ec.Id
                       join pl in contexto.PuntosCustodia on e.PuntoCustodiaId equals pl.Id into lugares
                       from pl in lugares.DefaultIfEmpty()
                       join t in contexto.TiposUnidad on vj.TipoUnidadId equals t.Id into tipos
                       from t in tipos.DefaultIfEmpty()
                       join c in contexto.Usuarios on vj.ConductorId equals c.Id into conductores
                       from c in conductores.DefaultIfEmpty()
                       join o in contexto.PuntosCustodia on vj.OrigenId equals o.Id into origenes
                       from o in origenes.DefaultIfEmpty()
                       join d in contexto.PuntosCustodia on vj.DestinoId equals d.Id into destinos
                       from d in destinos.DefaultIfEmpty()
                       where (soloDeCliente == null || v.ClienteId == soloDeCliente)
                          && (f.ClienteId == null || v.ClienteId == f.ClienteId)
                          && (f.Estado == null || vj.Estado == f.Estado)
                          && (codigo == null || (vj.CodigoMtrack != null && vj.CodigoMtrack!.Contains(codigo!)))
                          && (vinParcial == null || (vinExacto ? v.Vin == vinParcial : v.Vin.Contains(vinParcial!)))
                          && (f.ConductorId == null || vj.ConductorId == f.ConductorId)
                          && (f.InspectorId == null || vj.InspectorId == f.InspectorId)
                          && (f.OrigenId == null || vj.OrigenId == f.OrigenId)
                          && (f.DestinoId == null || vj.DestinoId == f.DestinoId)
                          && (f.Desde == null || vj.CreatedAt >= f.Desde)
                          && (f.Hasta == null || vj.CreatedAt <= f.Hasta)
                       orderby vj.CreatedAt descending, vj.Id, v.Vin, e.Orden
                       select new FilaEtapa
                       {
                           EtapaId = e.Id,
                           ViajeId = vj.Id,
                           Vin = v.Vin,
                           Marca = v.Marca,
                           TipoVehiculo = v.TipoVehiculo,
                           Operacion = v.Operacion,
                           ClienteId = v.ClienteId,
                           CodigoMtrack = vj.CodigoMtrack,
                           Placa = vj.Placa,
                           Trailer = vj.Trailer,
                           Unidad = t.Nombre,
                           ConductorNombre = c.Nombre,
                           ConductorCedula = c.Cedula,
                           Etapa = ec.Nombre,
                           Lugar = pl.Nombre,
                           EstadoViaje = vj.Estado,
                           EstadoUnidad = vv.EstadoUnidad,
                           Origen = o.Nombre,
                           Destino = d.Nombre,
                       };

        // Cada etapa da dos filas (salida y llegada): se piden las etapas necesarias para pasar del maximo.
        var etapas = await consulta.Take((maximo / 2) + 1).ToListAsync(ct);
        var ultimas = new Dictionary<(Guid Etapa, string Momento), (int Version, string Estado, DateTimeOffset Inicio, DateTimeOffset? Cierre)>();
        foreach (var lote in etapas.Select(e => e.EtapaId).Chunk(1000))
        {
            foreach (var i in await contexto.Inspecciones.AsNoTracking().Where(i => lote.Contains(i.EtapaId)).Select(i => new { i.EtapaId, i.Momento, i.Version, i.Estado, i.IniciadaAt, i.CerradaAt }).ToListAsync(ct))
            {
                var clave = (i.EtapaId, i.Momento);
                if (!ultimas.TryGetValue(clave, out var actual) || i.Version > actual.Version)
                {
                    ultimas[clave] = (i.Version, i.Estado, i.IniciadaAt, i.CerradaAt);
                }
            }
        }

        var filas = new List<FilaExportViaje>(etapas.Count * 2);
        foreach (var e in etapas)
        {
            var contextoFila = new ContextoExportacion(
                e.Vin, e.Marca, e.TipoVehiculo, e.Operacion, e.ClienteId, e.CodigoMtrack, e.Placa, e.Trailer, e.Unidad, e.ConductorNombre, e.ConductorCedula, e.Etapa, e.Lugar);
            foreach (var momento in new[] { clsMomento.SALIDA, clsMomento.LLEGADA })
            {
                filas.Add(ultimas.TryGetValue((e.EtapaId, momento), out var i)
                    ? new FilaExportViaje(contextoFila, e.EstadoViaje, e.EstadoUnidad, e.Origen, e.Destino, momento, i.Version, i.Estado, i.Inicio, i.Cierre)
                    : new FilaExportViaje(contextoFila, e.EstadoViaje, e.EstadoUnidad, e.Origen, e.Destino, momento, null, "sin_inspeccion", null, null));
            }
        }

        return Limitar(filas, maximo);
    }

    // ------------------------------------------------------------------------------------------- inspecciones

    private sealed class FilaInspeccion
    {
        public required clsInspeccion Inspeccion { get; init; }

        public required FilaEtapa Contexto { get; init; }

        public string? Inspector { get; init; }

        public string? TipoCierre { get; init; }

        public string? EntregaNombre { get; init; }

        public DateTimeOffset? EntregaFecha { get; init; }

        public string? EntregaIp { get; init; }

        public string? RecibeNombre { get; init; }

        public DateTimeOffset? RecibeFecha { get; init; }

        public string? RecibeIp { get; init; }

        public int Extras { get; init; }
    }

    public async Task<ResultadoExportacion<FilaExportInspeccion>> InspeccionesAsync(FiltrosInspecciones f, Guid? clienteId, int maximo, CancellationToken ct = default)
    {
        var vinExacto = f.Vin?.Length == clsVin.LONGITUD_ISO;
        var consulta = from i in contexto.Inspecciones.AsNoTracking()
                       join e in contexto.Etapas on i.EtapaId equals e.Id
                       join v in contexto.Vehiculos on e.VehiculoId equals v.Id
                       join ec in contexto.EtapasCatalogo on e.EtapaCatalogoId equals ec.Id
                       join vj in contexto.Viajes on i.ViajeId equals vj.Id
                       join pl in contexto.PuntosCustodia on e.PuntoCustodiaId equals pl.Id into lugares
                       from pl in lugares.DefaultIfEmpty()
                       join t in contexto.TiposUnidad on vj.TipoUnidadId equals t.Id into tipos
                       from t in tipos.DefaultIfEmpty()
                       join c in contexto.Usuarios on vj.ConductorId equals c.Id into conductores
                       from c in conductores.DefaultIfEmpty()
                       join u in contexto.Usuarios on i.UsuarioId equals u.Id into inspectores
                       from u in inspectores.DefaultIfEmpty()
                       join tr in contexto.Transferencias on i.Id equals tr.InspeccionId into transferencias
                       from tr in transferencias.DefaultIfEmpty()
                       join en in contexto.Usuarios on tr.EntregaUsuarioId equals (Guid?)en.Id into entregas
                       from en in entregas.DefaultIfEmpty()
                       join re in contexto.Usuarios on tr.RecibeUsuarioId equals re.Id into recibos
                       from re in recibos.DefaultIfEmpty()
                       where (clienteId == null || v.ClienteId == clienteId)
                          && (f.Vin == null || (vinExacto ? v.Vin == f.Vin : v.Vin.Contains(f.Vin)))
                          && (f.UsuarioId == null || i.UsuarioId == f.UsuarioId)
                          && (f.ViajeId == null || i.ViajeId == f.ViajeId)
                          && (f.Momento == null || i.Momento == f.Momento)
                          && (f.Estado == null || i.Estado == f.Estado)
                          && (f.Proceso == null || ec.Codigo == f.Proceso)
                          && (f.Operacion == null || v.Operacion == f.Operacion)
                          && (f.Marca == null || v.Marca == f.Marca)
                          && (f.OrigenId == null || vj.OrigenId == f.OrigenId)
                          && (f.DestinoId == null || vj.DestinoId == f.DestinoId)
                          && (f.Desde == null || i.Fecha >= f.Desde)
                          && (f.Hasta == null || i.Fecha <= f.Hasta)
                       orderby i.Fecha descending, i.Id
                       select new FilaInspeccion
                       {
                           Inspeccion = i,
                           Contexto = new FilaEtapa
                           {
                               EtapaId = e.Id,
                               ViajeId = vj.Id,
                               Vin = v.Vin,
                               Marca = v.Marca,
                               TipoVehiculo = v.TipoVehiculo,
                               Operacion = v.Operacion,
                               ClienteId = v.ClienteId,
                               CodigoMtrack = vj.CodigoMtrack,
                               Placa = vj.Placa,
                               Trailer = vj.Trailer,
                               Unidad = t.Nombre,
                               ConductorNombre = c.Nombre,
                               ConductorCedula = c.Cedula,
                               Etapa = ec.Nombre,
                               Lugar = pl.Nombre,
                               EstadoViaje = vj.Estado,
                               EstadoUnidad = string.Empty,
                           },
                           Inspector = u.Nombre,
                           TipoCierre = tr.TipoCierre,
                           EntregaNombre = en.Nombre,
                           EntregaFecha = tr.FirmaEntregaAt,
                           EntregaIp = tr.FirmaEntregaIp,
                           RecibeNombre = re.Nombre,
                           RecibeFecha = tr.FirmaRecibeAt,
                           RecibeIp = tr.FirmaRecibeIp,
                           Extras = contexto.InspeccionExtras.Count(x => x.InspeccionId == i.Id),
                       };

        var filas = (await consulta.Take(maximo + 1).ToListAsync(ct)).Select(x => new FilaExportInspeccion(
            Contexto(x.Contexto), x.Inspeccion.Momento, x.Inspeccion.Version, x.Inspeccion.Estado, x.Inspeccion.IniciadaAt, x.Inspeccion.CerradaAt, x.Inspector,
            x.TipoCierre, x.EntregaNombre, x.EntregaFecha, x.EntregaIp, x.RecibeNombre, x.RecibeFecha, x.RecibeIp, x.Extras)).ToList();
        return Limitar(filas, maximo);
    }

    // ---------------------------------------------------------------------------------------------- hallazgos

    private sealed class FilaHallazgo
    {
        public required clsHallazgo Hallazgo { get; init; }

        public required clsInspeccion Inspeccion { get; init; }

        public required FilaEtapa Contexto { get; init; }

        public int? ZonaNumero { get; init; }

        public string? ZonaLado { get; init; }

        public string? ZonaNombre { get; init; }

        public string? ZonaAiag { get; init; }
    }

    public async Task<ResultadoExportacion<FilaExportHallazgo>> HallazgosAsync(FiltrosHallazgos f, Guid? clienteId, int maximo, CancellationToken ct = default)
    {
        var consulta = from h in contexto.Hallazgos.AsNoTracking()
                       join i in contexto.Inspecciones on h.InspeccionId equals i.Id
                       join e in contexto.Etapas on i.EtapaId equals e.Id
                       join v in contexto.Vehiculos on e.VehiculoId equals v.Id
                       join ec in contexto.EtapasCatalogo on e.EtapaCatalogoId equals ec.Id
                       join vj in contexto.Viajes on i.ViajeId equals vj.Id
                       join pl in contexto.PuntosCustodia on e.PuntoCustodiaId equals pl.Id into lugares
                       from pl in lugares.DefaultIfEmpty()
                       join t in contexto.TiposUnidad on vj.TipoUnidadId equals t.Id into tipos
                       from t in tipos.DefaultIfEmpty()
                       join c in contexto.Usuarios on vj.ConductorId equals c.Id into conductores
                       from c in conductores.DefaultIfEmpty()
                       join z in contexto.ZonasDano on h.ZonaId equals z.Id into zonas
                       from z in zonas.DefaultIfEmpty()
                       join cu in contexto.Cuadrantes on z.CuadranteId equals cu.Id into cuadrantes
                       from cu in cuadrantes.DefaultIfEmpty()
                       where (clienteId == null || v.ClienteId == clienteId)
                          && (f.EstadoSeguimiento == null || h.EstadoSeguimiento == f.EstadoSeguimiento)
                          && (f.Origen == null || h.Origen == f.Origen)
                          && (f.Revisado == null || (h.Origen == clsOrigenHallazgo.CONDUCTOR && (h.RevisadoAt != null) == f.Revisado))
                          && (f.ViajeId == null || i.ViajeId == f.ViajeId)
                          && (f.Vin == null || v.Vin == f.Vin)
                          && (f.InspeccionId == null || h.InspeccionId == f.InspeccionId)
                          && (f.Nivel == null || h.NivelSeveridad == f.Nivel)
                          && (f.CuadranteNumero == null || z.Numero == f.CuadranteNumero)
                          && (f.Lado == null || z.Lado == f.Lado)
                          && (f.Proceso == null || ec.Codigo == f.Proceso)
                          && (f.Desde == null || i.CerradaAt >= f.Desde)
                          && (f.Hasta == null || i.CerradaAt <= f.Hasta)
                       orderby i.CerradaAt ?? h.CreatedAt descending, h.Id
                       select new FilaHallazgo
                       {
                           Hallazgo = h,
                           Inspeccion = i,
                           Contexto = new FilaEtapa
                           {
                               EtapaId = e.Id,
                               ViajeId = vj.Id,
                               Vin = v.Vin,
                               Marca = v.Marca,
                               TipoVehiculo = v.TipoVehiculo,
                               Operacion = v.Operacion,
                               ClienteId = v.ClienteId,
                               CodigoMtrack = vj.CodigoMtrack,
                               Placa = vj.Placa,
                               Trailer = vj.Trailer,
                               Unidad = t.Nombre,
                               ConductorNombre = c.Nombre,
                               ConductorCedula = c.Cedula,
                               Etapa = ec.Nombre,
                               Lugar = pl.Nombre,
                               EstadoViaje = vj.Estado,
                               EstadoUnidad = string.Empty,
                           },
                           ZonaNumero = z.Numero,
                           ZonaLado = z.Lado,
                           ZonaNombre = cu.Nombre,
                           ZonaAiag = z.AiagArea,
                       };

        var datos = await consulta.Take(maximo + 1).ToListAsync(ct);
        var ids = datos.Select(d => d.Hallazgo.Id).ToList();
        var fotos = new Dictionary<Guid, (DateTimeOffset Fecha, double? Lat, double? Lng)>();
        foreach (var lote in ids.Chunk(1000))
        {
            var evidencias = await contexto.Evidencias.AsNoTracking()
                .Where(e => e.HallazgoId != null && lote.Contains(e.HallazgoId.Value) && !e.EsSello && !e.EsConstancia)
                .OrderBy(e => e.CreatedAt)
                .Select(e => new { HallazgoId = e.HallazgoId!.Value, e.SelloFecha, e.SelloLat, e.SelloLng, e.SinUbicacion })
                .ToListAsync(ct);
            foreach (var e in evidencias)
            {
                // La foto propia del hallazgo es la primera que se tomo.
                fotos.TryAdd(e.HallazgoId, (e.SelloFecha, e.SinUbicacion ? null : e.SelloLat, e.SinUbicacion ? null : e.SelloLng));
            }
        }

        var filas = datos.Select(x =>
        {
            var foto = fotos.TryGetValue(x.Hallazgo.Id, out var f2) ? f2 : default((DateTimeOffset Fecha, double? Lat, double? Lng)?);
            return new FilaExportHallazgo(
                Contexto(x.Contexto), x.Inspeccion.Momento, x.Inspeccion.Version, x.Inspeccion.Estado, x.Inspeccion.IniciadaAt, x.Inspeccion.CerradaAt,
                x.ZonaLado, x.ZonaNumero, x.ZonaNombre, x.ZonaAiag, x.Hallazgo.ClaseDano, x.Hallazgo.TamanoDano, x.Hallazgo.NivelSeveridad, x.Hallazgo.EsPreexistente, x.Hallazgo.Origen,
                x.Hallazgo.EstadoSeguimiento, foto?.Fecha, foto?.Lat, foto?.Lng);
        }).ToList();
        return Limitar(filas, maximo);
    }

    private static ContextoExportacion Contexto(FilaEtapa e) =>
        new(e.Vin, e.Marca, e.TipoVehiculo, e.Operacion, e.ClienteId, e.CodigoMtrack, e.Placa, e.Trailer, e.Unidad, e.ConductorNombre, e.ConductorCedula, e.Etapa, e.Lugar);

    private static ResultadoExportacion<T> Limitar<T>(List<T> filas, int maximo) =>
        filas.Count > maximo ? new ResultadoExportacion<T>(filas.Take(maximo).ToList(), true) : new ResultadoExportacion<T>(filas, false);
}
