using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>Descartar un informe en borrador y dejar atras el de otra persona: solo un borrador cambia de estado, un cerrado nunca, y lo descartado o superado ya no admite cambios.</summary>
public class DescartarInformeTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static clsInspeccion Borrador() => clsInspeccion.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), clsMomento.LLEGADA, AHORA, 4.6, -74.1, null, null, AHORA);

    private static clsInspeccion Cerrada() => clsInspeccion.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), clsMomento.LLEGADA, AHORA, 4.6, -74.1, clsEstadoInspeccion.CERRADA, null, AHORA);

    [Fact]
    public void Descartar_un_borrador_lo_pasa_a_descartada_sin_fecha_de_cierre_y_lo_deja_fuera_de_juego()
    {
        var borrador = Borrador();

        borrador.Descartar(AHORA.AddMinutes(5));

        Assert.Equal(clsEstadoInspeccion.DESCARTADA, borrador.Estado);
        Assert.Null(borrador.CerradaAt);
        Assert.True(borrador.EstaFueraDeJuego);
        Assert.False(borrador.EstaCerrada);
        Assert.Equal(AHORA.AddMinutes(5), borrador.UpdatedAt);
    }

    [Fact]
    public void Descartar_un_informe_cerrado_es_un_conflicto_con_su_codigo_y_no_lo_cambia()
    {
        var cerrada = Cerrada();

        var ex = Assert.Throws<clsInformeCerradoNoSeDescartaException>(() => cerrada.Descartar(AHORA));

        Assert.Equal(clsCodigosError.INFORME_CERRADO_NO_SE_DESCARTA, ex.Codigo);
        Assert.Equal(clsEstadoInspeccion.CERRADA, cerrada.Estado);
        Assert.NotNull(cerrada.CerradaAt);
    }

    [Fact]
    public void El_rechazo_por_firmas_lleva_otro_motivo_que_el_de_cerrado()
    {
        var cerrado = Assert.IsType<Dictionary<string, object>>(new clsInformeCerradoNoSeDescartaException().Detalles);
        var conFirmas = Assert.IsType<Dictionary<string, object>>(new clsInformeCerradoNoSeDescartaException(conFirmas: true).Detalles);

        Assert.Equal(clsMotivosRechazo.INFORME_CERRADO, cerrado["motivo"]);
        Assert.Equal(clsMotivosRechazo.INFORME_CON_FIRMAS, conFirmas["motivo"]);
    }

    [Fact]
    public void Un_descartado_no_se_descarta_otra_vez_ni_se_supera()
    {
        var borrador = Borrador();
        borrador.Descartar(AHORA);

        Assert.Throws<clsConflictoException>(() => borrador.Descartar(AHORA));
        Assert.Throws<clsConflictoException>(() => borrador.Superar(AHORA));
    }

    [Fact]
    public void Superar_un_borrador_lo_pasa_a_superada_y_un_cerrado_no_se_supera()
    {
        var borrador = Borrador();

        borrador.Superar(AHORA);

        Assert.Equal(clsEstadoInspeccion.SUPERADA, borrador.Estado);
        Assert.True(borrador.EstaFueraDeJuego);
        Assert.Throws<clsConflictoException>(() => Cerrada().Superar(AHORA));
    }

    [Fact]
    public void Un_descartado_o_superado_ya_no_admite_cambios_ni_cierre()
    {
        var descartado = Borrador();
        descartado.Descartar(AHORA);
        var superado = Borrador();
        superado.Superar(AHORA);

        foreach (var informe in new[] { descartado, superado })
        {
            var ex = Assert.Throws<clsConflictoException>(informe.AsegurarEditable);
            Assert.Contains("descartada", ex.Message);
            Assert.Throws<clsConflictoException>(() => informe.Cerrar(new clsResumenCierre(0, 0, 0, 0, []), [], false, AHORA));
        }
    }

    [Fact]
    public void Los_estados_nuevos_no_son_validos_para_crear_ni_para_filtrar_y_estan_fuera_de_juego()
    {
        Assert.DoesNotContain(clsEstadoInspeccion.DESCARTADA, clsEstadoInspeccion.VALORES);
        Assert.DoesNotContain(clsEstadoInspeccion.SUPERADA, clsEstadoInspeccion.VALORES);
        Assert.Contains(clsEstadoInspeccion.DESCARTADA, clsEstadoInspeccion.FUERA_DE_JUEGO);
        Assert.Contains(clsEstadoInspeccion.SUPERADA, clsEstadoInspeccion.FUERA_DE_JUEGO);
        Assert.False(Borrador().EstaFueraDeJuego);
        Assert.False(Cerrada().EstaFueraDeJuego);
    }

    [Fact]
    public void Quien_no_es_el_autor_recibe_el_rechazo_con_su_codigo_y_su_motivo()
    {
        var ex = new clsDescarteSoloDelAutorException();

        Assert.Equal(clsCodigosError.DESCARTE_SOLO_AUTOR_O_COORDINADOR, ex.Codigo);
        Assert.Equal(clsMotivosRechazo.SOLO_AUTOR_O_COORDINADOR, Assert.IsType<Dictionary<string, object>>(ex.Detalles)["motivo"]);
    }
}
