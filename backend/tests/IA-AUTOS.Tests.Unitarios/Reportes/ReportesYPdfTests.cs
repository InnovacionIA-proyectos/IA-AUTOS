using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Aplicacion.Inspecciones;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Infraestructura.Reportes;
using IAAutos.Tests.Unitarios.Falsos;

namespace IAAutos.Tests.Unitarios.Reportes;

internal sealed class clsConsultasReportesFalsas : IConsultasReportes
{
    public ConteosRechazo Rechazo { get; set; } = new(0, 0, 0, []);

    /// <summary>El cliente al que se limito la ultima consulta (nulo: sin restriccion).</summary>
    public Guid? UltimoCliente { get; private set; }

    public Task<ConteosRechazo> ObtenerConteosRechazoAsync(Guid? soloDeCliente, CancellationToken ct = default)
    {
        UltimoCliente = soloDeCliente;
        return Task.FromResult(Rechazo);
    }
}

public class ReportesServiceTests
{
    [Fact]
    public async Task La_tasa_se_calcula_con_dos_decimales_frente_a_la_linea_base_y_ordena_las_operaciones()
    {
        var consultas = new clsConsultasReportesFalsas
        {
            Rechazo = new ConteosRechazo(10, 7, 4, [new ConteoOperacion("nacional", 3, 1), new ConteoOperacion("importacion", 4, 3)]),
        };

        var tasa = await new clsReportesService(consultas, new clsUsuarioActualFalso()).ObtenerTasaRechazoAsync();

        Assert.Equal(["importacion", "nacional"], tasa.PorOperacion.Select(o => o.Operacion));
        Assert.Equal(75m, tasa.PorOperacion[0].TasaActual);
        Assert.Equal(66.19m, tasa.PorOperacion[0].Variacion);
        Assert.Equal(33.33m, tasa.PorOperacion[1].TasaActual);
        Assert.Equal(31.38m, tasa.PorOperacion[1].Variacion);
        Assert.Equal(57.14m, tasa.TasaGlobal);
        Assert.Equal(7, tasa.TotalMovilizadas);
        Assert.Equal(3, tasa.TotalPendientes);
        Assert.Equal(4, tasa.TotalConNovedad);
        Assert.Equal(4, tasa.TotalNovedad);
    }

    [Fact]
    public async Task Una_operacion_sin_linea_base_conocida_usa_cero()
    {
        var consultas = new clsConsultasReportesFalsas { Rechazo = new ConteosRechazo(1, 1, 0, [new ConteoOperacion("rara", 1, 0)]) };

        var tasa = await new clsReportesService(consultas, new clsUsuarioActualFalso()).ObtenerTasaRechazoAsync();

        Assert.Equal(0m, tasa.PorOperacion[0].LineaBase);
    }
}

public class GeneradorPdfInventarioTests
{
    private static readonly DateTimeOffset FECHA = new(2026, 7, 17, 12, 30, 15, 123, TimeSpan.Zero);

    private static InventarioDto Inventario(string vin = "VIN123", int hallazgos = 1, string parte = "Capot", int etapas = 3)
    {
        var etapasDto = Enumerable.Range(1, etapas).Select(orden => new InventarioEtapaDto(
            Guid.NewGuid(),
            orden,
            orden == 1 ? "cargue" : "transporte",
            null,
            true,
            Guid.NewGuid(),
            "MTK-1",
            "activa",
            null,
            [
                new InventarioInspeccionDto(
                    Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "salida", FECHA, null, null, "cerrada", null, null, null, null, FECHA, FECHA, FECHA, FECHA, 1, null, "Inspección de salida",
                    Enumerable.Range(0, hallazgos).Select(_ => new HallazgoDto(
                        Guid.NewGuid(), Guid.NewGuid(), null, "golpe", "5_10cm", null, null, null, "frente", null, parte, null, "v2", "V2", null, "inspector", null, false, null, null,
                        "pendiente", false, [], null, null, FECHA, FECHA)).ToList(),
                    [],
                    null,
                    [],
                    null,
                    []),
            ])).ToList();
        return new InventarioDto(Guid.NewGuid(), vin, "Renault", null, null, null, null, "nacional", true, FECHA, FECHA, etapasDto);
    }

