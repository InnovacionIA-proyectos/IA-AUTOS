using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

public class PermisoSinInspectorTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 10, 8, 30, 0, TimeSpan.FromHours(-5));

    private static clsPermisoSinInspector Nuevo() =>
        clsPermisoSinInspector.Solicitar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "inspector_no_llego", null, AHORA, 10.4, -75.5, AHORA);

    [Fact]
    public void La_solicitud_nace_pendiente_con_su_motivo_su_momento_y_su_gps_y_vence_a_los_10_minutos()
    {
        var p = Nuevo();

        Assert.Equal(clsEstadoPermiso.PENDIENTE, p.Estado);
        Assert.False(p.EstaConcedido);
        Assert.Equal("inspector_no_llego", p.MotivoCodigo);
        Assert.Equal(10.4, p.Lat);
        Assert.Equal(AHORA.AddMinutes(10), p.VenceAt);
        Assert.False(p.PorTiempo);
        Assert.Null(p.ConcedidoPor);
    }

    [Fact]
    public void Un_coordinador_la_concede_con_su_id_y_no_se_concede_dos_veces()
    {
        var p = Nuevo();
        var coordinador = Guid.NewGuid();

        p.Conceder(coordinador, AHORA.AddMinutes(2));

        Assert.True(p.EstaConcedido);
        Assert.Equal(coordinador, p.ConcedidoPor);
        Assert.False(p.PorTiempo);
        Assert.Equal(AHORA.AddMinutes(2), p.ConcedidoAt);
        Assert.Throws<clsConflictoException>(() => p.Conceder(coordinador, AHORA.AddMinutes(3)));
        Assert.Throws<clsConflictoException>(() => p.ConcederPorTiempo(AHORA.AddMinutes(30)));
    }

    [Fact]
    public void El_reloj_nunca_concede_antes_de_los_10_minutos_y_lo_deja_marcado_sin_usuario()
    {
        var p = Nuevo();

        Assert.Throws<clsConflictoException>(() => p.ConcederPorTiempo(AHORA.AddMinutes(9).AddSeconds(59)));
        Assert.False(p.EstaConcedido);
        p.ConcederPorTiempo(AHORA.AddMinutes(10));

        Assert.True(p.EstaConcedido);
        Assert.True(p.PorTiempo);
        Assert.Null(p.ConcedidoPor);
    }

    [Fact]
    public void La_solicitud_exige_motivo_y_valida_el_gps_y_el_texto()
    {
        Assert.Throws<clsValidacionException>(() => clsPermisoSinInspector.Solicitar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, AHORA, null, null, AHORA));
        Assert.Throws<clsValidacionException>(() => clsPermisoSinInspector.Solicitar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "otro", null, AHORA, 91, 0, AHORA));
        Assert.Throws<clsValidacionException>(() => clsPermisoSinInspector.Solicitar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "otro", new string('x', 301), AHORA, null, null, AHORA));
    }
}
