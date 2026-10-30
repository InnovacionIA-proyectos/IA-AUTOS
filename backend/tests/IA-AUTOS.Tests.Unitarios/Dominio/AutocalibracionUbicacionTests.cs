using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Tests.Unitarios.Dominio;

public class AutocalibracionUbicacionTests
{
    private const double LAT = 6.2;
    private const double LNG = -75.5;
    private static readonly DateTimeOffset AHORA = new(2026, 11, 3, 6, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Guid USUARIO = Guid.NewGuid();

    private static double AlNorte(double metros) => LAT + (metros / clsConfirmacionSitio.RADIO_TIERRA_M * 180 / Math.PI);

    private static IReadOnlyList<(double Lat, double Lng, Guid Visita)> Fotos(params double[] metrosAlNorte) =>
        [.. metrosAlNorte.Select(m => (AlNorte(m), LNG, Guid.NewGuid()))];

    private static clsLugar LugarSinUbicacion() => clsLugar.Crear("Patio Medellín", Guid.NewGuid(), "Medellín", null, null, AHORA);

    [Fact]
    public void La_mediana_de_una_lista_impar_es_el_del_medio_y_la_de_una_par_el_promedio_de_los_dos_del_centro()
    {
        Assert.Equal(3, clsAutocalibracion.Mediana([5, 1, 3]));
        Assert.Equal(2.5, clsAutocalibracion.Mediana([4, 1, 3, 2]));
    }

    [Fact]
    public void La_mediana_de_una_lista_vacia_no_existe()
    {
        Assert.Throws<ArgumentException>(() => clsAutocalibracion.Mediana([]));
    }

    [Fact]
    public void El_percentil_90_deja_por_fuera_al_10_por_ciento_mas_alto()
    {
        Assert.Equal(9, clsAutocalibracion.Percentil([1, 2, 3, 4, 5, 6, 7, 8, 9, 100], 0.9));
        Assert.Equal(7, clsAutocalibracion.Percentil([7], 0.9));
    }

    [Fact]
    public void Con_menos_de_tres_visitas_distintas_no_propone_aunque_haya_muchas_fotos()
    {
        var visitaA = Guid.NewGuid();
        var visitaB = Guid.NewGuid();
        var fotos = new List<(double Lat, double Lng, Guid Visita)>
        {
            (LAT, LNG, visitaA), (LAT, LNG, visitaA), (LAT, LNG, visitaA), (LAT, LNG, visitaB), (LAT, LNG, visitaB),
        };

        Assert.Null(clsAutocalibracion.Proponer(fotos));
        Assert.Null(clsAutocalibracion.Proponer([]));
    }

    [Fact]
    public void Con_tres_visitas_propone_el_centro_en_la_mediana_y_cuenta_fotos_y_visitas()
    {
        var propuesta = clsAutocalibracion.Proponer(Fotos(0, 20, 40));

        Assert.NotNull(propuesta);
        Assert.Equal(AlNorte(20), propuesta.CentroLat, 9);
        Assert.Equal(LNG, propuesta.CentroLng, 9);
        Assert.Equal(3, propuesta.Fotos);
        Assert.Equal(3, propuesta.Visitas);
    }

    [Fact]
    public void Una_foto_lejana_no_arrastra_el_centro_porque_se_usa_la_mediana()
    {
        var propuesta = clsAutocalibracion.Proponer(Fotos(0, 10, 20, 30, 40000));

        Assert.NotNull(propuesta);
        Assert.Equal(AlNorte(20), propuesta.CentroLat, 9);
    }

    [Fact]
    public void El_radio_nunca_baja_del_minimo_aunque_las_fotos_esten_juntas()
    {
        var propuesta = clsAutocalibracion.Proponer(Fotos(0, 1, 2));

        Assert.Equal(clsAutocalibracion.RADIO_MINIMO_M, propuesta!.RadioMetros);
    }

    [Fact]
    public void El_radio_cubre_al_90_por_ciento_de_las_fotos_redondeado_hacia_arriba_a_diez_metros()
    {
        // Diez fotos a 0, 50, 100... 450 m: el centro queda a 225 m y la foto del percentil 90 esta a 225 m de el.
        var propuesta = clsAutocalibracion.Proponer(Fotos(0, 50, 100, 150, 200, 250, 300, 350, 400, 450));

        Assert.Equal(230, propuesta!.RadioMetros);
    }

    [Fact]
    public void El_radio_nunca_pasa_del_maximo()
    {
        var propuesta = clsAutocalibracion.Proponer(Fotos(0, 20000, 40000, 60000, 80000));

        Assert.Equal(clsAutocalibracion.RADIO_MAXIMO_M, propuesta!.RadioMetros);
    }

    [Fact]
    public void Una_propuesta_nueva_esta_pendiente_y_no_se_puede_crear_con_datos_invalidos()
    {
        var lugarId = Guid.NewGuid();
        var propuesta = clsPropuestaUbicacion.Crear(lugarId, LAT, LNG, 150, 9, 3, AHORA);

        Assert.True(propuesta.EstaPendiente);
        Assert.Equal(lugarId, propuesta.LugarId);
        Assert.Null(propuesta.DecididaPor);
        Assert.Throws<clsValidacionException>(() => clsPropuestaUbicacion.Crear(lugarId, 91, LNG, 150, 9, 3, AHORA));
        Assert.Throws<clsValidacionException>(() => clsPropuestaUbicacion.Crear(lugarId, LAT, LNG, 0, 9, 3, AHORA));
    }

    [Fact]
    public void Confirmar_o_descartar_deja_la_decision_con_su_autor_y_su_fecha()
    {
        var confirmada = clsPropuestaUbicacion.Crear(Guid.NewGuid(), LAT, LNG, 150, 9, 3, AHORA);
        var descartada = clsPropuestaUbicacion.Crear(Guid.NewGuid(), LAT, LNG, 150, 9, 3, AHORA);

        confirmada.Confirmar(USUARIO, AHORA.AddHours(1));
        descartada.Descartar(USUARIO, AHORA.AddHours(2));

        Assert.Equal(clsEstadoPropuesta.CONFIRMADA, confirmada.Estado);
        Assert.Equal(USUARIO, confirmada.DecididaPor);
        Assert.Equal(AHORA.AddHours(1), confirmada.DecididaAt);
        Assert.Equal(clsEstadoPropuesta.DESCARTADA, descartada.Estado);
        Assert.False(descartada.EstaPendiente);
    }

    [Fact]
    public void Una_propuesta_decidida_no_se_decide_otra_vez()
    {
        var propuesta = clsPropuestaUbicacion.Crear(Guid.NewGuid(), LAT, LNG, 150, 9, 3, AHORA);
        propuesta.Confirmar(USUARIO, AHORA);

        Assert.Throws<clsConflictoException>(() => propuesta.Confirmar(USUARIO, AHORA));
        Assert.Throws<clsConflictoException>(() => propuesta.Descartar(USUARIO, AHORA));
    }

    [Fact]
    public void La_carga_manual_deja_la_ubicacion_confirmada_con_su_autor()
    {
        var lugar = LugarSinUbicacion();

        lugar.FijarUbicacionConfirmada(6.1706, -75.5874, 1000, USUARIO, AHORA);

        Assert.Equal(clsEstadoUbicacion.CONFIRMADA, lugar.EstadoUbicacion);
        Assert.Equal(6.1706, lugar.Latitud);
        Assert.Equal(-75.5874, lugar.Longitud);
        Assert.Equal(1000, lugar.RadioMetros);
        Assert.Equal(USUARIO, lugar.UbicacionFijadaPor);
        Assert.Equal(AHORA, lugar.UbicacionFijadaAt);
    }

    [Fact]
    public void Las_coordenadas_van_juntas_el_radio_es_positivo_y_necesita_coordenadas()
    {
        var lugar = LugarSinUbicacion();

        Assert.Throws<clsValidacionException>(() => lugar.FijarUbicacionConfirmada(6.1, null, null, USUARIO, AHORA));
        Assert.Throws<clsValidacionException>(() => lugar.FijarUbicacionConfirmada(null, -75.5, null, USUARIO, AHORA));
        Assert.Throws<clsValidacionException>(() => lugar.FijarUbicacionConfirmada(91, -75.5, 100, USUARIO, AHORA));
        Assert.Throws<clsValidacionException>(() => lugar.FijarUbicacionConfirmada(6.1, 181, 100, USUARIO, AHORA));
        Assert.Throws<clsValidacionException>(() => lugar.FijarUbicacionConfirmada(6.1, -75.5, 0, USUARIO, AHORA));
        Assert.Throws<clsValidacionException>(() => lugar.FijarUbicacionConfirmada(6.1, -75.5, clsLugar.RADIO_MAXIMO_M + 1, USUARIO, AHORA));
        Assert.Throws<clsValidacionException>(() => lugar.FijarUbicacionConfirmada(null, null, 100, USUARIO, AHORA));
        Assert.Equal(clsEstadoUbicacion.SIN_UBICACION, lugar.EstadoUbicacion);
    }

    [Fact]
    public void Quitar_las_coordenadas_y_el_radio_deja_el_lugar_sin_ubicacion()
    {
        var lugar = LugarSinUbicacion();
        lugar.FijarUbicacionConfirmada(6.1, -75.5, 100, USUARIO, AHORA);

        lugar.FijarUbicacionConfirmada(null, null, null, USUARIO, AHORA);

        Assert.Equal(clsEstadoUbicacion.SIN_UBICACION, lugar.EstadoUbicacion);
        Assert.Null(lugar.Latitud);
        Assert.Null(lugar.RadioMetros);
        Assert.Null(lugar.UbicacionFijadaPor);
    }

    [Fact]
    public void Fijar_aqui_deja_la_ubicacion_pendiente_de_confirmar_sin_radio()
    {
        var lugar = LugarSinUbicacion();

        lugar.FijarUbicacionAqui(6.2, -75.5, USUARIO, AHORA);

        Assert.Equal(clsEstadoUbicacion.PENDIENTE_CONFIRMAR, lugar.EstadoUbicacion);
        Assert.Equal(6.2, lugar.Latitud);
        Assert.Null(lugar.RadioMetros);
        Assert.Equal(USUARIO, lugar.UbicacionFijadaPor);
    }

    [Fact]
    public void Fijar_aqui_no_cambia_una_ubicacion_que_ya_existe_pendiente_o_confirmada()
    {
        var pendiente = LugarSinUbicacion();
        pendiente.FijarUbicacionAqui(6.2, -75.5, USUARIO, AHORA);
        var confirmada = LugarSinUbicacion();
        confirmada.FijarUbicacionConfirmada(6.2, -75.5, 100, USUARIO, AHORA);

        Assert.Throws<clsConflictoException>(() => pendiente.FijarUbicacionAqui(6.3, -75.4, USUARIO, AHORA));
        Assert.Throws<clsConflictoException>(() => confirmada.FijarUbicacionAqui(6.3, -75.4, USUARIO, AHORA));
        Assert.Equal(6.2, confirmada.Latitud);
    }

    [Fact]
    public void Fijar_aqui_rechaza_coordenadas_invalidas()
    {
        Assert.Throws<clsValidacionException>(() => LugarSinUbicacion().FijarUbicacionAqui(95, -75.5, USUARIO, AHORA));
        Assert.Throws<clsValidacionException>(() => LugarSinUbicacion().FijarUbicacionAqui(6.2, double.NaN, USUARIO, AHORA));
    }
}
