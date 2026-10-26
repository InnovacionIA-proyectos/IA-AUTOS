using System.Globalization;
using System.IO.Compression;
using System.Text;
using IAAutos.Aplicacion.Exportaciones;

namespace IAAutos.Infraestructura.Reportes;

/// <summary>
/// Generador de archivos .xlsx sin dependencias de terceros (sin licencias): un libro de una hoja con la fila de titulos en negrita y congelada, filtros automaticos y anchos de
/// columna. Los textos van como cadenas incrustadas (nunca se evaluan como formula), los numeros como numeros y las fechas como fechas de Excel en hora de Colombia (UTC-5).
/// </summary>
internal sealed class clsGeneradorExcel : IGeneradorExcel
{
    private const string ESPACIO_HOJA = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const int ESTILO_TITULO = 1;
    private const int ESTILO_FECHA = 2;
    private static readonly DateTime ORIGEN_EXCEL = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly TimeSpan COLOMBIA = TimeSpan.FromHours(-5);

    public byte[] Generar(string hoja, IReadOnlyList<ColumnaExcel> columnas, IReadOnlyList<object?[]> filas)
    {
        using var salida = new MemoryStream();
        using (var zip = new ZipArchive(salida, ZipArchiveMode.Create, leaveOpen: true))
        {
            Escribir(zip, "[Content_Types].xml", CONTENT_TYPES);
            Escribir(zip, "_rels/.rels", RELACIONES_RAIZ);
            Escribir(zip, "xl/workbook.xml", Libro(hoja));
            Escribir(zip, "xl/_rels/workbook.xml.rels", RELACIONES_LIBRO);
            Escribir(zip, "xl/styles.xml", ESTILOS);
            Escribir(zip, "xl/worksheets/sheet1.xml", Hoja(columnas, filas));
        }

        return salida.ToArray();
    }

    private static void Escribir(ZipArchive zip, string ruta, string contenido)
    {
        var entrada = zip.CreateEntry(ruta, CompressionLevel.Optimal);
        using var flujo = entrada.Open();
        flujo.Write(new UTF8Encoding(false).GetBytes(contenido));
    }

    private static string Libro(string hoja) =>
        $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="{ESPACIO_HOJA}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{Atributo(NombreDeHoja(hoja))}" sheetId="1" r:id="rId1"/></sheets></workbook>""";

    /// <summary>El nombre de una hoja admite hasta 31 caracteres y no puede tener : \ / ? * [ ].</summary>
    private static string NombreDeHoja(string hoja)
    {
        var limpio = new string(hoja.Where(c => !":\\/?*[]".Contains(c)).ToArray()).Trim();
        return limpio.Length == 0 ? "Hoja1" : limpio[..Math.Min(limpio.Length, 31)];
    }

    private static string Hoja(IReadOnlyList<ColumnaExcel> columnas, IReadOnlyList<object?[]> filas)
    {
        var xml = new StringBuilder();
        xml.Append($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="{ESPACIO_HOJA}">""");
        var ultima = Referencia(Math.Max(columnas.Count, 1) - 1, filas.Count + 1);
        xml.Append($"""<dimension ref="A1:{ultima}"/>""");
        xml.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/><selection pane="bottomLeft"/></sheetView></sheetViews>""");
        xml.Append("""<sheetFormatPr defaultRowHeight="15"/>""");
        xml.Append("<cols>");
        for (var i = 0; i < columnas.Count; i++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<col min="{i + 1}" max="{i + 1}" width="{Math.Clamp(columnas[i].Ancho, 6, 80)}" customWidth="1"/>""");
        }

        xml.Append("</cols><sheetData>");
        xml.Append("""<row r="1">""");
        for (var c = 0; c < columnas.Count; c++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<c r="{Referencia(c, 1)}" s="{ESTILO_TITULO}" t="inlineStr"><is><t xml:space="preserve">{Texto(columnas[c].Titulo)}</t></is></c>""");
        }

        xml.Append("</row>");
        for (var f = 0; f < filas.Count; f++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"""<row r="{f + 2}">""");
            var fila = filas[f];
            for (var c = 0; c < fila.Length && c < columnas.Count; c++)
            {
                Celda(xml, Referencia(c, f + 2), fila[c]);
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData>");
        if (columnas.Count > 0)
        {
            xml.Append($"""<autoFilter ref="A1:{Referencia(columnas.Count - 1, Math.Max(filas.Count, 0) + 1)}"/>""");
        }

        xml.Append("</worksheet>");
        return xml.ToString();
    }

    private static void Celda(StringBuilder xml, string referencia, object? valor)
    {
        switch (valor)
        {
            case null:
                return;
            case DateTimeOffset fecha:
                xml.Append(CultureInfo.InvariantCulture, $"""<c r="{referencia}" s="{ESTILO_FECHA}"><v>{Serial(fecha).ToString("0.######", CultureInfo.InvariantCulture)}</v></c>""");
                return;
            case bool booleano:
                EscribirTexto(xml, referencia, booleano ? "Sí" : "No");
                return;
            case int or long or short or byte or decimal or double or float:
                xml.Append(CultureInfo.InvariantCulture, $"""<c r="{referencia}"><v>{Convert.ToString(valor, CultureInfo.InvariantCulture)}</v></c>""");
                return;
            default:
                var texto = Convert.ToString(valor, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(texto))
                {
                    EscribirTexto(xml, referencia, texto);
                }

                return;
        }
    }

    private static void EscribirTexto(StringBuilder xml, string referencia, string texto) =>
        xml.Append(CultureInfo.InvariantCulture, $"""<c r="{referencia}" t="inlineStr"><is><t xml:space="preserve">{Texto(texto)}</t></is></c>""");

    /// <summary>El numero de serie de Excel de la fecha, en hora de Colombia (dias desde 1899-12-30).</summary>
    private static double Serial(DateTimeOffset fecha) => (fecha.ToOffset(COLOMBIA).DateTime - ORIGEN_EXCEL).TotalDays;

    /// <summary>La referencia de una celda («A1», «AB12») a partir de la columna (desde 0) y la fila (desde 1).</summary>
    private static string Referencia(int columna, int fila)
    {
        var letras = new StringBuilder();
        for (var n = columna + 1; n > 0; n = (n - 1) / 26)
        {
            letras.Insert(0, (char)('A' + ((n - 1) % 26)));
        }

        return $"{letras}{fila.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Texto para el contenido de un elemento XML: sin los caracteres que XML no admite y con los reservados escapados.</summary>
    private static string Texto(string texto)
    {
        var limpio = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            if (c is '\t' or '\n' or '\r' || (c >= 0x20 && c is not '￾' and not '￿'))
            {
                limpio.Append(c);
            }
        }

        return limpio.ToString().Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string Atributo(string texto) => Texto(texto).Replace("\"", "&quot;", StringComparison.Ordinal);

    private const string CONTENT_TYPES =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""";

    private const string RELACIONES_RAIZ =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

    private const string RELACIONES_LIBRO =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""";

    /// <summary>Estilos: 0 normal, 1 titulo (negrita sobre fondo gris), 2 fecha con hora (yyyy-mm-dd hh:mm:ss).</summary>
    private const string ESTILOS =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy\-mm\-dd\ hh:mm:ss"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFD9E1F2"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""";
}
