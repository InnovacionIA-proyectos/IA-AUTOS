namespace IAAutos.Dominio.Inspecciones;

/// <summary>El resultado de confirmar que la evidencia sellada de una inspeccion de Cargue o Descargue se tomo en el lugar (guardado en <c>ConfirmacionSitio</c>).</summary>
public static class clsResultadoConfirmacionSitio
{
    public const string CONFIRMADO_EN_SITIO = "confirmado_en_sitio";
    public const string FUERA_DE_RANGO = "fuera_de_rango";
    public const string SIN_CONFIRMACION_POSIBLE = "sin_confirmacion_posible";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { CONFIRMADO_EN_SITIO, FUERA_DE_RANGO, SIN_CONFIRMACION_POSIBLE };

    public static string Nombre(string resultado) => resultado switch
    {
        CONFIRMADO_EN_SITIO => "Confirmado en sitio",
        FUERA_DE_RANGO => "Fuera del rango",
        SIN_CONFIRMACION_POSIBLE => "Sin confirmación posible",
        _ => resultado,
    };
}

/// <summary>Lo que se decidio de una foto: el resultado y, cuando se pudo calcular, la distancia (en metros) de la foto al centro del lugar.</summary>
public sealed record clsEvaluacionSitio(string Resultado, double? DistanciaM);

/// <summary>
/// Las reglas para confirmar «en sitio» una foto sellada. Nunca bloquea nada: solo marca. La duda del GPS juega a favor de quien tomo la foto y un GPS malo no produce una acusacion:
/// <list type="number">
/// <item>una foto sin ubicacion (0, 0), un lugar sin ubicacion o con la ubicacion aun por confirmar, una precision que no se reporto o que llega a la valvula: <c>sin_confirmacion_posible</c>;</item>
/// <item>si la distancia no supera el radio mas la precision de la foto: <c>confirmado_en_sitio</c>;</item>
/// <item>si la supera: <c>fuera_de_rango</c>.</item>
/// </list>
/// La distancia se redondea al centimetro antes de comparar para que el limite exacto no dependa del punto flotante.
/// </summary>
public static class clsConfirmacionSitio
{
    /// <summary>Radio medio de la Tierra, en metros, de la formula de gran circulo (haversine).</summary>
    public const double RADIO_TIERRA_M = 6_371_008.8;

    /// <summary>La distancia en metros entre dos coordenadas (haversine).</summary>
    public static double Distancia(double lat1, double lng1, double lat2, double lng2)
    {
        var fi1 = lat1 * Math.PI / 180;
        var fi2 = lat2 * Math.PI / 180;
        var dFi = (lat2 - lat1) * Math.PI / 180;
        var dLambda = (lng2 - lng1) * Math.PI / 180;
        var a = (Math.Sin(dFi / 2) * Math.Sin(dFi / 2)) + (Math.Cos(fi1) * Math.Cos(fi2) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2));
        return RADIO_TIERRA_M * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <param name="fotoLat">La latitud sellada de la foto; (0, 0) con <paramref name="fotoLng"/> es una foto sin ubicacion.</param>
    /// <param name="precisionM">La precision del GPS de la foto, en metros; nula si el dispositivo no la reporto.</param>
    /// <param name="lugarLat">El centro del lugar; nulo si el lugar no tiene ubicacion.</param>
    /// <param name="ubicacionConfirmada">True si la ubicacion del lugar esta confirmada (no pendiente de confirmar).</param>
    /// <param name="radioM">El radio, en metros, del lugar de esa etapa.</param>
    /// <param name="valvulaPrecisionM">La precision, en metros, a partir de la cual una foto no confirma ni acusa.</param>
    public static clsEvaluacionSitio Evaluar(
        double fotoLat,
        double fotoLng,
        double? precisionM,
        double? lugarLat,
        double? lugarLng,
        bool ubicacionConfirmada,
        double radioM,
        double valvulaPrecisionM)
    {
        var sinConfirmar = new clsEvaluacionSitio(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE, null);
        if ((fotoLat == 0 && fotoLng == 0) || lugarLat is null || lugarLng is null || !ubicacionConfirmada)
        {
            return sinConfirmar;
        }

        if (precisionM is not { } precision || !double.IsFinite(precision) || precision < 0 || precision >= valvulaPrecisionM)
        {
            return sinConfirmar;
        }

        var distancia = Math.Round(Distancia(fotoLat, fotoLng, lugarLat.Value, lugarLng.Value), 2);
        return new clsEvaluacionSitio(distancia <= radioM + precision ? clsResultadoConfirmacionSitio.CONFIRMADO_EN_SITIO : clsResultadoConfirmacionSitio.FUERA_DE_RANGO, distancia);
    }
}
