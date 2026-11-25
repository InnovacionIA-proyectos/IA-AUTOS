using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// El fin del viaje: cuando todas sus unidades activas tienen la revision de llegada del Descargue cerrada y la entrega cerrada (firmada, con reservas o con una sola firma) sin ser un rechazo, el viaje en curso pasa a
/// finalizado; con una entrega pendiente, o un carro rechazado que aun no se entrega en su destino nuevo, sigue en curso. Es la unica auditoria (<c>viaje.estado.finalizado</c>) de ese cambio y la unica cuenta de lo que
/// falta por entregar, la misma para el cierre de la entrega por parada y para el cierre a mano del viaje.
/// </summary>
public sealed class clsFinalizadorDelViaje(
    IViajeRepository viajes,
    IInspeccionRepository inspecciones,
    ITransferenciaCustodiaRepository transferencias,
    ICatalogosLectura catalogos,
    IAuditor auditor,
    IReloj reloj)
{
    /// <summary>Finaliza el viaje en curso que ya completo todas sus entregas: lo cambia de estado y lo audita. No guarda: lo guarda quien lo llama con el resto de su operacion.</summary>
    public async Task FinalizarAsync(clsViaje viaje, CancellationToken ct = default)
    {
        viaje.CambiarEstado(clsEstadoViaje.FINALIZADO, reloj.Ahora);
        await auditor.RegistrarDeViajeAsync(
            $"viaje.estado.{viaje.Estado}", "viajes", viaje.Id, viaje.Id, new DetalleAuditoria(new { estado = clsEstadoViaje.EN_CURSO }, new { estado = viaje.Estado }), ct: ct);
    }

    /// <summary>Cuantas unidades activas del viaje siguen sin entregar: sin la revision de llegada del Descargue cerrada o sin su entrega cerrada, o con la entrega rechazada.</summary>
    public async Task<int> PendientesAsync(clsViaje viaje, CancellationToken ct = default)
    {
        var codigosPorEtapa = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).ToDictionary(v => v.Id, v => v.Codigo);
        var etapas = await viajes.ObtenerEtapasAsync(viaje.Id, null, ct);
        var unidades = (await viajes.ObtenerVehiculosAsync(viaje.Id, ct)).Where(e => e.EstaActiva).ToList();
        var pendientes = 0;
        foreach (var enlace in unidades)
        {
            var descargue = etapas.FirstOrDefault(e => e.VehiculoId == enlace.VehiculoId && codigosPorEtapa.TryGetValue(e.EtapaCatalogoId, out var codigo) && codigo == clsCodigosEtapa.DESCARGUE);
            var llegada = descargue is null ? null : await inspecciones.ObtenerUltimaDeEtapaAsync(descargue.Id, clsMomento.LLEGADA, ct);
            var transferencia = llegada is null ? null : await transferencias.ObtenerPorInspeccionAsync(llegada.Id, ct);
            if (llegada is not { EstaCerrada: true } || transferencia is not { EstaCerrada: true, EstaRechazada: false })
            {
                pendientes++;
            }
        }

        return pendientes;
    }
}
