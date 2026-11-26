namespace IAAutos.Dominio.Viajes;

/// <summary>
/// El papel de un inspector en un viaje: <c>carga</c> si es el inspector del viaje o reclamo su cargue, <c>recibe</c> si solo tomo la entrega de una parada, y <c>carga_y_recibe</c> si hace las dos cosas
/// (el inspector del viaje que ademas tomo la entrega de una parada suya).
/// </summary>
public static class clsPapelEnElViaje
{
    public const string CARGA = "carga";
    public const string RECIBE = "recibe";
    public const string CARGA_Y_RECIBE = "carga_y_recibe";

    public static readonly IReadOnlyList<string> VALORES = [CARGA, RECIBE, CARGA_Y_RECIBE];

    /// <summary>El papel que resulta de cargar y/o recibir; nulo si no hace ninguna de las dos cosas.</summary>
    public static string? De(bool carga, bool recibe) =>
        (carga, recibe) switch
        {
            (true, true) => CARGA_Y_RECIBE,
            (true, false) => CARGA,
            (false, true) => RECIBE,
            _ => null,
        };
}
