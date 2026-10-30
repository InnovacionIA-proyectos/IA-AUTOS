using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Tests.Unitarios.Dominio;

public class InspeccionExtraTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 3, 6, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Guid INSPECCION = Guid.NewGuid();

    private static string CampoDelError(clsValidacionException ex) =>
        ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys.Single();

    [Fact]
    public void Un_extra_guarda_su_tipo_y_su_observacion_sin_espacios_de_mas()
    {
        var extra = clsInspeccionExtra.Crear(INSPECCION, "  platon ", null, "  rayón en la compuerta  ", AHORA);

        Assert.Equal("platon", extra.Tipo);
        Assert.Null(extra.Texto);
        Assert.Equal("rayón en la compuerta", extra.Observacion);
        Assert.Equal(INSPECCION, extra.InspeccionId);
        Assert.Equal(AHORA, extra.CreatedAt);
    }

    [Fact]
    public void El_tipo_otro_exige_su_texto_y_los_demas_tipos_no_lo_admiten()
    {
        var sinTexto = Assert.Throws<clsValidacionException>(() => clsInspeccionExtra.Crear(INSPECCION, "otro", "   ", null, AHORA));
        var conTexto = Assert.Throws<clsValidacionException>(() => clsInspeccionExtra.Crear(INSPECCION, "volco", "algo", null, AHORA));
        var otro = clsInspeccionExtra.Crear(INSPECCION, "otro", " portabicicletas ", null, AHORA);

        Assert.Equal("texto", CampoDelError(sinTexto));
        Assert.Equal("texto", CampoDelError(conTexto));
        Assert.Equal("portabicicletas", otro.Texto);
    }

    [Fact]
    public void Sin_tipo_el_extra_se_rechaza_y_los_limites_de_texto_y_observacion_se_respetan()
    {
        var sinTipo = Assert.Throws<clsValidacionException>(() => clsInspeccionExtra.Crear(INSPECCION, " ", null, null, AHORA));
        var textoLargo = Assert.Throws<clsValidacionException>(() => clsInspeccionExtra.Crear(INSPECCION, "otro", new string('x', 101), null, AHORA));
        var observacionLarga = Assert.Throws<clsValidacionException>(() => clsInspeccionExtra.Crear(INSPECCION, "volco", null, new string('x', 501), AHORA));
        var exacto = clsInspeccionExtra.Crear(INSPECCION, "otro", new string('x', 100), new string('y', 500), AHORA);

        Assert.Equal("tipo", CampoDelError(sinTipo));
        Assert.Equal("texto", CampoDelError(textoLargo));
        Assert.Equal("observacion", CampoDelError(observacionLarga));
        Assert.Equal(100, exacto.Texto!.Length);
        Assert.Equal(500, exacto.Observacion!.Length);
    }

    [Fact]
    public void Una_observacion_en_blanco_queda_sin_valor_y_actualizar_reemplaza_los_tres_campos()
    {
        var extra = clsInspeccionExtra.Crear(INSPECCION, "volco", null, "   ", AHORA);

        extra.Actualizar("otro", "escalera", "despues", AHORA.AddMinutes(3));

        Assert.Equal("otro", extra.Tipo);
        Assert.Equal("escalera", extra.Texto);
        Assert.Equal("despues", extra.Observacion);
        Assert.Equal(AHORA.AddMinutes(3), extra.UpdatedAt);
        Assert.Equal(AHORA, extra.CreatedAt);
        Assert.Throws<clsValidacionException>(() => extra.Actualizar("volco", "no se admite", null, AHORA));
        Assert.Equal("otro", extra.Tipo);
    }

    [Theory]
    [InlineData("platon", "Platón", null, "Platón")]
    [InlineData("otro", "Otro", "portabicicletas", "Otro: portabicicletas")]
    [InlineData("otro", "Otro", null, "Otro")]
    public void El_nombre_para_mostrar_es_el_del_tipo_o_otro_con_su_texto(string tipo, string nombreDelTipo, string? texto, string esperado) =>
        Assert.Equal(esperado, clsInspeccionExtra.NombreParaMostrar(tipo, nombreDelTipo, texto));

    [Fact]
    public void La_foto_de_un_extra_no_es_de_una_vista_ni_de_un_hallazgo_ni_de_la_acomodacion()
    {
        var extra = Guid.NewGuid();

        var foto = clsEvidencia.CrearDeExtra(INSPECCION, extra, "extra.jpg", AHORA, 4.6, -74.1, 4.5, "tablet-1", null, null, AHORA);

        Assert.Equal(extra, foto.ExtraId);
        Assert.Null(foto.VistaId);
        Assert.Null(foto.HallazgoId);
        Assert.False(foto.EsConstancia);
        Assert.False(foto.EsSello);
        Assert.Equal(4.6, foto.SelloLat);
    }

    [Fact]
    public void El_tipo_otro_del_catalogo_es_el_unico_que_admite_texto()
    {
        Assert.Equal("otro", clsTipoExtra.OTRO);
        var tipo = clsTipoExtra.Crear("barras_techo", "Barras de techo", 1, true, AHORA);
        Assert.Equal("barras_techo", tipo.Codigo);
        Assert.Equal("Barras de techo", tipo.Nombre);
    }
}
