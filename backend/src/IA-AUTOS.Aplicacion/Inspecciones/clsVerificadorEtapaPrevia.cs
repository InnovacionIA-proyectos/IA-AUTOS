using IAAutos.Aplicacion.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// La salida de una etapa exige que la etapa anterior del mismo VIN en el viaje este terminada: su inspeccion de llegada cerrada y ninguna transferencia suya sin tipo de cierre
/// (409 ETAPA_PREVIA_SIN_CIERRE con la etapa que falta cerrar y el motivo). La primera etapa no tiene anterior.
/// </summary>
public sealed class clsVerificadorEtapaPrevia(IEtapaRepository etapas, IInspeccionRepository inspecciones, ICatalogosLectura catalogos)
{
    public const string MENSAJE_ENTREGA_DEL_CARGUE = "Falta cerrar la entrega del cargue: fírmenla, recíbanla con reservas o registren la no firma";

    /// <summary>
    /// <paramref name="exigeEntregaDelCargue"/>: cuando la etapa anterior es el Cargue, ademas de la llegada cerrada la entrega del cargue tiene que EXISTIR y tener un tipo de cierre (una unidad sin transferencia no avanza); es la
    /// regla de las operaciones del conductor en ruta. Para cualquier otra etapa anterior solo cuenta que no haya una transferencia sin cierre.
    /// </summary>
    public async Task AsegurarCerradaAsync(clsEtapa etapa, CancellationToken ct = default, bool exigeEntregaDelCargue = false)
    {
        var previa = await etapas.ObtenerPreviaAsync(etapa.ViajeId, etapa.VehiculoId, etapa.Orden, ct);
        if (previa is null)
        {
            return;
        }

        var catalogoEtapa = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == previa.EtapaCatalogoId);
        var entregaDelCargue = exigeEntregaDelCargue && catalogoEtapa?.Codigo == clsCodigosEtapa.CARGUE;
        var motivo = !await inspecciones.TieneLlegadaCerradaAsync(previa.Id, ct)
            ? clsEtapaPreviaSinCierreException.MOTIVO_SIN_LLEGADA
            : await inspecciones.TieneTransferenciaSinCierreAsync(previa.Id, ct) || (entregaDelCargue && !await inspecciones.TieneTransferenciaConCierreAsync(previa.Id, ct))
                ? clsEtapaPreviaSinCierreException.MOTIVO_TRANSFERENCIA_ABIERTA
                : null;
        if (motivo is null)
        {
            return;
        }

        throw new clsEtapaPreviaSinCierreException(
            previa.Id,
            catalogoEtapa?["nombre"] as string ?? catalogoEtapa?.Codigo ?? "anterior",
            motivo,
            entregaDelCargue && motivo == clsEtapaPreviaSinCierreException.MOTIVO_TRANSFERENCIA_ABIERTA ? MENSAJE_ENTREGA_DEL_CARGUE : null);
    }
}
