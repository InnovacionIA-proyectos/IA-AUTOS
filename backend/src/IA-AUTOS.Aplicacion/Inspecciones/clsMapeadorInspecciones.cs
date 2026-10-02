using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Conversiones de entidades de dominio a DTO de lectura, compartidas por inventario, informe y escritura.</summary>
public static class clsMapeadorInspecciones
{
    public static InspeccionDto AInspeccion(clsInspeccion i) => new(
        i.Id, i.EtapaId, i.UsuarioId, i.Momento, i.Fecha, i.Lat, i.Lng, i.Estado, i.ConductorNombre, i.Placa, i.Trailer, i.Notas,
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
