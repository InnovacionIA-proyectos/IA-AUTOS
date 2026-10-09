using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>Servicio de dominio: arma la ruta estandar de un vehiculo con las etapas activas del catalogo (hoy Cargue, Transporte, Descargue).</summary>
public static class clsRutaCustodia
{
    /// <summary>
    /// Devuelve una etapa por cada etapa del catalogo recibida, en el mismo orden (1, 2, 3...). Falla si el catalogo no tiene etapas.
    /// </summary>
    public static IReadOnlyList<clsEtapa> CrearEtapasEstandar(
        Guid vehiculoId,
        IReadOnlyList<Guid> etapasCatalogoOrdenadas,
        DateTimeOffset ahora)
    {
        if (etapasCatalogoOrdenadas.Count == 0)
        {
            throw new clsErrorInternoException("El catálogo de etapas no tiene valores activos para armar la ruta");
        }

        var etapas = new List<clsEtapa>(etapasCatalogoOrdenadas.Count);
        for (var i = 0; i < etapasCatalogoOrdenadas.Count; i++)
        {
            etapas.Add(clsEtapa.Crear(vehiculoId, etapasCatalogoOrdenadas[i], null, i + 1, true, ahora));
        }

        return etapas;
    }
}
