using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Dominio.Catalogos;

/// <summary>El centro y el radio que propone la autocalibracion para un lugar, con cuantas fotos y cuantas visitas los sustentan.</summary>
public sealed record clsCalibracionPropuesta(double CentroLat, double CentroLng, int RadioMetros, int Fotos, int Visitas);

/// <summary>
/// La autocalibracion de la ubicacion de un lugar: a partir de las fotos selladas con ubicacion de sus visitas propone el CENTRO en la mediana de las coordenadas (la latitud y la longitud por separado, para
/// que una foto lejana no lo arrastre) y el RADIO segun la dispersion: la distancia al centro que cubre al 90 % de las fotos, redondeada hacia arriba a 10 m, entre <see cref="RADIO_MINIMO_M"/> y
/// <see cref="RADIO_MAXIMO_M"/>. Solo propone con al menos <see cref="VISITAS_MINIMAS"/> visitas distintas (dato ilustrativo que confirma el negocio, SUP-002).
/// </summary>
public static class clsAutocalibracion
{
    /// <summary>Visitas distintas que hacen falta para proponer (ilustrativo; lo confirma el negocio).</summary>
    public const int VISITAS_MINIMAS = 3;

    public const int RADIO_MINIMO_M = 100;
    public const int RADIO_MAXIMO_M = 5000;

    /// <summary>La mediana de una lista (el promedio de los dos del centro si es par).</summary>
    public static double Mediana(IReadOnlyList<double> valores)
    {
        if (valores.Count == 0)
        {
            throw new ArgumentException("No hay valores para calcular la mediana", nameof(valores));
        }

        var ordenados = valores.Order().ToList();
        var medio = ordenados.Count / 2;
        return ordenados.Count % 2 == 1 ? ordenados[medio] : (ordenados[medio - 1] + ordenados[medio]) / 2;
    }

    /// <summary>El valor bajo el que queda ese porcentaje (0 a 1) de la lista, por el metodo del rango mas cercano.</summary>
    public static double Percentil(IReadOnlyList<double> valores, double porcentaje)
    {
        var ordenados = valores.Order().ToList();
        var posicion = (int)Math.Ceiling(porcentaje * ordenados.Count) - 1;
        return ordenados[Math.Clamp(posicion, 0, ordenados.Count - 1)];
    }

    /// <param name="fotos">Las coordenadas de las fotos (latitud, longitud) y la visita (el viaje) de cada una.</param>
    /// <returns>La propuesta, o nulo si las fotos no son de al menos <see cref="VISITAS_MINIMAS"/> visitas distintas.</returns>
    public static clsCalibracionPropuesta? Proponer(IReadOnlyList<(double Lat, double Lng, Guid Visita)> fotos)
    {
        var visitas = fotos.Select(f => f.Visita).Distinct().Count();
        if (visitas < VISITAS_MINIMAS || fotos.Count == 0)
        {
            return null;
        }

        var lat = Mediana([.. fotos.Select(f => f.Lat)]);
        var lng = Mediana([.. fotos.Select(f => f.Lng)]);
        var cobertura = Percentil([.. fotos.Select(f => clsConfirmacionSitio.Distancia(lat, lng, f.Lat, f.Lng))], 0.9);
        var radio = (int)Math.Clamp(Math.Ceiling(cobertura / 10) * 10, RADIO_MINIMO_M, RADIO_MAXIMO_M);
        return new clsCalibracionPropuesta(lat, lng, radio, fotos.Count, visitas);
    }
}
