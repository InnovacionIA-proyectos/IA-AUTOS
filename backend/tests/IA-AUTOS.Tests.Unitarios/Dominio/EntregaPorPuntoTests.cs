using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

public class EntregaPorPuntoTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _entrega = Guid.NewGuid();
    private readonly Guid _recibe = Guid.NewGuid();

    private clsTransferenciaCustodia Nueva() => clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _recibe, AHORA);

    private static string CampoDelError(clsValidacionException ex) =>
        ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys.Single();

    [Fact]
    public void Quien_entrega_cierra_con_su_firma_y_su_motivo_y_la_parte_que_no_firmo_es_quien_recibe()
    {
        var t = Nueva();

        t.CerrarConUnaFirma(_entrega, "trazo", "No hay inspector en el punto", "10.0.0.5", AHORA);

        Assert.Equal(clsTipoCierre.RECIBO_SIN_ENTREGA, t.TipoCierre);
        Assert.Equal(clsParteFirmante.RECIBE, t.NoFirmaParte);
        Assert.Equal("No hay inspector en el punto", t.MotivoNoFirma);
        Assert.Equal(_entrega, t.EntregaUsuarioId);
        Assert.Equal("trazo", t.FirmaEntrega);
        Assert.Equal("10.0.0.5", t.FirmaEntregaIp);
        Assert.Null(t.FirmaRecibe);
        Assert.Null(t.FirmaRecibeAt);
        Assert.True(t.EstaCerrada);
        Assert.False(t.EstaCompleta);
    }

    [Fact]
    public void Quien_recibe_cierra_con_su_firma_y_su_motivo_y_quien_entrega_deja_de_figurar()
    {
        var t = Nueva();

        t.CerrarConUnaFirma(_recibe, "trazo", "El conductor no esta", null, AHORA);

        Assert.Equal(clsTipoCierre.RECIBO_SIN_ENTREGA, t.TipoCierre);
        Assert.Equal(clsParteFirmante.ENTREGA, t.NoFirmaParte);
        Assert.Null(t.EntregaUsuarioId);
        Assert.Null(t.FirmaEntrega);
        Assert.Null(t.FirmaEntregaAt);
        Assert.Equal("trazo", t.FirmaRecibe);
    }

    [Fact]
    public void Quien_ya_firmo_su_parte_cierra_sin_volver_a_firmar_y_quien_no_firmo_debe_firmar()
    {
        var yaFirmo = Nueva();
        yaFirmo.Firmar(_entrega, "firma-previa", "10.0.0.5", AHORA);
        var sinFirma = Nueva();

        yaFirmo.CerrarConUnaFirma(_entrega, null, "No hay inspector en el punto", "10.0.0.6", AHORA.AddMinutes(5));
        var ex = Assert.Throws<clsValidacionException>(() => sinFirma.CerrarConUnaFirma(_entrega, null, "No hay inspector en el punto", null, AHORA));

        Assert.Equal("firma-previa", yaFirmo.FirmaEntrega);
        Assert.Equal("10.0.0.5", yaFirmo.FirmaEntregaIp);
        Assert.Equal(AHORA, yaFirmo.FirmaEntregaAt);
        Assert.Equal("firma", CampoDelError(ex));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void El_motivo_es_obligatorio(string? motivo)
    {
        var ex = Assert.Throws<clsValidacionException>(() => Nueva().CerrarConUnaFirma(_entrega, "trazo", motivo, null, AHORA));

        Assert.Equal("motivo", CampoDelError(ex));
    }

    [Fact]
    public void El_motivo_admite_como_maximo_mil_caracteres()
    {
        Nueva().CerrarConUnaFirma(_entrega, "trazo", new string('x', 1000), null, AHORA);

        var ex = Assert.Throws<clsValidacionException>(() => Nueva().CerrarConUnaFirma(_entrega, "trazo", new string('x', 1001), null, AHORA));
        Assert.Equal("motivo", CampoDelError(ex));
    }

    [Fact]
    public void Solo_las_dos_partes_cierran_y_no_se_cierra_si_la_otra_parte_ya_firmo_o_la_transferencia_ya_esta_cerrada()
    {
        var conLaOtra = Nueva();
        conLaOtra.Firmar(_recibe, "firma-recibe", null, AHORA);
        var cerrada = Nueva();
        cerrada.CerrarConUnaFirma(_entrega, "trazo", "No hay inspector en el punto", null, AHORA);
        var conEntrega = Nueva();
        conEntrega.Firmar(_entrega, "firma-entrega", null, AHORA);

        Assert.Throws<clsProhibidoException>(() => Nueva().CerrarConUnaFirma(Guid.NewGuid(), "trazo", "motivo", null, AHORA));
        Assert.Throws<clsConflictoException>(() => conLaOtra.CerrarConUnaFirma(_entrega, "trazo", "motivo", null, AHORA));
        Assert.Throws<clsConflictoException>(() => conEntrega.CerrarConUnaFirma(_recibe, "trazo", "motivo", null, AHORA));
        Assert.Throws<clsConflictoException>(() => cerrada.CerrarConUnaFirma(_entrega, "trazo", "motivo", null, AHORA));
    }

    [Fact]
    public void Cambiar_el_lugar_de_destino_de_una_unidad_solo_toca_a_esa_unidad_y_una_unidad_que_no_sale_no_lo_cambia()
    {
        var viaje = Guid.NewGuid();
        var uno = Guid.NewGuid();
        var dos = Guid.NewGuid();
        var unidad = clsViajeVehiculo.Crear(viaje, Guid.NewGuid(), AHORA, uno);
        var otra = clsViajeVehiculo.Crear(viaje, Guid.NewGuid(), AHORA, uno);
        var noSale = clsViajeVehiculo.Crear(viaje, Guid.NewGuid(), AHORA, uno);
        noSale.MarcarNoSale("Golpe", Guid.NewGuid(), Guid.NewGuid(), null, AHORA);

        unidad.CambiarLugarDestino(dos, AHORA.AddHours(1));

        Assert.Equal(dos, unidad.LugarDestinoId);
        Assert.Equal(AHORA.AddHours(1), unidad.UpdatedAt);
        Assert.Equal(uno, otra.LugarDestinoId);
        Assert.Throws<clsConflictoException>(() => noSale.CambiarLugarDestino(dos, AHORA));
        Assert.Equal(uno, noSale.LugarDestinoId);
    }

    // ----------------------------------------------------------------------------- firmante externo (S7-HU-02)

    private static clsTransferenciaCustodia ConExterno(Guid entrega) =>
        clsTransferenciaCustodia.CrearConExterno(Guid.NewGuid(), entrega, "  Ana Restrepo ", " 1 020 304 050 ", AHORA);

    [Fact]
    public void La_transferencia_de_una_persona_externa_guarda_su_nombre_y_su_cedula_sin_usuario_que_reciba()
    {
        var t = ConExterno(_entrega);

        Assert.Null(t.RecibeUsuarioId);
        Assert.True(t.RecibeEsExterno);
        Assert.Equal("Ana Restrepo", t.RecibeExternoNombre);
        Assert.Equal("1 020 304 050", t.RecibeExternoCedula);
        Assert.Equal(_entrega, t.EntregaUsuarioId);
        Assert.False(t.EstaCerrada);
        // Quien recibe no es un usuario: solo quien entrega es parte con usuario.
        Assert.Equal(clsParteFirmante.ENTREGA, t.ParteDe(_entrega));
        Assert.Null(t.ParteDe(_recibe));
    }

    [Fact]
    public void Con_la_firma_de_quien_entrega_y_la_de_la_persona_externa_la_transferencia_se_cierra_como_firmada()
    {
        var t = ConExterno(_entrega);

        t.Firmar(_entrega, "trazo-conductor", "10.0.0.5", AHORA);
        Assert.False(t.EstaCerrada);
        t.FirmarComoExterno("trazo-ana", "10.0.0.5", AHORA);

        Assert.Equal(clsTipoCierre.FIRMADA, t.TipoCierre);
        Assert.Equal("trazo-conductor", t.FirmaEntrega);
        Assert.Equal("trazo-ana", t.FirmaRecibe);
        Assert.Equal(AHORA, t.FirmadaAt);
        Assert.True(t.EstaCompleta);
    }

    [Fact]
    public void Repetir_la_misma_firma_externa_en_una_transferencia_cerrada_no_cambia_nada_y_otra_firma_es_conflicto()
    {
        var t = ConExterno(_entrega);
        t.Firmar(_entrega, "trazo-conductor", null, AHORA);
        t.FirmarComoExterno("trazo-ana", null, AHORA);

        t.FirmarComoExterno("trazo-ana", null, AHORA.AddMinutes(1));

        Assert.Equal(AHORA, t.FirmadaAt);
        Assert.Throws<clsConflictoException>(() => t.FirmarComoExterno("otro-trazo", null, AHORA.AddMinutes(1)));
    }

    [Fact]
    public void La_firma_externa_es_obligatoria_y_solo_vale_en_una_transferencia_de_una_persona_externa()
    {
        var externa = ConExterno(_entrega);
        var deUsuarios = Nueva();

        var sinFirma = Assert.Throws<clsValidacionException>(() => externa.FirmarComoExterno(null, null, AHORA));

        Assert.Equal("firma_recibe", CampoDelError(sinFirma));
        Assert.Throws<clsConflictoException>(() => deUsuarios.FirmarComoExterno("trazo", null, AHORA));
    }

    [Theory]
    [InlineData(null, "1020304050", "recibe_nombre")]
    [InlineData("  ", "1020304050", "recibe_nombre")]
    [InlineData("Al", "1020304050", "recibe_nombre")]
    [InlineData("Ana Restrepo", null, "recibe_cedula")]
    [InlineData("Ana Restrepo", "   ", "recibe_cedula")]
    [InlineData("Ana Restrepo", "sin-numero", "recibe_cedula")]
    [InlineData("Ana Restrepo", "10203#0405", "recibe_cedula")]
    public void Los_datos_de_la_persona_externa_invalidos_se_rechazan_nombrando_el_campo(string? nombre, string? cedula, string campo)
    {
        var errores = clsTransferenciaCustodia.ErroresDelExterno(nombre, cedula);

        Assert.Equal([campo], errores.Keys.ToList());
        var ex = Assert.Throws<clsValidacionException>(() => clsTransferenciaCustodia.CrearConExterno(Guid.NewGuid(), _entrega, nombre, cedula, AHORA));
        Assert.Equal(campo, CampoDelError(ex));
    }

    [Fact]
    public void Un_nombre_o_una_cedula_demasiado_largos_se_rechazan_y_los_formatos_de_documento_usuales_se_aceptan()
    {
        Assert.Equal(["recibe_nombre"], clsTransferenciaCustodia.ErroresDelExterno(new string('a', 151), "1020304050").Keys.ToList());
        Assert.Equal(["recibe_cedula"], clsTransferenciaCustodia.ErroresDelExterno("Ana Restrepo", new string('1', 31)).Keys.ToList());
        foreach (var cedula in new[] { "1020304050", "1 020 304 050", "79.123.456", "1-020-304", "AB123456" })
        {
            Assert.Empty(clsTransferenciaCustodia.ErroresDelExterno("Ana Restrepo", cedula));
        }

        Assert.Equal(["recibe_cedula", "recibe_nombre"], clsTransferenciaCustodia.ErroresDelExterno(null, null).Keys.Order().ToList());
    }

    [Fact]
    public void Una_entrega_sin_quien_reciba_solo_se_cierra_con_la_firma_de_quien_entrega_y_su_motivo()
    {
        var t = clsTransferenciaCustodia.CrearSinReceptor(Guid.NewGuid(), _entrega, AHORA);

        Assert.Null(t.RecibeUsuarioId);
        Assert.False(t.RecibeEsExterno);
        t.CerrarConUnaFirma(_entrega, "trazo", "El concesionario cerró antes de la llegada", null, AHORA);

        Assert.Equal(clsTipoCierre.RECIBO_SIN_ENTREGA, t.TipoCierre);
        Assert.Equal(clsParteFirmante.RECIBE, t.NoFirmaParte);
        Assert.Equal("El concesionario cerró antes de la llegada", t.MotivoNoFirma);
        Assert.Null(t.ParteDe(_recibe));
    }
}
