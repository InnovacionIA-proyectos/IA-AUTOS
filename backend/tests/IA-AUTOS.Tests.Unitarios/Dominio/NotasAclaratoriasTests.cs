using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>La nota aclaratoria: un registro nuevo con su autor y su fecha, de una inspeccion, un hallazgo o una transferencia.</summary>
public class NotasAclaratoriasTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(clsEntidadNota.INSPECCION)]
    [InlineData(clsEntidadNota.HALLAZGO)]
    [InlineData(clsEntidadNota.TRANSFERENCIA)]
    public void Una_nota_guarda_su_registro_su_autor_su_texto_limpio_y_su_fecha(string entidad)
    {
        var registro = Guid.NewGuid();
        var autor = Guid.NewGuid();

        var nota = clsNotaAclaratoria.Crear(entidad, registro, autor, "  Se aclara el golpe  ", AHORA);

        Assert.NotEqual(Guid.Empty, nota.Id);
        Assert.Equal(entidad, nota.Entidad);
        Assert.Equal(registro, nota.EntidadId);
        Assert.Equal(autor, nota.AutorId);
        Assert.Equal("Se aclara el golpe", nota.Texto);
        Assert.Equal(AHORA, nota.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void El_texto_es_obligatorio(string? texto) =>
        Assert.Throws<clsValidacionException>(() => clsNotaAclaratoria.Crear(clsEntidadNota.HALLAZGO, Guid.NewGuid(), Guid.NewGuid(), texto, AHORA));

    [Fact]
    public void El_texto_admite_mil_caracteres_y_no_mil_uno()
    {
        Assert.Equal(1000, clsNotaAclaratoria.Crear(clsEntidadNota.INSPECCION, Guid.NewGuid(), Guid.NewGuid(), new string('a', 1000), AHORA).Texto.Length);
        Assert.Throws<clsValidacionException>(() => clsNotaAclaratoria.Crear(clsEntidadNota.INSPECCION, Guid.NewGuid(), Guid.NewGuid(), new string('a', 1001), AHORA));
    }

    [Fact]
    public void Solo_se_aclara_una_inspeccion_un_hallazgo_o_una_transferencia() =>
        Assert.Throws<clsValidacionException>(() => clsNotaAclaratoria.Crear("vehiculo", Guid.NewGuid(), Guid.NewGuid(), "x", AHORA));
}
