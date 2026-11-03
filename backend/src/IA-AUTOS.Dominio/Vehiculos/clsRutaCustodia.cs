using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>Codigos de las etapas del catalogo que tienen un lugar propio dentro de un viaje.</summary>
public static class clsCodigosEtapa
{
    public const string CARGUE = "cargue";
    public const string TRANSPORTE = "transporte";
    public const string DESCARGUE = "descargue";
}

/// <summary>Una etapa activa del catalogo, en el orden de la ruta.</summary>
public sealed record clsEtapaDeRuta(Guid Id, string Codigo);

/// <summary>Servicio de dominio: arma la ruta de un VIN dentro de un viaje con las etapas activas del catalogo (hoy Cargue, Transporte, Descargue).</summary>
public static class clsRutaCustodia
{
    /// <summary>
    /// Devuelve una etapa por cada etapa del catalogo recibida, en el mismo orden (1, 2, 3...). El Cargue se hace en el origen del
    /// viaje y el Descargue en su destino; las demas etapas quedan sin lugar. El personal de apoyo se marca solo en el Cargue.
    /// Falla si el catalogo no tiene etapas.
    /// </summary>
    public static IReadOnlyList<clsEtapa> CrearEtapasEstandar(
        Guid viajeId,
        Guid vehiculoId,
        IReadOnlyList<clsEtapaDeRuta> rutaOrdenada,
        Guid? origenId,
        Guid? destinoId,
        DateTimeOffset ahora)
    {
        if (rutaOrdenada.Count == 0)
        {
            throw new clsErrorInternoException("El catálogo de etapas no tiene valores activos para armar la ruta");
        }

        var etapas = new List<clsEtapa>(rutaOrdenada.Count);
        for (var i = 0; i < rutaOrdenada.Count; i++)
        {
            var codigo = rutaOrdenada[i].Codigo;
            var lugar = codigo switch
            {
                clsCodigosEtapa.CARGUE => origenId,
                clsCodigosEtapa.DESCARGUE => destinoId,
                _ => null,
            };
            etapas.Add(clsEtapa.Crear(vehiculoId, viajeId, rutaOrdenada[i].Id, lugar, i + 1, true, ahora));
        }

        return etapas;
    }
}
