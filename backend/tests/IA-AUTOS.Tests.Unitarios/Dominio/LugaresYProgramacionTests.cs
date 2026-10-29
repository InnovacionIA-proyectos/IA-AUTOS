using IAAutos.Aplicacion.Viajes;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

public class LugaresYProgramacionTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static string CampoDelError(clsValidacionException ex) =>
        ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys.Single();

    private static clsViaje NuevoViaje() => clsViaje.Crear("MTK-001", Guid.NewGuid(), "ABC123", null, null, null, null, null, Guid.NewGuid(), AHORA);

    // ----------------------------------------------------------------------------------------------- el lugar

    [Fact]
    public void Un_lugar_nuevo_sale_activo_y_sin_ubicacion_con_su_nombre_y_ciudad_limpios()
    {
        var tipo = Guid.NewGuid();

        var lugar = clsLugar.Crear("  CEDI Envigado ", tipo, "  Envigado ", null, null, AHORA);

        Assert.Equal("CEDI Envigado", lugar.Nombre);
        Assert.Equal("Envigado", lugar.Ciudad);
        Assert.Equal(tipo, lugar.TipoId);
        Assert.True(lugar.Activo);
        Assert.True(lugar.SinUbicacion);
        Assert.Null(lugar.Latitud);
        Assert.Null(lugar.Longitud);
        Assert.Null(lugar.RadioMetros);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_lugar_sin_ciudad_se_rechaza(string? ciudad)
    {
        var ex = Assert.Throws<clsValidacionException>(() => clsLugar.Crear("CEDI Envigado", Guid.NewGuid(), ciudad, null, null, AHORA));

        Assert.Equal("ciudad", CampoDelError(ex));
    }

    [Fact]
    public void La_ciudad_admite_como_maximo_cien_caracteres()
    {
        Assert.Equal(100, clsLugar.Crear("CEDI Envigado", Guid.NewGuid(), new string('a', 100), null, null, AHORA).Ciudad.Length);

        var ex = Assert.Throws<clsValidacionException>(() => clsLugar.Crear("CEDI Envigado", Guid.NewGuid(), new string('a', 101), null, null, AHORA));

        Assert.Equal("ciudad", CampoDelError(ex));
    }

    [Fact]
    public void Cambiar_el_lugar_solo_toca_lo_enviado()
    {
        var tipo = Guid.NewGuid();
        var lugar = clsLugar.Crear("CEDI Envigado", tipo, "Envigado", null, null, AHORA);
        var nuevoTipo = Guid.NewGuid();

        lugar.CambiarLugar(null, "Medellín", AHORA.AddHours(1));
        Assert.Equal(tipo, lugar.TipoId);
        Assert.Equal("Medellín", lugar.Ciudad);

        lugar.CambiarLugar(nuevoTipo, null, AHORA.AddHours(2));
        Assert.Equal(nuevoTipo, lugar.TipoId);
        Assert.Equal("Medellín", lugar.Ciudad);
        Assert.Equal(AHORA.AddHours(2), lugar.UpdatedAt);

        var ex = Assert.Throws<clsValidacionException>(() => lugar.CambiarLugar(null, " ", AHORA));
        Assert.Equal("ciudad", CampoDelError(ex));
    }

    // ----------------------------------------------------------------------------------------------- la fecha programada

    [Theory]
    [InlineData("2026-11-03 06:00", "2026-11-03T06:00:00-05:00")]
    [InlineData("2026-11-03 06:00:30", "2026-11-03T06:00:30-05:00")]
    [InlineData("2026-11-03T06:00", "2026-11-03T06:00:00-05:00")]
    [InlineData("  2026-11-03 06:00  ", "2026-11-03T06:00:00-05:00")]
    [InlineData("2026/11/03 06:00", "2026-11-03T06:00:00-05:00")]
    [InlineData("2026-11-03T06:00:00-05:00", "2026-11-03T06:00:00-05:00")]
    [InlineData("2026-11-03T11:00:00Z", "2026-11-03T06:00:00-05:00")]
    [InlineData("2026-11-03 06:00 +02:00", "2026-11-02T23:00:00-05:00")]
    public void La_fecha_de_la_carga_sin_zona_es_hora_de_colombia_y_con_zona_se_respeta(string texto, string esperada)
    {
        var fecha = clsFechaProgramada.DeCarga(texto);

        Assert.NotNull(fecha);
        Assert.Equal(DateTimeOffset.Parse(esperada), fecha!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mañana")]
    [InlineData("03/11/2026 6am")]
    [InlineData("2026-13-45 06:00")]
    [InlineData("2026-11-03")]
    [InlineData("2026-11-03 25:00")]
    public void Un_texto_que_no_es_fecha_y_hora_no_se_lee(string? texto)
    {
        Assert.Null(clsFechaProgramada.DeCarga(texto));
    }

    [Theory]
    [InlineData("2026-11-03T06:00:00-05:00", true)]
    [InlineData("2026-11-03T11:00:00Z", true)]
    [InlineData("2026-11-03T11:00:00.123Z", true)]
    [InlineData("2026-11-03T06:00:00", false)]
    [InlineData("2026-11-03 06:00", false)]
    [InlineData("", false)]
    [InlineData("no es fecha", false)]
    public void La_fecha_de_la_edicion_exige_su_zona_horaria(string texto, bool valida)
    {
        Assert.Equal(valida, clsFechaProgramada.DeIso(texto) is not null);
    }

    // ----------------------------------------------------------------------------------------------- el viaje

    [Fact]
    public void Un_viaje_nuevo_puede_traer_su_fecha_y_su_lugar_de_cargue_y_sin_ellos_nacen_nulos()
    {
        var lugar = Guid.NewGuid();
        var fecha = new DateTimeOffset(2026, 11, 3, 6, 0, 0, TimeSpan.FromHours(-5));

        var con = clsViaje.Crear("MTK-001", Guid.NewGuid(), "ABC123", null, null, null, null, null, Guid.NewGuid(), AHORA, fecha, lugar);
        var sin = NuevoViaje();

        Assert.Equal(fecha, con.FechaProgramada);
        Assert.Equal(lugar, con.LugarCargueId);
        Assert.Null(sin.FechaProgramada);
        Assert.Null(sin.LugarCargueId);
    }

    [Fact]
    public void Reprogramar_cambia_solo_lo_indicado_y_permite_quitar_la_fecha()
    {
        var lugar = Guid.NewGuid();
        var fecha = new DateTimeOffset(2026, 11, 3, 6, 0, 0, TimeSpan.FromHours(-5));
        var viaje = clsViaje.Crear("MTK-001", Guid.NewGuid(), "ABC123", null, null, null, null, null, Guid.NewGuid(), AHORA, fecha, lugar);
        var otraFecha = fecha.AddDays(1);
        var otroLugar = Guid.NewGuid();

        viaje.Reprogramar(true, otraFecha, false, null, AHORA.AddHours(1));
        Assert.Equal(otraFecha, viaje.FechaProgramada);
        Assert.Equal(lugar, viaje.LugarCargueId);

        viaje.Reprogramar(false, null, true, otroLugar, AHORA.AddHours(2));
        Assert.Equal(otraFecha, viaje.FechaProgramada);
        Assert.Equal(otroLugar, viaje.LugarCargueId);

        viaje.Reprogramar(true, null, true, null, AHORA.AddHours(3));
        Assert.Null(viaje.FechaProgramada);
        Assert.Null(viaje.LugarCargueId);
        Assert.Equal(AHORA.AddHours(3), viaje.UpdatedAt);
    }

    [Fact]
    public void Un_viaje_que_ya_no_esta_programado_no_se_reprograma()
    {
        var viaje = NuevoViaje();
        viaje.CambiarEstado(clsEstadoViaje.EN_CURSO, AHORA);

        Assert.Throws<clsConflictoException>(() => viaje.Reprogramar(true, AHORA, false, null, AHORA));
    }

    [Fact]
    public void La_unidad_del_viaje_puede_traer_su_lugar_de_destino()
    {
        var destino = Guid.NewGuid();

        var con = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA, destino);
        var sin = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);

        Assert.Equal(destino, con.LugarDestinoId);
        Assert.Null(sin.LugarDestinoId);
    }
}
