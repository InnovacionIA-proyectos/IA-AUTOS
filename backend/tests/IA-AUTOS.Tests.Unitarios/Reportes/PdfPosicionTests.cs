using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Infraestructura.Reportes;

namespace IAAutos.Tests.Unitarios.Reportes;

/// <summary>La posicion de la unidad en la ninera en el informe de la segunda inspeccion del cargue y en el inventario del VIN: se muestra cuando existe y su ausencia no cambia los documentos de antes.</summary>
public sealed partial class GeneradorPdfInformeTests
{
    /// <summary>El texto como lo deja el PDF: los caracteres fuera de ASCII van como escape octal de Windows-1252.</summary>
    private static string EnPdf(string texto) =>
        string.Concat(texto.Select(c => c < 128 ? c.ToString() : c == '—' ? "\\227" :"\\" + Convert.ToString(c, 8)));

    [Fact]
    public void El_informe_de_la_segunda_inspeccion_del_cargue_lista_cada_unidad_con_su_posicion()
    {
        var datos = Datos() with
        {
            Acomodacion = new AcomodacionInforme(
                [
                    new UnidadAcomodacionInforme("9FB45RFL8NM201002", 3, false),
                    new UnidadAcomodacionInforme("9FB45RFL8NM201001", 4, false),
                    new UnidadAcomodacionInforme("9FB45RFL8NM201003", null, true),
                    new UnidadAcomodacionInforme("9FB45RFL8NM201004", null, false),
                ],
                [Foto("Foto de la acomodación", pie: "SHA-256: abc")]),
        };

        var pdf = Plano(Texto(Generar(datos)));

        Assert.Contains(EnPdf("Acomodación en la niñera"), pdf);
        Assert.Contains(EnPdf("Posición 3 — VIN 9FB45RFL8NM201002"), pdf);
        Assert.Contains(EnPdf("Posición 4 — VIN 9FB45RFL8NM201001"), pdf);
        Assert.Contains(EnPdf("No sale del viaje — VIN 9FB45RFL8NM201003"), pdf);
        Assert.Contains(EnPdf("Sin posición — VIN 9FB45RFL8NM201004"), pdf);
        Assert.Contains(EnPdf("Fotos de la acomodación"), pdf);
        Assert.Contains("Sin fotos de vistas.", pdf);
        Assert.Equal(1, Imagenes(Texto(Generar(datos))));
    }

    [Fact]
    public void El_informe_de_siempre_no_trae_la_seccion_de_acomodacion()
    {
        var pdf = Plano(Texto(Generar(Datos(vistas: [Foto("Vista: Frontal")]))));

        Assert.DoesNotContain(EnPdf("Acomodación"), pdf);
        Assert.DoesNotContain(EnPdf("Posición"), pdf);
        Assert.Contains("Fotos de las vistas", pdf);
    }

    [Fact]
    public void El_inventario_muestra_la_posicion_en_ninera_solo_cuando_el_vin_fue_despachado_con_ella()
    {
        var sin = Inventario();
        var con = sin with { Inventario = sin.Inventario with { Etapas = [sin.Inventario.Etapas[0] with { Posicion = 4 }] } };

        var conPosicion = Plano(Texto(new clsGeneradorPdfInventario().Generar(con)));
        var sinPosicion = Plano(Texto(new clsGeneradorPdfInventario().Generar(sin)));

        Assert.Contains(EnPdf("Posición en niñera: 4"), conPosicion);
        Assert.DoesNotContain(EnPdf("Posición en niñera"), sinPosicion);
    }
}
