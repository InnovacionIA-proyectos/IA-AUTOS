using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Conversiones de entidades de dominio a DTO de lectura, compartidas por inventario, informe y escritura.</summary>
public static class clsMapeadorInspecciones
{
    public static InspeccionDto AInspeccion(clsInspeccion i) => AInspeccion(i, null, null, null);

    /// <summary>Con los datos del viaje de respaldo: lo capturado antes de los viajes (si lo hay) tiene prioridad sobre ellos.</summary>
    public static InspeccionDto AInspeccion(clsInspeccion i, string? placaViaje, string? trailerViaje, string? conductorViaje) => new(
        i.Id, i.EtapaId, i.ViajeId, i.UsuarioId, i.Momento, i.Fecha, i.Lat, i.Lng, i.Estado,
        i.ConductorNombre ?? conductorViaje, i.Placa ?? placaViaje, i.Trailer ?? trailerViaje, i.Notas,
        i.CreatedAt, i.UpdatedAt);

    public static HallazgoDto AHallazgo(clsHallazgo h) => new(
        h.Id, h.InspeccionId, h.ParteVehiculo, h.ClaseDano, h.TamanoDano,
        h.CoordsDiagrama is null ? null : new CoordsDiagramaDto(h.CoordsDiagrama.X, h.CoordsDiagrama.Y),
        h.CreatedAt, h.UpdatedAt);

    public static EvidenciaDto AEvidencia(clsEvidencia e) => new(
        e.Id, e.InspeccionId, e.HallazgoId, e.SelloFecha, e.SelloLat, e.SelloLng,
        clsUrlsArchivos.ParaStoragePath(e.StoragePath), e.CreatedAt, e.UpdatedAt);

    public static TransferenciaDto ATransferencia(clsTransferenciaCustodia t) => new(
        t.Id, t.InspeccionId, t.EntregaUsuarioId, t.RecibeUsuarioId, t.EstaCompleta, t.FirmadaAt, t.CreatedAt, t.UpdatedAt);
}
