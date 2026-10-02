using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>Tramo de la ruta de custodia de un VIN en un punto de custodia.</summary>
public sealed class clsEtapa : clsEntidadBase
{
    private clsEtapa()
    {
    }

    public Guid VehiculoId { get; private set; }

    public Guid PuntoCustodiaId { get; private set; }

    public int Orden { get; private set; }

    /// <summary>Si el punto exige informe nuevo (re-inspeccion) o solo firma. Lo define el administrador.</summary>
    public bool RequiereInspeccion { get; private set; }

    public static clsEtapa Crear(Guid vehiculoId, Guid puntoCustodiaId, int orden, bool requiereInspeccion, DateTimeOffset ahora)
    {
        if (orden < 1)
        {
            throw new clsValidacionException("El orden debe ser un entero >= 1");
        }

        var etapa = new clsEtapa
        {
            VehiculoId = vehiculoId,
            PuntoCustodiaId = puntoCustodiaId,
            Orden = orden,
            RequiereInspeccion = requiereInspeccion,
        };
        etapa.Inicializar(ahora);
        return etapa;
    }
}
