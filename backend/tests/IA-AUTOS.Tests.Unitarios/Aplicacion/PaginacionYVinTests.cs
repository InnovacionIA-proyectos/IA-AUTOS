using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>La regla unica del VIN y el formato comun de paginacion y orden de los listados.</summary>
public class PaginacionYVinTests
{
    private static readonly IReadOnlySet<string> CAMPOS = new HashSet<string> { "fecha", "vin", "created_at" };

    // ------------------------------------------------------------------------------------------------ VIN

    [Theory]
    [InlineData("9FBHSR4E8PM000001")]
    [InlineData("  9fbhsr4e8pm000001 ")]
    [InlineData("ABC123")]
    [InlineData("abc123")]
    [InlineData("ABCDEFGHJKLMNPRSTUVW")]
    [InlineData("CHASSS12345678901")]
    [InlineData("OOOOOO")]
    public void Un_vin_iso_o_un_chasis_de_6_a_20_alfanumericos_es_valido(string texto)
    {
        // Un chasis de 6 a 20 caracteres puede llevar I, O o Q; solo el de 17 caracteres se rige por el formato ISO (CHASSS12345678901 no lleva esas letras).
        Assert.Null(clsVin.Validar(texto));
    }

    [Theory]
    [InlineData("9FBHSR4E8PM0O0001")]
    [InlineData("9FBHSR4E8PM00Q001")]
    [InlineData("9FBHSR4E8PM00I001")]
    public void Un_vin_de_17_caracteres_con_I_O_o_Q_no_es_valido_y_el_motivo_lo_dice(string texto)
    {
        Assert.Equal(clsVin.MOTIVO_ISO, clsVin.Validar(texto));
        Assert.Contains("I, O ni Q", clsVin.MOTIVO_ISO);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("AB123")]
    [InlineData("ABCDEFGHJKLMNPRSTUVWX")]
    [InlineData("ABC-123456")]
    [InlineData("ABC 123456")]
    [InlineData("ÑANDU123456")]
    public void Un_texto_que_no_es_un_vin_ni_un_chasis_de_6_a_20_alfanumericos_no_es_valido(string? texto)
    {
        Assert.Equal(clsVin.MOTIVO_GENERAL, clsVin.Validar(texto));
    }

    [Theory]
    [InlineData("9FBHSR4E8PM0O0001", true)]
    [InlineData("9fbhsr4e8pm000001", true)]
    [InlineData("9FBHSR4E8PM00000", false)]
    [InlineData("9FBHSR4E8PM-00001", false)]
    [InlineData("renault", false)]
    [InlineData("ABC", false)]
    public void Solo_un_texto_de_17_alfanumericos_se_toma_como_un_vin_completo_al_buscar_por_fragmento(string texto, bool esperado)
    {
        Assert.Equal(esperado, clsVin.ParecePesquisaDeVinCompleto(texto));
    }

    [Fact]
    public void El_filtro_de_vin_normaliza_y_acumula_el_motivo_en_el_campo()
    {
        var errores = new Dictionary<string, string[]>();

        var valido = clsParseoFiltros.Vin("  9fbhsr4e8pm000001 ", "vin", errores);
        var vacio = clsParseoFiltros.Vin("  ", "vin", errores);
        var invalido = clsParseoFiltros.Vin("9FBHSR4E8PM0O0001", "vin", errores);

        Assert.Equal("9FBHSR4E8PM000001", valido);
        Assert.Null(vacio);
        Assert.Null(invalido);
        Assert.Equal([clsVin.MOTIVO_ISO], errores["vin"]);
    }

    // ------------------------------------------------------------------------------------------ paginacion

    [Fact]
    public void Sin_nada_la_pagina_es_la_primera_de_50()
    {
        var errores = new Dictionary<string, string[]>();

        var pagina = clsPaginacion.Pagina(null, null, errores);

        Assert.Equal(new PaginaSolicitada(1, 50), pagina);
        Assert.Equal(0, pagina.Desplazamiento);
        Assert.Empty(errores);
    }

    [Theory]
    [InlineData("3", "20", 3, 20, 40)]
    [InlineData("0", "0", 1, 1, 0)]
    [InlineData("-5", "-1", 1, 1, 0)]
    [InlineData("2", "5000", 2, 200, 200)]
    [InlineData(" 4 ", " 200 ", 4, 200, 600)]
    public void La_pagina_se_acota_desde_1_y_el_tamano_a_un_maximo_de_200(string pagina, string tamano, int paginaEsperada, int tamanoEsperado, int desplazamiento)
    {
        var errores = new Dictionary<string, string[]>();

        var r = clsPaginacion.Pagina(pagina, tamano, errores);

        Assert.Equal(new PaginaSolicitada(paginaEsperada, tamanoEsperado), r);
        Assert.Equal(desplazamiento, r.Desplazamiento);
        Assert.Empty(errores);
    }

    [Fact]
    public void Una_pagina_o_un_tamano_que_no_son_numeros_enteros_agregan_su_error()
    {
        var errores = new Dictionary<string, string[]>();

        var r = clsPaginacion.Pagina("abc", "1.5", errores);

        Assert.Equal(new PaginaSolicitada(1, 50), r);
        Assert.Contains("pagina", errores.Keys);
        Assert.Contains("tamano", errores.Keys);
    }

    [Fact]
    public void La_pagina_ya_numerica_se_acota_igual()
    {
        Assert.Equal(new PaginaSolicitada(1, 50), clsPaginacion.Pagina((int?)null, null));
        Assert.Equal(new PaginaSolicitada(1, 200), clsPaginacion.Pagina(-3, 99999));
    }

    // ----------------------------------------------------------------------------------------------- orden

    [Theory]
    [InlineData("fecha:asc", "fecha", false)]
    [InlineData("fecha:desc", "fecha", true)]
    [InlineData("FECHA:DESC", "fecha", true)]
    [InlineData("  vin ", "vin", false)]
    [InlineData("created_at:Desc", "created_at", true)]
    public void El_orden_acepta_campo_con_o_sin_sentido_sin_importar_las_mayusculas(string orden, string campo, bool descendente)
    {
        var errores = new Dictionary<string, string[]>();

        var r = clsPaginacion.Orden(orden, CAMPOS, errores);

        Assert.Equal(new OrdenSolicitado(campo, descendente), r);
        Assert.Empty(errores);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_orden_no_hay_orden_ni_error(string? orden)
    {
        var errores = new Dictionary<string, string[]>();

        Assert.Null(clsPaginacion.Orden(orden, CAMPOS, errores));
        Assert.Empty(errores);
    }

    [Theory]
    [InlineData("password_hash:asc")]
    [InlineData("fecha:sideways")]
    [InlineData("fecha:asc:desc")]
    [InlineData(":asc")]
    [InlineData("fecha;drop table")]
    public void Un_campo_no_permitido_o_un_formato_distinto_agrega_el_error_con_los_campos_permitidos(string orden)
    {
        var errores = new Dictionary<string, string[]>();

        var r = clsPaginacion.Orden(orden, CAMPOS, errores);

        Assert.Null(r);
        var mensaje = Assert.Single(errores["orden"]);
        Assert.Contains("campo:asc o campo:desc", mensaje);
        Assert.Contains("created_at, fecha, vin", mensaje);
    }
}
