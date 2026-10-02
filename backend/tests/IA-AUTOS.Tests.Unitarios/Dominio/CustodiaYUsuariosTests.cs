using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Tests.Unitarios.Dominio;

public class TransferenciaCustodiaTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _entrega = Guid.NewGuid();
    private readonly Guid _recibe = Guid.NewGuid();

    [Fact]
    public void Quien_entrega_y_quien_recibe_deben_ser_distintos()
    {
        Assert.Throws<clsValidacionException>(() => clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _entrega, "f1", "f2", AHORA));
    }

    [Fact]
    public void Firma_parcial_no_sella_la_fecha()
    {
        var t = clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _recibe, "firma-entrega.png", null, AHORA);

        Assert.False(t.EstaCompleta);
        Assert.Null(t.FirmadaAt);
    }

    [Fact]
    public void Ambas_firmas_sellan_la_fecha()
    {
        var t = clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _recibe, "a.png", "b.png", AHORA);

        Assert.True(t.EstaCompleta);
        Assert.Equal(AHORA, t.FirmadaAt);
    }

    [Fact]
    public void Aplicar_firma_acumula_y_sella_al_completar()
    {
        var t = clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _recibe, "a.png", null, AHORA);

        t.AplicarFirma(_entrega, _recibe, null, "b.png", AHORA.AddMinutes(3));

        Assert.Equal("a.png", t.FirmaEntrega);
        Assert.Equal("b.png", t.FirmaRecibe);
        Assert.Equal(AHORA.AddMinutes(3), t.FirmadaAt);
    }

    [Fact]
    public void Mientras_esta_incompleta_se_puede_corregir_la_firma()
    {
        var t = clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _recibe, "a.png", null, AHORA);

        t.AplicarFirma(_entrega, _recibe, "a2.png", null, AHORA.AddMinutes(1));

        Assert.Equal("a2.png", t.FirmaEntrega);
        Assert.Null(t.FirmadaAt);
    }

    [Fact]
    public void Transferencia_completa_es_inmutable_pero_acepta_el_reintento_identico()
    {
        var t = clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _recibe, "a.png", "b.png", AHORA);

        t.AplicarFirma(_entrega, _recibe, "a.png", "b.png", AHORA.AddHours(1));
        Assert.Equal(AHORA, t.FirmadaAt);

        Assert.Throws<clsConflictoException>(() => t.AplicarFirma(_entrega, _recibe, "otra.png", null, AHORA));
        Assert.Throws<clsConflictoException>(() => t.AplicarFirma(_recibe, _entrega, "a.png", "b.png", AHORA));
    }
}

public class UsuarioTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static clsUsuario Nuevo(string rol = clsRol.INSPECTOR) =>
        clsUsuario.Crear("Ana Perez", "  Ana.Perez@Ejemplo.COM ", rol, null, "scrypt:aa:bb", AHORA);

    [Fact]
    public void Crear_normaliza_el_correo_y_queda_activo()
    {
        var u = Nuevo();

        Assert.Equal("ana.perez@ejemplo.com", u.Email);
        Assert.True(u.Activo);
    }

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("a@b")]
    [InlineData("")]
    public void Crear_rechaza_correos_invalidos(string email)
    {
        Assert.Throws<clsValidacionException>(() => clsUsuario.Crear("X", email, clsRol.ADMIN, null, "h", AHORA));
    }

    [Fact]
    public void Crear_rechaza_rol_o_subtipo_fuera_del_catalogo()
    {
        Assert.Throws<clsValidacionException>(() => Nuevo("gerente"));
        Assert.Throws<clsValidacionException>(() => clsUsuario.Crear("X", "x@y.co", clsRol.INSPECTOR, "raro", "h", AHORA));
    }

    [Fact]
    public void Actualizar_conserva_los_campos_no_enviados()
    {
        var u = Nuevo();

        u.Actualizar(null, null, clsRol.CONDUCTOR, null, null, null, AHORA.AddDays(1));

        Assert.Equal(clsRol.CONDUCTOR, u.Rol);
        Assert.Equal("Ana Perez", u.Nombre);
        Assert.Equal("scrypt:aa:bb", u.PasswordHash);
        Assert.True(u.Activo);
    }

    [Fact]
    public void Retirar_hace_baja_logica()
    {
        var u = Nuevo();

        u.Retirar(AHORA);

        Assert.False(u.Activo);
    }

    [Fact]
    public void No_puede_eliminarse_a_si_mismo()
    {
        var u = Nuevo();

        Assert.Throws<clsConflictoException>(() => u.AsegurarPuedeSerEliminadoPor(u.Id));
        u.AsegurarPuedeSerEliminadoPor(Guid.NewGuid());
    }

    [Fact]
    public void Super_admin_esta_incluido_en_administracion_pero_no_en_escritura_de_campo()
    {
        Assert.Contains(clsRol.SUPER_ADMIN, clsRol.ADMINISTRACION);
        Assert.Contains(clsRol.ADMIN, clsRol.ADMINISTRACION);
        Assert.DoesNotContain(clsRol.SUPER_ADMIN, clsRol.ESCRITURA);
        Assert.Equal([clsRol.SUPER_ADMIN], clsRol.GESTION_USUARIOS);
    }
}
