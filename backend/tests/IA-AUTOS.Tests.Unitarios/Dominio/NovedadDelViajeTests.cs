using FluentValidation;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Aplicacion.Viajes.Validators;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>La novedad del viaje: el tipo del catalogo con su rol y su urgencia, la novedad con su texto, su GPS opcional y su foto, el validador de la peticion y el identificador de envio de la novedad de un carro.</summary>
public class NovedadDelViajeTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 21, 19, 5, 0, TimeSpan.Zero);
    private static readonly Guid VIAJE = Guid.NewGuid();
    private static readonly Guid USUARIO = Guid.NewGuid();

    private static clsNovedadViaje Crear(
        string? texto = "Se reventó una llanta", DateTimeOffset? fecha = null, double? lat = 4.7, double? lng = -74.2, string rol = "conductor", string? proceso = null, Guid? lugar = null,
        Guid? idEnvio = null, clsFotoNovedadViaje? foto = null) =>
        clsNovedadViaje.Crear(VIAJE, "falla_camion", USUARIO, rol, proceso, lugar, texto, fecha ?? AHORA, lat, lng, idEnvio, foto, AHORA);

    /// <summary>El primer mensaje de campo con el que se rechaza (el que lee quien consume la API en <c>details.fieldErrors</c>).</summary>
    private static string Mensaje(Action accion)
    {
        var detalles = (IDictionary<string, object>)Assert.Throws<clsValidacionException>(accion).Detalles!;
        return ((IDictionary<string, string[]>)detalles["fieldErrors"]).Values.First()[0];
    }

    // ------------------------------------------------------------------------------------------ el tipo

    [Fact]
    public void El_tipo_nace_para_todos_con_urgencia_media_y_activo_y_guarda_lo_que_se_le_da()
    {
        var porDefecto = clsTipoNovedadViaje.Crear("otro", "Otro", null, null, null, null, AHORA);
        var falla = clsTipoNovedadViaje.Crear("falla_camion", "Falla del camión", "conductor", "alta", 1, true, AHORA);

        Assert.Equal("todos", porDefecto.Rol);
        Assert.Equal("media", porDefecto.Urgencia);
        Assert.True(porDefecto.Activo);
        Assert.Equal("conductor", falla.Rol);
        Assert.Equal("alta", falla.Urgencia);
        Assert.Equal(1, falla.Orden);
    }

    [Fact]
    public void El_rol_y_la_urgencia_del_tipo_se_validan_y_se_pueden_cambiar_sin_perder_lo_que_no_se_envia()
    {
        Assert.Throws<clsValidacionException>(() => clsTipoNovedadViaje.Crear("x", "X", "administrador", null, null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => clsTipoNovedadViaje.Crear("x", "X", null, "critica", null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => clsTipoNovedadViaje.Crear("Con Mayusculas", "X", null, null, null, null, AHORA));
        var tipo = clsTipoNovedadViaje.Crear("falla_camion", "Falla del camión", "conductor", "alta", null, null, AHORA);

        tipo.Cambiar(null, "media", AHORA);
        Assert.Equal("conductor", tipo.Rol);
        Assert.Equal("media", tipo.Urgencia);
        tipo.Cambiar("inspector", null, AHORA);
        Assert.Equal("inspector", tipo.Rol);
        Assert.Equal("media", tipo.Urgencia);
        Assert.Throws<clsValidacionException>(() => tipo.Cambiar("otro_rol", null, AHORA));
    }

    [Theory]
    [InlineData("conductor", "conductor", true)]
    [InlineData("conductor", "inspector", false)]
    [InlineData("inspector", "inspector", true)]
    [InlineData("inspector", "conductor", false)]
    [InlineData("todos", "conductor", true)]
    [InlineData("todos", "inspector", true)]
    public void Un_tipo_lo_eligen_el_rol_que_indica_o_los_dos_si_es_para_todos(string rolDelTipo, string quienReporta, bool admite) =>
        Assert.Equal(admite, clsRolDeNovedadViaje.Admite(rolDelTipo, quienReporta));

    [Fact]
    public void El_aviso_de_la_novedad_del_viaje_es_un_tipo_de_notificacion_valido()
    {
        Assert.Equal("novedad_viaje", clsTipoNotificacion.NOVEDAD_VIAJE);
        Assert.Contains(clsTipoNotificacion.NOVEDAD_VIAJE, clsTipoNotificacion.VALORES);
    }

    // ------------------------------------------------------------------------------------------ la novedad

    [Fact]
    public void La_novedad_guarda_quien_la_reporto_su_texto_la_fecha_del_celular_y_el_GPS_y_la_hora_del_servidor()
    {
        var envio = Guid.NewGuid();
        var novedad = Crear(texto: "  Paro en el peaje  ", fecha: AHORA.AddHours(-3), idEnvio: envio);

        Assert.NotEqual(Guid.Empty, novedad.Id);
        Assert.Equal(VIAJE, novedad.ViajeId);
        Assert.Equal(USUARIO, novedad.UsuarioId);
        Assert.Equal("conductor", novedad.Rol);
        Assert.Equal("falla_camion", novedad.TipoCodigo);
        Assert.Equal("Paro en el peaje", novedad.Texto);
        Assert.Equal(AHORA.AddHours(-3), novedad.Fecha);
        Assert.Equal(AHORA, novedad.CreatedAt);
        Assert.Equal(4.7, novedad.Lat);
        Assert.Equal(-74.2, novedad.Lng);
        Assert.False(novedad.SinUbicacion);
        Assert.False(novedad.ConFoto);
        Assert.Equal(envio, novedad.IdEnvio);
    }

    [Fact]
    public void Sin_GPS_la_novedad_se_acepta_y_queda_sin_ubicacion()
    {
        var novedad = Crear(lat: null, lng: null);

        Assert.True(novedad.SinUbicacion);
        Assert.Null(novedad.Lat);
        Assert.Null(novedad.Lng);
    }

    [Fact]
    public void El_texto_es_obligatorio_de_300_caracteres_como_maximo_y_la_fecha_es_obligatoria()
    {
        Assert.Equal("Escribe qué pasó", Mensaje(() => Crear(texto: "   ")));
        Assert.Equal("Escribe qué pasó", Mensaje(() => Crear(texto: null)));
        Assert.Equal("Escribe máximo 300 caracteres", Mensaje(() => Crear(texto: new string('x', 301))));
        Assert.Equal(300, Crear(texto: new string('x', 300)).Texto.Length);
        Assert.Throws<clsValidacionException>(() => clsNovedadViaje.Crear(VIAJE, "falla_camion", USUARIO, "conductor", null, null, "Algo", null, 4.7, -74.2, null, null, AHORA));
    }

    [Fact]
    public void El_GPS_va_completo_y_dentro_de_rango()
    {
        Assert.Throws<clsValidacionException>(() => Crear(lat: 4.7, lng: null));
        Assert.Throws<clsValidacionException>(() => Crear(lat: null, lng: -74.2));
        Assert.Throws<clsValidacionException>(() => Crear(lat: 95, lng: -74.2));
        Assert.Throws<clsValidacionException>(() => Crear(lat: 4.7, lng: -190));
    }

    [Fact]
    public void El_proceso_solo_puede_ser_cargue_o_entrega()
    {
        var parada = Guid.NewGuid();

        Assert.Equal("cargue", Crear(rol: "inspector", proceso: "cargue").Proceso);
        var entrega = Crear(rol: "inspector", proceso: "entrega", lugar: parada);
        Assert.Equal("entrega", entrega.Proceso);
        Assert.Equal(parada, entrega.LugarId);
        Assert.Throws<clsValidacionException>(() => Crear(rol: "inspector", proceso: "descargue"));
    }

    [Fact]
    public void La_foto_se_guarda_con_su_sello_y_una_foto_sin_GPS_queda_en_cero_cero_sin_ubicacion()
    {
        var foto = clsFotoNovedadViaje.Crear(" llanta.jpg ", AHORA, 4.7, -74.2, 5, " celular-1 ", new string('a', 64));
        var sinGps = clsFotoNovedadViaje.Crear("llanta.jpg", AHORA, null, null, null, null, new string('a', 64));

        var novedad = Crear(foto: foto);

        Assert.True(novedad.ConFoto);
        Assert.Equal("llanta.jpg", novedad.FotoStoragePath);
        Assert.Equal(AHORA, novedad.FotoSelloFecha);
        Assert.Equal(4.7, novedad.FotoSelloLat);
        Assert.Equal(-74.2, novedad.FotoSelloLng);
        Assert.Equal(5, novedad.FotoPrecision);
        Assert.Equal("celular-1", novedad.FotoDispositivoId);
        Assert.Equal(new string('a', 64), novedad.FotoHashSha256);
        Assert.False(foto.SinUbicacion);
        Assert.True(sinGps.SinUbicacion);
        Assert.Equal((0d, 0d), (sinGps.SelloLat, sinGps.SelloLng));
    }

    [Fact]
    public void La_foto_valida_su_sello_su_precision_y_su_dispositivo()
    {
        Assert.Throws<clsValidacionException>(() => clsFotoNovedadViaje.Crear("a.jpg", AHORA, 95, -74.2, null, null, new string('a', 64)));
        Assert.Throws<clsValidacionException>(() => clsFotoNovedadViaje.Crear("a.jpg", AHORA, 4.7, -190, null, null, new string('a', 64)));
        Assert.Throws<clsValidacionException>(() => clsFotoNovedadViaje.Crear("a.jpg", AHORA, 4.7, -74.2, -1, null, new string('a', 64)));
        Assert.Throws<clsValidacionException>(() => clsFotoNovedadViaje.Crear("a.jpg", AHORA, 4.7, -74.2, null, new string('d', 101), new string('a', 64)));
    }

    // ------------------------------------------------------------------------------------------ la peticion

    private static RegistrarNovedadViajeRequest Peticion(
        string? tipo = "falla_camion", string? texto = "Se reventó una llanta", DateTimeOffset? fecha = null, double? lat = 4.7, double? lng = -74.2, FotoEvidenciaRequest? evidencia = null,
        Guid? idEnvio = null, string? proceso = null, Guid? lugar = null) =>
        new(tipo, texto, fecha ?? AHORA, lat, lng, evidencia, idEnvio, proceso, lugar);

    private static IReadOnlyList<string> Errores(RegistrarNovedadViajeRequest peticion) =>
        new RegistrarNovedadViajeRequestValidator().Validate(peticion).Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void Una_peticion_completa_o_sin_GPS_ni_foto_es_valida()
    {
        Assert.Empty(Errores(Peticion()));
        Assert.Empty(Errores(Peticion(lat: null, lng: null)));
        Assert.Empty(Errores(Peticion(texto: new string('x', 300), idEnvio: Guid.NewGuid())));
        Assert.Empty(Errores(Peticion(proceso: "entrega", lugar: Guid.NewGuid())));
    }

    [Fact]
    public void La_peticion_pide_tipo_texto_de_hasta_300_caracteres_y_fecha()
    {
        Assert.Contains("Tipo de novedad desconocido", Errores(Peticion(tipo: " ")));
        Assert.Contains("Escribe qué pasó", Errores(Peticion(texto: "   ")));
        Assert.Contains("Escribe qué pasó", Errores(Peticion(texto: null)));
        Assert.Contains("Escribe máximo 300 caracteres", Errores(new RegistrarNovedadViajeRequest("falla_camion", new string('x', 301), AHORA, null, null, null, null, null, null)));
        Assert.NotEmpty(Errores(new RegistrarNovedadViajeRequest("falla_camion", "Algo", null, null, null, null, null, null, null)));
    }

    [Fact]
    public void La_peticion_exige_latitud_y_longitud_juntas_dentro_de_rango_y_uuid_validos()
    {
        Assert.Contains("Envíe la latitud y la longitud juntas, o ninguna si no hay GPS", Errores(Peticion(lat: 4.7, lng: null)));
        Assert.Contains("Envíe la latitud y la longitud juntas, o ninguna si no hay GPS", Errores(Peticion(lat: null, lng: -74.2)));
        Assert.Contains("lat fuera de rango (-90 a 90)", Errores(Peticion(lat: 95)));
        Assert.Contains("lng fuera de rango (-180 a 180)", Errores(Peticion(lng: -190)));
        Assert.Contains("Debe ser un UUID válido", Errores(Peticion(idEnvio: Guid.Empty)));
        Assert.Contains("Debe ser un UUID válido", Errores(Peticion(lugar: Guid.Empty)));
        Assert.Contains("El proceso debe ser cargue o entrega", Errores(Peticion(proceso: "descargue")));
    }

    [Fact]
    public void La_foto_de_la_peticion_se_valida_y_no_admite_texto_libre()
    {
        var foto = new FotoEvidenciaRequest("llanta.png", AHORA, 4.7, -74.2, 5, "celular-1", null, null);

        Assert.Empty(Errores(Peticion(evidencia: foto)));
        Assert.Empty(Errores(Peticion(evidencia: foto with { SelloLat = null, SelloLng = null })));
        Assert.Contains("La novedad no admite texto libre en la foto", Errores(Peticion(evidencia: foto with { Nota = "Una nota" })));
        Assert.NotEmpty(Errores(Peticion(evidencia: foto with { StoragePath = "../secreto.png" })));
        Assert.NotEmpty(Errores(Peticion(evidencia: foto with { SelloFecha = null })));
    }

    // ------------------------------------------------------------------------------------------ el envio de la novedad de un carro

    [Fact]
    public void La_novedad_de_un_carro_guarda_el_identificador_del_envio_y_sin_el_queda_nulo()
    {
        var envio = Guid.NewGuid();

        var conEnvio = clsHallazgo.CrearNovedad(Guid.NewGuid(), "piedra", "5_10cm", AHORA, envio);
        var sinEnvio = clsHallazgo.CrearNovedad(Guid.NewGuid(), "piedra", "5_10cm", AHORA);

        Assert.Equal(envio, conEnvio.IdEnvio);
        Assert.Null(sinEnvio.IdEnvio);
        Assert.Equal(clsOrigenHallazgo.CONDUCTOR, conEnvio.Origen);
    }
}
