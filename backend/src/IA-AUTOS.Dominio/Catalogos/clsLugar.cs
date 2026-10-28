using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Catalogos;

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
