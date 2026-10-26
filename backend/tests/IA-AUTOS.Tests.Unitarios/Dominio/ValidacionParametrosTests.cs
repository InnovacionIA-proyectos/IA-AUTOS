using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Parametros;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>El valor de un parametro se valida contra su tipo y se guarda normalizado; un valor que no cumple se rechaza en el campo valor y el parametro no cambia.</summary>
public class ValidacionParametrosTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid USUARIO = Guid.NewGuid();

    private static readonly IReadOnlySet<string> NIVELES = new HashSet<string> { "v1_plus", "v1", "v2", "v3" };

    /// <summary>La entidad solo se crea desde la base: se arma por reflexion.</summary>
    private static clsParametro Parametro(string clave, string tipo, string? valor)
    {
        var p = (clsParametro)Activator.CreateInstance(typeof(clsParametro), nonPublic: true)!;
        typeof(clsParametro).GetProperty(nameof(clsParametro.Clave))!.SetValue(p, clave);
        typeof(clsParametro).GetProperty(nameof(clsParametro.Tipo))!.SetValue(p, tipo);
        typeof(clsParametro).GetProperty(nameof(clsParametro.Valor))!.SetValue(p, valor);
        return p;
    }

    private static string? CampoConError(clsValidacionException ex) =>
        ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys.SingleOrDefault();

    private static void Rechaza(clsParametro p, string? valor, IReadOnlySet<string>? codigos = null)
    {
        var antes = p.Valor;
        var ex = Assert.Throws<clsValidacionException>(() => p.CambiarValor(valor, codigos, USUARIO, AHORA));
        Assert.Equal("valor", CampoConError(ex));
        Assert.Equal(antes, p.Valor);
        Assert.Null(p.ActualizadoPor);
    }

    // ------------------------------------------------------------------------------------ hora

    [Theory]
    [InlineData("07:30", "07:30")]
    [InlineData(" 23:59 ", "23:59")]
    [InlineData("00:00", "00:00")]
    public void La_hora_acepta_HH_mm(string entrada, string guardado)
    {
        var p = Parametro("reporte_diario_hora", clsTiposParametro.HORA, "06:00");

        Assert.True(p.CambiarValor(entrada, null, USUARIO, AHORA));

        Assert.Equal(guardado, p.Valor);
        Assert.Equal(USUARIO, p.ActualizadoPor);
        Assert.Equal(AHORA, p.UpdatedAt);
    }

    [Theory]
    [InlineData("6:00")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("seis")]
    [InlineData("06:00:00")]
    [InlineData("0600")]
    public void La_hora_con_otro_formato_se_rechaza_y_sigue_la_anterior(string valor) =>
        Rechaza(Parametro("reporte_diario_hora", clsTiposParametro.HORA, "06:00"), valor);

    // ------------------------------------------------------------------------------------ numero

    [Theory]
    [InlineData("15", "15")]
    [InlineData(" 240 ", "240")]
    [InlineData("1000000", "1000000")]
    [InlineData("007", "7")]
    public void El_numero_es_un_entero_positivo(string entrada, string guardado)
    {
        var p = Parametro("url_firmada_minutos", clsTiposParametro.NUMERO, "15");

        p.CambiarValor(entrada, null, USUARIO, AHORA);

        Assert.Equal(guardado, p.Valor);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("1,5")]
    [InlineData("1000001")]
    [InlineData("abc")]
    [InlineData("1e3")]
    [InlineData("99999999999")]
    public void Un_numero_que_no_es_entero_positivo_se_rechaza(string valor) =>
        Rechaza(Parametro("url_firmada_minutos", clsTiposParametro.NUMERO, "15"), valor);

    // ------------------------------------------------------------------------------------ porcentaje

    [Theory]
    [InlineData("0", "0")]
    [InlineData("100", "100")]
    [InlineData("1.2", "1.2")]
    [InlineData("1.50", "1.5")]
    [InlineData("99.9999", "99.9999")]
    public void El_porcentaje_va_de_0_a_100_con_punto_decimal(string entrada, string guardado)
    {
        var p = Parametro("objetivo_diario_novedad", clsTiposParametro.PORCENTAJE, "1.2");

        p.CambiarValor(entrada, null, USUARIO, AHORA);

        Assert.Equal(guardado, p.Valor);
    }

    [Theory]
    [InlineData("100.1")]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("1,2")]
    [InlineData("12%")]
    [InlineData("uno")]
    [InlineData("1.23456")]
    public void Un_porcentaje_fuera_de_rango_o_mal_escrito_se_rechaza(string valor) =>
        Rechaza(Parametro("objetivo_diario_novedad", clsTiposParametro.PORCENTAJE, "1.2"), valor);

    // ------------------------------------------------------------------------------------ lista

    [Fact]
    public void La_lista_acepta_codigos_existentes_sin_espacios_ni_repetidos()
    {
        var p = Parametro("niveles_criticos", clsTiposParametro.LISTA, "v1_plus,v1");

        p.CambiarValor(" v1_plus , v2,v2 ", NIVELES, USUARIO, AHORA);

        Assert.Equal("v1_plus,v2", p.Valor);
    }

    [Theory]
    [InlineData("v1,v9")]
    [InlineData("critico")]
    [InlineData(",,,")]
    public void La_lista_con_un_codigo_que_no_existe_o_sin_codigos_se_rechaza(string valor) =>
        Rechaza(Parametro("niveles_criticos", clsTiposParametro.LISTA, "v1_plus,v1"), valor, NIVELES);

    [Fact]
    public void El_mensaje_de_la_lista_dice_cuales_codigos_no_existen_y_cuales_se_permiten()
    {
        var p = Parametro("niveles_criticos", clsTiposParametro.LISTA, "v1");

        var ex = Assert.Throws<clsValidacionException>(() => p.CambiarValor("v1,v9", NIVELES, USUARIO, AHORA));

        var mensaje = ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"])["valor"].Single();
        Assert.Contains("v9", mensaje);
        Assert.Contains("v1_plus", mensaje);
    }

    // ------------------------------------------------------------------------------------ correos

    [Fact]
    public void Los_correos_se_guardan_en_minusculas_sin_espacios_ni_repetidos()
    {
        var p = Parametro("reporte_diario_destinatarios", clsTiposParametro.CORREOS, null);

        p.CambiarValor(" Calidad@Empresa.com , gerencia@empresa.com,calidad@empresa.com ", null, USUARIO, AHORA);

        Assert.Equal("calidad@empresa.com,gerencia@empresa.com", p.Valor);
    }

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("a@b")]
    [InlineData("uno@empresa.com,malo")]
    [InlineData("a b@empresa.com")]
    [InlineData("uno@empresa.com;dos@empresa.com")]
    public void Un_correo_no_valido_se_rechaza(string valor) =>
        Rechaza(Parametro("reporte_diario_destinatarios", clsTiposParametro.CORREOS, null), valor);

    // ------------------------------------------------------------------------------------ sin valor y sin cambio

    [Theory]
    [InlineData(clsClavesParametros.REPORTE_DIARIO_DESTINATARIOS, clsTiposParametro.CORREOS)]
    [InlineData(clsClavesParametros.RETENCION_DIAS, clsTiposParametro.NUMERO)]
    public void Los_parametros_que_lo_admiten_quedan_sin_valor_con_nulo_o_vacio(string clave, string tipo)
    {
        var a = Parametro(clave, tipo, tipo == clsTiposParametro.CORREOS ? "a@b.co" : "30");
        var b = Parametro(clave, tipo, tipo == clsTiposParametro.CORREOS ? "a@b.co" : "30");

        Assert.True(a.CambiarValor(null, null, USUARIO, AHORA));
        Assert.True(b.CambiarValor("   ", null, USUARIO, AHORA));

        Assert.Null(a.Valor);
        Assert.Null(b.Valor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Los_demas_parametros_siempre_tienen_valor(string? valor) =>
        Rechaza(Parametro("url_firmada_minutos", clsTiposParametro.NUMERO, "15"), valor);

    [Fact]
    public void Dar_el_mismo_valor_no_cambia_nada()
    {
        var p = Parametro("reporte_diario_hora", clsTiposParametro.HORA, "06:00");

        var cambio = p.CambiarValor("06:00", null, USUARIO, AHORA);

        Assert.False(cambio);
        Assert.Null(p.ActualizadoPor);
        Assert.Equal(default, p.UpdatedAt);
    }

    [Fact]
    public void Un_texto_admite_hasta_mil_caracteres()
    {
        var p = Parametro("nota", clsTiposParametro.TEXTO, "x");

        p.CambiarValor(new string('a', 1000), null, USUARIO, AHORA);

        Assert.Equal(1000, p.Valor!.Length);
        Rechaza(Parametro("nota", clsTiposParametro.TEXTO, "x"), new string('a', 1001));
    }
}
