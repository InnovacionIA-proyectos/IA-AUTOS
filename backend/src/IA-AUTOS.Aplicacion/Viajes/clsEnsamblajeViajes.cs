using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Sofasa;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// Agrega VIN a un viaje: crea el enlace, las etapas de la ruta (Cargue en el origen, Transporte, Descargue en el destino) y vincula
/// al viaje las ordenes Sofasa de ese VIN que aun no tenian uno. Lo comparten la creacion de viajes, la asignacion de VIN y la
/// carga por Excel. Solo agrega al contexto: quien lo llama confirma.
/// </summary>
public sealed class clsEnsamblajeViajes(IViajeRepository viajes, IEtapaRepository etapas, IOrdenSofasaRepository ordenes, IReloj reloj)
{
    public async Task AgregarVehiculosAsync(
        clsViaje viaje,
        IReadOnlyCollection<clsVehiculo> vehiculos,
        bool conPersonalApoyo,
        IReadOnlyList<clsEtapaDeRuta> ruta,
        CancellationToken ct)
    {
        foreach (var vehiculo in vehiculos)
        {
            viajes.Agregar(clsViajeVehiculo.Crear(viaje.Id, vehiculo.Id, reloj.Ahora));
            foreach (var etapa in clsRutaCustodia.CrearEtapasEstandar(viaje.Id, vehiculo.Id, ruta, viaje.OrigenId, viaje.DestinoId, conPersonalApoyo, reloj.Ahora))
            {
                etapas.Agregar(etapa);
            }
        }

        await VincularOrdenesAsync(viaje, vehiculos.Select(v => v.Vin).ToList(), ct);
    }

    /// <summary>
    /// Las ordenes Sofasa de esos VIN sin viaje pasan a este, salvo que la orden traiga un codigo MTrack distinto: ese codigo y el VIN
    /// juntos identifican el viaje.
    /// </summary>
    private async Task VincularOrdenesAsync(clsViaje viaje, IReadOnlyCollection<string> vines, CancellationToken ct)
    {
        foreach (var orden in await ordenes.ObtenerSinViajePorVinesAsync(vines, ct))
        {
            if (orden.CodigoMtrack is null || string.Equals(orden.CodigoMtrack, viaje.CodigoMtrack, StringComparison.OrdinalIgnoreCase))
            {
                orden.AsignarViaje(viaje.Id, reloj.Ahora);
            }
        }
    }
}
