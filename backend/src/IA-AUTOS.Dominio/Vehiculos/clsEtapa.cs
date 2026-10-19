using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>
/// Tramo de un VIN dentro de un viaje: una etapa del catalogo (cargue, transporte...) y, si se conoce, el lugar donde se hace.
/// Las etapas nacen al agregar el VIN a un viaje.
/// </summary>
public sealed class clsEtapa : clsEntidadBase
{
    private clsEtapa()
    {
    }

    public Guid VehiculoId { get; private set; }

    /// <summary>Viaje al que pertenece la etapa.</summary>
    public Guid ViajeId { get; private set; }

    /// <summary>Etapa del catalogo (el "proceso").</summary>
    public Guid EtapaCatalogoId { get; private set; }

    /// <summary>Lugar (punto de custodia) donde se hace la etapa. Nulo si el viaje no lo define para esa etapa.</summary>
    public Guid? PuntoCustodiaId { get; private set; }

    public int Orden { get; private set; }

    /// <summary>Si la etapa exige informe nuevo (re-inspeccion) o solo firma.</summary>
    public bool RequiereInspeccion { get; private set; }

    /// <summary>
    /// Solo tiene sentido en la etapa de Cargue: con personal de apoyo la inspeccion de salida la hace un inspector; sin el, la hace
    /// el conductor.
    /// </summary>
    public bool ConPersonalApoyo { get; private set; }

    public static clsEtapa Crear(
        Guid vehiculoId,
        Guid viajeId,
        Guid etapaCatalogoId,
        Guid? puntoCustodiaId,
        int orden,
        bool requiereInspeccion,
        bool conPersonalApoyo,
        DateTimeOffset ahora)
    {
        if (orden < 1)
        {
            throw new clsValidacionException("El orden debe ser un entero >= 1");
        }

        var etapa = new clsEtapa
        {
            VehiculoId = vehiculoId,
            ViajeId = viajeId,
            EtapaCatalogoId = etapaCatalogoId,
            PuntoCustodiaId = puntoCustodiaId,
            Orden = orden,
            RequiereInspeccion = requiereInspeccion,
            ConPersonalApoyo = conPersonalApoyo,
        };
        etapa.Inicializar(ahora);
        return etapa;
    }

    public void CambiarLugar(Guid? puntoCustodiaId, DateTimeOffset ahora)
    {
        PuntoCustodiaId = puntoCustodiaId;
        Tocar(ahora);
    }

    public void CambiarPersonalApoyo(bool conPersonalApoyo, DateTimeOffset ahora)
    {
        ConPersonalApoyo = conPersonalApoyo;
        Tocar(ahora);
    }
}
