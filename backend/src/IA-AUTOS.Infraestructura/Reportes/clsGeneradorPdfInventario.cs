using System.Globalization;
using System.Text;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;

namespace IAAutos.Infraestructura.Reportes;

/// <summary>
/// Generador de PDF minimo y sin dependencias (sin licencias de terceros): texto en Helvetica estandar (fuentes base-14, sin
/// incrustar) sobre paginas A4, con paginacion y ajuste de lineas largas. Reproduce el contenido del inventario original.
/// </summary>
internal sealed class clsGeneradorPdfInventario : IGeneradorPdfInventario
{
    private const double ANCHO = 595;
    private const double ALTO = 842;
    private const double MARGEN = 50;
    private const double Y_INICIAL = 800;
    private const double ANCHO_UTIL = ANCHO - (2 * MARGEN);
    private const double FACTOR_ANCHO_CARACTER = 0.55;

    private static readonly Encoding WIN_ANSI = CrearWinAnsi();

    private readonly record struct Linea(string Texto, double Tamano, bool Negrita);

    public byte[] Generar(InventarioDto inventario)
    {
        var paginas = Paginar(Componer(inventario));
        return Ensamblar(paginas);
    }

    private static List<object> Componer(InventarioDto inv)
    {
        var elementos = new List<object>
        {
            new Linea("Traza Autos 360° — Botero Soto", 16, true),
            4.0,
            new Linea($"VIN: {inv.Vin}", 12, true),
            new Linea($"Marca: {inv.Marca}   Operación: {inv.Operacion}", 11, false),
            new Linea($"Modelo: {inv.Modelo ?? "—"}   Color: {inv.Color ?? "—"}", 11, false),
            6.0,
        };

        foreach (var etapa in inv.Etapas)
        {
            elementos.Add(new Linea($"Proceso: {etapa.Proceso} (orden {etapa.Orden})", 12, true));
            foreach (var inspeccion in etapa.Inspecciones)
            {
                var fecha = inspeccion.Fecha.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                elementos.Add(new Linea($"  {inspeccion.MomentoNombre} — {inspeccion.Estado} — {fecha}", 11, false));
                foreach (var hallazgo in inspeccion.Hallazgos)
                {
                    elementos.Add(new Linea($"    • {hallazgo.ParteVehiculo}: {hallazgo.ClaseDano} ({hallazgo.TamanoDano})", 11, false));
                }

                elementos.Add(new Linea($"    Evidencias: {inspeccion.Evidencias.Count}", 11, false));
            }

            elementos.Add(4.0);
        }

        return elementos;
    }

    /// <summary>Reparte las lineas en paginas: salta de pagina cuando no queda espacio y parte las lineas que no caben a lo ancho.</summary>
    private static List<List<(Linea Linea, double Y)>> Paginar(List<object> elementos)
    {
        var paginas = new List<List<(Linea, double)>> { new() };
        var y = Y_INICIAL;

        foreach (var elemento in elementos)
        {
            if (elemento is double espacio)
            {
                y -= espacio;
                continue;
            }

            var linea = (Linea)elemento;
            foreach (var tramo in Partir(linea))
            {
                if (y < MARGEN)
                {
                    paginas.Add([]);
                    y = Y_INICIAL;
                }

                paginas[^1].Add((tramo, y));
                y -= tramo.Tamano + 6;
            }
        }

        return paginas;
    }

    private static IEnumerable<Linea> Partir(Linea linea)
    {
        var maximo = Math.Max((int)(ANCHO_UTIL / (linea.Tamano * FACTOR_ANCHO_CARACTER)), 10);
        var texto = linea.Texto;
        if (texto.Length <= maximo)
        {
            yield return linea;
            yield break;
        }

        var sangria = new string(' ', texto.Length - texto.TrimStart().Length);
        var resto = texto;
        var primera = true;
        while (resto.Length > 0)
        {
            var prefijo = primera ? string.Empty : sangria + "  ";
            var capacidad = Math.Max(maximo - prefijo.Length, 10);
            var corte = resto.Length <= capacidad ? resto.Length : CortarEnEspacio(resto, capacidad);
            yield return linea with { Texto = prefijo + resto[..corte].TrimEnd() };
            resto = resto[corte..].TrimStart();
            primera = false;
        }
    }

