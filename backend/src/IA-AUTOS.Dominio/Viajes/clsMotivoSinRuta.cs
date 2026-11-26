using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Viajes;

/// <summary>
/// Por que el conductor todavia no puede registrar la ruta de una unidad («Reportar novedad» y «Llegue»): la unidad no sale del viaje (no sale o bajada) o la entrega de su cargue no esta cerrada (la llegada del cargue
/// cerrada y su transferencia con un tipo de cierre). Es la misma regla que aplica el servidor al recibir esas operaciones.
/// </summary>
public static class clsMotivoSinRuta
{
    /// <summary>El motivo por el que la unidad no puede registrar ruta, o nulo si puede.</summary>
    public static string? De(string estadoUnidad, bool entregaDelCargueCerrada)
    {
        if (estadoUnidad != clsEstadoUnidad.ACTIVA)
        {
            return clsMotivosRechazo.UNIDAD_NO_SALE;
        }

        return entregaDelCargueCerrada ? null : clsMotivosRechazo.ENTREGA_DEL_CARGUE_SIN_CERRAR;
    }
}
