using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Unitarios.Falsos;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>Lo que el conductor lee de un hallazgo: la zona, la clase, el tamano, el tipo de novedad, el origen, si es preexistente y la fecha; nada del seguimiento ni de la calidad.</summary>
public class VisibilidadConductorTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static HallazgoDto Completo() => new(
        Guid.NewGuid(), Guid.NewGuid(), null, "golpe", "5_10cm", null, 7, "izquierdo", null, "Puerta delantera", "Izquierdo 7 · Puerta delantera", "golpe",
        "v1", "V1", "Se rechaza el vehículo", "inspector", "Nota interna", true, Guid.NewGuid(), Guid.NewGuid(), "validado", true, ["foto_hallazgo"], Guid.NewGuid(), AHORA, AHORA, AHORA);

    [Fact]
    public void El_conductor_conserva_lo_que_ve_en_lectura_y_pierde_lo_demas()
    {
        var original = Completo();

        var paraConductor = original.ParaConductor();

        Assert.Equal((original.Id, original.InspeccionId, original.Zona, original.ClaseDano, original.TamanoDano), (paraConductor.Id, paraConductor.InspeccionId, paraConductor.Zona, paraConductor.ClaseDano, paraConductor.TamanoDano));
        Assert.Equal((original.CuadranteNumero, original.Lado, original.TipoNovedad, original.Origen, original.EsPreexistente), (paraConductor.CuadranteNumero, paraConductor.Lado, paraConductor.TipoNovedad, paraConductor.Origen, paraConductor.EsPreexistente));
        Assert.Equal((original.CreatedAt, original.UpdatedAt), (paraConductor.CreatedAt, paraConductor.UpdatedAt));
        Assert.Null(paraConductor.NivelSeveridad);
        Assert.Null(paraConductor.NivelNombre);
        Assert.Null(paraConductor.NivelAccion);
        Assert.Null(paraConductor.Nota);
        Assert.Null(paraConductor.HallazgoOrigenId);
        Assert.Null(paraConductor.ConfirmadoPor);
        Assert.Null(paraConductor.EstadoSeguimiento);
        Assert.Null(paraConductor.EvidenciaCompleta);
        Assert.Null(paraConductor.EvidenciaFaltante);
        Assert.Null(paraConductor.RevisadoPor);
        Assert.Null(paraConductor.RevisadoAt);
        // El original no se toca.
        Assert.Equal("v1", original.NivelSeveridad);
        Assert.Equal("validado", original.EstadoSeguimiento);
    }

    [Theory]
    [InlineData(clsRol.CONDUCTOR, true)]
    [InlineData(clsRol.INSPECTOR, false)]
    [InlineData(clsRol.COORDINADOR_CALIDAD, false)]
    [InlineData(clsRol.ADMIN, false)]
    [InlineData(clsRol.SUPER_ADMIN, false)]
    public void Solo_el_conductor_ve_los_hallazgos_en_lectura(string rol, bool esConductor) =>
        Assert.Equal(esConductor, new clsUsuarioActualFalso { Id = Guid.NewGuid(), Rol = rol }.EsConductor());
}
