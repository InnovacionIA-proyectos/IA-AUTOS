using System.Globalization;
using System.Text.Json;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Una zona del vehiculo con hallazgos: su numero y lado y la etiqueta que lleva en el panel.</summary>
public sealed record ZonaConHallazgos(int? CuadranteNumero, string? Lado, string Nombre, string Etiqueta);

/// <summary>Lo que se dibuja de los paneles: los que tienen regiones de zonas con hallazgos y las zonas que no se pueden dibujar.</summary>
public sealed record PanelesDibujados(IReadOnlyList<PanelInforme> Paneles, IReadOnlyList<string> ZonasSinRegion);

/// <summary>
/// Arma los paneles del dibujo de danos para un PDF: de cada panel con imagen toma las regiones de las zonas con hallazgos (zona = lado + numero de cuadrante; una zona se ve desde varios paneles)
/// y las deja listas para resaltar. Las zonas que no tienen region en ningun panel se listan aparte.
/// </summary>
public sealed class clsPanelesDeDano(ICatalogosLectura catalogos, IImagenesPanelDano imagenes, IAlmacenArchivos almacen)
{
    public async Task<PanelesDibujados> ArmarAsync(IReadOnlyList<ZonaConHallazgos> zonas, CancellationToken ct = default)
    {
        if (zonas.Count == 0)
        {
            return new PanelesDibujados([], []);
        }

        var regiones = await catalogos.ValoresAsync(clsNombresCatalogo.CUADRANTE_REGIONES, ct);
        var paneles = (await catalogos.ValoresAsync(clsNombresCatalogo.PANELES_DANO, ct)).Where(p => p.Activo).OrderBy(p => p.TryGetValue("orden", out var o) && o is int n ? n : 0).ToList();
        var dibujadas = new HashSet<ZonaConHallazgos>();
        var resultado = new List<PanelInforme>();
        var sinImagen = new List<string>();

        foreach (var panel in paneles)
        {
            var codigo = panel.Codigo ?? string.Empty;
            var delPanel = new List<RegionPanel>();
            foreach (var zona in zonas.Where(z => z.CuadranteNumero is not null))
            {
                var region = regiones.FirstOrDefault(r => Texto(r, "panel") == codigo && Numero(r) == zona.CuadranteNumero && Texto(r, "lado") == zona.Lado);
                if (region is not null && Puntos(region) is { Count: >= 3 } puntos)
                {
                    delPanel.Add(new RegionPanel(puntos, zona.Etiqueta));
                    dibujadas.Add(zona);
                }
            }

            if (delPanel.Count == 0)
            {
                continue;
            }

            var nombreArchivo = await imagenes.ObtenerNombreAsync(codigo, ct);
            var imagen = nombreArchivo is null ? null : await almacen.LeerAsync(nombreArchivo, ct);
            if (imagen is null)
            {
                sinImagen.Add($"{Texto(panel, "nombre") ?? codigo}: sin imagen, no se puede dibujar");
                continue;
            }

            resultado.Add(new PanelInforme(Texto(panel, "nombre") ?? codigo, imagen, delPanel));
        }

        var sinRegion = zonas.Where(z => !dibujadas.Contains(z)).Select(z => $"{z.Etiqueta}: {z.Nombre}").Concat(sinImagen).ToList();
        return new PanelesDibujados(resultado, sinRegion);
    }

    private static string? Texto(IReadOnlyDictionary<string, object?> valor, string clave) => valor.TryGetValue(clave, out var v) ? v as string : null;

    private static int? Numero(IReadOnlyDictionary<string, object?> valor) => valor.TryGetValue("numero", out var v) && v is int n ? n : null;

    /// <summary>El poligono de la region: <c>{"puntos": [[x, y], ...]}</c> con x e y entre 0 y 1.</summary>
    private static List<PuntoPanel>? Puntos(IReadOnlyDictionary<string, object?> region)
    {
        if (!region.TryGetValue("geometria", out var g) || g is not JsonElement geometria || !geometria.TryGetProperty("puntos", out var puntos) || puntos.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var lista = new List<PuntoPanel>();
        foreach (var punto in puntos.EnumerateArray())
        {
            if (punto.ValueKind == JsonValueKind.Array && punto.GetArrayLength() >= 2
                && punto[0].TryGetDouble(out var x) && punto[1].TryGetDouble(out var y) && double.IsFinite(x) && double.IsFinite(y))
            {
                lista.Add(new PuntoPanel(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1)));
            }
        }

        return lista;
    }

    /// <summary>La etiqueta de una zona con varios hallazgos: sus numeros separados por coma («1, 3»).</summary>
    public static string Etiqueta(IEnumerable<int> numeros) => string.Join(", ", numeros.Order().Select(n => n.ToString(CultureInfo.InvariantCulture)));
}
