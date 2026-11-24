using IAAutos.Aplicacion.Parametros;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Parametros;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>El plazo de cada permiso guardado al nacer, las visitas de la autocalibracion, la precision guardada en el lugar y las esperas del destino, todo con el valor que TI ajusta.</summary>
public class ParametrosDeAvisosYTiemposTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 10, 8, 0, 0, TimeSpan.FromHours(-5));

    private sealed class ParametrosFalsos(Dictionary<string, string> valores) : IParametros
    {
        public Task<string?> ValorAsync(string clave, CancellationToken ct = default) => Task.FromResult(valores.GetValueOrDefault(clave));

        public async Task<int> EnteroAsync(string clave, int porDefecto, CancellationToken ct = default) =>
            int.TryParse(await ValorAsync(clave, ct), out var valor) ? valor : porDefecto;

        public Task<IReadOnlyList<string>> ListaAsync(string clave, IReadOnlyList<string> porDefecto, CancellationToken ct = default) => Task.FromResult(porDefecto);

        public void Invalidar()
        {
        }
    }

    private static clsPermisoSinInspector Solicitar(int espera) =>
        clsPermisoSinInspector.Solicitar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "inspector_no_llego", null, AHORA, 10.4, -75.5, AHORA, espera);

    // ------------------------------------------------------------------------------------------------ permiso

    [Fact]
    public void La_solicitud_guarda_al_nacer_el_plazo_con_la_espera_vigente()
    {
        var permiso = Solicitar(15);

        Assert.Equal(AHORA.AddMinutes(15), permiso.VenceAt);
        Assert.Equal(15, permiso.EsperaMinutos);
        Assert.False(permiso.EstaVencido(AHORA.AddMinutes(14).AddSeconds(59)));
        Assert.True(permiso.EstaVencido(AHORA.AddMinutes(15)));
    }

    [Fact]
    public void Sin_indicar_la_espera_el_plazo_es_de_10_minutos()
    {
        var permiso = clsPermisoSinInspector.Solicitar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "inspector_no_llego", null, AHORA, null, null, AHORA);

        Assert.Equal(AHORA.AddMinutes(clsPermisoSinInspector.MINUTOS_POR_DEFECTO), permiso.VenceAt);
        Assert.Equal(10, permiso.EsperaMinutos);
    }

    [Fact]
    public void Una_espera_de_cero_o_negativa_se_lleva_a_un_minuto()
    {
        Assert.Equal(AHORA.AddMinutes(1), Solicitar(0).VenceAt);
        Assert.Equal(AHORA.AddMinutes(1), Solicitar(-5).VenceAt);
    }

    [Fact]
    public void El_reloj_no_concede_antes_del_plazo_guardado_y_si_en_el()
    {
        var permiso = Solicitar(15);

        // A los 10 minutos todavia no: el plazo de esta solicitud es 15.
        Assert.Throws<clsConflictoException>(() => permiso.ConcederPorTiempo(AHORA.AddMinutes(10)));
        Assert.False(permiso.EstaConcedido);
        permiso.ConcederPorTiempo(AHORA.AddMinutes(15));

        Assert.True(permiso.EstaConcedido);
        Assert.True(permiso.PorTiempo);
        Assert.False(permiso.EstaVencido(AHORA.AddMinutes(30)));
    }

    // ------------------------------------------------------------------------------------ autocalibracion

    [Fact]
    public void La_autocalibracion_propone_con_las_visitas_que_pide_el_parametro()
    {
        var dosVisitas = new List<(double Lat, double Lng, Guid Visita)> { (6.2, -75.5, Guid.NewGuid()), (6.2, -75.5, Guid.NewGuid()) };
        var unaVisita = new List<(double Lat, double Lng, Guid Visita)> { (6.2, -75.5, Guid.NewGuid()), (6.2, -75.5, Guid.NewGuid()) };
        var mismaVisita = Guid.NewGuid();
        unaVisita = [.. unaVisita.Select(f => (f.Lat, f.Lng, mismaVisita))];

        Assert.NotNull(clsAutocalibracion.Proponer(dosVisitas, 2));
        Assert.Null(clsAutocalibracion.Proponer(unaVisita, 2));
        // Con el valor por defecto (3) dos visitas no alcanzan; un parametro en 0 se lleva a 1.
        Assert.Null(clsAutocalibracion.Proponer(dosVisitas));
        Assert.NotNull(clsAutocalibracion.Proponer(unaVisita, 0));
    }

    // ------------------------------------------------------------------------------------------- lugar

    private static clsLugar Lugar() => clsLugar.Crear("Patio de Prueba", Guid.NewGuid(), "Medellín", null, null, AHORA);

    [Fact]
    public void Fijar_la_ubicacion_aqui_guarda_la_precision_del_gps_en_el_lugar()
    {
        var lugar = Lugar();

        lugar.FijarUbicacionAqui(6.2, -75.5, Guid.NewGuid(), AHORA, 12.5);

        Assert.Equal(12.5, lugar.PrecisionM);
        Assert.Equal(clsEstadoUbicacion.PENDIENTE_CONFIRMAR, lugar.EstadoUbicacion);
    }

    [Fact]
    public void Sin_precision_el_lugar_queda_sin_precision_y_una_precision_negativa_se_rechaza()
    {
        var lugar = Lugar();

        Assert.Throws<clsValidacionException>(() => lugar.FijarUbicacionAqui(6.2, -75.5, Guid.NewGuid(), AHORA, -1));
        Assert.Throws<clsValidacionException>(() => lugar.FijarUbicacionAqui(6.2, -75.5, Guid.NewGuid(), AHORA, double.NaN));
        Assert.Equal(clsEstadoUbicacion.SIN_UBICACION, lugar.EstadoUbicacion);
        lugar.FijarUbicacionAqui(6.2, -75.5, Guid.NewGuid(), AHORA);
        Assert.Null(lugar.PrecisionM);
    }

    [Fact]
    public void Confirmar_con_las_mismas_coordenadas_conserva_la_precision_y_con_otras_la_borra()
    {
        var usuario = Guid.NewGuid();
        var lugar = Lugar();
        lugar.FijarUbicacionAqui(6.2, -75.5, usuario, AHORA, 12);

        lugar.FijarUbicacionConfirmada(6.2, -75.5, 300, usuario, AHORA.AddMinutes(5));
        Assert.Equal(12, lugar.PrecisionM);
        Assert.Equal(clsEstadoUbicacion.CONFIRMADA, lugar.EstadoUbicacion);

        lugar.FijarUbicacionConfirmada(6.3, -75.5, 300, usuario, AHORA.AddMinutes(10));
        Assert.Null(lugar.PrecisionM);
    }

    // --------------------------------------------------------------------------------- espera del destino

    [Fact]
    public async Task El_plan_B_se_habilita_con_las_esperas_por_defecto_a_los_45_minutos()
    {
        var espera = new clsEsperaDelDestino(new ParametrosFalsos([]));

        var plazos = await espera.PlazosAsync(AHORA);

        Assert.Equal(AHORA.AddMinutes(15), plazos.AvisaAt);
        Assert.Equal(AHORA.AddMinutes(45), plazos.HabilitaPlanBAt);
        Assert.False(await espera.PlanBHabilitadoAsync(AHORA, AHORA.AddMinutes(44).AddSeconds(59)));
        Assert.True(await espera.PlanBHabilitadoAsync(AHORA, AHORA.AddMinutes(45)));
    }

    [Fact]
    public async Task Las_esperas_del_destino_siguen_los_parametros_vigentes_y_nunca_bajan_de_un_minuto()
    {
        var espera = new clsEsperaDelDestino(new ParametrosFalsos(new()
        {
            [clsClavesParametros.ESPERA_INSPECTOR_AVISO_MINUTOS] = "5",
            [clsClavesParametros.ESPERA_INSPECTOR_PLAN_B_MINUTOS] = "10",
        }));

        Assert.Equal(AHORA.AddMinutes(15), (await espera.PlazosAsync(AHORA)).HabilitaPlanBAt);
        Assert.Equal(5, await espera.AvisoMinutosAsync());

        var enCero = new clsEsperaDelDestino(new ParametrosFalsos(new()
        {
            [clsClavesParametros.ESPERA_INSPECTOR_AVISO_MINUTOS] = "0",
            [clsClavesParametros.ESPERA_INSPECTOR_PLAN_B_MINUTOS] = "-3",
        }));

        Assert.Equal(AHORA.AddMinutes(2), (await enCero.PlazosAsync(AHORA)).HabilitaPlanBAt);
    }

    [Fact]
    public void Los_nombres_de_las_seis_claves_de_los_tiempos_y_los_avisos_son_los_de_la_base()
    {
        Assert.Equal("permiso_sin_inspector_minutos", clsClavesParametros.PERMISO_SIN_INSPECTOR_MINUTOS);
        Assert.Equal("espera_inspector_aviso_minutos", clsClavesParametros.ESPERA_INSPECTOR_AVISO_MINUTOS);
        Assert.Equal("espera_inspector_plan_b_minutos", clsClavesParametros.ESPERA_INSPECTOR_PLAN_B_MINUTOS);
        Assert.Equal("viaje_sin_movimiento_horas", clsClavesParametros.VIAJE_SIN_MOVIMIENTO_HORAS);
        Assert.Equal("firma_cargue_pendiente_minutos", clsClavesParametros.FIRMA_CARGUE_PENDIENTE_MINUTOS);
        Assert.Equal("propuesta_ubicacion_visitas", clsClavesParametros.PROPUESTA_UBICACION_VISITAS);
    }
}
