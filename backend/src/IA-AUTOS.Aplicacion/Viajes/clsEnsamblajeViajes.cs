using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Sofasa;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// Agrega VIN a un viaje: crea el enlace con el destino de la unidad, las etapas de la ruta (Cargue en el origen, Transporte, Descargue en el destino de la unidad) y vincula
/// al viaje las ordenes Sofasa de ese VIN que aun no tenian uno. Lo comparten la creacion de viajes, la asignacion de VIN y la
/// carga por Excel. Solo agrega al contexto: quien lo llama confirma.
/// </summary>
public sealed class clsEnsamblajeViajes(IViajeRepository viajes, IEtapaRepository etapas, IOrdenSofasaRepository ordenes, ICatalogosLectura catalogos, IReloj reloj)
{
    /// <summary>
    /// <paramref name="lugarDestinoPorVehiculo"/> trae el lugar de destino de cada unidad (un lugar del catalogo <c>lugares</c>): el Descargue de la unidad se crea en el tipo de ese lugar. Una unidad sin
    /// entrada (o con valor nulo, como la que reemplaza a una de un viaje anterior al destino por unidad) no tiene destino y su Descargue queda sin lugar.
    /// </summary>
    public async Task AgregarVehiculosAsync(
        clsViaje viaje,
        IReadOnlyCollection<clsVehiculo> vehiculos,
        bool conPersonalApoyo,
        IReadOnlyList<clsEtapaDeRuta> ruta,
        CancellationToken ct,
        IReadOnlyDictionary<Guid, Guid?>? lugarDestinoPorVehiculo = null)
    {
        var puntoDeLosLugares = lugarDestinoPorVehiculo is { Count: > 0 } ? await PuntoDeLosLugaresAsync(ct) : new Dictionary<Guid, Guid>();
        foreach (var vehiculo in vehiculos)
        {
            Guid? lugarDestino = lugarDestinoPorVehiculo is not null && lugarDestinoPorVehiculo.TryGetValue(vehiculo.Id, out var destino) ? destino : null;
            viajes.Agregar(clsViajeVehiculo.Crear(viaje.Id, vehiculo.Id, reloj.Ahora, lugarDestino));
            Guid? puntoDestino = lugarDestino is { } lugar && puntoDeLosLugares.TryGetValue(lugar, out var punto) ? punto : viaje.DestinoId;
            foreach (var etapa in clsRutaCustodia.CrearEtapasEstandar(viaje.Id, vehiculo.Id, ruta, viaje.OrigenId, puntoDestino, conPersonalApoyo, reloj.Ahora))
            {
                etapas.Agregar(etapa);
            }
        }

        await VincularOrdenesAsync(viaje, vehiculos.Select(v => v.Vin).ToList(), ct);
    }

    /// <summary>
    /// El tipo de lugar (<c>puntos-custodia</c>) de cada lugar del catalogo <c>lugares</c>: la etapa de una unidad se crea en el tipo del lugar donde se entrega. Los lugares inactivos tambien cuentan (una unidad que ya
    /// iba a uno conserva su etapa).
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, Guid>> PuntoDeLosLugaresAsync(CancellationToken ct)
    {
        var puntos = (await catalogos.ValoresAsync(clsNombresCatalogo.PUNTOS_CUSTODIA, ct)).Where(p => p.Codigo is not null).ToDictionary(p => p.Codigo!, p => p.Id);
        var resultado = new Dictionary<Guid, Guid>();
        foreach (var lugar in await catalogos.ValoresAsync(clsNombresCatalogo.LUGARES, ct))
        {
            if (lugar.TryGetValue("tipo", out var tipo) && tipo is string codigo && puntos.TryGetValue(codigo, out var punto))
            {
                resultado[lugar.Id] = punto;
            }
        }

        return resultado;
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
