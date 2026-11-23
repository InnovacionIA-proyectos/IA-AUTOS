using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>Conversiones de lo guardado de un viaje a DTO de lectura, compartidas por el viaje y el inventario del VIN.</summary>
public static class clsMapeadorViajes
{
    /// <summary>
    /// El detalle de una unidad que no sale; nulo si la unidad no esta en ese estado. <paramref name="motivos"/> son los nombres del catalogo <c>motivos-no-sale</c> por codigo: la etiqueta legible sale de ahi. Un «no sale» de
    /// antes del motivo de catalogo se lee con el motivo <c>dano_v1_plus</c> y su texto de siempre.
    /// </summary>
    public static NoSaleDto? ANoSale(
        string estadoUnidad,
        string? motivoCodigo,
        string? motivoTexto,
        Guid? hallazgoId,
        Guid? marcadoPor,
        string? marcadoPorNombre,
        DateTimeOffset? fecha,
        string? vinReemplazo,
        IReadOnlyDictionary<string, string> motivos)
    {
        if (estadoUnidad != clsEstadoUnidad.NO_SALE || marcadoPor is not { } marcador || fecha is not { } cuando)
        {
            return null;
        }

        var codigo = motivoCodigo ?? IAAutos.Dominio.Catalogos.clsCodigosMotivoNoSale.DANO_V1_PLUS;
        return new NoSaleDto(codigo, motivos.GetValueOrDefault(codigo) ?? codigo, motivoTexto, hallazgoId, marcador, marcadoPorNombre, cuando, vinReemplazo);
    }

    /// <summary>El detalle de una unidad bajada del viaje; nulo si no esta bajada.</summary>
    public static BajadoDto? ABajado(string estadoUnidad, string? motivo, Guid? bajadoPor, string? bajadoPorNombre, DateTimeOffset? fecha) =>
        estadoUnidad == clsEstadoUnidad.BAJADO && motivo is not null && bajadoPor is { } quien && fecha is { } cuando ? new BajadoDto(motivo, quien, bajadoPorNombre, cuando) : null;
}
