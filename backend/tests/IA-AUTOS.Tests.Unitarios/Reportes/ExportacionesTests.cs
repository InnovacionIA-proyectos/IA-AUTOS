using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using IAAutos.Aplicacion.Exportaciones;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Infraestructura.Reportes;
using IAAutos.Tests.Unitarios.Falsos;

namespace IAAutos.Tests.Unitarios.Reportes;

/// <summary>El generador de Excel (un paquete .xlsx valido, con fechas de Colombia y textos a salvo) y el servicio que arma las exportaciones.</summary>
public sealed class ExportacionesTests
{
    private static readonly XNamespace NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XDocument Hoja(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        using var flujo = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        return XDocument.Load(flujo);
    }

    private static XElement Celda(XDocument hoja, string referencia) => hoja.Descendants(NS + "c").Single(c => (string)c.Attribute("r")! == referencia);

    // ------------------------------------------------------------------------------------ generador

    [Fact]
    public void El_archivo_es_un_paquete_con_todas_sus_partes_y_cada_una_es_xml_bien_formado()
    {
        var bytes = new clsGeneradorExcel().Generar("Viajes", [new("A", 10)], [["x"]]);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var partes = zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/workbook.xml", "xl/worksheets/sheet1.xml"], partes);
        foreach (var entrada in zip.Entries)
        {
            using var flujo = entrada.Open();
            Assert.NotNull(XDocument.Load(flujo).Root);
        }

        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(bytes, 0, 2));
    }

    [Fact]
    public void La_fila_de_titulos_va_en_negrita_congelada_con_filtros_y_anchos()
    {
        var bytes = new clsGeneradorExcel().Generar("Hallazgos", [new("VIN", 20), new("Marca", 14)], [["V1", "Renault"], ["V2", "Toyota"]]);

        var hoja = Hoja(bytes);

        Assert.Equal("A1:B3", (string)hoja.Descendants(NS + "autoFilter").Single().Attribute("ref")!);
        Assert.Equal("A1:B3", (string)hoja.Descendants(NS + "dimension").Single().Attribute("ref")!);
        Assert.Equal("frozen", (string)hoja.Descendants(NS + "pane").Single().Attribute("state")!);
        Assert.Equal(["20", "14"], hoja.Descendants(NS + "col").Select(c => (string)c.Attribute("width")!).ToList());
        Assert.Equal("VIN", Celda(hoja, "A1").Descendants(NS + "t").Single().Value);
        Assert.Equal("1", (string)Celda(hoja, "A1").Attribute("s")!);
        Assert.Equal("Toyota", Celda(hoja, "B3").Descendants(NS + "t").Single().Value);
    }

    [Fact]
    public void Los_numeros_van_como_numeros_las_fechas_como_fechas_de_Excel_en_hora_de_Colombia_y_el_si_o_no_como_texto()
    {
        // 2026-10-22 03:00 UTC son las 22:00 del 21 en Colombia.
        var fecha = new DateTimeOffset(2026, 10, 22, 3, 0, 0, TimeSpan.Zero);
        var bytes = new clsGeneradorExcel().Generar("Prueba", [new("n", 8), new("d", 8), new("f", 8), new("b", 8), new("v", 8), new("t", 8)], [[7, 1.5m, fecha, true, null, "hola"]]);

        var hoja = Hoja(bytes);

        Assert.Equal("7", Celda(hoja, "A2").Element(NS + "v")!.Value);
        Assert.Equal("1.5", Celda(hoja, "B2").Element(NS + "v")!.Value);
        Assert.Null(Celda(hoja, "A2").Attribute("t"));
        var esperada = (new DateTime(2026, 10, 21, 22, 0, 0) - new DateTime(1899, 12, 30)).TotalDays;
        Assert.Equal(esperada, double.Parse(Celda(hoja, "C2").Element(NS + "v")!.Value, CultureInfo.InvariantCulture), 5);
        Assert.Equal("2", (string)Celda(hoja, "C2").Attribute("s")!);
        Assert.Equal("Sí", Celda(hoja, "D2").Descendants(NS + "t").Single().Value);
        Assert.DoesNotContain(hoja.Descendants(NS + "c"), c => (string)c.Attribute("r")! == "E2");
        Assert.Equal("hola", Celda(hoja, "F2").Descendants(NS + "t").Single().Value);
    }

    [Fact]
    public void Los_textos_se_escapan_y_pierden_los_caracteres_de_control_que_xml_no_admite()
    {
        var bytes = new clsGeneradorExcel().Generar("P", [new("t", 10)], [["A&B <c> \"d\" 'e'\u0001\u0008 ñ ó 😀"], ["=SUMA(A1:A2)"]]);

        var hoja = Hoja(bytes);

        Assert.Equal("A&B <c> \"d\" 'e' ñ ó 😀", Celda(hoja, "A2").Descendants(NS + "t").Single().Value);
        // Un texto que empieza con = no es una formula: va como cadena.
        Assert.Equal("inlineStr", (string)Celda(hoja, "A3").Attribute("t")!);
        Assert.Equal("=SUMA(A1:A2)", Celda(hoja, "A3").Descendants(NS + "t").Single().Value);
    }

    [Fact]
    public void Despues_de_la_columna_Z_las_referencias_siguen_con_AA()
    {
        var columnas = Enumerable.Range(1, 30).Select(i => new ColumnaExcel($"c{i}", 8)).ToList();
        var fila = Enumerable.Range(1, 30).Select(i => (object?)i).ToArray();

        var hoja = Hoja(new clsGeneradorExcel().Generar("P", columnas, [fila]));

        Assert.Equal("26", Celda(hoja, "Z2").Element(NS + "v")!.Value);
        Assert.Equal("27", Celda(hoja, "AA2").Element(NS + "v")!.Value);
        Assert.Equal("30", Celda(hoja, "AD2").Element(NS + "v")!.Value);
        Assert.Equal("A1:AD2", (string)hoja.Descendants(NS + "autoFilter").Single().Attribute("ref")!);
    }

    [Fact]
    public void El_nombre_de_la_hoja_se_limpia_y_se_recorta_a_31_caracteres()
    {
        var bytes = new clsGeneradorExcel().Generar("Viajes: del [mes] de / octubre? * de dos mil veintiseis", [new("A", 8)], []);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var flujo = zip.GetEntry("xl/workbook.xml")!.Open();
        var nombre = (string)XDocument.Load(flujo).Descendants(NS + "sheet").Single().Attribute("name")!;
        Assert.True(nombre.Length <= 31);
        Assert.DoesNotContain(nombre, c => ":\\/?*[]".Contains(c));
    }

    [Fact]
    public void Sin_filas_queda_solo_la_fila_de_titulos()
    {
        var hoja = Hoja(new clsGeneradorExcel().Generar("P", [new("A", 8), new("B", 8)], []));

        Assert.Single(hoja.Descendants(NS + "row"));
        Assert.Equal("A1:B1", (string)hoja.Descendants(NS + "autoFilter").Single().Attribute("ref")!);
    }

    // ------------------------------------------------------------------------------------ servicio

    private sealed class ConsultasFalsas : IConsultasExportaciones
    {
        public bool Excedido { get; set; }

        public Guid? ClienteRecibido { get; set; }

        public Guid? ClienteDeViajes { get; private set; }

        private static ContextoExportacion Contexto(Guid cliente) =>
            new("VIN-1", "Renault", null, "nacional", cliente, "MTK-1", "ABC123", "TR-1", "Niñera", "Carlos", "1032", "Cargue", null);

        public Task<ResultadoExportacion<FilaExportViaje>> ViajesAsync(FiltrosViajes filtros, Guid? soloDeCliente, int maximo, CancellationToken ct = default)
        {
            ClienteDeViajes = soloDeCliente;
            return Task.FromResult(new ResultadoExportacion<FilaExportViaje>(
                [new(Contexto(clsCatalogosLecturaFalso.SOFASA), "en_curso", "activa", null, null, "salida", 1, "cerrada", null, null)], Excedido));
        }

        public Task<ResultadoExportacion<FilaExportInspeccion>> InspeccionesAsync(FiltrosInspecciones filtros, Guid? clienteId, int maximo, CancellationToken ct = default)
        {
            ClienteRecibido = clienteId;
            return Task.FromResult(new ResultadoExportacion<FilaExportInspeccion>([], Excedido));
        }

        public Task<ResultadoExportacion<FilaExportHallazgo>> HallazgosAsync(FiltrosHallazgos filtros, Guid? clienteId, int maximo, CancellationToken ct = default)
        {
            ClienteRecibido = clienteId;
            return Task.FromResult(new ResultadoExportacion<FilaExportHallazgo>([], Excedido));
        }
    }

    private sealed class ExcelFalso : IGeneradorExcel
    {
        public string? Hoja { get; private set; }

        public IReadOnlyList<ColumnaExcel>? Columnas { get; private set; }

        public IReadOnlyList<object?[]>? Filas { get; private set; }

        public byte[] Generar(string hoja, IReadOnlyList<ColumnaExcel> columnas, IReadOnlyList<object?[]> filas)
        {
            (Hoja, Columnas, Filas) = (hoja, columnas, filas);
            return [1, 2, 3];
        }
    }

    private static (clsExportacionesService Servicio, ConsultasFalsas Consultas, ExcelFalso Excel) Crear(string rol = clsRol.COORDINADOR_CALIDAD)
    {
        var consultas = new ConsultasFalsas();
        var excel = new ExcelFalso();
        var actual = new clsUsuarioActualFalso { Id = Guid.NewGuid(), Rol = rol, ClienteId = rol == clsRol.SUPER_ADMIN ? null : clsCatalogosLecturaFalso.SOFASA };
        // 2026-10-22 03:00 UTC: en Colombia todavia es el 21.
        var servicio = new clsExportacionesService(consultas, excel, new clsCatalogosLecturaFalso(), actual, new clsRelojFijo(new DateTimeOffset(2026, 10, 22, 3, 0, 0, TimeSpan.Zero)));
        return (servicio, consultas, excel);
    }

    [Fact]
    public async Task El_archivo_se_llama_como_la_exportacion_con_el_dia_de_Colombia_y_las_filas_llevan_el_nombre_del_cliente()
    {
        var (servicio, _, excel) = Crear();

        var archivo = await servicio.ExportarViajesAsync(new FiltrosViajesRequest(null, null, null, null, null, null, null, null, null, null));

        Assert.Equal("viajes-2026-10-21.xlsx", archivo.Nombre);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo.TipoContenido);
        Assert.Equal([1, 2, 3], archivo.Contenido);
        Assert.Equal("Viajes", excel.Hoja);
        var fila = Assert.Single(excel.Filas!);
        Assert.Equal(excel.Columnas!.Count, fila.Length);
        Assert.Equal("Sofasa", fila[excel.Columnas.ToList().FindIndex(c => c.Titulo == "Cliente")]);
        Assert.Equal("VIN-1", fila[0]);
    }

    [Fact]
    public async Task Cada_exportacion_pasa_a_la_consulta_el_cliente_de_quien_la_pide()
    {
        var (servicio, consultas, _) = Crear();

        await servicio.ExportarViajesAsync(new FiltrosViajesRequest(null, null, null, null, null, null, null, null, null, null));
        Assert.Equal(clsCatalogosLecturaFalso.SOFASA, consultas.ClienteDeViajes);
        await servicio.ExportarInspeccionesAsync(new FiltrosInspeccionesRequest(null, null, null, null, null, null, null, null, null, null, null, null, null));
        Assert.Equal(clsCatalogosLecturaFalso.SOFASA, consultas.ClienteRecibido);
        consultas.ClienteRecibido = null;
        await servicio.ExportarHallazgosAsync(new FiltrosHallazgosRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null));
        Assert.Equal(clsCatalogosLecturaFalso.SOFASA, consultas.ClienteRecibido);
    }

    [Fact]
    public async Task El_super_administrador_exporta_todos_los_clientes_o_elige_uno_y_los_demas_no_piden_otro()
    {
        var (superAdmin, consultasSuper, _) = Crear(clsRol.SUPER_ADMIN);
        var (coordinador, _, _) = Crear();
        var vacio = new FiltrosInspeccionesRequest(null, null, null, null, null, null, null, null, null, null, null, null, null);

        await superAdmin.ExportarInspeccionesAsync(vacio);
        Assert.Null(consultasSuper.ClienteRecibido);
        await superAdmin.ExportarInspeccionesAsync(vacio with { ClienteId = clsCatalogosLecturaFalso.OTRO.ToString() });
        Assert.Equal(clsCatalogosLecturaFalso.OTRO, consultasSuper.ClienteRecibido);
        await Assert.ThrowsAsync<clsProhibidoException>(() =>
            coordinador.ExportarInspeccionesAsync(vacio with { ClienteId = clsCatalogosLecturaFalso.OTRO.ToString() }));
        await Assert.ThrowsAsync<clsValidacionException>(() => coordinador.ExportarInspeccionesAsync(vacio with { ClienteId = "no-es-uuid" }));
    }

    [Fact]
    public async Task Un_archivo_con_mas_filas_que_el_maximo_se_rechaza_con_400_para_que_se_acote()
    {
        var (servicio, consultas, _) = Crear();
        consultas.Excedido = true;

        var ex = await Assert.ThrowsAsync<clsValidacionException>(() => servicio.ExportarViajesAsync(new FiltrosViajesRequest(null, null, null, null, null, null, null, null, null, null)));
        Assert.Contains(clsExportacionesService.MAXIMO_FILAS.ToString(CultureInfo.InvariantCulture), ex.Message + string.Join(' ', ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Values.SelectMany(v => v)));
        await Assert.ThrowsAsync<clsValidacionException>(() => servicio.ExportarHallazgosAsync(new FiltrosHallazgosRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)));
    }
}
