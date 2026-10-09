using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>Averia detectada, marcada sobre el diagrama del vehiculo.</summary>
public sealed class clsHallazgo : clsEntidadBase
{
    private const int PARTE_MAX = 80;

    private clsHallazgo()
    {
    }

    public Guid InspeccionId { get; private set; }

    /// <summary>Parte y posicion del vehiculo en texto legible (ej. "Guardafango delantero derecho").</summary>
    public string ParteVehiculo { get; private set; } = string.Empty;

    public string ClaseDano { get; private set; } = string.Empty;

    public string TamanoDano { get; private set; } = string.Empty;

    public clsCoordsDiagrama? CoordsDiagrama { get; private set; }

    public static clsHallazgo Crear(
        Guid inspeccionId,
        string parteVehiculo,
        string claseDano,
        string tamanoDano,
        clsCoordsDiagrama? coords,
        DateTimeOffset ahora)
    {
        var parte = clsValidador.Requerido(parteVehiculo, "La parte del vehículo es requerida");
        if (parte.Length > PARTE_MAX)
        {
            throw new clsValidacionException($"La parte del vehículo admite máximo {PARTE_MAX} caracteres");
        }

        // La clase y el tamano se validan contra los catalogos (en la aplicacion y con una FK en la base): aqui solo se exige que vengan.
        claseDano = clsValidador.Requerido(claseDano, "clase_dano es requerida");
        tamanoDano = clsValidador.Requerido(tamanoDano, "tamano_dano es requerido");

        var hallazgo = new clsHallazgo
        {
            InspeccionId = inspeccionId,
            ParteVehiculo = parte,
            ClaseDano = claseDano,
            TamanoDano = tamanoDano,
            CoordsDiagrama = coords,
        };
        hallazgo.Inicializar(ahora);
        return hallazgo;
    }
}
