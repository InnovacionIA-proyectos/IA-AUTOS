using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Tests.Unitarios.Dominio;

public class ConfirmacionSitioTests
{
    private const double LAT = 6.2;
    private const double LNG = -75.5;
    private static readonly DateTimeOffset AHORA = new(2026, 11, 3, 6, 0, 0, TimeSpan.FromHours(-5));

    /// <summary>La latitud de un punto al norte del centro, a esa distancia en metros.</summary>
    private static double AlNorte(double metros) => LAT + (metros / clsConfirmacionSitio.RADIO_TIERRA_M * 180 / Math.PI);

    private static clsEvaluacionSitio Evaluar(double metros, double? precision, double radio = 1000, bool confirmada = true, double? lugarLat = LAT, double valvula = 500) =>
        clsConfirmacionSitio.Evaluar(AlNorte(metros), LNG, precision, lugarLat, lugarLat is null ? null : LNG, confirmada, radio, valvula);

    [Fact]
    public void Un_grado_de_latitud_mide_unos_111_kilometros_y_uno_de_longitud_en_el_ecuador_lo_mismo()
    {
        Assert.Equal(111195.08, clsConfirmacionSitio.Distancia(0, 0, 1, 0), 1);
        Assert.Equal(111195.08, clsConfirmacionSitio.Distancia(0, 0, 0, 1), 1);
        Assert.Equal(111195.08 / 2, clsConfirmacionSitio.Distancia(0, 0, 0.5, 0), 1);
    }

    [Fact]
    public void La_distancia_coincide_con_la_que_se_uso_para_ubicar_el_punto_y_es_simetrica()
    {
        Assert.Equal(400, clsConfirmacionSitio.Distancia(LAT, LNG, AlNorte(400), LNG), 6);
        Assert.Equal(clsConfirmacionSitio.Distancia(LAT, LNG, AlNorte(2500), LNG), clsConfirmacionSitio.Distancia(AlNorte(2500), LNG, LAT, LNG), 9);
        Assert.Equal(0, clsConfirmacionSitio.Distancia(LAT, LNG, LAT, LNG));
    }

    [Fact]
    public void Dentro_del_rango_queda_confirmado_en_sitio_con_su_distancia()
    {
        var r = Evaluar(400, 25);

        Assert.Equal(clsResultadoConfirmacionSitio.CONFIRMADO_EN_SITIO, r.Resultado);
        Assert.Equal(400, r.DistanciaM!.Value, 1);
    }

    [Fact]
    public void La_duda_del_GPS_juega_a_favor_y_el_limite_exacto_confirma()
    {
        // 1.200 m con precision de 200 m: 1.200 no supera 1.000 + 200. A 1.201 m con la misma precision, si.
        Assert.Equal(clsResultadoConfirmacionSitio.CONFIRMADO_EN_SITIO, Evaluar(1200, 200).Resultado);
        Assert.Equal(clsResultadoConfirmacionSitio.FUERA_DE_RANGO, Evaluar(1201, 200).Resultado);
    }

    [Fact]
    public void Lejos_del_centro_con_buena_precision_queda_fuera_del_rango()
    {
        var r = Evaluar(2500, 30);

        Assert.Equal(clsResultadoConfirmacionSitio.FUERA_DE_RANGO, r.Resultado);
        Assert.Equal(2500, r.DistanciaM!.Value, 1);
    }

    [Theory]
    [InlineData(800)]
    [InlineData(500)]
    [InlineData(5000)]
    public void Una_precision_que_llega_a_la_valvula_no_confirma_ni_acusa(double precision)
    {
        Assert.Equal(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE, Evaluar(2500, precision).Resultado);
        Assert.Equal(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE, Evaluar(10, precision).Resultado);
    }

    [Fact]
    public void Una_precision_justo_debajo_de_la_valvula_si_cuenta()
    {
        Assert.Equal(clsResultadoConfirmacionSitio.FUERA_DE_RANGO, Evaluar(2500, 499.9).Resultado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void Sin_precision_reportada_o_invalida_no_hay_confirmacion_posible(double? precision) =>
        Assert.Equal(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE, Evaluar(2500, precision).Resultado);

    [Fact]
    public void Una_foto_sin_ubicacion_es_neutra()
    {
        var r = clsConfirmacionSitio.Evaluar(0, 0, 10, LAT, LNG, true, 1000, 500);

        Assert.Equal(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE, r.Resultado);
        Assert.Null(r.DistanciaM);
    }

    [Fact]
    public void Un_lugar_sin_ubicacion_o_con_la_ubicacion_pendiente_de_confirmar_no_confirma_ni_acusa()
    {
        Assert.Equal(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE, Evaluar(2500, 20, lugarLat: null).Resultado);
        Assert.Equal(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE, Evaluar(2500, 20, confirmada: false).Resultado);
        Assert.Equal(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE, Evaluar(10, 20, confirmada: false).Resultado);
    }

    [Fact]
    public void El_radio_del_lugar_manda_sobre_el_resto()
    {
        Assert.Equal(clsResultadoConfirmacionSitio.FUERA_DE_RANGO, Evaluar(400, 25, radio: 300).Resultado);
        Assert.Equal(clsResultadoConfirmacionSitio.CONFIRMADO_EN_SITIO, Evaluar(400, 25, radio: 500).Resultado);
    }

    [Fact]
    public void Los_nombres_de_los_resultados_se_muestran_en_espanol()
    {
        Assert.Equal("Confirmado en sitio", clsResultadoConfirmacionSitio.Nombre(clsResultadoConfirmacionSitio.CONFIRMADO_EN_SITIO));
        Assert.Equal("Fuera del rango", clsResultadoConfirmacionSitio.Nombre(clsResultadoConfirmacionSitio.FUERA_DE_RANGO));
        Assert.Equal("Sin confirmación posible", clsResultadoConfirmacionSitio.Nombre(clsResultadoConfirmacionSitio.SIN_CONFIRMACION_POSIBLE));
        Assert.Equal(3, clsResultadoConfirmacionSitio.VALORES.Count);
    }

    [Fact]
    public void El_resultado_se_guarda_en_un_borrador_y_una_cerrada_es_inmutable()
    {
        var inspeccion = clsInspeccion.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), clsMomento.LLEGADA, AHORA, 4.6, -74.1, null, null, AHORA);
        var foto = Guid.NewGuid();

        inspeccion.RegistrarConfirmacionSitio(clsResultadoConfirmacionSitio.FUERA_DE_RANGO, 2500.5, foto, AHORA.AddMinutes(1));

        Assert.Equal(clsResultadoConfirmacionSitio.FUERA_DE_RANGO, inspeccion.ConfirmacionSitio);
        Assert.Equal(2500.5, inspeccion.ConfirmacionDistanciaM);
        Assert.Equal(foto, inspeccion.ConfirmacionEvidenciaId);
        Assert.Throws<ArgumentException>(() => inspeccion.RegistrarConfirmacionSitio("inventado", null, foto, AHORA));
        inspeccion.Cerrar(new clsResumenCierre(0, 0, 0, 0, []), [], true, AHORA.AddMinutes(2));
        Assert.Throws<clsConflictoException>(() => inspeccion.RegistrarConfirmacionSitio(clsResultadoConfirmacionSitio.CONFIRMADO_EN_SITIO, 1, foto, AHORA.AddMinutes(3)));
        Assert.Equal(clsResultadoConfirmacionSitio.FUERA_DE_RANGO, inspeccion.ConfirmacionSitio);
    }
}