    private static string Texto(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static byte[] GenerarPdf(InventarioDto inventario) =>
        new clsGeneradorPdfInventario().Generar(new DatosInventarioPdf(inventario, new Dictionary<Guid, FotoInforme>(), [], [], 0));

    [Fact]
    public void Genera_un_pdf_con_estructura_valida_y_tabla_xref_coherente()
    {
        var pdf = GenerarPdf(Inventario());
        var texto = Texto(pdf);

        Assert.StartsWith("%PDF-1.4", texto);
        Assert.EndsWith("%%EOF\n", texto);

        var inicio = int.Parse(Regex.Match(texto, @"startxref\n(\d+)\n").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.StartsWith("xref", texto[inicio..]);

        var tamano = int.Parse(Regex.Match(texto, @"/Size (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var entradas = Regex.Matches(texto[inicio..], @"(\d{10}) 00000 n ");
        Assert.Equal(tamano - 1, entradas.Count);
        for (var i = 0; i < entradas.Count; i++)
        {
            var desplazamiento = int.Parse(entradas[i].Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i + 1} 0 obj", texto[desplazamiento..]);
        }

        var longitud = Regex.Match(texto, @"/Length (\d+) >>\nstream\n");
        var flujo = texto[(longitud.Index + longitud.Length)..];
        Assert.StartsWith("endstream", flujo[int.Parse(longitud.Groups[1].Value, CultureInfo.InvariantCulture)..].TrimStart('\n'));
    }

    [Fact]
    public void Incluye_el_contenido_del_inventario()
    {
        var texto = Texto(GenerarPdf(Inventario("9FBHSR4E8PM000123", hallazgos: 2)));

        Assert.Contains("VIN: 9FBHSR4E8PM000123", texto);
        Assert.Contains("Marca: Renault   Operaci\\363n: nacional", texto);
        Assert.Contains("Modelo: \\227   Color: \\227", texto); // raya larga en WinAnsi
        Assert.Contains("Proceso: cargue \\(orden 1\\)".Replace("\\(", "\\(").Replace("\\)", "\\)"), texto);
        Assert.Contains("\\225 Capot: golpe \\(5_10cm\\)", texto);
        Assert.Contains("Evidencias: 0", texto);
        Assert.Contains("Inspecci\\363n de salida \\227 cerrada \\227 2026-07-17T12:30:15.123Z", texto);
    }

    [Fact]
    public void Escapa_parentesis_y_barras_para_no_romper_la_cadena_del_pdf()
    {
        var texto = Texto(GenerarPdf(Inventario(parte: @"Panel (izq.) \ der.")));

        Assert.Contains(@"Panel \(izq.\) \\ der.", texto);
    }

    [Fact]
    public void Los_caracteres_fuera_de_winansi_se_reemplazan_sin_fallar()
    {
        var texto = Texto(GenerarPdf(Inventario(parte: "Capot 日本 ✓")));

        Assert.Contains("Capot ", texto);
        Assert.StartsWith("%PDF-1.4", texto);
    }

    [Fact]
    public void Un_inventario_largo_se_reparte_en_varias_paginas_con_conteo_consistente()
    {
        var pdf = GenerarPdf(Inventario(hallazgos: 60, etapas: 3));
        var texto = Texto(pdf);

        var paginas = Regex.Matches(texto, @"/Type /Page /Parent").Count;
        var conteo = int.Parse(Regex.Match(texto, @"/Count (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(paginas > 1);
        Assert.Equal(paginas, conteo);
        Assert.Equal(180, Regex.Matches(texto, Regex.Escape("\\225 Capot: golpe")).Count);
    }

    [Fact]
    public void Una_linea_muy_larga_se_parte_en_varias_sin_salirse_de_la_pagina()
    {
        var parte = string.Join(' ', Enumerable.Repeat("palabra", 40));

        var texto = Texto(GenerarPdf(Inventario(parte: parte, etapas: 1)));

        var tramos = Regex.Matches(texto, @"\(\s*\\225 palabra|\(\s+palabra").Count + Regex.Matches(texto, @"\(\s{6,}palabra").Count;
        Assert.True(tramos >= 1);
        Assert.True(Regex.Matches(texto, "palabra").Count == 40);
        foreach (Match m in Regex.Matches(texto, @"\((.*)\) Tj"))
        {
            Assert.True(m.Groups[1].Value.Length < 120);
        }
    }

    [Fact]
    public void Sin_etapas_genera_solo_el_encabezado()
    {
        var texto = Texto(GenerarPdf(Inventario(etapas: 0)));

        Assert.Contains("VIN: VIN123", texto);
        Assert.DoesNotContain("Proceso:", texto);
    }
}
