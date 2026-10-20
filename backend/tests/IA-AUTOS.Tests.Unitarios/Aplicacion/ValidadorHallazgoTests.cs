using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Inspecciones.Validators;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>Reglas de forma del cuerpo de un hallazgo: la zona (cuadrante + lado, o zona provisional), el preexistente y la nota.</summary>
public class ValidadorHallazgoTests
{
    private static readonly FotoEvidenciaRequest FOTO = new("3f2b6c1e-0000-4000-8000-123456789abc.png", DateTimeOffset.UtcNow, 4.6, -74.1, null, null, null, null);

    private static CrearHallazgoRequest Solicitud(
        int? cuadrante = null,
        string? lado = null,
        string? provisional = null,
        bool? esPreexistente = null,
        Guid? origen = null,
        string? nota = null) =>
        new(cuadrante, lado, provisional, "golpe", "5_10cm", "v2", esPreexistente, origen, nota, FOTO);

    private static Dictionary<string, string[]> Errores(CrearHallazgoRequest solicitud) =>
        new CrearHallazgoRequestValidator().Validate(solicitud).Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

    [Theory]
    [InlineData(1, "izquierdo")]
    [InlineData(7, "derecho")]
    [InlineData(25, "izquierdo")]
    [InlineData(26, null)]
    [InlineData(38, null)]
    public void Un_cuadrante_con_el_lado_que_le_corresponde_es_valido(int cuadrante, string? lado)
    {
        Assert.Empty(Errores(Solicitud(cuadrante: cuadrante, lado: lado)));
    }

    [Theory]
    [InlineData("frente")]
    [InlineData("llanta_delantera_izquierda")]
    [InlineData("llanta_delantera_derecha")]
    [InlineData("llanta_trasera_izquierda")]
    [InlineData("llanta_trasera_derecha")]
    public void Una_zona_provisional_de_la_lista_es_valida(string zona)
    {
        Assert.Empty(Errores(Solicitud(provisional: zona)));
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(25, "arriba")]
    [InlineData(7, "")]
    public void Del_1_al_25_el_lado_es_obligatorio_y_debe_ser_izquierdo_o_derecho(int cuadrante, string? lado)
    {
        var errores = Errores(Solicitud(cuadrante: cuadrante, lado: lado));

        Assert.Contains("Lado", errores.Keys);
    }

    [Theory]
    [InlineData(26, "izquierdo")]
    [InlineData(30, "derecho")]
    public void Del_26_en_adelante_el_lado_no_se_envia(int cuadrante, string lado)
    {
        var errores = Errores(Solicitud(cuadrante: cuadrante, lado: lado));

        Assert.Equal(["Lado"], errores.Keys);
        Assert.Contains("26 en adelante", errores["Lado"][0]);
    }

    [Fact]
    public void La_zona_es_exactamente_una_de_las_dos_formas()
    {
        Assert.Contains("CuadranteNumero", Errores(Solicitud()).Keys);
        Assert.Contains("CuadranteNumero", Errores(Solicitud(cuadrante: 7, lado: "izquierdo", provisional: "frente")).Keys);
        Assert.Contains("CuadranteNumero", Errores(Solicitud(provisional: "  ")).Keys);
    }

    [Fact]
    public void Una_zona_provisional_no_lleva_lado_y_debe_ser_de_la_lista()
    {
        Assert.Contains("Lado", Errores(Solicitud(provisional: "frente", lado: "izquierdo")).Keys);
        Assert.Contains("ZonaProvisional", Errores(Solicitud(provisional: "techo")).Keys);
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

        Assert.Empty(Errores(Solicitud(provisional: "frente", esPreexistente: true, origen: origen)));
        Assert.Contains("HallazgoOrigenId", Errores(Solicitud(provisional: "frente", esPreexistente: true)).Keys);
        Assert.Contains("HallazgoOrigenId", Errores(Solicitud(provisional: "frente", origen: origen)).Keys);
        Assert.Contains("HallazgoOrigenId", Errores(Solicitud(provisional: "frente", esPreexistente: false, origen: origen)).Keys);
        Assert.Contains("HallazgoOrigenId", Errores(Solicitud(provisional: "frente", esPreexistente: true, origen: Guid.Empty)).Keys);
    }

    [Fact]
    public void La_nota_admite_hasta_500_caracteres_y_la_foto_es_obligatoria()
    {
        Assert.Empty(Errores(Solicitud(provisional: "frente", nota: new string('n', 500))));
        Assert.Contains("Nota", Errores(Solicitud(provisional: "frente", nota: new string('n', 501))).Keys);
        Assert.Contains("Evidencia", Errores(Solicitud(provisional: "frente") with { Evidencia = null }).Keys);
    }

    [Theory]
    [InlineData(null, "5_10cm", "ClaseDano")]
    [InlineData("golpe", " ", "TamanoDano")]
    public void La_clase_y_el_tamano_son_obligatorios(string? clase, string tamano, string campo)
    {
        var errores = Errores(Solicitud(provisional: "frente") with { ClaseDano = clase, TamanoDano = tamano });

        Assert.Contains(campo, errores.Keys);
    }
}
