using IAAutos.Aplicacion.Viajes;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>La normalizacion del titulo de una columna del archivo de carga, de la que depende que dos archivos con los mismos titulos sean la misma forma.</summary>
public class MapeoDeLaCargaTests
{
    [Theory]
    [InlineData("VIN", "vin")]
    [InlineData("  Placa   Camión ", "placa camion")]
    [InlineData("CÉDULA\tCONDUCTOR", "cedula conductor")]
    [InlineData("Año  de  Modelo", "ano de modelo")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void El_titulo_se_normaliza_sin_tildes_ni_mayusculas_y_con_espacios_simples(string? titulo, string esperado) =>
        Assert.Equal(esperado, clsMapeoCargaViajesService.Normalizar(titulo));

    [Fact]
    public void Los_campos_que_se_pueden_elegir_son_los_de_una_fila_de_la_carga()
    {
        Assert.Equal(
            ["cedula_conductor", "codigo_mtrack", "destino", "fecha_programada", "origen", "placa", "trailer", "unidad", "vin"],
            clsMapeoCargaViajesService.CAMPOS.Order().ToList());
    }
}
