using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Catalogos;

/// <summary>El estado de una propuesta de autocalibracion de la ubicacion de un lugar.</summary>
public static class clsEstadoPropuesta
{
    public const string PENDIENTE = "pendiente";
    public const string CONFIRMADA = "confirmada";
    public const string DESCARTADA = "descartada";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { PENDIENTE, CONFIRMADA, DESCARTADA };
}

/// <summary>
/// La propuesta de centro y radio que la autocalibracion hace para un lugar sin ubicacion confirmada, a partir de las fotos selladas con ubicacion de varias visitas: el centro es la mediana de sus coordenadas
/// y el radio sale de su dispersion. El lugar NO cambia mientras la propuesta siga pendiente: el coordinador de calidad la confirma (el lugar queda con ese centro y radio como ubicacion confirmada) o la descarta,
/// y una propuesta decidida no se decide otra vez ni se modifica.
/// </summary>
public sealed class clsPropuestaUbicacion : clsEntidadBase
{
    private clsPropuestaUbicacion()
    {
    }

    public Guid LugarId { get; private set; }

    public double CentroLat { get; private set; }

    public double CentroLng { get; private set; }

    public int RadioMetros { get; private set; }

    /// <summary>Cuantas fotos sellaron el calculo.</summary>
    public int Fotos { get; private set; }

    /// <summary>De cuantas visitas distintas (viajes) salen esas fotos.</summary>
    public int Visitas { get; private set; }

    public string Estado { get; private set; } = clsEstadoPropuesta.PENDIENTE;

    public Guid? DecididaPor { get; private set; }

    public DateTimeOffset? DecididaAt { get; private set; }

    public bool EstaPendiente => Estado == clsEstadoPropuesta.PENDIENTE;

    public static clsPropuestaUbicacion Crear(Guid lugarId, double centroLat, double centroLng, int radioMetros, int fotos, int visitas, DateTimeOffset ahora)
    {
        clsValidador.Latitud(centroLat, "centro_lat");
        clsValidador.Longitud(centroLng, "centro_lng");
        if (radioMetros <= 0 || fotos < 0 || visitas < 0)
        {
            throw new clsValidacionException("El radio debe ser mayor que cero y las fotos y las visitas no pueden ser negativas");
        }

        var propuesta = new clsPropuestaUbicacion { LugarId = lugarId, CentroLat = centroLat, CentroLng = centroLng, RadioMetros = radioMetros, Fotos = fotos, Visitas = visitas };
        propuesta.Inicializar(ahora);
        return propuesta;
    }

    /// <summary>La coordinadora la confirma: queda decidida con su autor y su fecha. 409 si ya estaba decidida.</summary>
    public void Confirmar(Guid usuarioId, DateTimeOffset ahora) => Decidir(clsEstadoPropuesta.CONFIRMADA, usuarioId, ahora);

    /// <summary>La coordinadora la descarta: el lugar no cambia. 409 si ya estaba decidida.</summary>
    public void Descartar(Guid usuarioId, DateTimeOffset ahora) => Decidir(clsEstadoPropuesta.DESCARTADA, usuarioId, ahora);

    private void Decidir(string estado, Guid usuarioId, DateTimeOffset ahora)
    {
        if (!EstaPendiente)
        {
            throw new clsConflictoException($"La propuesta ya fue {Estado}: una propuesta decidida no se decide otra vez");
        }

        Estado = estado;
        DecididaPor = usuarioId;
        DecididaAt = ahora;
        Tocar(ahora);
    }
}

public interface ILugarRepository
{
    /// <summary>El lugar (para cambiar su ubicacion), o nulo si no existe.</summary>
    Task<clsLugar?> ObtenerAsync(Guid id, CancellationToken ct = default);
}

public interface IPropuestaUbicacionRepository
{
    Task<clsPropuestaUbicacion?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>Las propuestas (opcionalmente solo las de un estado), de la mas reciente a la mas antigua, con el nombre de su lugar.</summary>
    Task<IReadOnlyList<(clsPropuestaUbicacion Propuesta, string LugarNombre)>> ListarAsync(string? estado, CancellationToken ct = default);

    /// <summary>La propuesta mas reciente de cada lugar (sea cual sea su estado), para no volver a proponer lo mismo.</summary>
    Task<IReadOnlyDictionary<Guid, clsPropuestaUbicacion>> UltimaPorLugarAsync(IReadOnlyCollection<Guid> lugarIds, CancellationToken ct = default);

    void Agregar(clsPropuestaUbicacion propuesta);
}
