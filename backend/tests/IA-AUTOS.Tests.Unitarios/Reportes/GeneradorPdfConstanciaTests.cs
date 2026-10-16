using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Dominio.Custodia;
using IAAutos.Infraestructura.Reportes;

namespace IAAutos.Tests.Unitarios.Reportes;

public class GeneradorPdfConstanciaTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static string Texto(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    /// <summary>JPEG minimo: solo lo que el generador lee (el marcador SOF0 con el tamano y los 3 componentes).</summary>
    private static byte[] Jpeg(int ancho = 40, int alto = 30) =>
    [
        0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(alto >> 8), (byte)alto, (byte)(ancho >> 8), (byte)ancho, 0x03,
        0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01, 0xFF, 0xD9,
    ];

    /// <summary>PNG RGB de 8 bits sin entrelazado, con sus datos comprimidos de verdad (el generador no valida los CRC).</summary>
    private static byte[] Png(int ancho = 2, int alto = 2, int tipoColor = 2, byte[]? paleta = null)
    {
        static byte[] Trozo(string nombre, byte[] cuerpo)
        {
            var longitud = new[] { (byte)(cuerpo.Length >> 24), (byte)(cuerpo.Length >> 16), (byte)(cuerpo.Length >> 8), (byte)cuerpo.Length };
            return [.. longitud, .. Encoding.ASCII.GetBytes(nombre), .. cuerpo, 0, 0, 0, 0];
        }

        var cabecera = new byte[13];
        cabecera[3] = (byte)ancho;
        cabecera[7] = (byte)alto;
        cabecera[8] = 8;
        cabecera[9] = (byte)tipoColor;
        var canales = tipoColor == 2 ? 3 : 1;
        var crudo = new byte[alto * (1 + (ancho * canales))];
        using var comprimido = new MemoryStream();
        using (var zlib = new ZLibStream(comprimido, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(crudo);
        }

        var firma = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var partes = new List<byte[]> { firma, Trozo("IHDR", cabecera) };
        if (paleta is not null)
        {
            partes.Add(Trozo("PLTE", paleta));
        }

        partes.Add(Trozo("IDAT", comprimido.ToArray()));
        partes.Add(Trozo("IEND", []));
        return [.. partes.SelectMany(p => p)];
    }

    private static FirmaConstancia Firma(string persona, string? trazo = "M0 0 L40 20 L80 0", byte[]? imagen = null, string? ip = "10.0.0.5") =>
        new(persona, AHORA.AddMinutes(1), ip, imagen is null ? trazo : null, imagen);

    private static FirmaConstancia SinFirma(string persona) => new(persona, null, null, null, null);

    private static DatosConstancia Datos(
        string? tipoCierre = clsTipoCierre.FIRMADA,
        FirmaConstancia? entrega = null,
        FirmaConstancia? recibe = null,
        string? reservas = null,
        string? noFirmaParte = null,
        string? motivo = null,
        string? testigo = null,
        IReadOnlyList<FotoConstancia>? fotos = null) =>
        new(
            "9FBHSR4E8PM000123", "Renault", "Duster", "cargue", "Inspección de llegada", "MTK-1", "ABC123", tipoCierre,
            entrega ?? Firma("Ines Inspectora"), recibe ?? Firma("Carlos Conductor", ip: "10.0.0.9"), reservas, noFirmaParte, motivo, testigo,
            tipoCierre is null or clsTipoCierre.NO_FIRMA ? null : AHORA.AddMinutes(2), fotos ?? [], AHORA.AddMinutes(10));

    private static byte[] Generar(DatosConstancia datos) => new clsGeneradorPdfConstancia().Generar(datos);

    [Fact]
    public void La_constancia_firmada_lleva_el_contexto_las_dos_firmas_con_su_fecha_e_ip_y_el_trazo_dibujado()
    {
        var pdf = Generar(Datos());
        var texto = Texto(pdf);

        Assert.StartsWith("%PDF-1.4", texto);
        Assert.EndsWith("%%EOF\n", texto);
        Assert.Contains("(Constancia de transferencia de custodia) Tj", texto);
        Assert.Contains("(VIN: 9FBHSR4E8PM000123) Tj", texto);
        Assert.Contains("(Cierre de la transferencia: firmada) Tj", texto);
        Assert.Contains("(Entrega: Ines Inspectora) Tj", texto);
        Assert.Contains("(Recibe: Carlos Conductor) Tj", texto);
        Assert.Contains("IP 10.0.0.5", texto);
        Assert.Contains("IP 10.0.0.9", texto);
        Assert.Contains("2026-07-17 12:01:00 UTC", texto);
        Assert.Contains("Firmada por ambas partes el 2026-07-17 12:02:00 UTC", texto);
        Assert.Equal(2, Regex.Matches(texto, @" m\n").Count);
        Assert.Contains(" l\nS\n", texto);
        Assert.DoesNotContain("/Subtype /Image", texto);
    }

    [Fact]
    public void La_constancia_con_reservas_lleva_las_observaciones_con_los_parentesis_escapados()
    {
        var texto = Texto(Generar(Datos(clsTipoCierre.CON_RESERVAS, reservas: "Rayón (lado izquierdo) \\ puerta")));

        Assert.Contains("(Cierre de la transferencia: firmada con reservas) Tj", texto);
        Assert.Contains("(Reservas de quien recibe) Tj", texto);
        Assert.Contains(@"(Ray\363n \(lado izquierdo\) \\ puerta) Tj", texto);
    }

    [Fact]
    public void La_constancia_de_no_firma_lleva_motivo_testigo_la_parte_que_se_nego_y_la_foto_incrustada()
    {
        var datos = Datos(
            clsTipoCierre.NO_FIRMA,
            recibe: SinFirma("Carlos Conductor"),
            noFirmaParte: clsParteFirmante.RECIBE,
            motivo: "Se niega a firmar el recibo",
            testigo: "Tomás Testigo",
            fotos: [new FotoConstancia(Jpeg(), "Foto de la constancia · sin ubicación")]);

        var texto = Texto(Generar(datos));

        Assert.Contains("(Cierre de la transferencia: no firma) Tj", texto);
        Assert.Contains("(Constancia de no firma) Tj", texto);
        Assert.Contains(@"(Parte que no firm\363: quien recibe) Tj", texto);
        Assert.Contains("(Motivo: Se niega a firmar el recibo) Tj", texto);
        Assert.Contains(@"(Testigo: Tom\341s Testigo) Tj", texto);
        Assert.Contains(@"(Se neg\363 a firmar \(ver la constancia de no firma\)) Tj", texto);
        Assert.Contains("/Subtype /Image /Width 40 /Height 30 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", texto);
        Assert.Contains("/XObject << /Im1 5 0 R >>", texto);
        Assert.Contains(" /Im1 Do Q", texto);
    }

    [Fact]
    public void La_constancia_abierta_dice_quien_no_ha_firmado()
    {
        var texto = Texto(Generar(Datos(tipoCierre: null, recibe: SinFirma("Carlos Conductor"))));

        Assert.Contains("(Cierre de la transferencia: abierta \\(sin tipo de cierre\\)) Tj", texto);
        Assert.Contains("(No ha firmado) Tj", texto);
    }

    [Fact]
    public void La_firma_guardada_como_imagen_se_incrusta_y_un_png_se_pasa_con_su_predictor()
    {
        var datos = Datos(entrega: Firma("Ines Inspectora", imagen: Png()), recibe: Firma("Carlos Conductor", imagen: Jpeg(60, 20)));

        var texto = Texto(Generar(datos));

        Assert.Equal(2, Regex.Matches(texto, "/Subtype /Image").Count);
        Assert.Contains("/Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns 2 >>", texto);
        Assert.Contains("/Filter /DCTDecode", texto);
        Assert.Contains("/Im1 5 0 R /Im2 6 0 R", texto);
    }

    [Fact]
    public void Un_png_con_paleta_se_incrusta_como_indexado()
    {
        var paleta = new byte[] { 255, 0, 0, 0, 255, 0 };
        var datos = Datos(fotos: [new FotoConstancia(Png(tipoColor: 3, paleta: paleta), "Foto")]);

        var texto = Texto(Generar(datos));

        Assert.Contains("/ColorSpace [/Indexed /DeviceRGB 1 <FF0000" + "00FF00>]", texto);
        Assert.Contains("/Colors 1", texto);
    }

    [Fact]
    public void Una_imagen_que_no_se_puede_incrustar_se_indica_en_lugar_de_omitirse_o_fallar()
    {
        var gif = Encoding.ASCII.GetBytes("GIF89a-no-soportado");
        var pngCorrupto = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 5, 0x49, 0x48, 0x44, 0x52 };
        var datos = Datos(entrega: Firma("Ines Inspectora", imagen: gif), fotos: [new FotoConstancia(pngCorrupto, "Foto")]);

        var texto = Texto(Generar(datos));

        Assert.Equal(2, Regex.Matches(texto, Regex.Escape(@"(\(Imagen registrada; su formato no se puede incrustar en el PDF\)) Tj")).Count);
        Assert.DoesNotContain("/Subtype /Image", texto);
    }

    [Theory]
    [InlineData("M")]
    [InlineData("")]
    [InlineData("M5 5")]
    [InlineData("M5 5 L5 5")]
    public void Un_trazo_que_no_se_puede_dibujar_se_indica_sin_romper_el_documento(string trazo)
    {
        var texto = Texto(Generar(Datos(entrega: Firma("Ines Inspectora", trazo: trazo))));

        Assert.StartsWith("%PDF-1.4", texto);
        Assert.Contains(@"(\(Firma manuscrita registrada; el trazo no se pudo dibujar\)) Tj", texto);
    }

    [Fact]
    public void Las_curvas_relativas_y_los_comandos_de_cierre_del_trazo_se_dibujan()
    {
        var texto = Texto(Generar(Datos(entrega: Firma("Ines Inspectora", trazo: "M10,10 c 5,5 10,5 20,0 h 10 v -10 z"))));

        Assert.Contains(" m\n", texto);
        Assert.True(Regex.Matches(texto, @" l\n").Count >= 4);
    }

    [Fact]
    public void El_documento_es_un_pdf_estructuralmente_valido_con_sus_desplazamientos_correctos()
    {
        var datos = Datos(clsTipoCierre.NO_FIRMA, recibe: SinFirma("Carlos"), noFirmaParte: clsParteFirmante.RECIBE, motivo: "Se niega", testigo: "Tomás",
            fotos: [new FotoConstancia(Jpeg(), "a"), new FotoConstancia(Png(), "b")]);

        var texto = Texto(Generar(datos));

        var inicio = int.Parse(Regex.Match(texto, @"startxref\n(\d+)\n%%EOF").Groups[1].Value);
        Assert.StartsWith("xref\n0 ", texto[inicio..]);
        var total = int.Parse(Regex.Match(texto, @"/Size (\d+) /Root").Groups[1].Value);
        var desplazamientos = Regex.Matches(texto[inicio..], @"(\d{10}) 00000 n \n").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.Equal(total - 1, desplazamientos.Count);
        for (var i = 0; i < desplazamientos.Count; i++)
        {
            Assert.StartsWith($"{i + 1} 0 obj\n", texto[desplazamientos[i]..]);
        }
    }

    [Fact]
    public void Con_muchas_fotos_el_documento_pasa_a_otra_pagina_y_ninguna_se_pierde()
    {
        var fotos = Enumerable.Range(0, 7).Select(i => new FotoConstancia(Jpeg(300, 220), $"Foto {i}")).ToList();

        var texto = Texto(Generar(Datos(fotos: fotos)));

        var paginas = int.Parse(Regex.Match(texto, @"/Count (\d+)").Groups[1].Value);
        Assert.True(paginas >= 2);
        Assert.Equal(7, Regex.Matches(texto, "/Subtype /Image").Count);
        Assert.Equal(paginas, Regex.Matches(texto, "/Type /Page /Parent").Count);
    }

    [Fact]
    public void Un_texto_largo_se_parte_en_varias_lineas_sin_cortar_palabras_a_la_mitad()
    {
        var motivo = string.Join(' ', Enumerable.Repeat("incumplimiento", 30));
        var datos = Datos(clsTipoCierre.NO_FIRMA, recibe: SinFirma("Carlos"), noFirmaParte: clsParteFirmante.RECIBE, motivo: motivo, testigo: "Tomás");

        var texto = Texto(Generar(datos));

        var lineas = Regex.Matches(texto, @"\((Motivo: )?(incumplimiento[ incumplimiento]*)\) Tj").Select(m => m.Groups[2].Value).ToList();
        Assert.True(lineas.Count >= 2);
        Assert.All(lineas, l => Assert.All(l.Split(' '), palabra => Assert.Equal("incumplimiento", palabra)));
    }
}
