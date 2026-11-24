using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

public class PapelEnElViajeTests
{
    [Theory]
    [InlineData(true, false, "carga")]
    [InlineData(false, true, "recibe")]
    [InlineData(true, true, "carga_y_recibe")]
    [InlineData(false, false, null)]
    public void El_papel_sale_de_si_carga_y_si_recibe(bool carga, bool recibe, string? esperado) =>
        Assert.Equal(esperado, clsPapelEnElViaje.De(carga, recibe));

    [Fact]
    public void Los_valores_publicados_son_los_tres_papeles()
    {
        Assert.Equal(["carga", "recibe", "carga_y_recibe"], clsPapelEnElViaje.VALORES);
    }
}
