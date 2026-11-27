using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>La foto de una evidencia con la forma que el viaje usa para sus fotos (la misma de la novedad del viaje): el enlace firmado lo escribe la API al responder.</summary>
public static class clsFotosDelViaje
{
    /// <summary>
    /// La evidencia como foto del viaje. Con <paramref name="sinCoordenadasSiNoHay"/> una foto tomada sin senal de GPS lleva la latitud y la longitud vacias; sin el, valen 0 como en las demas fotos del viaje.
    /// </summary>
    public static FotoNovedadViajeDto DeEvidencia(clsEvidencia e, bool sinCoordenadasSiNoHay = false) => new(
        e.SelloFecha,
        sinCoordenadasSiNoHay && e.SinUbicacion ? null : e.SelloLat,
        sinCoordenadasSiNoHay && e.SinUbicacion ? null : e.SelloLng,
        clsUrlsArchivos.ParaStoragePath(e.StoragePath),
        null,
        e.HashSha256,
        e.Precision,
        e.DispositivoId,
        e.SinUbicacion,
        e.StoragePath);
}
