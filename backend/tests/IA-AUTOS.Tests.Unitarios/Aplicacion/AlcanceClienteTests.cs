using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Unitarios.Falsos;
using IAAutos.Tests.Unitarios.Reportes;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>La visibilidad por cliente en lectura: a quien se limita, y que solo el super administrador pide un cliente distinto del suyo.</summary>
public class AlcanceClienteTests
{
    private static readonly Guid SOFASA = Guid.NewGuid();
    private static readonly Guid OTRO = Guid.NewGuid();

    private static clsUsuarioActualFalso Usuario(string? rol, Guid? cliente) => new() { Id = Guid.NewGuid(), Rol = rol, ClienteId = cliente };

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.ADMIN)]
    public void Coordinador_comercial_y_administrador_se_limitan_a_su_cliente(string rol)
    {
        Assert.Equal(SOFASA, Usuario(rol, SOFASA).ClienteRestringido());
    }

    [Fact]
    public void Quien_es_de_un_cliente_pero_no_tiene_ninguno_asignado_no_ve_nada()
    {
        Assert.Equal(Guid.Empty, Usuario(clsRol.COORDINADOR_CALIDAD, null).ClienteRestringido());
    }

    [Theory]
    [InlineData(clsRol.SUPER_ADMIN)]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    [InlineData(null)]
    public void El_super_administrador_y_quien_se_limita_por_viaje_no_se_limitan_por_cliente(string? rol)
    {
        Assert.Null(Usuario(rol, SOFASA).ClienteRestringido());
    }

    [Fact]
    public void El_super_administrador_elige_cualquier_cliente_o_ninguno()
    {
        var superAdmin = Usuario(clsRol.SUPER_ADMIN, null);

        Assert.Equal(OTRO, superAdmin.ClienteDelFiltro(OTRO));
        Assert.Null(superAdmin.ClienteDelFiltro(null));
    }

    [Fact]
    public void Quien_se_limita_a_su_cliente_recibe_siempre_el_suyo_y_pedir_otro_es_403()
    {
        var coordinador = Usuario(clsRol.COORDINADOR_CALIDAD, SOFASA);

        Assert.Equal(SOFASA, coordinador.ClienteDelFiltro(null));
        Assert.Equal(SOFASA, coordinador.ClienteDelFiltro(SOFASA));
        Assert.Throws<clsProhibidoException>(() => coordinador.ClienteDelFiltro(OTRO));
    }

    [Fact]
    public async Task Los_reportes_cuentan_solo_las_unidades_del_cliente_de_quien_consulta()
    {
        var consultas = new clsConsultasReportesFalsas();

        await new clsReportesService(consultas, Usuario(clsRol.COORDINADOR_CALIDAD, SOFASA)).ObtenerTasaRechazoAsync();
        Assert.Equal(SOFASA, consultas.UltimoCliente);
        await new clsReportesService(consultas, Usuario(clsRol.ADMIN, SOFASA)).ObtenerTasaRechazoAsync();
        Assert.Equal(SOFASA, consultas.UltimoCliente);
        await new clsReportesService(consultas, Usuario(clsRol.SUPER_ADMIN, null)).ObtenerTasaRechazoAsync();
        Assert.Null(consultas.UltimoCliente);
    }
}
