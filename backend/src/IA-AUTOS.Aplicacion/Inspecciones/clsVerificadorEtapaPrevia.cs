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
    public async Task AsegurarCerradaAsync(clsEtapa etapa, CancellationToken ct = default)
    {
        var previa = await etapas.ObtenerPreviaAsync(etapa.ViajeId, etapa.VehiculoId, etapa.Orden, ct);
        if (previa is null)
        {
            return;
        }

        var motivo = !await inspecciones.TieneLlegadaCerradaAsync(previa.Id, ct)
            ? clsEtapaPreviaSinCierreException.MOTIVO_SIN_LLEGADA
            : await inspecciones.TieneTransferenciaSinCierreAsync(previa.Id, ct) ? clsEtapaPreviaSinCierreException.MOTIVO_TRANSFERENCIA_ABIERTA : null;
        if (motivo is null)
        {
            return;
        }

        var nombre = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == previa.EtapaCatalogoId);
        throw new clsEtapaPreviaSinCierreException(previa.Id, nombre?["nombre"] as string ?? nombre?.Codigo ?? "anterior", motivo);
    }
}
