using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Reportes;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Tests.Unitarios.Dominio;

public class VehiculosTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Crear_normaliza_el_vin_a_mayusculas()
    {
        var v = clsVehiculo.Crear(" 9fbhsr4e8pm000123 ", "Renault", " Sandero ", null, clsOperacion.NACIONAL, AHORA);

        Assert.Equal("9FBHSR4E8PM000123", v.Vin);
        Assert.Equal("Sandero", v.Modelo);
        Assert.Null(v.Color);
    }

    [Fact]
    public void Crear_acepta_vin_laxo_pero_exige_marca_y_operacion_valida()
    {
        Assert.Equal("CHASIS-1", clsVehiculo.Crear("chasis-1", "Toyota", null, null, clsOperacion.IMPORTACION, AHORA).Vin);
        Assert.Throws<clsValidacionException>(() => clsVehiculo.Crear("A", " ", null, null, clsOperacion.NACIONAL, AHORA));
        Assert.Throws<clsValidacionException>(() => clsVehiculo.Crear("A", "B", null, null, "otra", AHORA));
        Assert.Throws<clsValidacionException>(() => clsVehiculo.Crear(" ", "B", null, null, clsOperacion.NACIONAL, AHORA));
    }

    [Fact]
    public void Recarga_reemplaza_marca_y_operacion_pero_conserva_modelo_y_color()
    {
        var v = clsVehiculo.Crear("V1", "Renault", "Sandero", "Rojo", clsOperacion.NACIONAL, AHORA);

        v.ActualizarDesdeCarga("Toyota", null, null, clsOperacion.IMPORTACION, AHORA.AddDays(1));

        Assert.Equal("Toyota", v.Marca);
        Assert.Equal(clsOperacion.IMPORTACION, v.Operacion);
        Assert.Equal("Sandero", v.Modelo);
        Assert.Equal("Rojo", v.Color);
    }

    [Fact]
    public void Sofasa_crea_como_nacional_con_marca_por_defecto_y_no_cambia_la_operacion()
    {
        var v = clsVehiculo.CrearDesdeSofasa("VINSOFASA00000001", null, "Duster", AHORA);
        Assert.Equal("Sofasa", v.Marca);
        Assert.Equal(clsOperacion.NACIONAL, v.Operacion);

        var importado = clsVehiculo.Crear("VIN2", "Toyota", "X", null, clsOperacion.IMPORTACION, AHORA);
        importado.ActualizarDesdeSofasa("Renault", null, AHORA);
        Assert.Equal(clsOperacion.IMPORTACION, importado.Operacion);
        Assert.Equal("X", importado.Modelo);
    }

    [Theory]
    [InlineData("9FBHSR4E8PM000123", true)]
    [InlineData("9fbhsr4e8pm000123", true)]
    [InlineData("9FBHSR4E8PM00012", false)]
    [InlineData("9FBHSR4E8PM00012I", false)]
    [InlineData("9FBHSR4E8PM00012O", false)]
    [InlineData("9FBHSR4E8PM00012Q", false)]
    public void Vin_iso_3779(string vin, bool esperado)
    {
        Assert.Equal(esperado, clsVin.EsFormatoIso(vin));
    }

    [Fact]
    public void Ruta_estandar_crea_una_etapa_por_cada_etapa_del_catalogo_en_su_orden_y_exigen_inspeccion()
    {
        Guid[] catalogo = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var vehiculoId = Guid.NewGuid();

        var etapas = clsRutaCustodia.CrearEtapasEstandar(vehiculoId, catalogo, AHORA);

        Assert.Equal([1, 2, 3], etapas.Select(e => e.Orden));
        Assert.Equal(catalogo, etapas.Select(e => e.EtapaCatalogoId));
        Assert.All(etapas, e => Assert.Null(e.PuntoCustodiaId));
        Assert.All(etapas, e => Assert.True(e.RequiereInspeccion));
        Assert.All(etapas, e => Assert.Equal(vehiculoId, e.VehiculoId));
    }

    [Fact]
    public void La_ruta_sigue_el_catalogo_si_se_agrega_o_se_retira_una_etapa()
    {
        var cuatro = clsRutaCustodia.CrearEtapasEstandar(Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], AHORA);
        var una = clsRutaCustodia.CrearEtapasEstandar(Guid.NewGuid(), [Guid.NewGuid()], AHORA);

        Assert.Equal([1, 2, 3, 4], cuatro.Select(e => e.Orden));
        Assert.Single(una);
    }

    [Fact]
    public void Ruta_estandar_falla_como_error_interno_si_el_catalogo_no_tiene_etapas_activas()
    {
        var ex = Assert.Throws<clsErrorInternoException>(() => clsRutaCustodia.CrearEtapasEstandar(Guid.NewGuid(), [], AHORA));

        Assert.Equal(clsCodigosError.INTERNAL_ERROR, ex.Codigo);
    }

    [Fact]
    public void Etapa_exige_orden_positivo()
    {
        Assert.Throws<clsValidacionException>(() => clsEtapa.Crear(Guid.NewGuid(), Guid.NewGuid(), null, 0, true, AHORA));
    }
}

public class ReglasReportesTests
{
    [Fact]
    public void Movilizado_requiere_todas_las_etapas_cerradas_y_al_menos_una()
    {
        Assert.True(clsReglasReportes.EsMovilizado([true, true, true]));
        Assert.False(clsReglasReportes.EsMovilizado([true, false, true]));
        Assert.False(clsReglasReportes.EsMovilizado([]));
    }

    [Theory]
    [InlineData(1, 3, 33.33)]
    [InlineData(2, 3, 66.67)]
    [InlineData(0, 10, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(1, 8, 12.5)]
    public void Porcentaje_con_dos_decimales(int parte, int total, double esperado)
    {
        Assert.Equal((decimal)esperado, clsReglasReportes.Porcentaje(parte, total));
    }

    [Fact]
    public void Linea_base_y_variacion()
    {
        Assert.Equal(8.81m, clsReglasReportes.LineaBaseDe(clsOperacion.IMPORTACION));
        Assert.Equal(8.34m, clsReglasReportes.LineaBaseDe(clsOperacion.EXPORTACION));
        Assert.Equal(1.95m, clsReglasReportes.LineaBaseDe(clsOperacion.NACIONAL));
        Assert.Equal(-1.31m, clsReglasReportes.Variacion(0.64m, 1.95m));
    }
}
