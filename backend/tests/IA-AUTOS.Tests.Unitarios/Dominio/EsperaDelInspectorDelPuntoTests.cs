using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>La espera del inspector del punto: nace con la hora del celular (nunca posterior a la del servidor), una sola vez termina y la marca del aviso no cambia su estado.</summary>
public class EsperaDelInspectorDelPuntoTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 18, 8, 20, 0, TimeSpan.FromHours(-5));

    private static clsEsperaInspectorPunto Activar(DateTimeOffset? activadaAt = null, string? texto = null, double? lat = 10.39, double? precision = 12) =>
        clsEsperaInspectorPunto.Activar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "inspector_no_llego", texto, activadaAt ?? AHORA.AddMinutes(-20), lat, lat is null ? null : -75.48, precision, AHORA);

    [Fact]
    public void La_espera_nace_esperando_con_la_hora_del_celular_y_la_hora_en_que_llego_al_servidor()
    {
        var espera = Activar();

        Assert.Equal(clsEstadoEspera.ESPERANDO, espera.Estado);
        Assert.True(espera.EstaEsperando);
        Assert.Equal(AHORA.AddMinutes(-20), espera.ActivadaAt);
        Assert.Equal(AHORA, espera.CreatedAt);
        Assert.Null(espera.AvisoAt);
        Assert.Null(espera.InspectorId);
        Assert.Null(espera.CerradaAt);
    }

    [Fact]
    public void La_hora_del_celular_posterior_a_la_del_servidor_se_guarda_como_la_del_servidor()
    {
        var espera = Activar(AHORA.AddMinutes(10));

        Assert.Equal(AHORA, espera.ActivadaAt);
    }

    [Fact]
    public void Sin_gps_no_se_guarda_la_precision_y_el_texto_admite_hasta_300_caracteres()
    {
        Assert.Null(Activar(lat: null, precision: 12).PrecisionM);
        Assert.Equal(12, Activar().PrecisionM);
        Assert.NotNull(Activar(texto: new string('a', 300)));

        var ex = Assert.Throws<clsValidacionException>(() => Activar(texto: new string('a', 301)));
        var detalles = Assert.IsType<Dictionary<string, object>>(ex.Detalles);
        Assert.True(Assert.IsType<Dictionary<string, string[]>>(detalles["fieldErrors"]).ContainsKey("motivo_sin_inspector_texto"));
    }

    [Fact]
    public void Un_inspector_que_toma_la_entrega_termina_la_espera_con_su_hora_y_despues_ya_no_cambia()
    {
        var espera = Activar();
        var inspector = Guid.NewGuid();

        espera.RegistrarLlegadaDelInspector(inspector, AHORA.AddMinutes(1));

        Assert.Equal(clsEstadoEspera.INSPECTOR_LLEGO, espera.Estado);
        Assert.Equal(inspector, espera.InspectorId);
        Assert.Equal(AHORA.AddMinutes(1), espera.InspectorLlegoAt);
        Assert.False(espera.EstaEsperando);
        Assert.Throws<clsConflictoException>(() => espera.CerrarSinInspectorDelPunto(Guid.NewGuid(), AHORA.AddMinutes(2)));
        Assert.Throws<clsConflictoException>(() => espera.Anular(AHORA.AddMinutes(2)));
        Assert.Throws<clsConflictoException>(() => espera.RegistrarLlegadaDelInspector(Guid.NewGuid(), AHORA.AddMinutes(2)));
    }

    [Fact]
    public void El_cierre_sin_inspector_del_punto_guarda_quien_y_cuando_y_la_espera_ya_no_admite_al_inspector()
    {
        var espera = Activar();
        var conductor = Guid.NewGuid();

        espera.CerrarSinInspectorDelPunto(conductor, AHORA.AddMinutes(30));

        Assert.Equal(clsEstadoEspera.CERRADA_SIN_INSPECTOR_DEL_PUNTO, espera.Estado);
        Assert.Equal(AHORA.AddMinutes(30), espera.CerradaAt);
        Assert.Equal(conductor, espera.CerradaPor);
        Assert.Throws<clsConflictoException>(() => espera.RegistrarLlegadaDelInspector(Guid.NewGuid(), AHORA.AddMinutes(31)));
    }

    [Fact]
    public void Un_viaje_cancelado_anula_la_espera_abierta_y_solo_una_vez()
    {
        var espera = Activar();

        espera.Anular(AHORA.AddMinutes(5));

        Assert.Equal(clsEstadoEspera.ANULADA, espera.Estado);
        Assert.Throws<clsConflictoException>(() => espera.Anular(AHORA.AddMinutes(6)));
    }

    [Fact]
    public void La_marca_del_aviso_se_guarda_una_vez_y_no_cambia_el_estado()
    {
        var espera = Activar();

        espera.MarcarAvisada(AHORA.AddMinutes(1));
        espera.MarcarAvisada(AHORA.AddMinutes(2));

        Assert.Equal(AHORA.AddMinutes(1), espera.AvisoAt);
        Assert.Equal(clsEstadoEspera.ESPERANDO, espera.Estado);
    }

    [Fact]
    public void Los_estados_y_el_tipo_de_aviso_nuevos_estan_en_sus_listas()
    {
        Assert.Equal(["anulada", "cerrada_sin_inspector_del_punto", "esperando", "inspector_llego"], clsEstadoEspera.VALORES.Order().ToList());
        Assert.Contains(clsTipoNotificacion.ESPERA_INSPECTOR_PUNTO, clsTipoNotificacion.VALORES);
        Assert.Equal("espera_inspector_punto", clsTipoNotificacion.ESPERA_INSPECTOR_PUNTO);
    }

    [Fact]
    public void La_transferencia_se_marca_como_entregada_sin_inspector_del_punto_solo_cuando_se_pide()
    {
        var transferencia = IAAutos.Dominio.Custodia.clsTransferenciaCustodia.CrearSinReceptor(Guid.NewGuid(), Guid.NewGuid(), AHORA);

        Assert.False(transferencia.SinInspectorDelPunto);
        transferencia.MarcarSinInspectorDelPunto();
        Assert.True(transferencia.SinInspectorDelPunto);
    }
}
