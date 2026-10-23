using System.Globalization;
using System.Net;
using System.Text;
using IAAutos.Aplicacion.Reportes.DTOReportes;

namespace IAAutos.Aplicacion.ReporteDiario;

/// <summary>El asunto y el cuerpo en HTML del reporte diario: las mismas cifras que sirve <c>GET /api/reportes/indicador-diario</c> y la mediana del tiempo de captura de ese dia.</summary>
public static class clsCorreoReporteDiario
{
    private const string TABLA = "border-collapse:collapse;margin:8px 0 16px 0;font-size:14px";
    private const string CELDA = "border:1px solid #c9ced6;padding:6px 10px;";
    private const string ENCABEZADO = CELDA + "background:#eef1f5;text-align:left;";
    private const string NUMERO = CELDA + "text-align:right;";

    /// <summary>El dia como AAAA-MM-DD.</summary>
    public static string Fecha(DateOnly dia) => dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Asunto(DateOnly dia) => $"Reporte diario IA Autos {Fecha(dia)}";

    public static string Construir(IndicadorDiarioDto indicador, TiempoCapturaDto tiempo)
    {
        var html = new StringBuilder();
        html.Append("<html><body style=\"font-family:Arial,Helvetica,sans-serif;color:#1f2933\">");
        html.Append(CultureInfo.InvariantCulture, $"<h2>Reporte diario IA Autos {Fecha(indicador.Fecha)}</h2>");
        html.Append("<p>Unidades despachadas ese día y cuántas tuvieron novedad (al menos un hallazgo de nivel V1 o V1+), frente al objetivo.</p>");

        html.Append("<h3>Indicador del día</h3>");
        Abrir(html, "Despachados", "Con novedad", "% con novedad", "Objetivo (%)", "Frente al objetivo");
        Fila(html, true, [Texto(indicador.Despachados), Texto(indicador.ConNovedad), Texto(indicador.Porcentaje), Texto(indicador.Objetivo), FrenteAlObjetivo(indicador.Porcentaje, indicador.Objetivo)]);
        html.Append("</table>");

        html.Append("<h3>Por operación</h3>");
        if (indicador.PorOperacion.Count == 0)
        {
            html.Append("<p>Sin unidades despachadas ese día.</p>");
        }
        else
        {
            Abrir(html, "Operación", "Despachados", "Con novedad", "% con novedad", "Frente al objetivo");
            foreach (var o in indicador.PorOperacion)
            {
                Fila(html, false, [o.Operacion, Texto(o.Despachados), Texto(o.ConNovedad), Texto(o.Porcentaje), FrenteAlObjetivo(o.Porcentaje, indicador.Objetivo)]);
            }

            html.Append("</table>");
        }

        html.Append("<h3>Por cliente</h3>");
        if (indicador.PorCliente.Count == 0)
        {
            html.Append("<p>Sin unidades despachadas ese día.</p>");
        }
        else
        {
            Abrir(html, "Cliente", "Despachados", "Con novedad", "% con novedad", "Frente al objetivo");
            foreach (var c in indicador.PorCliente)
            {
                Fila(html, false, [c.ClienteNombre ?? "—", Texto(c.Despachados), Texto(c.ConNovedad), Texto(c.Porcentaje), FrenteAlObjetivo(c.Porcentaje, indicador.Objetivo)]);
            }

            html.Append("</table>");
        }

        html.Append("<h3>Tiempo de captura</h3>");
        if (tiempo.MedianaMinutos is not { } mediana)
        {
            html.Append("<p>No hubo inspecciones de Cargue o Descargue cerradas ese día.</p>");
        }
        else
        {
            html.Append(CultureInfo.InvariantCulture, $"<p>Mediana entre el inicio y el cierre de la inspección: <strong>{Texto(mediana)} minutos</strong> ({tiempo.PorProceso.Sum(p => p.Inspecciones)} inspecciones).</p>");
            Abrir(html, "Proceso", "Mediana (minutos)", "Inspecciones");
            foreach (var p in tiempo.PorProceso)
            {
                Fila(html, false, [p.Proceso, Texto(p.MedianaMinutos), Texto(p.Inspecciones)]);
            }

            html.Append("</table>");
        }

        html.Append("<p>Se adjunta el Excel con los hallazgos del día.</p>");
        html.Append("</body></html>");
        return html.ToString();
    }

    /// <summary>Un resumen en texto del reporte, para el mensaje de la notificacion que lo registra.</summary>
    public static string Resumen(IndicadorDiarioDto indicador, TiempoCapturaDto tiempo) =>
        $"Día {Fecha(indicador.Fecha)}: {Texto(indicador.Despachados)} despachados, {Texto(indicador.ConNovedad)} con novedad ({Texto(indicador.Porcentaje)} %), objetivo {Texto(indicador.Objetivo)} %. " +
        (tiempo.MedianaMinutos is { } mediana ? $"Mediana de captura: {Texto(mediana)} min." : "Sin inspecciones cerradas ese día.");

    /// <summary>Un numero con coma decimal y sin ceros sobrantes (1,2 · 0 · 12,5).</summary>
    internal static string Texto(decimal valor) => valor.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Texto(int valor) => valor.ToString(CultureInfo.InvariantCulture);

    private static string FrenteAlObjetivo(decimal porcentaje, decimal objetivo) => porcentaje > objetivo ? "Por encima" : "Dentro";

    private static void Abrir(StringBuilder html, params string[] encabezados)
    {
        html.Append(CultureInfo.InvariantCulture, $"<table style=\"{TABLA}\"><tr>");
        foreach (var e in encabezados)
        {
            html.Append(CultureInfo.InvariantCulture, $"<th style=\"{ENCABEZADO}\">{WebUtility.HtmlEncode(e)}</th>");
        }

        html.Append("</tr>");
    }

    /// <summary>Una fila: la primera columna es texto y las demas se alinean a la derecha (numeros); <paramref name="todasNumericas"/> alinea tambien la primera.</summary>
    private static void Fila(StringBuilder html, bool todasNumericas, string[] celdas)
    {
        html.Append("<tr>");
        for (var i = 0; i < celdas.Length; i++)
        {
            var estilo = i == 0 && !todasNumericas ? CELDA : NUMERO;
            html.Append(CultureInfo.InvariantCulture, $"<td style=\"{estilo}\">{WebUtility.HtmlEncode(celdas[i])}</td>");
        }

        html.Append("</tr>");
    }
}
