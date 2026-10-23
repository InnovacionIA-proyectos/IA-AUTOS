using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>Conversiones de lo guardado de un viaje a DTO de lectura, compartidas por el viaje y el inventario del VIN.</summary>
public static class clsMapeadorViajes
{
    /// <summary>El detalle de una unidad que no sale; nulo mientras la unidad esta activa (o si los datos del «no sale» estan incompletos).</summary>
    public static NoSaleDto? ANoSale(
        string estadoUnidad, string? motivo, Guid? hallazgoId, Guid? marcadoPor, string? marcadoPorNombre, DateTimeOffset? fecha, string? vinReemplazo) =>
        estadoUnidad == clsEstadoUnidad.NO_SALE && motivo is not null && hallazgoId is { } hallazgo && marcadoPor is { } marcador && fecha is { } cuando
            ? new NoSaleDto(motivo, hallazgo, marcador, marcadoPorNombre, cuando, vinReemplazo)
            : null;
}
