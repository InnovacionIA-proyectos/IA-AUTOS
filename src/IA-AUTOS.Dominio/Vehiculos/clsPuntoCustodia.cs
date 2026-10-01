using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Vehiculos;

/// <summary>Catalogo de los 7 puntos de custodia (solo lectura desde la aplicacion).</summary>
public sealed class clsPuntoCustodia : clsEntidadBase
{
    private clsPuntoCustodia()
    {
    }

    public string Nombre { get; private set; } = string.Empty;
}