    private static int CortarEnEspacio(string texto, int capacidad)
    {
        var espacio = texto.LastIndexOf(' ', capacidad - 1, capacidad);
        return espacio > capacidad / 2 ? espacio : capacidad;
    }

    private static byte[] Ensamblar(List<List<(Linea Linea, double Y)>> paginas)
    {
        // Objetos: 1 catalogo, 2 paginas, 3 y 4 fuentes, luego (pagina, contenido) por cada pagina.
        var cuerpos = new List<byte[]>();
        var idsPagina = Enumerable.Range(0, paginas.Count).Select(i => 5 + (2 * i)).ToList();

        cuerpos.Add(Texto("<< /Type /Catalog /Pages 2 0 R >>"));
        cuerpos.Add(Texto($"<< /Type /Pages /Kids [{string.Join(' ', idsPagina.Select(id => $"{id} 0 R"))}] /Count {paginas.Count} >>"));
        cuerpos.Add(Texto("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        cuerpos.Add(Texto("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));

        for (var i = 0; i < paginas.Count; i++)
        {
            var contenido = ContenidoDePagina(paginas[i]);
            cuerpos.Add(Texto(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {ANCHO.ToString(CultureInfo.InvariantCulture)} {ALTO.ToString(CultureInfo.InvariantCulture)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {idsPagina[i] + 1} 0 R >>"));
            cuerpos.Add([.. Texto($"<< /Length {contenido.Length} >>\nstream\n"), .. contenido, .. Texto("\nendstream")]);
        }

        using var salida = new MemoryStream();
        salida.Write(Texto("%PDF-1.4\n%âãÏÓ\n"));
        var desplazamientos = new List<long>();
        for (var i = 0; i < cuerpos.Count; i++)
        {
            desplazamientos.Add(salida.Position);
            salida.Write(Texto($"{i + 1} 0 obj\n"));
            salida.Write(cuerpos[i]);
            salida.Write(Texto("\nendobj\n"));
        }

        var inicioXref = salida.Position;
        var tabla = new StringBuilder();
        tabla.Append("xref\n0 ").Append(cuerpos.Count + 1).Append('\n').Append("0000000000 65535 f \n");
        foreach (var desplazamiento in desplazamientos)
        {
            tabla.Append(desplazamiento.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        tabla.Append("trailer\n<< /Size ").Append(cuerpos.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(inicioXref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        salida.Write(Texto(tabla.ToString()));
        return salida.ToArray();
    }

    private static byte[] ContenidoDePagina(List<(Linea Linea, double Y)> lineas)
    {
        using var flujo = new MemoryStream();
        flujo.Write(Texto("0.06 0.14 0.28 rg\n"));
        foreach (var (linea, y) in lineas)
        {
            var fuente = linea.Negrita ? "F2" : "F1";
            flujo.Write(Texto(
                $"BT /{fuente} {linea.Tamano.ToString(CultureInfo.InvariantCulture)} Tf {MARGEN.ToString(CultureInfo.InvariantCulture)} {y.ToString("0.##", CultureInfo.InvariantCulture)} Td ("));
            flujo.Write(CadenaPdf(linea.Texto));
            flujo.Write(Texto(") Tj ET\n"));
        }

        return flujo.ToArray();
    }

    /// <summary>Cadena literal de PDF en WinAnsi: se escapan parentesis y barra, y los bytes no imprimibles en octal.</summary>
    private static byte[] CadenaPdf(string texto)
    {
        var bytes = WIN_ANSI.GetBytes(texto);
        var salida = new List<byte>(bytes.Length);
        foreach (var b in bytes)
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
            {
                salida.Add((byte)'\\');
                salida.Add(b);
            }
            else if (b is < 32 or > 126)
            {
                salida.AddRange(Encoding.ASCII.GetBytes("\\" + Convert.ToString(b, 8).PadLeft(3, '0')));
            }
            else
            {
                salida.Add(b);
            }
        }

        return [.. salida];
    }

    private static byte[] Texto(string texto) => Encoding.Latin1.GetBytes(texto);

    private static Encoding CrearWinAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }
}
