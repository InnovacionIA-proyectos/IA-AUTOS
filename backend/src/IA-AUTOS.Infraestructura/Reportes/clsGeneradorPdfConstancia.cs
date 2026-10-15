using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Dominio.Custodia;
using static IAAutos.Infraestructura.Reportes.clsPdfBasico;

namespace IAAutos.Infraestructura.Reportes;

/// <summary>
/// Constancia de la transferencia de custodia en PDF, sin dependencias de terceros: texto en Helvetica (fuentes base-14), las firmas (como imagen
/// incrustada o como trazo dibujado con lineas) y las fotos incrustadas. Las fotos JPEG se incrustan tal cual; las PNG de 8 bits sin entrelazado
/// (gris, color o paleta) tambien; una imagen en otro formato se indica con una nota en lugar de omitirse en silencio.
/// </summary>
internal sealed partial class clsGeneradorPdfConstancia : IGeneradorPdfConstancia
{
    private const double ANCHO = 595;
    private const double ALTO = 842;
    private const double MARGEN = 50;
    private const double Y_INICIAL = 800;
    private const double ANCHO_UTIL = ANCHO - (2 * MARGEN);
    private const double FACTOR_ANCHO_CARACTER = 0.55;
    private const double FIRMA_ANCHO = 220;
    private const double FIRMA_ALTO = 70;
    private const double FOTO_ANCHO = 300;
    private const double FOTO_ALTO = 220;

    private abstract record Elemento;

    private sealed record Linea(string Texto, double Tamano, bool Negrita) : Elemento;

    private sealed record Espacio(double Alto) : Elemento;

    private sealed record Dibujo(ImagenPdf Imagen, double Ancho, double Alto) : Elemento;

    private sealed record Trazo(IReadOnlyList<List<(double X, double Y)>> Lineas, double Ancho, double Alto) : Elemento;

    private sealed record ImagenPdf(int Id, byte[] Cuerpo, string Diccionario);

    public byte[] Generar(DatosConstancia datos)
    {
        var imagenes = new List<ImagenPdf>();
        var elementos = Componer(datos, imagenes);
        return Ensamblar(Paginar(elementos), imagenes);
    }

    // ------------------------------------------------------------------------------------------------ contenido

    private static List<Elemento> Componer(DatosConstancia d, List<ImagenPdf> imagenes)
    {
        var e = new List<Elemento>
        {
            new Linea("Traza Autos 360° — Botero Soto", 11, false),
            new Linea("Constancia de transferencia de custodia", 16, true),
            new Espacio(6),
            new Linea($"VIN: {d.Vin}", 12, true),
            new Linea($"Marca: {d.Marca}   Modelo: {d.Modelo ?? "—"}", 11, false),
            new Linea($"Etapa: {d.Proceso} — {d.MomentoNombre}", 11, false),
            new Linea($"Viaje (MTrack): {d.CodigoMtrack ?? "—"}   Placa: {d.Placa ?? "—"}", 11, false),
            new Espacio(6),
            new Linea($"Cierre de la transferencia: {NombreCierre(d.TipoCierre)}", 13, true),
        };
        if (d.FirmadaAt is { } firmada)
        {
            e.Add(new Linea($"Firmada por ambas partes el {Fecha(firmada)}", 11, false));
        }

        e.Add(new Espacio(8));
        AgregarFirma(e, "Entrega", d.Entrega, d.NoFirmaParte == clsParteFirmante.ENTREGA, imagenes);
        e.Add(new Espacio(6));
        AgregarFirma(e, "Recibe", d.Recibe, d.NoFirmaParte == clsParteFirmante.RECIBE, imagenes);

        if (d.Reservas is not null)
        {
            e.Add(new Espacio(8));
            e.Add(new Linea("Reservas de quien recibe", 13, true));
            e.Add(new Linea(d.Reservas, 11, false));
        }

        if (d.TipoCierre == clsTipoCierre.NO_FIRMA)
        {
            e.Add(new Espacio(8));
            e.Add(new Linea("Constancia de no firma", 13, true));
            e.Add(new Linea($"Parte que no firmó: {(d.NoFirmaParte == clsParteFirmante.ENTREGA ? "quien entrega" : "quien recibe")}", 11, false));
            e.Add(new Linea($"Motivo: {d.MotivoNoFirma}", 11, false));
            e.Add(new Linea($"Testigo: {d.TestigoNombre ?? "—"}", 11, false));
        }

        foreach (var foto in d.Fotos)
        {
            e.Add(new Espacio(6));
            AgregarImagen(e, foto.Contenido, FOTO_ANCHO, FOTO_ALTO, imagenes);
            e.Add(new Linea(foto.Pie, 9, false));
        }

        e.Add(new Espacio(10));
        e.Add(new Linea($"Documento generado el {Fecha(d.GeneradaAt)}", 9, false));
        return e;
    }

