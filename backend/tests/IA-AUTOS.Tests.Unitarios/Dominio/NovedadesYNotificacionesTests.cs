using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>La novedad del conductor, las notificaciones, los dispositivos push, las alertas por rol y el estado de transporte de una unidad.</summary>
public class NovedadesYNotificacionesTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 21, 9, 15, 0, TimeSpan.Zero);

    private static clsNotificacion Notificacion(
        string tipo = clsTipoNotificacion.NOVEDAD_CONDUCTOR,
        string canal = clsCanalNotificacion.BANDEJA,
        Guid? usuario = null,
        DateTimeOffset? enviadaAt = null,
        string? error = null) =>
        clsNotificacion.Crear(
            tipo, clsUrgenciaNotificacion.ALTA, usuario ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Novedad en ruta", "Un golpe en la unidad", canal, enviadaAt, error, "novedad_conductor:x:y:bandeja", AHORA);

    // ------------------------------------------------------------------------------------ novedad

    [Theory]
    [InlineData("golpe")]
    [InlineData("piedra")]
    [InlineData("granizo")]
    [InlineData("incidente_vial")]
    public void La_novedad_del_conductor_no_tiene_zona_clase_nivel_ni_texto_libre(string tipo)
    {
        var inspeccion = Guid.NewGuid();

        var novedad = clsHallazgo.CrearNovedad(inspeccion, tipo, "5_10cm", AHORA);

        Assert.Equal(inspeccion, novedad.InspeccionId);
        Assert.Equal(tipo, novedad.TipoNovedad);
        Assert.Equal("5_10cm", novedad.TamanoDano);
        Assert.Equal(clsOrigenHallazgo.CONDUCTOR, novedad.Origen);
        Assert.Null(novedad.ZonaId);
        Assert.Null(novedad.ClaseDano);
        Assert.Null(novedad.NivelSeveridad);
        Assert.Null(novedad.Nota);
        Assert.Null(novedad.RevisadoAt);
        Assert.False(novedad.EsPreexistente);
        Assert.Equal(clsEstadoSeguimiento.PENDIENTE, novedad.EstadoSeguimiento);
    }

    [Fact]
    public void La_novedad_exige_un_tipo_de_la_lista_y_un_tamano()
    {
        Assert.Throws<clsValidacionException>(() => clsHallazgo.CrearNovedad(Guid.NewGuid(), "robo", "5_10cm", AHORA));
        Assert.Throws<clsValidacionException>(() => clsHallazgo.CrearNovedad(Guid.NewGuid(), "golpe", "  ", AHORA));
    }

    [Fact]
    public void Revisar_la_novedad_la_completa_con_zona_clase_nivel_y_quien_la_reviso()
    {
        var novedad = clsHallazgo.CrearNovedad(Guid.NewGuid(), "golpe", "1_5cm", AHORA);
        var zona = Guid.NewGuid();
        var inspector = Guid.NewGuid();

        novedad.Revisar(zona, "rotura", "v1", "  5_10cm  ", "  Sobre el frente  ", inspector, AHORA.AddHours(1));

        Assert.Equal(zona, novedad.ZonaId);
        Assert.Equal("rotura", novedad.ClaseDano);
        Assert.Equal("v1", novedad.NivelSeveridad);
        Assert.Equal("5_10cm", novedad.TamanoDano);
        Assert.Equal("Sobre el frente", novedad.Nota);
        Assert.Equal(inspector, novedad.RevisadoPor);
        Assert.Equal(AHORA.AddHours(1), novedad.RevisadoAt);
        Assert.Equal("golpe", novedad.TipoNovedad);
    }

    [Fact]
    public void Revisar_sin_corregir_el_tamano_conserva_el_del_conductor()
    {
        var novedad = clsHallazgo.CrearNovedad(Guid.NewGuid(), "piedra", "mas_20cm", AHORA);

        novedad.Revisar(Guid.NewGuid(), "golpe", "v3", "  ", null, Guid.NewGuid(), AHORA);

        Assert.Equal("mas_20cm", novedad.TamanoDano);
        Assert.Null(novedad.Nota);
    }

    [Fact]
    public void Una_novedad_se_revisa_una_sola_vez()
    {
        var novedad = clsHallazgo.CrearNovedad(Guid.NewGuid(), "granizo", "1_5cm", AHORA);
        novedad.Revisar(Guid.NewGuid(), "golpe", "v2", null, null, Guid.NewGuid(), AHORA);

        var ex = Assert.Throws<clsHallazgoYaRevisadoException>(() => novedad.Revisar(Guid.NewGuid(), "rotura", "v1", null, null, Guid.NewGuid(), AHORA));

        Assert.Equal(clsCodigosError.HALLAZGO_YA_REVISADO, ex.Codigo);
        Assert.Equal("v2", novedad.NivelSeveridad);
    }

    [Fact]
    public void Solo_se_revisa_un_hallazgo_del_conductor()
    {
        var delInspector = clsHallazgo.Crear(Guid.NewGuid(), Guid.NewGuid(), "golpe", "1_5cm", "v2", clsOrigenHallazgo.INSPECTOR, null, null, null, AHORA);

        var ex = Assert.Throws<clsConflictoException>(() => delInspector.Revisar(Guid.NewGuid(), "golpe", "v1", null, null, Guid.NewGuid(), AHORA));

        Assert.Equal(clsCodigosError.CONFLICT, ex.Codigo);
        Assert.Null(delInspector.RevisadoAt);
    }

    [Fact]
    public void La_nota_de_la_revision_admite_quinientos_caracteres()
    {
        var novedad = clsHallazgo.CrearNovedad(Guid.NewGuid(), "golpe", "1_5cm", AHORA);

        Assert.Throws<clsValidacionException>(() => novedad.Revisar(Guid.NewGuid(), "golpe", "v2", null, new string('n', clsHallazgo.NOTA_MAX + 1), Guid.NewGuid(), AHORA));
        Assert.Null(novedad.RevisadoAt);
    }

    // ------------------------------------------------------------------------------ notificaciones

    [Fact]
    public void Una_notificacion_nace_sin_leer_ni_respuesta_y_recorta_lo_que_excede_sus_limites()
    {
        var n = clsNotificacion.Crear(
            clsTipoNotificacion.NOVEDAD_CONDUCTOR, clsUrgenciaNotificacion.ALTA, Guid.NewGuid(), null, null, null, null, null,
            new string('t', 300), new string('m', 1500), clsCanalNotificacion.BANDEJA, AHORA, null, "clave", AHORA);

        Assert.False(n.EstaLeida);
        Assert.False(n.EstaRespondida);
        Assert.Equal(clsNotificacion.TITULO_MAX, n.Titulo.Length);
        Assert.Equal(clsNotificacion.MENSAJE_MAX, n.Mensaje.Length);
        Assert.Equal(AHORA, n.CreatedAt);
    }

    [Fact]
    public void Solo_el_reporte_diario_no_tiene_destinatario_y_solo_el_se_envia_por_correo()
    {
        Assert.Throws<clsValidacionException>(() => clsNotificacion.Crear(
            clsTipoNotificacion.UMBRAL_PATRON, clsUrgenciaNotificacion.MEDIA, null, null, null, null, null, null, "t", "m", clsCanalNotificacion.BANDEJA, AHORA, null, "c", AHORA));
        Assert.Throws<clsValidacionException>(() => Notificacion(clsTipoNotificacion.NOVEDAD_CONDUCTOR, clsCanalNotificacion.CORREO));

        var reporte = clsNotificacion.Crear(
            clsTipoNotificacion.REPORTE_DIARIO, clsUrgenciaNotificacion.MEDIA, null, null, null, null, null, null, "t", "m", clsCanalNotificacion.CORREO, AHORA, null, "c", AHORA);
        Assert.Null(reporte.UsuarioId);
    }

    [Fact]
    public void Una_notificacion_enviada_no_tiene_error_de_envio_y_una_fallida_lo_guarda()
    {
        Assert.Throws<clsValidacionException>(() => Notificacion(canal: clsCanalNotificacion.PUSH, enviadaAt: AHORA, error: "falló"));

        var fallida = Notificacion(canal: clsCanalNotificacion.PUSH, error: new string('e', 800));

        Assert.Null(fallida.EnviadaAt);
        Assert.Equal(clsNotificacion.ERROR_MAX, fallida.ErrorEnvio!.Length);
    }

    [Theory]
    [InlineData("inventado", "alta", "bandeja")]
    [InlineData("novedad_conductor", "baja", "bandeja")]
    [InlineData("novedad_conductor", "alta", "sms")]
    public void Una_notificacion_con_tipo_urgencia_o_canal_fuera_de_la_lista_no_se_crea(string tipo, string urgencia, string canal)
    {
        Assert.Throws<clsValidacionException>(() => clsNotificacion.Crear(
            tipo, urgencia, Guid.NewGuid(), null, null, null, null, null, "t", "m", canal, AHORA, null, "c", AHORA));
    }

    [Fact]
    public void Leer_la_notificacion_otra_vez_no_cambia_la_primera_lectura()
    {
        var n = Notificacion();

        n.MarcarLeida(AHORA);
        n.MarcarLeida(AHORA.AddHours(2));

        Assert.True(n.EstaLeida);
        Assert.Equal(AHORA, n.LeidaAt);
    }

    [Theory]
    [InlineData("  En camino a revisarlo  ", "En camino a revisarlo")]
    [InlineData("Ok", "Ok")]
    public void El_texto_de_la_respuesta_se_limpia(string texto, string esperado) => Assert.Equal(esperado, clsNotificacion.TextoDeRespuesta(texto));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void La_respuesta_exige_texto(string? texto) => Assert.Throws<clsValidacionException>(() => clsNotificacion.TextoDeRespuesta(texto));

    [Fact]
    public void La_respuesta_admite_hasta_mil_caracteres()
    {
        Assert.Equal(clsNotificacion.RESPUESTA_MAX, clsNotificacion.TextoDeRespuesta(new string('r', clsNotificacion.RESPUESTA_MAX)).Length);
        Assert.Throws<clsValidacionException>(() => clsNotificacion.TextoDeRespuesta(new string('r', clsNotificacion.RESPUESTA_MAX + 1)));
    }

    // ------------------------------------------------------------------------------ dispositivos

    [Fact]
    public void Un_dispositivo_se_registra_activo_con_su_token_plataforma_y_modelo()
    {
        var usuario = Guid.NewGuid();

        var d = clsDispositivo.Registrar(usuario, "  token-1  ", "android", "  Pixel 8  ", AHORA);

        Assert.Equal(usuario, d.UsuarioId);
        Assert.Equal("token-1", d.TokenFcm);
        Assert.Equal("android", d.Plataforma);
        Assert.Equal("Pixel 8", d.Modelo);
        Assert.True(d.Activo);
        Assert.Equal(AHORA, d.UltimoUsoAt);
    }

    [Theory]
    [InlineData(null, "android")]
    [InlineData("  ", "android")]
    [InlineData("token", "symbian")]
    [InlineData("token", null)]
    public void Un_dispositivo_exige_token_y_plataforma_validos(string? token, string? plataforma) =>
        Assert.Throws<clsValidacionException>(() => clsDispositivo.Registrar(Guid.NewGuid(), token, plataforma, null, AHORA));

    [Fact]
    public void El_mismo_token_pasa_al_usuario_que_lo_registra_y_queda_activo()
    {
        var d = clsDispositivo.Registrar(Guid.NewGuid(), "token-1", "android", "Pixel 8", AHORA);
        d.Desactivar(AHORA.AddHours(1));
        var nuevo = Guid.NewGuid();

        d.Actualizar(nuevo, "ios", "iPhone 15", AHORA.AddHours(2));

        Assert.Equal(nuevo, d.UsuarioId);
        Assert.Equal("ios", d.Plataforma);
        Assert.Equal("iPhone 15", d.Modelo);
        Assert.True(d.Activo);
        Assert.Equal(AHORA.AddHours(2), d.UltimoUsoAt);
    }

    [Fact]
    public void Desactivar_un_dispositivo_ya_inactivo_no_cambia_su_fecha()
    {
        var d = clsDispositivo.Registrar(Guid.NewGuid(), "token-1", "web", null, AHORA);

        d.Desactivar(AHORA.AddHours(1));
        d.Desactivar(AHORA.AddHours(5));

        Assert.False(d.Activo);
        Assert.Equal(AHORA.AddHours(1), d.UpdatedAt);
    }

    // ---------------------------------------------------------------------------------- alertas

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD, true)]
    [InlineData(clsRol.ADMIN, true)]
    [InlineData(clsRol.SUPER_ADMIN, true)]
    [InlineData(clsRol.INSPECTOR, true)]
    [InlineData(clsRol.CONDUCTOR, true)]
    [InlineData(clsRol.COMERCIAL, false)]
    public void La_capacidad_ver_alertas_es_de_los_roles_que_reciben_avisos(string rol, bool tiene) =>
        Assert.Equal(tiene, clsCapacidades.De(rol).Contains(clsCapacidades.VER_ALERTAS));

    // ------------------------------------------------------------------------ estado de transporte

    [Theory]
    [InlineData(false, 0, clsEstadoTransporte.SIN_REGISTRO)]
    [InlineData(false, 2, clsEstadoTransporte.CON_NOVEDAD)]
    [InlineData(true, 0, clsEstadoTransporte.SIN_NOVEDAD)]
    [InlineData(true, 1, clsEstadoTransporte.LLEGADA_CON_NOVEDAD)]
    public void El_estado_de_transporte_distingue_lo_registrado_de_la_llegada(bool llegada, int novedades, string esperado) =>
        Assert.Equal(esperado, clsEstadoTransporte.De(llegada, novedades));

    // ------------------------------------------------------------------------------ errores 409

    [Fact]
    public void Los_conflictos_de_la_llegada_y_de_la_unidad_que_no_sale_llevan_su_codigo()
    {
        Assert.Equal(clsCodigosError.LLEGADA_YA_CERRADA, new clsLlegadaYaCerradaException("VIN1").Codigo);
        Assert.Equal(clsCodigosError.UNIDAD_NO_SALE, new clsUnidadNoSaleException("VIN1").Codigo);
    }
}
