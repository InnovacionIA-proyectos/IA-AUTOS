using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>Foto sellada con fecha y ubicacion. Append-only por diseno.</summary>
public sealed class clsEvidencia : clsEntidadBase
{
    private clsEvidencia()
    {
    }

    public Guid InspeccionId { get; private set; }

    public Guid? HallazgoId { get; private set; }

    public string StoragePath { get; private set; } = string.Empty;

    public DateTimeOffset SelloFecha { get; private set; }

    public double? SelloLat { get; private set; }

    public double? SelloLng { get; private set; }

    public static clsEvidencia Crear(
        Guid inspeccionId,
        Guid? hallazgoId,
        string storagePath,
        DateTimeOffset selloFecha,
        double? selloLat,
        double? selloLng,
        DateTimeOffset ahora)
    {
        clsValidador.Latitud(selloLat, "sello_lat");
        clsValidador.Longitud(selloLng, "sello_lng");

        var evidencia = new clsEvidencia
        {
            InspeccionId = inspeccionId,
            HallazgoId = hallazgoId,
            StoragePath = clsValidador.Requerido(storagePath, "storage_path es requerido"),
            SelloFecha = selloFecha,
            SelloLat = selloLat,
            SelloLng = selloLng,
        };
        evidencia.Inicializar(ahora);
        return evidencia;
    }
}
