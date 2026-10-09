using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>Tramo de la ruta de custodia de un VIN: una etapa del catalogo (cargue, transporte...) y, si se conoce, el lugar donde se hace.</summary>
public sealed class clsEtapa : clsEntidadBase
{
    private clsEtapa()
    {
    }

    public Guid VehiculoId { get; private set; }

    /// <summary>Etapa del catalogo (el "proceso").</summary>
    public Guid EtapaCatalogoId { get; private set; }

    /// <summary>Lugar (punto de custodia) donde se hace la etapa. Nulo mientras no se haya asignado.</summary>
    public Guid? PuntoCustodiaId { get; private set; }

    public int Orden { get; private set; }

    /// <summary>Si la etapa exige informe nuevo (re-inspeccion) o solo firma. Lo define el administrador.</summary>
    public bool RequiereInspeccion { get; private set; }

    public static clsEtapa Crear(Guid vehiculoId, Guid etapaCatalogoId, Guid? puntoCustodiaId, int orden, bool requiereInspeccion, DateTimeOffset ahora)
    {
        if (orden < 1)
        {
            throw new clsValidacionException("El orden debe ser un entero >= 1");
        }

        var etapa = new clsEtapa
        {
            VehiculoId = vehiculoId,
            EtapaCatalogoId = etapaCatalogoId,
            PuntoCustodiaId = puntoCustodiaId,
            Orden = orden,
            RequiereInspeccion = requiereInspeccion,
        };
        etapa.Inicializar(ahora);
        return etapa;
    }
}
