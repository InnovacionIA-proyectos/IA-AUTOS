using System.Text;
using System.Text.RegularExpressions;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Dominio.Custodia;
using IAAutos.Infraestructura.Reportes;
using IAAutos.Tests.Unitarios.Falsos;

namespace IAAutos.Tests.Unitarios.Reportes;

/// <summary>El PDF del informe de una inspeccion y el del inventario con fotos y paneles: su contenido, las imagenes incrustadas, el panel con sus zonas marcadas y las notas de lo que no se pudo incluir.</summary>
public sealed partial class GeneradorPdfInformeTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    private static string Texto(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    /// <summary>Todo el texto del PDF en una sola linea: las lineas que el generador parte por el ancho se vuelven a unir con un espacio.</summary>
    private static string Plano(string pdf) =>
        string.Join(' ', TextoDeLinea().Matches(pdf).Select(m => m.Groups[1].Value.Replace(@"\(", "(").Replace(@"\)", ")").Replace(@"\\", @"\")));

    [GeneratedRegex(@"\(((?:\\.|[^\\)])*)\) Tj")]
    private static partial Regex TextoDeLinea();

    private static int Imagenes(string pdf) => ImagenXObject().Count(pdf);

    private static int Paginas(string pdf) => Paginas().Match(pdf) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    [GeneratedRegex(@"/Subtype /Image")]
    private static partial Regex ImagenXObject();

    [GeneratedRegex(@"/Type /Pages /Kids \[[^\]]*\] /Count (\d+)")]
    private static partial Regex Paginas();

    private static FotoInforme Foto(string titulo, byte[]? contenido = null, params string[] pie) =>
        new(contenido ?? clsImagenesDePrueba.Jpeg(), titulo, [titulo, .. pie]);

    private static FirmaConstancia Firma(string persona) => new(persona, AHORA.AddMinutes(-30), "10.0.0.5", "M0 0 L40 20 L80 0", null);

    private static HallazgoInforme Hallazgo(int numero, string zona = "Izquierdo 7 · Puerta delantera", IReadOnlyList<FotoInforme>? fotos = null, IReadOnlyList<SeguimientoInforme>? seguimiento = null) =>
        new(numero, zona, "golpe", "5_10cm", "V1+", "Se recupera antes del despacho", "inspector", false, "pendiente", "Golpe en la puerta", fotos ?? [], seguimiento ?? [], []);

    private static DatosInforme Datos(
        IReadOnlyList<FotoInforme>? vistas = null,
        IReadOnlyList<HallazgoInforme>? hallazgos = null,
        IReadOnlyList<PanelInforme>? paneles = null,
        IReadOnlyList<string>? sinRegion = null,
        TransferenciaInforme? transferencia = null,
        IReadOnlyList<NotaInforme>? notas = null,
        int omitidas = 0) =>
        new(
            "9FBHSR4E8PM000123", "Renault", "Duster", "SUV", "Gris", "nacional", "Sofasa", "MTK-001", "ABC123", "TR-1", "Ninera", "Carlos Conductor", "1032456789",
            "cargue", "CEDI 1", "Inspección de salida", 2, "cerrada", AHORA.AddHours(-2), AHORA.AddHours(-1), "Ines Inspectora", "Sin novedad adicional",
            vistas ?? [], hallazgos ?? [], paneles ?? [], sinRegion ?? [], transferencia, notas ?? [], omitidas, AHORA);

    private static byte[] Generar(DatosInforme datos) => new clsGeneradorPdfInforme().Generar(datos);

    // ------------------------------------------------------------------------------------ informe

    [Fact]
    public void El_informe_lleva_los_datos_de_la_unidad_el_viaje_y_la_etapa()
    {
        var pdf = Texto(Generar(Datos()));

        Assert.StartsWith("%PDF-1.4", pdf);
        foreach (var texto in new[] { "9FBHSR4E8PM000123", "Renault", "Duster", "Sofasa", "MTK-001", "ABC123", "TR-1", "Ninera", "Carlos Conductor", "1032456789", "CEDI 1", "Ines Inspectora", "2026-10-22 13:00:00 UTC", "2026-10-22 14:00:00 UTC" })
        {
            Assert.Contains(texto, pdf);
        }

        Assert.Contains("Informe de inspecci", pdf);
        Assert.Contains("Sin hallazgos.", pdf);
        Assert.Contains("no tiene transferencia", pdf);
    }

    [Fact]
    public void Las_fotos_de_las_vistas_van_incrustadas_con_su_sello()
    {
        var vistas = new[]
        {
            Foto("Vista: Frontal", pie: ["Fecha y hora: 2026-10-22 13:05:00 UTC", "Ubicación: 4.60971, -74.08175", "Sello del servidor: 2026-10-22 13:05:03 UTC", "SHA-256: abc123def456"]),
            Foto("Vista: Trasera", clsImagenesDePrueba.Png()),
            Foto("Vista: Superior", clsImagenesDePrueba.Jpeg(80, 60)),
        };

        var pdf = Texto(Generar(Datos(vistas)));

        Assert.Equal(3, Imagenes(pdf));
        Assert.Contains("Vista: Frontal", pdf);
        Assert.Contains("4.60971, -74.08175", pdf);
        Assert.Contains("Sello del servidor: 2026-10-22 13:05:03 UTC", pdf);
        Assert.Contains("SHA-256: abc123def456", pdf);
    }

    [Fact]
    public void Una_foto_cuyo_archivo_ya_no_esta_o_no_se_puede_incrustar_deja_una_nota_en_su_lugar()
    {
        var vistas = new[] { new FotoInforme(null, "Vista: Frontal", ["Vista: Frontal"]), new FotoInforme([1, 2, 3], "Vista: Trasera", ["Vista: Trasera"]) };

        var pdf = Texto(Generar(Datos(vistas)));

        Assert.Equal(0, Imagenes(pdf));
        Assert.Contains("Archivo no disponible en el almac", pdf);
        Assert.Contains("No se pudo incrustar la foto", pdf);
    }

    [Fact]
    public void Cada_hallazgo_trae_su_zona_clase_tamano_nivel_origen_y_foto()
    {
        var hallazgos = new[]
        {
            Hallazgo(1, fotos: [Foto("Foto del hallazgo")]),
            Hallazgo(2, "Frente", fotos: [Foto("Foto del hallazgo"), Foto("Foto del hallazgo")]),
        };

        var pdf = Texto(Generar(Datos(hallazgos: hallazgos)));

        Assert.Contains("Hallazgo 1 ", pdf);
        Assert.Contains("Hallazgo 2 ", pdf);
        Assert.Contains("Frente", pdf);
        Assert.Contains("Clase: golpe", pdf);
        Assert.Contains("5_10cm", pdf);
        Assert.Contains("V1+", pdf);
        Assert.Contains("Se recupera antes del despacho", pdf);
        Assert.Contains("Golpe en la puerta", pdf);
        Assert.Equal(3, Imagenes(pdf));
    }

    [Fact]
    public void El_panel_de_danos_incrusta_la_imagen_marca_las_zonas_y_lista_las_que_no_tienen_dibujo()
    {
        var region = new RegionPanel([new(0.2, 0.2), new(0.6, 0.2), new(0.6, 0.5), new(0.2, 0.5)], "1, 3");
        var paneles = new[] { new PanelInforme("Costado izquierdo", clsImagenesDePrueba.Png(8, 4), [region]) };

        var pdf = Texto(Generar(Datos(paneles: paneles, sinRegion: ["2: Frente"])));

        Assert.Equal(1, Imagenes(pdf));
        Assert.Contains("Costado izquierdo", pdf);
        Assert.Contains("(1, 3) Tj", pdf);
        Assert.Contains("0.85 0.1 0.1 RG", pdf);
        Assert.Contains(" m\n", pdf);
        Assert.Contains("hallazgo 2: Frente", pdf);
    }

    [Fact]
    public void Una_region_sin_puntos_suficientes_no_se_dibuja()
    {
        var paneles = new[] { new PanelInforme("Superior", clsImagenesDePrueba.Png(), [new RegionPanel([new(0.1, 0.1), new(0.2, 0.2)], "X")]) };

        var pdf = Texto(Generar(Datos(paneles: paneles)));

        Assert.DoesNotContain("(X) Tj", pdf);
    }

    [Fact]
    public void La_transferencia_trae_las_firmas_las_reservas_y_las_notas()
    {
        var transferencia = new TransferenciaInforme(
            clsTipoCierre.CON_RESERVAS, AHORA.AddMinutes(-20), Firma("Ines Inspectora"), Firma("Carlos Conductor"), "Rayon en la puerta", null, null, null, null,
            [new NotaInforme(AHORA.AddMinutes(-10), "Ana Coordinadora", "Aclaro la hora de entrega")]);

        var pdf = Texto(Generar(Datos(transferencia: transferencia)));

        Assert.Contains("firmada con reservas", pdf);
        Assert.Contains("Entrega: Ines Inspectora", pdf);
        Assert.Contains("Recibe: Carlos Conductor", pdf);
        Assert.Contains("IP 10.0.0.5", pdf);
        Assert.Contains("Rayon en la puerta", pdf);
        Assert.Contains("Aclaro la hora de entrega", pdf);
        Assert.Contains("Ana Coordinadora", pdf);
        // Las firmas dibujadas son trazos, no imagenes.
        Assert.Equal(0, Imagenes(pdf));
        Assert.Contains(" l\n", pdf);
    }

    [Fact]
    public void La_constancia_de_no_firma_trae_el_motivo_el_testigo_y_su_foto()
    {
        var transferencia = new TransferenciaInforme(
            clsTipoCierre.NO_FIRMA, null, Firma("Ines Inspectora"), new FirmaConstancia("Carlos Conductor", null, null, null, null), null, clsParteFirmante.RECIBE, "Se nego a firmar", "Pedro Testigo",
            Foto("Foto de la constancia de no firma"), []);

        var pdf = Texto(Generar(Datos(transferencia: transferencia)));

        Assert.Contains("no firma", pdf);
        Assert.Contains("Se nego a firmar", pdf);
        Assert.Contains("Pedro Testigo", pdf);
        Assert.Contains("Se neg", pdf);
        Assert.Equal(1, Imagenes(pdf));
    }

    [Fact]
    public void Las_firmas_guardadas_como_imagen_se_incrustan()
    {
        var firma = new FirmaConstancia("Carlos Conductor", AHORA, "10.0.0.9", null, clsImagenesDePrueba.Png(20, 10));
        var transferencia = new TransferenciaInforme(clsTipoCierre.FIRMADA, AHORA, firma, Firma("Ines Inspectora"), null, null, null, null, null, []);

        var pdf = Texto(Generar(Datos(transferencia: transferencia)));

        Assert.Equal(1, Imagenes(pdf));
    }

    [Fact]
    public void El_historial_de_seguimiento_y_las_notas_de_los_hallazgos_van_en_su_seccion()
    {
        var pasos = new[]
        {
            new SeguimientoInforme(AHORA.AddMinutes(-40), "revision", "pendiente", "validado", "Ana Coordinadora", "Se valida el hallazgo", null),
            new SeguimientoInforme(AHORA.AddMinutes(-30), "seguimiento", "validado", "recuperado", "Luis Calidad", null, Foto("Foto del sello de quien valida")),
        };
        var hallazgo = Hallazgo(1, seguimiento: pasos) with { NotasAclaratorias = [new NotaInforme(AHORA.AddMinutes(-5), "Ines Inspectora", "El golpe ya estaba")] };

        var pdf = Texto(Generar(Datos(hallazgos: [hallazgo])));

        Assert.Contains("Historial de seguimiento", pdf);
        Assert.Contains("pendiente", pdf);
        Assert.Contains("validado", pdf);
        Assert.Contains("recuperado", pdf);
        Assert.Contains("Se valida el hallazgo", Plano(pdf));
        Assert.Contains("Luis Calidad", pdf);
        Assert.Contains("El golpe ya estaba", Plano(pdf));
        Assert.Equal(1, Imagenes(pdf));
    }

    [Fact]
    public void Las_fotos_que_no_caben_se_avisan_y_el_documento_crece_en_paginas_sin_perder_nada()
    {
        var hallazgos = Enumerable.Range(1, 40).Select(i => Hallazgo(i, $"Zona {i}", [Foto("Foto del hallazgo")])).ToList();

        var pdf = Texto(Generar(Datos(hallazgos: hallazgos, omitidas: 7)));

        Assert.Equal(40, Imagenes(pdf));
        Assert.True(Paginas(pdf) > 5);
        Assert.Contains("no incluye 7 foto(s)", Plano(pdf));
        Assert.Contains("Hallazgo 40 ", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
    }

    [Fact]
    public void Los_textos_con_caracteres_especiales_no_rompen_el_documento()
    {
        var hallazgo = Hallazgo(1, @"Panel (izq.) \ der. ñ 日本 ✓") with { Nota = new string('x', 400) };

        var pdf = Texto(Generar(Datos(hallazgos: [hallazgo])));

        Assert.Contains(@"Panel \(izq.\) \\ der.", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
    }

    // ------------------------------------------------------------------------------------ inventario

    private static DatosInventarioPdf Inventario(IReadOnlyDictionary<Guid, FotoInforme>? fotos = null, IReadOnlyList<PanelInforme>? paneles = null, IReadOnlyList<string>? sinRegion = null, int omitidas = 0, bool activo = true)
    {
        var evidencia = new IAAutos.Aplicacion.Inspecciones.DTOInspecciones.EvidenciaDto(
            EvidenciaId, Guid.NewGuid(), null, AHORA, 4.6, -74.1, null, null, AHORA, AHORA, null, "abc", AHORA, null, null, null, false, false, false);
        var inspeccion = new IAAutos.Aplicacion.Vehiculos.DTOVehiculos.InventarioInspeccionDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "salida", AHORA, null, null, "cerrada", null, null, null, null, AHORA, AHORA, AHORA, AHORA, 1, null, "Inspección de salida",
            [], [evidencia], null, [], null, []);
        var etapa = new IAAutos.Aplicacion.Vehiculos.DTOVehiculos.InventarioEtapaDto(Guid.NewGuid(), 1, "cargue", null, true, Guid.NewGuid(), "MTK-1", false, "activa", null, [inspeccion]);
        var inventario = new IAAutos.Aplicacion.Vehiculos.DTOVehiculos.InventarioDto(Guid.NewGuid(), "VIN123", "Renault", null, null, null, null, "nacional", activo, AHORA, AHORA, [etapa]);
        return new DatosInventarioPdf(inventario, fotos ?? new Dictionary<Guid, FotoInforme>(), paneles ?? [], sinRegion ?? [], omitidas);
    }

    private static readonly Guid EvidenciaId = Guid.NewGuid();

    [Fact]
    public void El_inventario_incluye_las_fotos_de_sus_inspecciones_con_su_sello()
    {
        var fotos = new Dictionary<Guid, FotoInforme> { [EvidenciaId] = Foto("Vista: Frontal", pie: "SHA-256: abc") };

        var pdf = Texto(new clsGeneradorPdfInventario().Generar(Inventario(fotos)));

        Assert.Equal(1, Imagenes(pdf));
        Assert.Contains("Vista: Frontal", pdf);
        Assert.Contains("SHA-256: abc", pdf);
        Assert.Contains("Evidencias: 1", pdf);
    }

    [Fact]
    public void El_inventario_incluye_los_paneles_con_la_cantidad_de_hallazgos_de_cada_zona_y_avisa_lo_que_dejo_fuera()
    {
        var region = new RegionPanel([new(0.1, 0.1), new(0.5, 0.1), new(0.5, 0.4)], "3");
        var paneles = new[] { new PanelInforme("Superior", clsImagenesDePrueba.Jpeg(), [region]) };

        var pdf = Texto(new clsGeneradorPdfInventario().Generar(Inventario(paneles: paneles, sinRegion: ["2: Frente"], omitidas: 4)));

        Assert.Equal(1, Imagenes(pdf));
        Assert.Contains("Superior", pdf);
        Assert.Contains("(3) Tj", pdf);
        Assert.Contains("2: Frente hallazgo(s)", Plano(pdf));
        Assert.Contains("no incluye 4 foto(s)", Plano(pdf));
    }

    [Fact]
    public void Un_vehiculo_dado_de_baja_lo_dice_en_el_inventario_y_uno_activo_no()
    {
        Assert.Contains("dado de baja", Texto(new clsGeneradorPdfInventario().Generar(Inventario(activo: false))));
        Assert.DoesNotContain("dado de baja", Texto(new clsGeneradorPdfInventario().Generar(Inventario())));
    }
}
