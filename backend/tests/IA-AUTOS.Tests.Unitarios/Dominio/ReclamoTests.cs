using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

public class ReclamoTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 3, 6, 0, 0, TimeSpan.FromHours(-5));

    [Theory]
    [InlineData(clsProcesoReclamo.CARGUE)]
    [InlineData(clsProcesoReclamo.ENTREGA)]
    public void Un_reclamo_nuevo_queda_activo_con_su_inspector_su_proceso_y_su_fecha(string proceso)
    {
        var viaje = Guid.NewGuid();
        var inspector = Guid.NewGuid();

        var reclamo = clsReclamo.Crear(viaje, inspector, proceso, AHORA);

        Assert.NotEqual(Guid.Empty, reclamo.Id);
        Assert.Equal(viaje, reclamo.ViajeId);
        Assert.Equal(inspector, reclamo.InspectorId);
        Assert.Equal(proceso, reclamo.Proceso);
        Assert.Equal(AHORA, reclamo.ReclamadoAt);
        Assert.True(reclamo.EstaActivo);
        Assert.Null(reclamo.LiberadoAt);
        Assert.Null(reclamo.LiberadoPor);
    }

    [Theory]
    [InlineData("descargue")]
    [InlineData("")]
    [InlineData("Cargue")]
    public void Un_proceso_que_no_es_cargue_ni_entrega_se_rechaza(string proceso)
    {
        Assert.Throws<clsValidacionException>(() => clsReclamo.Crear(Guid.NewGuid(), Guid.NewGuid(), proceso, AHORA));
    }

    [Fact]
    public void Liberar_guarda_quien_y_cuando_y_un_reclamo_liberado_no_se_libera_otra_vez()
    {
        var reclamo = clsReclamo.Crear(Guid.NewGuid(), Guid.NewGuid(), clsProcesoReclamo.CARGUE, AHORA);
        var coordinador = Guid.NewGuid();

        reclamo.Liberar(coordinador, AHORA.AddHours(2));

        Assert.False(reclamo.EstaActivo);
        Assert.Equal(coordinador, reclamo.LiberadoPor);
        Assert.Equal(AHORA.AddHours(2), reclamo.LiberadoAt);
        Assert.Throws<clsConflictoException>(() => reclamo.Liberar(Guid.NewGuid(), AHORA.AddHours(3)));
        Assert.Equal(coordinador, reclamo.LiberadoPor);
    }

    [Fact]
    public void El_inspector_del_viaje_se_asigna_y_se_vacia()
    {
        var viaje = clsViaje.Crear("MTK-001", Guid.NewGuid(), "ABC123", null, null, null, null, null, Guid.NewGuid(), AHORA);
        var inspector = Guid.NewGuid();

        viaje.AsignarInspector(inspector, AHORA.AddMinutes(1));
        Assert.Equal(inspector, viaje.InspectorId);
        Assert.True(viaje.Participa(inspector));

        viaje.AsignarInspector(null, AHORA.AddMinutes(2));
        Assert.Null(viaje.InspectorId);
        Assert.False(viaje.Participa(inspector));
        Assert.Equal(AHORA.AddMinutes(2), viaje.UpdatedAt);
    }
}