    private static void AgregarFirma(List<Elemento> e, string rol, FirmaConstancia firma, bool seNego, List<ImagenPdf> imagenes)
    {
        e.Add(new Linea($"{rol}: {firma.Persona}", 12, true));
        if (firma.Imagen is null && firma.Trazo is null)
        {
            e.Add(new Linea(seNego ? "Se negó a firmar (ver la constancia de no firma)" : "No ha firmado", 11, false));
            return;
        }

        var ip = firma.Ip is null ? string.Empty : $" · IP {firma.Ip}";
        e.Add(new Linea($"Firmó el {(firma.Fecha is { } f ? Fecha(f) : "—")}{ip}", 11, false));
        if (firma.Imagen is not null)
        {
            AgregarImagen(e, firma.Imagen, FIRMA_ANCHO, FIRMA_ALTO, imagenes);
        }
        else if (Trazar(firma.Trazo!) is { } trazo)
        {
            e.Add(trazo);
        }
        else
        {
            e.Add(new Linea("(Firma manuscrita registrada; el trazo no se pudo dibujar)", 10, false));
        }
    }

    private static void AgregarImagen(List<Elemento> e, byte[] contenido, double anchoMax, double altoMax, List<ImagenPdf> imagenes)
    {
        if (Incrustar(contenido, imagenes.Count + 1) is not { } imagen)
        {
            e.Add(new Linea("(Imagen registrada; su formato no se puede incrustar en el PDF)", 10, false));
            return;
        }

        imagenes.Add(imagen.Imagen);
        var escala = Math.Min(1.0, Math.Min(anchoMax / imagen.Ancho, altoMax / imagen.Alto));
        e.Add(new Dibujo(imagen.Imagen, imagen.Ancho * escala, imagen.Alto * escala));
    }

    private static string NombreCierre(string? tipo) => tipo switch
    {
        clsTipoCierre.FIRMADA => "firmada",
        clsTipoCierre.CON_RESERVAS => "firmada con reservas",
        clsTipoCierre.NO_FIRMA => "no firma",
        _ => "abierta (sin tipo de cierre)",
    };

    private static string Fecha(DateTimeOffset fecha) => fecha.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------------ firma como trazo

