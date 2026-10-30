using System.Text.Json;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Catalogos;

/// <summary>
/// Zona numerada de la carroceria (puerta delantera, pilar central, capo...), por tipo de vehiculo. Es la misma zona aunque se vea
/// desde varias vistas: donde se dibuja en cada vista lo define <see cref="clsCuadranteVista"/>.
/// </summary>
public sealed class clsCuadrante : clsCatalogoBase
{
    private clsCuadrante()
    {
    }

    public string? TipoVehiculo { get; private set; }

    /// <summary>Numero del cuadrante (1 en adelante); es unico por tipo de vehiculo.</summary>
    public int Numero { get; private set; }

    /// <summary>Si el cuadrante lleva lado (dos zonas: izquierdo y derecho) o es una sola zona sin lado. Lo declara el archivo del dibujo.</summary>
    public bool LlevaLado { get; private set; } = true;

    public static clsCuadrante Crear(string? tipoVehiculo, int? numero, string? nombre, int? orden, bool? activo, DateTimeOffset ahora, bool llevaLado = true)
    {
        var cuadrante = new clsCuadrante { TipoVehiculo = clsVistaFoto.TipoVehiculoValido(tipoVehiculo), Numero = NumeroValido(numero), LlevaLado = llevaLado };
        cuadrante.Iniciar(nombre, orden, activo, ahora);
        return cuadrante;
    }

    /// <summary>Cambia lo propio del cuadrante; <paramref name="cambiaTipoVehiculo"/> distingue "no enviado" de "enviado en nulo".</summary>
    public void Cambiar(string? tipoVehiculo, bool cambiaTipoVehiculo, int? numero, DateTimeOffset ahora)
    {
        if (cambiaTipoVehiculo)
        {
            TipoVehiculo = clsVistaFoto.TipoVehiculoValido(tipoVehiculo);
        }

        if (numero is not null)
        {
            Numero = NumeroValido(numero);
        }

        Tocar(ahora);
    }

    private static int NumeroValido(int? numero) =>
        numero is >= 1 ? numero.Value : throw clsErroresCampos.De("numero", "El número debe ser un entero >= 1");
}

/// <summary>
/// Donde se dibuja un cuadrante en una vista de foto. La geometria es un poligono normalizado (coordenadas entre 0 y 1 sobre la imagen
/// de la vista) para que la app y el portal dibujen las mismas regiones. Las vistas adicionales no tienen cuadrantes.
/// </summary>
public sealed class clsCuadranteVista : clsEntidadBase
{
    private clsCuadranteVista()
    {
    }

    public Guid CuadranteId { get; private set; }

    public Guid VistaFotoId { get; private set; }

    /// <summary>Poligono en JSON: {"puntos":[[x,y],...]}.</summary>
    public string Geometria { get; private set; } = string.Empty;

    public static clsCuadranteVista Crear(Guid cuadranteId, Guid vistaFotoId, string? geometria, DateTimeOffset ahora)
    {
        var aparicion = new clsCuadranteVista { CuadranteId = cuadranteId, VistaFotoId = vistaFotoId, Geometria = GeometriaValida(geometria) };
        aparicion.Inicializar(ahora);
        return aparicion;
    }

    /// <summary>Cambia la vista o el poligono; lo no enviado se conserva.</summary>
    public void Cambiar(Guid? vistaFotoId, string? geometria, bool cambiaGeometria, DateTimeOffset ahora)
    {
        if (vistaFotoId is not null)
        {
            VistaFotoId = vistaFotoId.Value;
        }

        if (cambiaGeometria)
        {
            Geometria = GeometriaValida(geometria);
        }

        Tocar(ahora);
    }

    /// <summary>Verifica que sea {"puntos":[[x,y],...]} con al menos 3 puntos y coordenadas entre 0 y 1, y la devuelve compacta.</summary>
    public static string GeometriaValida(string? geometria)
    {
        if (string.IsNullOrWhiteSpace(geometria))
        {
            throw clsErroresCampos.De("geometria", "La geometría es requerida");
        }

        const string FORMATO = "La geometría debe ser {\"puntos\":[[x,y],...]} con al menos 3 puntos y coordenadas entre 0 y 1";
        try
        {
            using var documento = JsonDocument.Parse(geometria);
            if (documento.RootElement.ValueKind != JsonValueKind.Object
                || !documento.RootElement.TryGetProperty("puntos", out var puntos)
                || puntos.ValueKind != JsonValueKind.Array
                || puntos.GetArrayLength() < 3)
            {
                throw clsErroresCampos.De("geometria", FORMATO);
            }

            foreach (var punto in puntos.EnumerateArray())
            {
                if (punto.ValueKind != JsonValueKind.Array
                    || punto.GetArrayLength() != 2
                    || punto.EnumerateArray().Any(c => c.ValueKind != JsonValueKind.Number || c.GetDouble() is < 0 or > 1))
                {
                    throw clsErroresCampos.De("geometria", FORMATO);
                }
            }

            return JsonSerializer.Serialize(documento.RootElement);
        }
        catch (JsonException)
        {
            throw clsErroresCampos.De("geometria", FORMATO);
        }
    }
}
