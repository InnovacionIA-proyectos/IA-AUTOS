using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// La guarda de la unidad del viaje: un carro que no sale (o que se bajo del viaje) no se revisa ni se firma. Crear, corregir, agregar hallazgos, extras o fotos, cerrar una inspeccion de su cargue y firmar o cerrar
/// su transferencia responden 409 «La unidad no sale de este viaje» (<c>details.motivo: unidad_no_sale</c>, o <c>unidad_bajada</c> si se bajo); vuelven a aceptarse despues de «vuelve a salir».
/// </summary>
public sealed class clsGuardaDeUnidad(IViajeRepository viajes, IEtapaRepository etapas, IInspeccionRepository inspecciones)
{
    /// <summary>409 si la unidad del viaje no esta activa; sin vinculo con el viaje (inspecciones anteriores a los viajes) no hay nada que vigilar.</summary>
    public async Task AsegurarActivaAsync(Guid viajeId, Guid vehiculoId, CancellationToken ct = default)
    {
        var enlace = await viajes.ObtenerVehiculoAsync(viajeId, vehiculoId, ct);
        if (enlace is null || enlace.EstaActiva)
        {
            return;
        }

        throw enlace.Bajado
            ? new clsConflictoException("La unidad se bajó de este viaje", clsMotivosRechazo.UNIDAD_BAJADA)
            : new clsConflictoException("La unidad no sale de este viaje", clsMotivosRechazo.UNIDAD_NO_SALE);
    }

    /// <summary>Lo mismo para la unidad de una inspeccion ya creada (la unidad de su etapa en su viaje).</summary>
    public async Task AsegurarActivaDeInspeccionAsync(clsInspeccion inspeccion, CancellationToken ct = default)
    {
        if (await etapas.ObtenerPorIdAsync(inspeccion.EtapaId, ct) is { } etapa)
        {
            await AsegurarActivaAsync(etapa.ViajeId, etapa.VehiculoId, ct);
        }
    }

    /// <summary>Lo mismo para la unidad de la inspeccion con ese identificador (la transferencia se firma sobre una inspeccion); no hace nada si la inspeccion no existe: de eso se encarga quien la pide.</summary>
    public async Task AsegurarActivaDeInspeccionAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        if (await inspecciones.ObtenerPorIdAsync(inspeccionId, ct) is { } inspeccion)
        {
            await AsegurarActivaDeInspeccionAsync(inspeccion, ct);
        }
    }
}
