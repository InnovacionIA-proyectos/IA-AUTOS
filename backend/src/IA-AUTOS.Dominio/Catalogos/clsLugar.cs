using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Catalogos;

/// <summary>El estado de la ubicacion de un lugar: solo una ubicacion confirmada sirve para confirmar «en sitio» la evidencia de una inspeccion.</summary>
public static class clsEstadoUbicacion
{
    public const string SIN_UBICACION = "sin_ubicacion";
    public const string PENDIENTE_CONFIRMAR = "pendiente_confirmar";
    public const string CONFIRMADA = "confirmada";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { SIN_UBICACION, PENDIENTE_CONFIRMAR, CONFIRMADA };
}

/// <summary>
/// Un lugar REAL (CEDI Envigado, Concesionario Autolarte Medellin...): tiene nombre (unico), tipo (uno de los tipos de lugar: CEDI 1, Origen, Certificadora, Destino, CEDI 2 o Concesionario), ciudad y estado. La
/// ubicacion (latitud, longitud y radio en metros) es opcional: un lugar sin ubicacion es valido y se ve «sin ubicacion». Las coordenadas van juntas o ninguna y el radio es mayor que cero.
/// </summary>
public sealed class clsLugar : clsCatalogoBase
{
    public const int CIUDAD_MAX = 100;

    private clsLugar()
    {
    }

    /// <summary>El tipo de lugar (<c>tblPuntosCustodia</c>, que queda como catalogo de tipos).</summary>
    public Guid TipoId { get; private set; }

    public string Ciudad { get; private set; } = string.Empty;

    public double? Latitud { get; private set; }

    public double? Longitud { get; private set; }

    /// <summary>El radio, en metros, dentro del que se considera que alguien esta en el lugar. Nulo mientras no se ha fijado.</summary>
    public int? RadioMetros { get; private set; }

    /// <summary>El estado de la ubicacion (<see cref="clsEstadoUbicacion"/>): sin ubicacion, pendiente de confirmar (la fijo alguien en el momento) o confirmada.</summary>
    public string EstadoUbicacion { get; private set; } = clsEstadoUbicacion.SIN_UBICACION;

    /// <summary>Quien fijo la ubicacion (quien la cargo, la fijo en el momento o confirmo la propuesta); nulo en los lugares cuya ubicacion ya existia antes.</summary>
    public Guid? UbicacionFijadaPor { get; private set; }

    public DateTimeOffset? UbicacionFijadaAt { get; private set; }

    public const int RADIO_MAXIMO_M = 100_000;

    /// <summary>True mientras el lugar no tiene coordenadas.</summary>
    public bool SinUbicacion => Latitud is null || Longitud is null;

    public static clsLugar Crear(string? nombre, Guid tipoId, string? ciudad, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var lugar = new clsLugar { TipoId = tipoId, Ciudad = CiudadValida(ciudad) };
        lugar.Iniciar(nombre, orden, activo, ahora);
        return lugar;
    }

    /// <summary>Cambia el tipo y la ciudad (un valor nulo conserva el actual).</summary>
    public void CambiarLugar(Guid? tipoId, string? ciudad, DateTimeOffset ahora)
    {
        if (tipoId is not null)
        {
            TipoId = tipoId.Value;
        }

        if (ciudad is not null)
        {
            Ciudad = CiudadValida(ciudad);
        }

        Tocar(ahora);
    }

    /// <summary>
    /// Fija la ubicacion CONFIRMADA del lugar (la carga manual de quien administra el catalogo o la propuesta de autocalibracion que el coordinador confirma): las coordenadas van juntas o ninguna, el radio es
    /// mayor que cero y necesita coordenadas. Sin coordenadas ni radio quita la ubicacion (el lugar queda «sin ubicacion»).
    /// </summary>
    public void FijarUbicacionConfirmada(double? latitud, double? longitud, int? radioMetros, Guid usuarioId, DateTimeOffset ahora)
    {
        if ((latitud is null) != (longitud is null))
        {
            throw clsErroresCampos.De(latitud is null ? "latitud" : "longitud", "La latitud y la longitud van juntas: envíe las dos o ninguna");
        }

        if (latitud is null)
        {
            if (radioMetros is not null)
            {
                throw clsErroresCampos.De("radio_metros", "El radio necesita las coordenadas del lugar");
            }

            Latitud = null;
            Longitud = null;
            RadioMetros = null;
            EstadoUbicacion = clsEstadoUbicacion.SIN_UBICACION;
            UbicacionFijadaPor = null;
            UbicacionFijadaAt = null;
            Tocar(ahora);
            return;
        }

        ValidarCoordenadas(latitud.Value, longitud!.Value);
        if (radioMetros is { } radio && (radio <= 0 || radio > RADIO_MAXIMO_M))
        {
            throw clsErroresCampos.De("radio_metros", $"El radio debe estar entre 1 y {RADIO_MAXIMO_M} metros");
        }

        Latitud = latitud;
        Longitud = longitud;
        RadioMetros = radioMetros;
        EstadoUbicacion = clsEstadoUbicacion.CONFIRMADA;
        UbicacionFijadaPor = usuarioId;
        UbicacionFijadaAt = ahora;
        Tocar(ahora);
    }

    /// <summary>
    /// «Fijar ubicacion aqui»: quien esta en el lugar y el lugar no tiene ubicacion deja la suya, que queda PENDIENTE DE CONFIRMAR (todavia no genera marcas de «fuera del rango» para nadie). 409 si el lugar ya tiene
    /// ubicacion (pendiente o confirmada): una ubicacion existente la cambia quien administra el catalogo.
    /// </summary>
    public void FijarUbicacionAqui(double latitud, double longitud, Guid usuarioId, DateTimeOffset ahora)
    {
        if (EstadoUbicacion != clsEstadoUbicacion.SIN_UBICACION)
        {
            throw new clsConflictoException(EstadoUbicacion == clsEstadoUbicacion.CONFIRMADA
                ? "El lugar ya tiene su ubicación confirmada: solo quien administra los catálogos la cambia"
                : "El lugar ya tiene una ubicación pendiente de confirmar");
        }

        ValidarCoordenadas(latitud, longitud);
        Latitud = latitud;
        Longitud = longitud;
        RadioMetros = null;
        EstadoUbicacion = clsEstadoUbicacion.PENDIENTE_CONFIRMAR;
        UbicacionFijadaPor = usuarioId;
        UbicacionFijadaAt = ahora;
        Tocar(ahora);
    }

    private static void ValidarCoordenadas(double latitud, double longitud)
    {
        if (!double.IsFinite(latitud) || latitud is < -90 or > 90)
        {
            throw clsErroresCampos.De("latitud", "La latitud debe estar entre -90 y 90");
        }

        if (!double.IsFinite(longitud) || longitud is < -180 or > 180)
        {
            throw clsErroresCampos.De("longitud", "La longitud debe estar entre -180 y 180");
        }
    }

    private static string CiudadValida(string? ciudad)
    {
        var limpia = ciudad?.Trim();
        if (string.IsNullOrEmpty(limpia))
        {
            throw clsErroresCampos.De("ciudad", "La ciudad es requerida");
        }

        return limpia.Length <= CIUDAD_MAX ? limpia : throw clsErroresCampos.De("ciudad", $"La ciudad admite máximo {CIUDAD_MAX} caracteres");
    }
}
