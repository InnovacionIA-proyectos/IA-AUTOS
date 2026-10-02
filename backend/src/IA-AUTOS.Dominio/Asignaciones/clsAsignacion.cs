using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Asignaciones;

/// <summary>El administrador asigna que VIN se transporta en cual ninera (placa) y con que conductor.</summary>
public sealed class clsAsignacion : clsEntidadBase
{
    private clsAsignacion()
    {
    }

    public Guid VehiculoId { get; private set; }

    public string Ninera { get; private set; } = string.Empty;

    public Guid? ConductorId { get; private set; }

    public Guid AsignadoPor { get; private set; }

    public static clsAsignacion Crear(Guid vehiculoId, string ninera, Guid? conductorId, Guid asignadoPor, DateTimeOffset ahora)
    {
        var asignacion = new clsAsignacion
        {
            VehiculoId = vehiculoId,
            Ninera = clsValidador.Requerido(ninera, "La niñera es requerida"),
            ConductorId = conductorId,
            AsignadoPor = asignadoPor,
        };
        asignacion.Inicializar(ahora);
        return asignacion;
    }
}
