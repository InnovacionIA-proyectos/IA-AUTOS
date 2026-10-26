using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Inspecciones.Validators;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>El motivo por el que no esta el inspector, la novedad de proceso que deja y su paso por la correccion de una inspeccion.</summary>
public class NovedadProcesoTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid VIAJE = Guid.NewGuid();
    private static readonly Guid VEHICULO = Guid.NewGuid();
    private static readonly Guid ETAPA = Guid.NewGuid();
    private static readonly Guid USUARIO = Guid.NewGuid();

    private static clsNovedadProceso Cargue(string? motivo, string? texto = null, double? lat = 4.6, double? lng = -74.1) =>
        clsNovedadProceso.Crear(clsTipoNovedadProceso.CARGUE_SIN_INSPECTOR, VIAJE, VEHICULO, ETAPA, Guid.NewGuid(), motivo, texto, USUARIO, AHORA, lat, lng, AHORA);

    private static clsInspeccion Salida(string? motivo = null, string? texto = null, string momento = clsMomento.SALIDA) =>
        clsInspeccion.Crear(ETAPA, VIAJE, USUARIO, momento, AHORA, 4.6, -74.1, null, null, AHORA, motivo, texto);

    // ------------------------------------------------------------------------------------------ catalogo

    [Fact]
    public void El_motivo_nace_activo_sin_exigir_texto_y_otro_lo_exige()
    {
        var lista = clsMotivoSinInspector.Crear("inspector_no_llego", "El inspector no llegó", null, null, null, AHORA);
        var otro = clsMotivoSinInspector.Crear("otro", "Otro", true, 3, null, AHORA);

        Assert.False(lista.RequiereTexto);
        Assert.True(lista.Activo);
        Assert.True(otro.RequiereTexto);
        Assert.Equal(3, otro.Orden);
    }

    [Fact]
    public void El_codigo_del_motivo_sigue_las_reglas_de_los_catalogos_y_requiere_texto_se_puede_cambiar()
    {
        Assert.Throws<clsValidacionException>(() => clsMotivoSinInspector.Crear("Con Mayusculas", "Nombre", null, null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => clsMotivoSinInspector.Crear("ok", " ", null, null, null, AHORA));
        var motivo = clsMotivoSinInspector.Crear("ok", "Nombre", false, null, null, AHORA);

        motivo.CambiarRequiereTexto(true, AHORA);
        motivo.CambiarRequiereTexto(null, AHORA);

        Assert.True(motivo.RequiereTexto);
    }

    // ------------------------------------------------------------------------------------------ novedad

    [Fact]
    public void El_cargue_sin_inspector_guarda_su_motivo_su_texto_y_el_momento_y_lugar()
    {
        var novedad = Cargue("otro", "  El inspector se enfermó  ");

        Assert.NotEqual(Guid.Empty, novedad.Id);
        Assert.Equal(clsTipoNovedadProceso.CARGUE_SIN_INSPECTOR, novedad.Tipo);
        Assert.Equal("otro", novedad.MotivoCodigo);
        Assert.Equal("El inspector se enfermó", novedad.MotivoTexto);
        Assert.Equal((AHORA, 4.6, -74.1), (novedad.Fecha, novedad.Lat, novedad.Lng));
    }

    [Fact]
    public void El_cargue_sin_inspector_exige_motivo_pero_el_recibo_sin_entrega_no()
    {
        var sinMotivo = Assert.Throws<clsValidacionException>(() => Cargue(null));
        var recibo = clsNovedadProceso.Crear(clsTipoNovedadProceso.RECIBO_SIN_ENTREGA, VIAJE, VEHICULO, ETAPA, Guid.NewGuid(), null, null, USUARIO, AHORA, null, null, AHORA);

        Assert.Contains("motivo_sin_inspector", ((Dictionary<string, string[]>)((Dictionary<string, object>)sinMotivo.Detalles!)["fieldErrors"]).Keys);
        Assert.Null(recibo.MotivoCodigo);
        Assert.Null(recibo.Lat);
    }

    [Fact]
    public void El_texto_exige_un_motivo_y_tiene_un_maximo_de_300_caracteres()
    {
        Assert.Throws<clsValidacionException>(() => clsNovedadProceso.Crear(clsTipoNovedadProceso.RECIBO_SIN_ENTREGA, VIAJE, VEHICULO, ETAPA, Guid.NewGuid(), null, "texto", USUARIO, AHORA, null, null, AHORA));
        Assert.NotNull(Cargue("otro", new string('x', 300)));
        Assert.Throws<clsValidacionException>(() => Cargue("otro", new string('x', 301)));
    }

    [Fact]
    public void El_tipo_y_la_ubicacion_se_validan()
    {
        Assert.Throws<clsValidacionException>(() => clsNovedadProceso.Crear("otra", VIAJE, VEHICULO, ETAPA, Guid.NewGuid(), "otro", null, USUARIO, AHORA, null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => Cargue("otro", null, lat: 91));
        Assert.Throws<clsValidacionException>(() => Cargue("otro", null, lng: -181));
    }

    // ------------------------------------------------------------------------------------------ inspeccion

    [Fact]
    public void Una_salida_guarda_su_motivo_y_su_texto_y_una_normal_no_tiene()
    {
        var conMotivo = Salida("otro", "  El inspector se enfermó ");
        var normal = Salida();

        Assert.Equal("otro", conMotivo.MotivoSinInspector);
        Assert.Equal("El inspector se enfermó", conMotivo.MotivoSinInspectorTexto);
        Assert.Null(normal.MotivoSinInspector);
        Assert.Null(normal.MotivoSinInspectorTexto);
    }

    [Fact]
    public void El_motivo_solo_es_de_una_salida_y_el_texto_exige_motivo_y_cabe_en_300()
    {
        Assert.Throws<clsValidacionException>(() => Salida("otro", momento: clsMomento.LLEGADA));
        Assert.Throws<clsValidacionException>(() => Salida(null, "texto"));
        Assert.NotNull(Salida("otro", new string('x', 300)));
        Assert.Throws<clsValidacionException>(() => Salida("otro", new string('x', 301)));
    }

    [Fact]
    public void La_correccion_de_una_salida_hecha_sin_inspector_conserva_el_motivo()
    {
        var original = Salida("otro", "El inspector se enfermó");
        original.Cerrar(new clsResumenCierre(0, 0, 0, 0, []), [], exigeUbicacionPropia: false, AHORA);

        var correccion = original.Corregir(USUARIO, AHORA, 4.6, -74.1, null, AHORA);

        Assert.Equal(2, correccion.Version);
        Assert.Equal("otro", correccion.MotivoSinInspector);
        Assert.Equal("El inspector se enfermó", correccion.MotivoSinInspectorTexto);
    }

    // ------------------------------------------------------------------------------------------ validacion del cuerpo

    private static readonly CrearInspeccionRequestValidator VALIDADOR = new();

    private static CrearInspeccionRequest Cuerpo(string? motivo = null, string? texto = null) =>
        new(ETAPA, "salida", AHORA, 4.6, -74.1, null, null, motivo, texto);

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("otro", null, true)]
    [InlineData("otro", "Un texto", true)]
    [InlineData("  ", null, false)]
    [InlineData("", null, false)]
    [InlineData("otro", "   ", false)]
    [InlineData("otro", "", false)]
    public void El_cuerpo_no_admite_un_motivo_ni_un_texto_en_blanco(string? motivo, string? texto, bool valido) =>
        Assert.Equal(valido, VALIDADOR.Validate(Cuerpo(motivo, texto)).IsValid);

    [Fact]
    public void El_texto_del_motivo_del_cuerpo_admite_hasta_300_caracteres_y_el_motivo_hasta_30()
    {
        Assert.True(VALIDADOR.Validate(Cuerpo("otro", new string('x', 300))).IsValid);
        Assert.False(VALIDADOR.Validate(Cuerpo("otro", new string('x', 301))).IsValid);
        Assert.False(VALIDADOR.Validate(Cuerpo(new string('a', 31))).IsValid);
    }
}
