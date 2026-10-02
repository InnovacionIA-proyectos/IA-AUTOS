using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>Posicion marcada sobre el diagrama de la carroceria, en coordenadas normalizadas [0, 1].</summary>
public sealed record clsCoordsDiagrama
{
    public clsCoordsDiagrama(double x, double y)
    {
        if (x is < 0 or > 1 || y is < 0 or > 1)
        {
            throw new clsValidacionException("Las coordenadas del diagrama deben estar entre 0 y 1");
        }

        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}
