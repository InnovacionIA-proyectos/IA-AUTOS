using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Tests.Unitarios.Dominio;

public class InspeccionesTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static clsInspeccion NuevaInspeccion(string momento = clsMomento.INFORME, double? lat = 4.6, double? lng = -74.1, string? notas = null) =>
        clsInspeccion.Crear(Guid.NewGuid(), Guid.NewGuid(), momento, AHORA, lat, lng, null, "  Juan  ", "ABC123", null, notas, AHORA);

    [Fact]
    public void Crear_valida_estado_borrador_por_defecto_y_limpia_textos()
    {
        var insp = NuevaInspeccion();

        Assert.Equal(clsEstadoInspeccion.BORRADOR, insp.Estado);
        Assert.False(insp.EstaCerrada);
        Assert.Equal("Juan", insp.ConductorNombre);
        Assert.Null(insp.Trailer);
        Assert.NotEqual(Guid.Empty, insp.Id);
    }

    [Theory]
    [InlineData("check_in")]
    [InlineData("check_out")]
    [InlineData("informe")]
    public void Crear_acepta_los_momentos_del_catalogo(string momento)
    {
        Assert.Equal(momento, NuevaInspeccion(momento).Momento);
    }

    [Fact]
    public void Crear_rechaza_momento_fuera_del_catalogo()
    {
        var ex = Assert.Throws<clsValidacionException>(() => NuevaInspeccion("otro"));
        Assert.Equal(clsCodigosError.VALIDATION_ERROR, ex.Codigo);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void Crear_rechaza_coordenadas_fuera_de_rango(double lat, double lng)
    {
        Assert.Throws<clsValidacionException>(() => NuevaInspeccion(lat: lat, lng: lng));
    }

    [Fact]
    public void Crear_rechaza_notas_de_mas_de_1000_caracteres()
    {
        Assert.Throws<clsValidacionException>(() => NuevaInspeccion(notas: new string('x', 1001)));
        Assert.NotNull(NuevaInspeccion(notas: new string('x', 1000)).Notas);
    }

    [Fact]
    public void Cerrar_sin_novedades_ni_fotos_es_valido()
    {
        var insp = NuevaInspeccion();

        insp.Cerrar(new clsResumenCierre(0, 0, 0), AHORA.AddMinutes(5));

        Assert.True(insp.EstaCerrada);
        Assert.Equal(AHORA.AddMinutes(5), insp.UpdatedAt);
    }

    [Fact]
    public void Cerrar_con_hallazgos_exige_al_menos_una_foto()
    {
        var insp = NuevaInspeccion();

        var ex = Assert.Throws<clsValidacionException>(() => insp.Cerrar(new clsResumenCierre(0, 0, 2), AHORA));

        var errores = Assert.IsAssignableFrom<IReadOnlyList<string>>(ex.Detalles);
        Assert.Single(errores);
        Assert.Contains("novedad", errores[0]);
        Assert.False(insp.EstaCerrada);
    }

    [Fact]
    public void Cerrar_exige_geolocalizacion_en_toda_foto()
    {
        var insp = NuevaInspeccion();

        var ex = Assert.Throws<clsValidacionException>(() => insp.Cerrar(new clsResumenCierre(3, 2, 0), AHORA));

        var errores = Assert.IsAssignableFrom<IReadOnlyList<string>>(ex.Detalles);
        Assert.Contains("2 foto(s) sin geolocalización", errores[0]);
    }

    [Fact]
    public void Cerrar_acumula_ambos_errores()
    {
        var insp = NuevaInspeccion();

        var ex = Assert.Throws<clsValidacionException>(() => insp.Cerrar(new clsResumenCierre(0, 1, 1), AHORA));

        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<string>>(ex.Detalles).Count);
    }

    [Fact]
    public void Una_inspeccion_cerrada_es_inmutable_y_no_se_cierra_dos_veces()
    {
        var insp = NuevaInspeccion();
        insp.Cerrar(new clsResumenCierre(1, 0, 1), AHORA);

        Assert.Throws<clsConflictoException>(insp.AsegurarEditable);
        Assert.Throws<clsConflictoException>(() => insp.Cerrar(new clsResumenCierre(1, 0, 1), AHORA));
    }

    [Fact]
    public void Una_inspeccion_en_borrador_es_editable()
    {
        NuevaInspeccion().AsegurarEditable();
    }
}

public class HallazgosYEvidenciasTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Hallazgo_valido_conserva_coordenadas()
    {
        var h = clsHallazgo.Crear(Guid.NewGuid(), "  Guardafango delantero derecho ", "golpe", "5_10cm", new clsCoordsDiagrama(0.2, 0.8), AHORA);

        Assert.Equal("Guardafango delantero derecho", h.ParteVehiculo);
        Assert.Equal(0.2, h.CoordsDiagrama!.X);
    }

    [Theory]
    [InlineData("", "golpe", "5_10cm")]
    [InlineData("Capot", "no_existe", "5_10cm")]
    [InlineData("Capot", "golpe", "no_existe")]
    public void Hallazgo_rechaza_campos_invalidos(string parte, string clase, string tamano)
    {
        Assert.Throws<clsValidacionException>(() => clsHallazgo.Crear(Guid.NewGuid(), parte, clase, tamano, null, AHORA));
    }

    [Fact]
    public void Hallazgo_rechaza_parte_de_mas_de_80_caracteres()
    {
        Assert.Throws<clsValidacionException>(() => clsHallazgo.Crear(Guid.NewGuid(), new string('p', 81), "golpe", "1_5cm", null, AHORA));
    }

    [Theory]
    [InlineData(-0.1, 0.5)]
    [InlineData(0.5, 1.1)]
    public void Coordenadas_del_diagrama_deben_estar_normalizadas(double x, double y)
    {
        Assert.Throws<clsValidacionException>(() => new clsCoordsDiagrama(x, y));
    }

    [Fact]
    public void Evidencia_requiere_ruta_y_valida_rango_del_sello()
    {
        Assert.Throws<clsValidacionException>(() => clsEvidencia.Crear(Guid.NewGuid(), null, " ", AHORA, 1, 1, AHORA));
        Assert.Throws<clsValidacionException>(() => clsEvidencia.Crear(Guid.NewGuid(), null, "a.jpg", AHORA, 100, 1, AHORA));

        var ok = clsEvidencia.Crear(Guid.NewGuid(), null, "a.jpg", AHORA, null, null, AHORA);
        Assert.Null(ok.SelloLat);
    }
}
