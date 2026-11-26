using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>Las reglas del dominio al calificar los danos: el nivel del conductor se trata igual en una zona y en un extra, el dano de un extra del conductor nace sin nivel y cualquier dano sin nivel se califica una sola vez.</summary>
public class CalificacionDeDanosTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 3, 6, 0, 0, TimeSpan.FromHours(-5));

    private static string MensajeDe(clsValidacionException ex) =>
        ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Single().Value.Single();

    [Fact]
    public void El_dano_de_un_extra_del_conductor_nace_sin_nivel_sin_zona_y_con_el_nombre_del_extra()
    {
        var extra = Guid.NewGuid();

        var h = clsHallazgo.CrearDeExtra(Guid.NewGuid(), extra, "Platón", "golpe", "5_10cm", null, AHORA, clsOrigenHallazgo.CONDUCTOR);

        Assert.Equal(clsOrigenHallazgo.CONDUCTOR, h.Origen);
        Assert.Null(h.NivelSeveridad);
        Assert.Null(h.ZonaId);
        Assert.Equal(extra, h.ExtraId);
        Assert.Equal("Platón", h.ParteVehiculo);
    }

    [Fact]
    public void El_nivel_del_conductor_recibe_el_mismo_error_en_una_zona_y_en_un_extra()
    {
        var enZona = Assert.ThrowsAny<clsValidacionException>(
            () => clsHallazgo.Crear(Guid.NewGuid(), Guid.NewGuid(), "golpe", "1_5cm", "v2", clsOrigenHallazgo.CONDUCTOR, null, null, null, AHORA));
        var enExtra = Assert.ThrowsAny<clsValidacionException>(
            () => clsHallazgo.CrearDeExtra(Guid.NewGuid(), Guid.NewGuid(), "Platón", "golpe", "5_10cm", "v2", AHORA, clsOrigenHallazgo.CONDUCTOR));

        Assert.Equal(clsHallazgo.MENSAJE_NIVEL_DEL_CONDUCTOR, MensajeDe(enZona));
        Assert.Equal(MensajeDe(enZona), MensajeDe(enExtra));
    }

    [Fact]
    public void El_inspector_sigue_dando_el_nivel_del_dano_de_un_extra()
    {
        Assert.ThrowsAny<clsValidacionException>(
            () => clsHallazgo.CrearDeExtra(Guid.NewGuid(), Guid.NewGuid(), "Platón", "golpe", "5_10cm", null, AHORA));
    }

    [Fact]
    public void Calificar_un_extra_conserva_su_extra_y_no_le_pone_zona()
    {
        var extra = Guid.NewGuid();
        var h = clsHallazgo.CrearDeExtra(Guid.NewGuid(), extra, "Platón", "golpe", "5_10cm", null, AHORA, clsOrigenHallazgo.CONDUCTOR);
        var inspector = Guid.NewGuid();

        h.Revisar(null, "golpe", "v2", null, null, inspector, AHORA.AddHours(1));

        Assert.Equal("v2", h.NivelSeveridad);
        Assert.Equal(extra, h.ExtraId);
        Assert.Null(h.ZonaId);
        Assert.Equal(inspector, h.RevisadoPor);
        Assert.Equal(clsOrigenHallazgo.CONDUCTOR, h.Origen);
    }

    [Fact]
    public void Un_extra_no_se_califica_con_una_zona()
    {
        var h = clsHallazgo.CrearDeExtra(Guid.NewGuid(), Guid.NewGuid(), "Platón", "golpe", "5_10cm", null, AHORA, clsOrigenHallazgo.CONDUCTOR);

        Assert.ThrowsAny<clsValidacionException>(() => h.Revisar(Guid.NewGuid(), "golpe", "v2", null, null, Guid.NewGuid(), AHORA));
        Assert.Null(h.NivelSeveridad);
    }

    [Fact]
    public void Un_dano_con_zona_conserva_la_suya_si_no_se_manda_otra()
    {
        var zona = Guid.NewGuid();
        var h = clsHallazgo.Crear(Guid.NewGuid(), zona, "golpe", "1_5cm", null, clsOrigenHallazgo.CONDUCTOR, null, null, null, AHORA);

        h.Revisar(null, "golpe", "v3", null, null, Guid.NewGuid(), AHORA);

        Assert.Equal(zona, h.ZonaId);
        Assert.Equal("v3", h.NivelSeveridad);
    }

    [Fact]
    public void Un_dano_sin_nivel_de_cualquier_origen_se_califica()
    {
        var delInspector = clsHallazgo.Crear(Guid.NewGuid(), Guid.NewGuid(), "golpe", "1_5cm", "v2", clsOrigenHallazgo.INSPECTOR, null, null, null, AHORA);

        var ex = Assert.Throws<clsConflictoException>(() => delInspector.Revisar(null, "golpe", "v1", null, null, Guid.NewGuid(), AHORA));

        Assert.Equal("El hallazgo ya tiene nivel", ex.Message);
        Assert.Null(delInspector.RevisadoAt);
    }

    [Fact]
    public void Un_dano_calificado_responde_ya_revisado_y_no_ya_tiene_nivel()
    {
        var h = clsHallazgo.CrearDeExtra(Guid.NewGuid(), Guid.NewGuid(), "Platón", "golpe", "5_10cm", null, AHORA, clsOrigenHallazgo.CONDUCTOR);
        h.Revisar(null, "golpe", "v2", null, null, Guid.NewGuid(), AHORA);

        var ex = Assert.Throws<clsHallazgoYaRevisadoException>(() => h.Revisar(null, "golpe", "v1", null, null, Guid.NewGuid(), AHORA));

        Assert.Equal(clsCodigosError.HALLAZGO_YA_REVISADO, ex.Codigo);
        Assert.Equal("v2", h.NivelSeveridad);
    }
}
