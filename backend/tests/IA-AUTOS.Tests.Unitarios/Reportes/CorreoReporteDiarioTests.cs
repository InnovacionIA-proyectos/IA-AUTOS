using IAAutos.Aplicacion.ReporteDiario;
using IAAutos.Aplicacion.Reportes.DTOReportes;

namespace IAAutos.Tests.Unitarios.Reportes;

/// <summary>El cuerpo del correo del reporte diario y el dia que reporta el proceso (siempre el anterior, en hora de Colombia).</summary>
public sealed class CorreoReporteDiarioTests
{
    private static readonly Guid CLIENTE = Guid.NewGuid();

    private static IndicadorDiarioDto Indicador(string cliente = "Sofasa") =>
        new(new DateOnly(2026, 10, 21), 80, 1, 1.25m, 1.2m, [new IndicadorOperacionDto("nacional", 50, 1, 2m), new IndicadorOperacionDto("importacion", 30, 0, 0m)], [new IndicadorClienteDto(CLIENTE, cliente, 80, 1, 1.25m)]);

    private static readonly TiempoCapturaDto TIEMPO = new(12.5m, [new TiempoProcesoDto("cargue", 10m, 3), new TiempoProcesoDto("descargue", 15m, 1)], []);

    [Fact]
    public void El_asunto_lleva_la_fecha_del_dia_reportado()
    {
        Assert.Equal("Reporte diario IA Autos 2026-10-21", clsCorreoReporteDiario.Asunto(new DateOnly(2026, 10, 21)));
    }

    [Fact]
    public void El_cuerpo_trae_el_indicador_las_tablas_por_operacion_y_cliente_y_la_mediana_con_coma_decimal()
    {
        var html = clsCorreoReporteDiario.Construir(Indicador(), TIEMPO);

        Assert.Contains("Reporte diario IA Autos 2026-10-21", html);
        Assert.Contains(">80</td>", html);
        Assert.Contains(">1,25</td>", html);
        Assert.Contains(">1,2</td>", html);
        Assert.Contains(">Por encima</td>", html);
        Assert.Contains(">nacional</td>", html);
        Assert.Contains(">importacion</td>", html);
        Assert.Contains(">Sofasa</td>", html);
        Assert.Contains("<strong>12,5 minutos</strong> (4 inspecciones)", html);
        Assert.Contains(">cargue</td>", html);
    }

    [Fact]
    public void Un_porcentaje_igual_o_menor_al_objetivo_esta_dentro_de_el()
    {
        var html = clsCorreoReporteDiario.Construir(Indicador() with { Porcentaje = 1.2m }, TIEMPO);

        Assert.Contains(">Dentro</td>", html);
    }

    [Fact]
    public void El_nombre_del_cliente_se_escapa_y_el_correo_no_trae_rechazos()
    {
        var html = clsCorreoReporteDiario.Construir(Indicador("Cliente <b>&</b>"), TIEMPO);

        Assert.Contains("Cliente &lt;b&gt;&amp;&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>&</b>", html);
        Assert.DoesNotContain("rechazo", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sin_inspecciones_ni_despachos_el_cuerpo_lo_dice()
    {
        var html = clsCorreoReporteDiario.Construir(new IndicadorDiarioDto(new DateOnly(2026, 10, 21), 0, 0, 0m, 1.2m, [], []), new TiempoCapturaDto(null, [], []));

        Assert.Contains("Sin unidades despachadas ese día.", html);
        Assert.Contains("No hubo inspecciones de Cargue o Descargue cerradas ese día.", html);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1.2, "1,2")]
    [InlineData(33.33, "33,33")]
    [InlineData(50, "50")]
    [InlineData(12.5, "12,5")]
    public void Los_numeros_llevan_coma_decimal_y_sin_ceros_sobrantes(double valor, string esperado)
    {
        Assert.Equal(esperado, clsCorreoReporteDiario.Texto((decimal)valor));
    }

    [Theory]
    [InlineData("2026-10-22T06:00:00-05:00", "2026-10-21")]
    [InlineData("2026-10-22T00:00:00-05:00", "2026-10-21")]
    [InlineData("2026-10-22T23:59:00-05:00", "2026-10-21")]
    // 02:00 UTC del 22 son las 21:00 del 21 en Colombia: el reporte es el del 20.
    [InlineData("2026-10-22T02:00:00+00:00", "2026-10-20")]
    [InlineData("2026-01-01T08:00:00-05:00", "2025-12-31")]
    public void El_dia_reportado_es_el_anterior_en_hora_de_Colombia(string ahora, string esperado)
    {
        Assert.Equal(DateOnly.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), clsReporteDiarioService.DiaReportado(DateTimeOffset.Parse(ahora, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
