using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Conversiones de entidades de dominio a DTO de lectura, compartidas por inventario, informe y escritura.</summary>
public static class clsMapeadorInspecciones
{
    public static InspeccionDto AInspeccion(clsInspeccion i, NovedadesDeInspeccionDto novedades) => AInspeccion(i, null, null, null, novedades);

    /// <summary>
    /// Con los datos del viaje de respaldo (lo capturado antes de los viajes, si lo hay, tiene prioridad sobre ellos) y el motivo y las novedades de proceso de la inspeccion
    /// (<see cref="NovedadesDeInspeccionDto.VACIAS"/> si no tiene).
    /// </summary>
    public static InspeccionDto AInspeccion(clsInspeccion i, string? placaViaje, string? trailerViaje, string? conductorViaje, NovedadesDeInspeccionDto novedades) => new(
        i.Id, i.EtapaId, i.ViajeId, i.UsuarioId, i.Momento, i.Fecha, i.Lat, i.Lng, i.Estado,
        i.ConductorNombre ?? conductorViaje, i.Placa ?? placaViaje, i.Trailer ?? trailerViaje, i.Notas,
        i.CreatedAt, i.UpdatedAt,
        i.IniciadaAt, i.CerradaAt, i.Version, i.CorrigeInspeccionId, clsMomento.Nombre(i.Momento),
        novedades.Motivo, novedades.Novedades);

    /// <summary>
    /// El hallazgo con lo que se lee de otras tablas: su zona (y el nombre de su cuadrante), su nivel de severidad y lo que le falta a su evidencia. Sin zona
    /// (hallazgo historico sin ubicar) la zona de texto es la parte del vehiculo que se escribio entonces.
    /// </summary>
    public static HallazgoDto AHallazgo(clsHallazgo h, clsZonaDano? zona, string? cuadranteNombre, clsNivelSeveridad? nivel, IReadOnlyList<string> evidenciaFaltante) => new(
        h.Id, h.InspeccionId, h.ParteVehiculo, h.ClaseDano, h.TamanoDano,
        h.CoordsDiagrama is null ? null : new CoordsDiagramaDto(h.CoordsDiagrama.X, h.CoordsDiagrama.Y),
        zona?.Numero, zona?.Lado, zona is { Codigo: not null } ? zona.Codigo : null,
        cuadranteNombre, TextoDeZona(zona, cuadranteNombre) ?? h.ParteVehiculo,
        h.TipoNovedad, h.NivelSeveridad, nivel?.Nombre, nivel?.Accion,
        h.Origen, h.Nota, h.EsPreexistente, h.HallazgoOrigenId, h.ConfirmadoPor, h.EstadoSeguimiento,
        evidenciaFaltante.Count == 0, evidenciaFaltante,
        h.RevisadoPor, h.RevisadoAt, h.CreatedAt, h.UpdatedAt);

    public static HallazgoCreadoDto AHallazgoCreado(HallazgoDto hallazgo, clsEvidencia foto) => new(hallazgo, AEvidencia(foto));

    /// <summary>
    /// El texto de una zona para mostrar: «Izquierdo 7 · Puerta delantera» (con lado), «Techo 30» (sin lado) o el nombre de una zona provisional
    /// («Frente»). Nulo si no hay zona.
    /// </summary>
    public static string? TextoDeZona(clsZonaDano? zona, string? cuadranteNombre)
    {
        if (zona is null)
        {
            return null;
        }

        if (zona.EsProvisional)
        {
            return zona.Nombre;
        }

        return zona.Lado is { Length: > 0 } lado
            ? $"{char.ToUpperInvariant(lado[0])}{lado[1..]} {zona.Numero} · {cuadranteNombre}"
            : $"{cuadranteNombre} {zona.Numero}";
    }

    public static EvidenciaDto AEvidencia(clsEvidencia e) => new(
        e.Id, e.InspeccionId, e.HallazgoId, e.SelloFecha, e.SelloLat, e.SelloLng,
        clsUrlsArchivos.ParaStoragePath(e.StoragePath), null, e.CreatedAt, e.UpdatedAt,
        e.VistaId, e.HashSha256, e.SelloServidorAt, e.Precision, e.DispositivoId, e.Nota, e.SinUbicacion, e.EsConstancia, e.EsSello, e.StoragePath);

    public static TransferenciaDto ATransferencia(clsTransferenciaCustodia t) => new(
        t.Id, t.InspeccionId, t.EntregaUsuarioId, t.RecibeUsuarioId, t.EstaCompleta, t.FirmadaAt, t.CreatedAt, t.UpdatedAt,
        t.TipoCierre, t.FirmaEntregaAt, t.FirmaRecibeAt, t.ReservasTexto, t.NoFirmaParte, t.MotivoNoFirma, t.TestigoUsuarioId, t.EvidenciaConstanciaId);
}
