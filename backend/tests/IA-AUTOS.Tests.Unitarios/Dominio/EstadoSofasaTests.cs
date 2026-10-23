using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>El estado de una orden Sofasa: el codigo es el StatusCode real (con sus mayusculas), no cambia y sigue las reglas de nombre y orden de los demas catalogos.</summary>
public class EstadoSofasaTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("ENTREGADA", "ENTREGADA")]
    [InlineData("  En Ruta  ", "En Ruta")]
    [InlineData("10", "10")]
    [InlineData("EN_BODEGA-2 (revision)", "EN_BODEGA-2 (revision)")]
    public void El_codigo_se_recorta_pero_conserva_sus_mayusculas_y_su_forma(string recibido, string esperado)
    {
        var estado = clsEstadoSofasa.Crear(recibido, "Etiqueta", null, null, AHORA);

        Assert.Equal(esperado, estado.Codigo);
        Assert.True(estado.Activo);
        Assert.Equal(0, estado.Orden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void El_codigo_es_obligatorio(string? codigo)
    {
        var ex = Assert.Throws<clsValidacionException>(() => clsEstadoSofasa.Crear(codigo, "Etiqueta", null, null, AHORA));

        Assert.Contains("codigo", ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys);
    }

    [Fact]
    public void El_codigo_no_pasa_de_100_caracteres_ni_lleva_saltos_de_linea()
    {
        Assert.NotNull(clsEstadoSofasa.Crear(new string('A', 100), "Etiqueta", null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => clsEstadoSofasa.Crear(new string('A', 101), "Etiqueta", null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => clsEstadoSofasa.Crear("UNO\r\nDOS", "Etiqueta", null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => clsEstadoSofasa.Crear("UNO\tDOS", "Etiqueta", null, null, AHORA));
    }

    [Fact]
    public void El_nombre_y_el_orden_siguen_las_reglas_de_los_demas_catalogos()
    {
        Assert.Throws<clsValidacionException>(() => clsEstadoSofasa.Crear("A", null, null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => clsEstadoSofasa.Crear("A", new string('N', 101), null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => clsEstadoSofasa.Crear("A", "Etiqueta", -1, null, AHORA));
        Assert.False(clsEstadoSofasa.Crear("A", "Etiqueta", 3, false, AHORA).Activo);
    }

    [Fact]
    public void Actualizar_cambia_solo_lo_recibido_y_nunca_el_codigo()
    {
        var estado = clsEstadoSofasa.Crear("ENTREGADA", "Entregada", 1, true, AHORA);

        estado.Actualizar("Entregada al cliente", null, false, AHORA.AddMinutes(5));

        Assert.Equal("ENTREGADA", estado.Codigo);
        Assert.Equal("Entregada al cliente", estado.Nombre);
        Assert.Equal(1, estado.Orden);
        Assert.False(estado.Activo);
        Assert.IsAssignableFrom<IConCodigo>(estado);
    }
}