    /// <summary>
    /// Dibuja una ruta SVG como lineas: M, L, H, V y Z con exactitud; las curvas (C, S, Q, T, A) se aproximan con sus puntos finales. Devuelve
    /// nulo si no tiene al menos dos puntos.
    /// </summary>
    private static Trazo? Trazar(string ruta)
    {
        var lineas = new List<List<(double, double)>>();
        List<(double X, double Y)>? actual = null;
        double x = 0, y = 0, inicioX = 0, inicioY = 0;
        var comando = '\0';
        var numeros = new List<double>();

        void Aplicar()
        {
            var relativo = char.IsLower(comando);
            var c = char.ToUpperInvariant(comando);
            int tam = c switch { 'M' or 'L' or 'T' => 2, 'H' or 'V' => 1, 'C' => 6, 'S' or 'Q' => 4, 'A' => 7, _ => 0 };
            if (tam == 0)
            {
                numeros.Clear();
                return;
            }

            for (var i = 0; i + tam <= numeros.Count; i += tam)
            {
                double nx = x, ny = y;
                switch (c)
                {
                    case 'H':
                        nx = relativo ? x + numeros[i] : numeros[i];
                        break;
                    case 'V':
                        ny = relativo ? y + numeros[i] : numeros[i];
                        break;
                    default:
                        nx = relativo ? x + numeros[i + tam - 2] : numeros[i + tam - 2];
                        ny = relativo ? y + numeros[i + tam - 1] : numeros[i + tam - 1];
                        break;
                }

                if (c == 'M' && i == 0)
                {
                    actual = [(nx, ny)];
                    lineas.Add(actual);
                    inicioX = nx;
                    inicioY = ny;
                }
                else
                {
                    if (actual is null)
                    {
                        actual = [(x, y)];
                        lineas.Add(actual);
                    }

                    actual.Add((nx, ny));
                }

                x = nx;
                y = ny;
            }

            numeros.Clear();
        }

        foreach (Match m in TokenRuta().Matches(ruta))
        {
            if (char.IsLetter(m.Value[0]))
            {
                Aplicar();
                comando = m.Value[0];
                if (char.ToUpperInvariant(comando) == 'Z' && actual is not null)
                {
                    actual.Add((inicioX, inicioY));
                    x = inicioX;
                    y = inicioY;
                }
            }
            else if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n))
            {
                numeros.Add(n);
            }
        }

        Aplicar();
        var puntos = lineas.SelectMany(l => l).ToList();
        if (puntos.Count < 2)
        {
            return null;
        }

        var minX = puntos.Min(p => p.Item1);
        var minY = puntos.Min(p => p.Item2);
        var ancho = puntos.Max(p => p.Item1) - minX;
        var alto = puntos.Max(p => p.Item2) - minY;
        var escala = Math.Min(ancho > 0 ? FIRMA_ANCHO / ancho : double.MaxValue, alto > 0 ? FIRMA_ALTO / alto : double.MaxValue);
        if (escala is double.MaxValue)
        {
            return null;
        }

        // El eje Y del SVG crece hacia abajo y el del PDF hacia arriba: se invierte dentro de la caja.
        var cajaAlto = alto * escala;
        var normalizadas = lineas
            .Select(l => l.Select(p => ((p.Item1 - minX) * escala, cajaAlto - ((p.Item2 - minY) * escala))).ToList())
            .ToList();
        return new Trazo(normalizadas, ancho * escala, cajaAlto);
    }

    [GeneratedRegex(@"[A-Za-z]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex TokenRuta();

    // ------------------------------------------------------------------------------------------------ imagenes

    private readonly record struct ImagenIncrustada(ImagenPdf Imagen, int Ancho, int Alto);

    private static ImagenIncrustada? Incrustar(byte[] b, int id) =>
        EsJpeg(b) ? Jpeg(b, id) : EsPng(b) ? Png(b, id) : null;

    private static bool EsJpeg(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    private static bool EsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    /// <summary>JPEG: se incrusta tal cual (DCTDecode); solo hay que leer su tamano y sus componentes del marcador SOF.</summary>
    private static ImagenIncrustada? Jpeg(byte[] b, int id)
    {
        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marcador = b[i + 1];
            if (marcador is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                i += 2;
                continue;
            }

            var longitud = (b[i + 2] << 8) | b[i + 3];
            if (marcador is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                var alto = (b[i + 5] << 8) | b[i + 6];
                var ancho = (b[i + 7] << 8) | b[i + 8];
                var espacio = b[i + 9] switch { 1 => "/DeviceGray", 3 => "/DeviceRGB", _ => null };
                if (espacio is null || ancho <= 0 || alto <= 0)
                {
                    return null;
                }

                var dic = $"<< /Type /XObject /Subtype /Image /Width {ancho} /Height {alto} /ColorSpace {espacio} /BitsPerComponent 8 /Filter /DCTDecode /Length {b.Length} >>";
                return new ImagenIncrustada(new ImagenPdf(id, b, dic), ancho, alto);
            }

            i += 2 + longitud;
        }

        return null;
    }

    /// <summary>PNG de 8 bits sin entrelazado (gris, color o paleta): los datos comprimidos se pasan tal cual con el predictor de PNG.</summary>
    private static ImagenIncrustada? Png(byte[] b, int id)
    {
        int ancho = 0, alto = 0, profundidad = 0, tipo = -1, entrelazado = 1;
        byte[]? paleta = null;
        using var datos = new MemoryStream();
        var i = 8;
        while (i + 12 <= b.Length)
        {
            var longitud = (int)(((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3]);
            var nombre = Encoding.ASCII.GetString(b, i + 4, 4);
            var inicio = i + 8;
            if (longitud < 0 || inicio + longitud > b.Length)
            {
                return null;
            }

            switch (nombre)
            {
                case "IHDR" when longitud >= 13:
                    ancho = (b[inicio] << 24) | (b[inicio + 1] << 16) | (b[inicio + 2] << 8) | b[inicio + 3];
                    alto = (b[inicio + 4] << 24) | (b[inicio + 5] << 16) | (b[inicio + 6] << 8) | b[inicio + 7];
                    profundidad = b[inicio + 8];
                    tipo = b[inicio + 9];
                    entrelazado = b[inicio + 12];
                    break;
                case "PLTE":
                    paleta = b[inicio..(inicio + longitud)];
                    break;
                case "IDAT":
                    datos.Write(b, inicio, longitud);
                    break;
            }

            i = inicio + longitud + 4;
        }

        if (ancho <= 0 || alto <= 0 || profundidad != 8 || entrelazado != 0 || datos.Length == 0)
        {
            return null;
        }

        var (colores, espacio) = tipo switch
        {
            0 => (1, "/DeviceGray"),
            2 => (3, "/DeviceRGB"),
            3 when paleta is { Length: > 0 } => (1, $"[/Indexed /DeviceRGB {(paleta.Length / 3) - 1} <{Convert.ToHexString(paleta)}>]"),
            _ => (0, string.Empty),
        };
        if (colores == 0)
        {
            return null;
        }

        var cuerpo = datos.ToArray();
        var dic = $"<< /Type /XObject /Subtype /Image /Width {ancho} /Height {alto} /ColorSpace {espacio} /BitsPerComponent 8 /Filter /FlateDecode " +
                  $"/DecodeParms << /Predictor 15 /Colors {colores} /BitsPerComponent 8 /Columns {ancho} >> /Length {cuerpo.Length} >>";
        return new ImagenIncrustada(new ImagenPdf(id, cuerpo, dic), ancho, alto);
    }

    // ------------------------------------------------------------------------------------------------ maquetacion

    private static List<List<(Elemento Elemento, double Y)>> Paginar(List<Elemento> elementos)
    {
        var paginas = new List<List<(Elemento, double)>> { new() };
        var y = Y_INICIAL;

        void Colocar(Elemento elemento, double alto)
        {
            if (y - alto < MARGEN)
            {
                paginas.Add([]);
                y = Y_INICIAL;
            }

            // La esquina inferior del elemento: y baja primero y luego se coloca.
            y -= alto;
            paginas[^1].Add((elemento, y));
        }

        foreach (var elemento in elementos)
        {
            switch (elemento)
            {
                case Espacio espacio:
                    y -= espacio.Alto;
                    break;
                case Linea linea:
                    foreach (var tramo in Partir(linea))
                    {
                        Colocar(tramo, tramo.Tamano + 6);
                    }

                    break;
                case Dibujo dibujo:
                    Colocar(dibujo, dibujo.Alto + 6);
                    break;
                case Trazo trazo:
                    Colocar(trazo, trazo.Alto + 6);
                    break;
            }
        }

        return paginas;
    }

    private static IEnumerable<Linea> Partir(Linea linea)
    {
        var maximo = Math.Max((int)(ANCHO_UTIL / (linea.Tamano * FACTOR_ANCHO_CARACTER)), 10);
        var resto = linea.Texto;
        if (resto.Length <= maximo)
        {
            yield return linea;
            yield break;
        }

        while (resto.Length > 0)
        {
            var corte = resto.Length <= maximo ? resto.Length : CortarEnEspacio(resto, maximo);
            yield return linea with { Texto = resto[..corte].TrimEnd() };
            resto = resto[corte..].TrimStart();
        }
    }

    private static int CortarEnEspacio(string texto, int capacidad)
    {
        var espacio = texto.LastIndexOf(' ', capacidad - 1, capacidad);
        return espacio > capacidad / 2 ? espacio : capacidad;
    }

    // ------------------------------------------------------------------------------------------------ ensamblado

    private static byte[] Ensamblar(List<List<(Elemento Elemento, double Y)>> paginas, List<ImagenPdf> imagenes)
    {
        // Objetos: 1 catalogo, 2 paginas, 3 y 4 fuentes, 5.. imagenes, luego (pagina, contenido) por cada pagina.
        var primeraImagen = 5;
        var primeraPagina = primeraImagen + imagenes.Count;
        var idsPagina = Enumerable.Range(0, paginas.Count).Select(i => primeraPagina + (2 * i)).ToList();
        var cuerpos = new List<byte[]>
        {
            Texto("<< /Type /Catalog /Pages 2 0 R >>"),
            Texto($"<< /Type /Pages /Kids [{string.Join(' ', idsPagina.Select(id => $"{id} 0 R"))}] /Count {paginas.Count} >>"),
            Texto("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Texto("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"),
        };
        foreach (var imagen in imagenes)
        {
            cuerpos.Add([.. Texto(imagen.Diccionario + "\nstream\n"), .. imagen.Cuerpo, .. Texto("\nendstream")]);
        }

        for (var i = 0; i < paginas.Count; i++)
        {
            var usadas = paginas[i].Select(p => p.Elemento).OfType<Dibujo>().Select(d => d.Imagen).DistinctBy(im => im.Id).ToList();
            var xobjects = usadas.Count == 0
                ? string.Empty
                : $" /XObject << {string.Join(' ', usadas.Select(im => $"/Im{im.Id} {primeraImagen + im.Id - 1} 0 R"))} >>";
            var contenido = ContenidoDePagina(paginas[i]);
            cuerpos.Add(Texto(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(ANCHO)} {Num(ALTO)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >>{xobjects} >> /Contents {idsPagina[i] + 1} 0 R >>"));
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

    private static byte[] ContenidoDePagina(List<(Elemento Elemento, double Y)> elementos)
    {
        using var flujo = new MemoryStream();
        foreach (var (elemento, y) in elementos)
        {
            switch (elemento)
            {
                case Linea linea:
                    flujo.Write(Texto("0.06 0.14 0.28 rg\n"));
                    flujo.Write(Texto($"BT /{(linea.Negrita ? "F2" : "F1")} {Num(linea.Tamano)} Tf {Num(MARGEN)} {Num(y)} Td ("));
                    flujo.Write(CadenaPdf(linea.Texto));
                    flujo.Write(Texto(") Tj ET\n"));
                    break;
                case Dibujo dibujo:
                    flujo.Write(Texto($"q {Num(dibujo.Ancho)} 0 0 {Num(dibujo.Alto)} {Num(MARGEN)} {Num(y)} cm /Im{dibujo.Imagen.Id} Do Q\n"));
                    break;
                case Trazo trazo:
                    flujo.Write(Texto("q 0.06 0.14 0.28 RG 1.2 w 1 J 1 j\n"));
                    foreach (var linea in trazo.Lineas.Where(l => l.Count > 1))
                    {
                        flujo.Write(Texto($"{Num(MARGEN + linea[0].X)} {Num(y + linea[0].Y)} m\n"));
                        foreach (var punto in linea.Skip(1))
                        {
                            flujo.Write(Texto($"{Num(MARGEN + punto.X)} {Num(y + punto.Y)} l\n"));
                        }

                        flujo.Write(Texto("S\n"));
                    }

                    flujo.Write(Texto("Q\n"));
                    break;
            }
        }

        return flujo.ToArray();
    }

    private static string Num(double valor) => valor.ToString("0.##", CultureInfo.InvariantCulture);
}
