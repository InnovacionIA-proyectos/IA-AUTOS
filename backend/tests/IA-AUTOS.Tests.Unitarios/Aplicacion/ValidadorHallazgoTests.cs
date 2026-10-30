using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Inspecciones.Validators;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>
/// Reglas de forma del cuerpo de un hallazgo: la zona (cuadrante + lado), el preexistente y la nota. Que un cuadrante lleve lado o no lo decide el catalogo al resolver la zona,
/// no el validador.
/// </summary>
public class ValidadorHallazgoTests
{
    private static readonly FotoEvidenciaRequest FOTO = new("3f2b6c1e-0000-4000-8000-123456789abc.png", DateTimeOffset.UtcNow, 4.6, -74.1, null, null, null, null);

    private static CrearHallazgoRequest Solicitud(
        int? cuadrante = 54,
        string? lado = null,
        bool? esPreexistente = null,
        Guid? origen = null,
        string? nota = null) =>
        new(cuadrante, lado, "golpe", "5_10cm", "v2", esPreexistente, origen, nota, FOTO);

    private static Dictionary<string, string[]> Errores(CrearHallazgoRequest solicitud) =>
        new CrearHallazgoRequestValidator().Validate(solicitud).Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

    [Theory]
    [InlineData(1, "izquierdo")]
    [InlineData(7, "derecho")]
    [InlineData(35, "izquierdo")]
    [InlineData(54, null)]
    [InlineData(59, null)]
    public void Un_cuadrante_con_o_sin_lado_es_valido_en_su_forma(int cuadrante, string? lado)
    {
        Assert.Empty(Errores(Solicitud(cuadrante: cuadrante, lado: lado)));
    }

    [Theory]
    [InlineData("arriba")]
    [InlineData("")]
    [InlineData("Izquierdo")]
    public void El_lado_si_viene_debe_ser_izquierdo_o_derecho(string lado)
    {
        var errores = Errores(Solicitud(cuadrante: 7, lado: lado));

        Assert.Equal(["Lado"], errores.Keys);
    }

    [Fact]
    public void Sin_cuadrante_la_zona_no_existe()
    {
        var errores = Errores(Solicitud(cuadrante: null));

        Assert.Equal("La zona indicada no existe", errores["CuadranteNumero"][0]);
    }

    [Fact]
    public void El_numero_de_cuadrante_debe_ser_mayor_o_igual_a_1()
    {
        Assert.Contains("CuadranteNumero", Errores(Solicitud(cuadrante: 0, lado: "izquierdo")).Keys);
        Assert.Contains("CuadranteNumero", Errores(Solicitud(cuadrante: -3)).Keys);
    }

    [Fact]
    public void Un_hallazgo_preexistente_exige_su_origen_y_solo_ese_lleva_origen()
    {
        var origen = Guid.NewGuid();

        Assert.Empty(Errores(Solicitud(esPreexistente: true, origen: origen)));
        Assert.Contains("HallazgoOrigenId", Errores(Solicitud(esPreexistente: true)).Keys);
        Assert.Contains("HallazgoOrigenId", Errores(Solicitud(origen: origen)).Keys);
        Assert.Contains("HallazgoOrigenId", Errores(Solicitud(esPreexistente: false, origen: origen)).Keys);
        Assert.Contains("HallazgoOrigenId", Errores(Solicitud(esPreexistente: true, origen: Guid.Empty)).Keys);
    }

    [Fact]
    public void La_nota_admite_hasta_500_caracteres_y_la_foto_es_obligatoria()
    {
        Assert.Empty(Errores(Solicitud(nota: new string('n', 500))));
        Assert.Contains("Nota", Errores(Solicitud(nota: new string('n', 501))).Keys);
        Assert.Contains("Evidencia", Errores(Solicitud() with { Evidencia = null }).Keys);
    }

    [Theory]
    [InlineData(null, "5_10cm", "ClaseDano")]
    [InlineData("golpe", " ", "TamanoDano")]
    public void La_clase_y_el_tamano_son_obligatorios(string? clase, string tamano, string campo)
    {
        var errores = Errores(Solicitud() with { ClaseDano = clase, TamanoDano = tamano });

        Assert.Contains(campo, errores.Keys);
    }
}
