using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Conductor;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>El texto de los avisos escribe los valores de catalogo con su nombre y, si el codigo ya no esta en el catalogo, con el codigo.</summary>
public class AvisosConNombreTests
{
    private static ValorCatalogo Tamano(string codigo, string nombre) =>
        new(new Dictionary<string, object?> { ["id"] = Guid.NewGuid(), ["codigo"] = codigo, ["nombre"] = nombre, ["activo"] = true });

    [Fact]
    public void El_tamano_se_escribe_con_el_nombre_del_catalogo()
    {
        IReadOnlyList<ValorCatalogo> catalogo = [Tamano("1_5cm", "1 a 5 cm"), Tamano("5_10cm", "5 a 10 cm")];

        Assert.Equal("1 a 5 cm", clsConductorService.NombreDelTamano(catalogo, "1_5cm"));
        Assert.Equal("5 a 10 cm", clsConductorService.NombreDelTamano(catalogo, "5_10cm"));
    }

    [Fact]
    public void Un_tamano_que_ya_no_esta_en_el_catalogo_se_escribe_con_su_codigo()
    {
        IReadOnlyList<ValorCatalogo> catalogo = [Tamano("1_5cm", "1 a 5 cm")];

        Assert.Equal("tamano_retirado", clsConductorService.NombreDelTamano(catalogo, "tamano_retirado"));
        Assert.Equal("1_5cm", clsConductorService.NombreDelTamano([], "1_5cm"));
    }
}
