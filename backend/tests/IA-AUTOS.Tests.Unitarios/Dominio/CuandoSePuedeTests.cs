using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

public class CuandoSePuedeTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 10, 8, 30, 0, TimeSpan.FromHours(-5));

    private static clsInspeccion Inspeccion(string momento, string? motivo) =>
        clsInspeccion.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), momento, AHORA, 4.6, -74.1, null, null, AHORA, motivo);

    [Fact]
    public void Un_lugar_nuevo_no_admite_cargue_sin_inspector_y_el_coordinador_lo_activa_y_lo_desactiva()
    {
        var lugar = clsLugar.Crear("CEDI Envigado", Guid.NewGuid(), "Envigado", 1, true, AHORA);
        Assert.False(lugar.AdmiteCargueSinInspector);

        lugar.CambiarAdmiteCargueSinInspector(true, AHORA);
        Assert.True(lugar.AdmiteCargueSinInspector);

        lugar.CambiarAdmiteCargueSinInspector(false, AHORA);
        Assert.False(lugar.AdmiteCargueSinInspector);
    }

    [Theory]
    [InlineData(clsMomento.SALIDA)]
    [InlineData(clsMomento.LLEGADA)]
    public void El_motivo_de_la_excepcion_se_admite_en_la_salida_y_en_la_llegada(string momento)
    {
        var inspeccion = Inspeccion(momento, "inspector_no_llego");

        Assert.Equal("inspector_no_llego", inspeccion.MotivoSinInspector);
    }

    [Fact]
    public void Sin_motivo_la_inspeccion_de_llegada_nace_sin_excepcion()
    {
        Assert.Null(Inspeccion(clsMomento.LLEGADA, null).MotivoSinInspector);
    }

    [Theory]
    [InlineData(clsEstadoUnidad.ACTIVA, true, null)]
    [InlineData(clsEstadoUnidad.ACTIVA, false, "entrega_del_cargue_sin_cerrar")]
    [InlineData(clsEstadoUnidad.NO_SALE, true, "unidad_no_sale")]
    [InlineData(clsEstadoUnidad.NO_SALE, false, "unidad_no_sale")]
    [InlineData(clsEstadoUnidad.BAJADO, true, "unidad_no_sale")]
    public void El_motivo_sin_ruta_de_la_unidad_es_la_regla_que_aplica_el_servidor(string estado, bool entregaDelCargueCerrada, string? esperado)
    {
        Assert.Equal(esperado, clsMotivoSinRuta.De(estado, entregaDelCargueCerrada));
    }

    [Fact]
    public void Los_motivos_nuevos_de_rechazo_son_estables()
    {
        Assert.Equal("lugar_no_admite_cargue_sin_inspector", clsMotivosRechazo.LUGAR_NO_ADMITE_CARGUE_SIN_INSPECTOR);
        Assert.Equal("transporte_lo_arma_el_sistema", clsMotivosRechazo.TRANSPORTE_LO_ARMA_EL_SISTEMA);
        Assert.Equal("entrega_del_cargue_sin_cerrar", clsMotivosRechazo.ENTREGA_DEL_CARGUE_SIN_CERRAR);
    }

    [Fact]
    public void La_etapa_previa_sin_cierre_conserva_su_motivo_y_puede_decir_que_falta_cerrar_la_entrega_del_cargue()
    {
        var comun = new clsEtapaPreviaSinCierreException(Guid.NewGuid(), "Cargue", clsEtapaPreviaSinCierreException.MOTIVO_TRANSFERENCIA_ABIERTA);
        var delCargue = new clsEtapaPreviaSinCierreException(Guid.NewGuid(), "Cargue", clsEtapaPreviaSinCierreException.MOTIVO_TRANSFERENCIA_ABIERTA, "Falta cerrar la entrega del cargue");

        Assert.Contains("no tiene tipo de cierre", comun.Message);
        Assert.Equal("Falta cerrar la entrega del cargue", delCargue.Message);
        Assert.Equal("transferencia_sin_cierre", ((Dictionary<string, object>)delCargue.Detalles!)["motivo"]);
    }
}
