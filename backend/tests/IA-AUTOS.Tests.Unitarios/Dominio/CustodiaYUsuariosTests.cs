using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Tests.Unitarios.Dominio;

public class TransferenciaCustodiaTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _entrega = Guid.NewGuid();
    private readonly Guid _recibe = Guid.NewGuid();
    private readonly Guid _testigo = Guid.NewGuid();
    private readonly Guid _foto = Guid.NewGuid();

    private clsTransferenciaCustodia Nueva() => clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _recibe, AHORA);

    [Fact]
    public void Crear_exige_usuarios_distintos_y_nace_abierta_sin_firmas()
    {
        Assert.Throws<clsValidacionException>(() => clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _entrega, AHORA));

        var t = Nueva();

        Assert.Null(t.FirmaEntrega);
        Assert.Null(t.FirmaRecibe);
        Assert.Null(t.TipoCierre);
        Assert.False(t.EstaCerrada);
        Assert.False(t.EstaCompleta);
    }

    [Fact]
    public void Cada_uno_firma_su_parte_con_su_fecha_e_ip()
    {
        var t = Nueva();

        t.Firmar(_entrega, "M0 0 L10 10", "10.0.0.5", AHORA.AddMinutes(1));

        Assert.Equal("M0 0 L10 10", t.FirmaEntrega);
        Assert.Equal(AHORA.AddMinutes(1), t.FirmaEntregaAt);
        Assert.Equal("10.0.0.5", t.FirmaEntregaIp);
        Assert.Null(t.FirmaRecibe);
        Assert.Null(t.FirmaRecibeAt);
        Assert.Null(t.FirmadaAt);
        Assert.Null(t.TipoCierre);

        t.Firmar(_recibe, "M5 5 L20 20", "10.0.0.9", AHORA.AddMinutes(2));

        Assert.Equal("M5 5 L20 20", t.FirmaRecibe);
        Assert.Equal(AHORA.AddMinutes(2), t.FirmaRecibeAt);
        Assert.Equal("10.0.0.9", t.FirmaRecibeIp);
        Assert.Equal(AHORA.AddMinutes(2), t.FirmadaAt);
        Assert.Equal(clsTipoCierre.FIRMADA, t.TipoCierre);
        Assert.True(t.EstaCompleta);
    }

    [Fact]
    public void Nadie_firma_por_otro()
    {
        var t = Nueva();
        var tercero = Guid.NewGuid();

        var ex = Assert.Throws<clsProhibidoException>(() => t.Firmar(tercero, "firma", "1.1.1.1", AHORA));

        Assert.Equal(clsCodigosError.FORBIDDEN, ex.Codigo);
        Assert.Null(t.FirmaEntrega);
        Assert.Null(t.FirmaRecibe);
        Assert.Null(t.FirmaEntregaIp);
        Assert.Null(t.ParteDe(tercero));
        Assert.Equal(clsParteFirmante.ENTREGA, t.ParteDe(_entrega));
        Assert.Equal(clsParteFirmante.RECIBE, t.ParteDe(_recibe));
    }

    [Fact]
    public void La_firma_es_obligatoria_y_la_ip_se_acota()
    {
        var t = Nueva();

        Assert.Throws<clsValidacionException>(() => t.Firmar(_entrega, "", null, AHORA));

        t.Firmar(_entrega, "firma", new string('1', 80), AHORA);
        Assert.Equal(clsTransferenciaCustodia.IP_MAX, t.FirmaEntregaIp!.Length);
    }

    [Fact]
    public void Mientras_esta_abierta_cada_uno_puede_repetir_su_propia_firma()
    {
        var t = Nueva();
        t.Firmar(_entrega, "primera", "1.1.1.1", AHORA);

        t.Firmar(_entrega, "corregida", "2.2.2.2", AHORA.AddMinutes(1));

        Assert.Equal("corregida", t.FirmaEntrega);
        Assert.Equal("2.2.2.2", t.FirmaEntregaIp);
    }

    [Fact]
    public void Una_transferencia_firmada_es_inmutable_pero_acepta_el_reintento_identico()
    {
        var t = Nueva();
        t.Firmar(_entrega, "a", null, AHORA);
        t.Firmar(_recibe, "b", null, AHORA);

        t.Firmar(_entrega, "a", null, AHORA.AddHours(1));
        t.Firmar(_recibe, "b", null, AHORA.AddHours(1));

        Assert.Throws<clsConflictoException>(() => t.Firmar(_entrega, "otra", null, AHORA));
        Assert.Throws<clsConflictoException>(() => t.Firmar(_recibe, "otra", null, AHORA));
        Assert.Equal(AHORA, t.FirmadaAt);
        Assert.Equal("a", t.FirmaEntrega);
    }

    [Fact]
    public void Recibir_con_reservas_exige_las_observaciones()
    {
        var t = Nueva();
        t.Firmar(_entrega, "a", null, AHORA);

        foreach (var vacio in new string?[] { null, "", "   " })
        {
            Assert.Throws<clsValidacionException>(() => t.FirmarConReservas(_recibe, "b", vacio, null, AHORA));
        }

        Assert.Null(t.FirmaRecibe);
        Assert.Null(t.TipoCierre);
        Assert.Throws<clsValidacionException>(() => t.FirmarConReservas(_recibe, "b", new string('x', 1001), null, AHORA));
    }

    [Fact]
    public void Recibir_con_reservas_cierra_la_transferencia_con_ese_tipo()
    {
        var t = Nueva();
        t.Firmar(_entrega, "a", "1.1.1.1", AHORA);

        t.FirmarConReservas(_recibe, "b", "  El parachoques tiene un rayón  ", "2.2.2.2", AHORA.AddMinutes(3));

        Assert.Equal(clsTipoCierre.CON_RESERVAS, t.TipoCierre);
        Assert.Equal("El parachoques tiene un rayón", t.ReservasTexto);
        Assert.Equal("b", t.FirmaRecibe);
        Assert.Equal("2.2.2.2", t.FirmaRecibeIp);
        Assert.Equal(AHORA.AddMinutes(3), t.FirmadaAt);
        Assert.True(t.EstaCerrada);
        Assert.Throws<clsConflictoException>(() => t.FirmarConReservas(_recibe, "c", "otra", null, AHORA));
        Assert.Throws<clsConflictoException>(() => t.Firmar(_recibe, "c", null, AHORA));
    }

    [Fact]
    public void Las_reservas_dejadas_antes_de_que_firme_quien_entrega_cierran_cuando_esta_firma()
    {
        var t = Nueva();

        t.FirmarConReservas(_recibe, "b", "Observación", null, AHORA);
        Assert.Null(t.TipoCierre);
        t.Firmar(_entrega, "a", null, AHORA.AddMinutes(1));

        Assert.Equal(clsTipoCierre.CON_RESERVAS, t.TipoCierre);
    }

    [Fact]
    public void Si_quien_recibe_firma_normal_descarta_unas_reservas_anteriores()
    {
        var t = Nueva();
        t.FirmarConReservas(_recibe, "b", "Observación", null, AHORA);

        t.Firmar(_recibe, "b2", null, AHORA.AddMinutes(1));
        t.Firmar(_entrega, "a", null, AHORA.AddMinutes(2));

        Assert.Null(t.ReservasTexto);
        Assert.Equal(clsTipoCierre.FIRMADA, t.TipoCierre);
    }

    [Fact]
    public void Solo_quien_recibe_firma_con_reservas()
    {
        var t = Nueva();

        Assert.Throws<clsProhibidoException>(() => t.FirmarConReservas(_entrega, "a", "obs", null, AHORA));
        Assert.Throws<clsProhibidoException>(() => t.FirmarConReservas(Guid.NewGuid(), "a", "obs", null, AHORA));
    }

    [Fact]
    public void La_no_firma_exige_motivo_testigo_y_foto_y_cierra_la_transferencia()
    {
        var t = Nueva();
        t.Firmar(_entrega, "a", null, AHORA);

        Assert.Throws<clsValidacionException>(() => t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, " ", _testigo, _foto, AHORA));
        Assert.Throws<clsValidacionException>(() => t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "Se niega", null, _foto, AHORA));
        Assert.Throws<clsValidacionException>(() => t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "Se niega", _testigo, null, AHORA));
        Assert.Throws<clsValidacionException>(() => t.RegistrarNoFirma(_entrega, "otro", "Se niega", _testigo, _foto, AHORA));
        Assert.Null(t.TipoCierre);

        t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "  Se niega a firmar el recibo  ", _testigo, _foto, AHORA.AddMinutes(5));

        Assert.Equal(clsTipoCierre.NO_FIRMA, t.TipoCierre);
        Assert.Equal(clsParteFirmante.RECIBE, t.NoFirmaParte);
        Assert.Equal("Se niega a firmar el recibo", t.MotivoNoFirma);
        Assert.Equal(_testigo, t.TestigoUsuarioId);
        Assert.Equal(_foto, t.EvidenciaConstanciaId);
        Assert.Null(t.FirmadaAt);
        Assert.True(t.EstaCerrada);
    }

    [Fact]
    public void El_testigo_es_una_tercera_persona()
    {
        var t = Nueva();

        Assert.Throws<clsValidacionException>(() => t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "Se niega", _entrega, _foto, AHORA));
        Assert.Throws<clsValidacionException>(() => t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "Se niega", _recibe, _foto, AHORA));
    }

    [Fact]
    public void La_no_firma_la_registra_la_otra_parte_y_no_si_ya_firmo()
    {
        var t = Nueva();
        t.Firmar(_recibe, "b", null, AHORA);

        Assert.Throws<clsProhibidoException>(() => t.RegistrarNoFirma(_entrega, clsParteFirmante.ENTREGA, "Me niego", _testigo, _foto, AHORA));
        Assert.Throws<clsProhibidoException>(() => t.RegistrarNoFirma(Guid.NewGuid(), clsParteFirmante.RECIBE, "Se niega", _testigo, _foto, AHORA));
        Assert.Throws<clsConflictoException>(() => t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "Se niega", _testigo, _foto, AHORA));
        Assert.Null(t.TipoCierre);
    }

    [Fact]
    public void Una_transferencia_con_no_firma_es_inmutable()
    {
        var t = Nueva();
        t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "Se niega", _testigo, _foto, AHORA);

        Assert.Throws<clsConflictoException>(() => t.Firmar(_entrega, "a", null, AHORA));
        Assert.Throws<clsConflictoException>(() => t.Firmar(_recibe, "b", null, AHORA));
        Assert.Throws<clsConflictoException>(() => t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "Otra vez", _testigo, _foto, AHORA));
    }

    [Fact]
    public void Los_tipos_de_cierre_y_las_partes_son_los_del_contrato()
    {
        Assert.Equal(["con_reservas", "firmada", "no_firma"], clsTipoCierre.VALORES.Order(StringComparer.Ordinal));
        Assert.Equal(["entrega", "recibe"], clsParteFirmante.VALORES.Order(StringComparer.Ordinal));
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

        u.Actualizar(null, null, clsRol.CONDUCTOR, null, null, null, AHORA.AddDays(1), "1234567");

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
        Assert.Equal([clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD], clsRol.GESTION_USUARIOS);
    }
}
