using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>Servicio de dominio: arma la ruta estandar Cargue, Transporte, Descargue de un vehiculo.</summary>
public static class clsRutaCustodia
{
    /// <summary>
    /// Devuelve las etapas de la ruta estandar. Falla si el catalogo no contiene alguno de los puntos.
    /// </summary>
    public static IReadOnlyList<clsEtapa> CrearEtapasEstandar(
        Guid vehiculoId,
        IReadOnlyDictionary<string, Guid> puntoIdPorNombre,
        DateTimeOffset ahora)
    {
        var etapas = new List<clsEtapa>(clsPuntoCustodiaNombre.RUTA_CARGA.Length);
        for (var i = 0; i < clsPuntoCustodiaNombre.RUTA_CARGA.Length; i++)
        {
            var nombre = clsPuntoCustodiaNombre.RUTA_CARGA[i];
            if (!puntoIdPorNombre.TryGetValue(nombre, out var puntoId))
            {
                throw new clsErrorInternoException($"Falta el punto de custodia \"{nombre}\" en el catálogo");
            }

            etapas.Add(clsEtapa.Crear(vehiculoId, puntoId, i + 1, true, ahora));
        }

        return etapas;
    }
}
