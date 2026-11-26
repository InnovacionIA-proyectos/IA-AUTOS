using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>La firma de la entrega por parada: hora y GPS del momento (que pueden faltar), reservas por carro, el rechazo por novedad y el cierre con una sola firma con su motivo codificado.</summary>
public class EntregaPorParadaTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 18, 15, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MOMENTO = new(2026, 11, 18, 14, 10, 0, TimeSpan.FromHours(-5));
    private readonly Guid _entrega = Guid.NewGuid();
    private readonly Guid _recibe = Guid.NewGuid();

    private clsTransferenciaCustodia Nueva() => clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _recibe, AHORA);

    [Fact]
    public void La_firma_guarda_la_hora_y_el_gps_del_momento_aparte_de_cuando_llego_y_sin_gps_queda_sin_ubicacion()
    {
        var t = Nueva();

        t.FirmarConMomento(_entrega, "trazo-1", null, new MomentoDeFirma(MOMENTO, 4.71, -74.07, 8), "10.0.0.1", AHORA, porNinera: false);
        t.FirmarConMomento(_recibe, "trazo-2", null, new MomentoDeFirma(MOMENTO, null, null, null), "10.0.0.2", AHORA, porNinera: false);

        Assert.Equal(MOMENTO, t.FirmaEntregaMomentoAt);
        Assert.Equal(4.71, t.FirmaEntregaLat);
        Assert.Equal(AHORA, t.FirmaEntregaAt);
        Assert.Equal(MOMENTO, t.FirmaRecibeMomentoAt);
        Assert.Null(t.FirmaRecibeLat);
        Assert.False(t.FirmaPorNinera);
        Assert.Equal(clsTipoCierre.FIRMADA, t.TipoCierre);
    }

    [Fact]
    public void Quien_recibe_deja_reservas_y_cierra_con_reservas_y_si_quien_entrega_ya_habia_firmado_su_firma_se_retira()
    {
        var t = Nueva();
        t.FirmarConMomento(_entrega, "trazo-1", null, null, null, AHORA, porNinera: false);

        t.FirmarConMomento(_recibe, "trazo-2", "Llegó sucio y sin manual", null, null, AHORA, porNinera: false);

        Assert.Null(t.FirmaEntrega);
        Assert.False(t.EstaCerrada);
        t.FirmarConMomento(_entrega, "trazo-3", null, null, null, AHORA.AddMinutes(1), porNinera: false);
        Assert.Equal(clsTipoCierre.CON_RESERVAS, t.TipoCierre);
        Assert.Equal("Llegó sucio y sin manual", t.ReservasTexto);
    }

    [Fact]
    public void Las_reservas_de_la_persona_externa_no_se_borran_al_firmar_y_el_carro_cierra_con_reservas()
    {
        var t = clsTransferenciaCustodia.CrearConExterno(Guid.NewGuid(), _entrega, "Rosa Ríos", "52123456", AHORA);

        t.FirmarConMomento(_entrega, "trazo-1", null, null, null, AHORA, porNinera: false);
        t.FirmarComoExterno("trazo-rosa", null, AHORA, null, "Falta el manual");

        Assert.Equal(clsTipoCierre.CON_RESERVAS, t.TipoCierre);
        Assert.Equal("Falta el manual", t.ReservasTexto);
    }

    [Fact]
    public void Un_carro_rechazado_cierra_con_las_dos_firmas_unido_al_dano_y_a_quien_lo_marco_sin_contar_como_entregado()
    {
        var t = Nueva();
        var hallazgo = Guid.NewGuid();

        t.FirmarConMomento(_recibe, "trazo-2", null, new MomentoDeFirma(MOMENTO, 4.71, -74.07, null), null, AHORA, porNinera: false, rechazoHallazgoId: hallazgo);
        Assert.False(t.EstaCerrada);
        t.FirmarConMomento(_entrega, "trazo-1", null, null, null, AHORA, porNinera: false);

        Assert.Equal(clsTipoCierre.RECHAZADO, t.TipoCierre);
        Assert.True(t.EstaRechazada);
        Assert.Equal(hallazgo, t.RechazoHallazgoId);
        Assert.Equal(_recibe, t.RechazoMarcadoPor);
        Assert.Null(t.ReservasTexto);
        t.FirmarConMomento(_entrega, "otro", null, null, null, AHORA, porNinera: false);
        Assert.Equal("trazo-1", t.FirmaEntrega);
        Assert.Equal(clsTipoCierre.RECHAZADO, t.TipoCierre);
    }

    [Fact]
    public void Las_reservas_se_rechazan_si_van_vacias_o_pasan_de_mil_caracteres_y_solo_las_deja_quien_recibe()
    {
        var t = Nueva();

        Assert.Throws<clsValidacionException>(() => t.FirmarConMomento(_recibe, "trazo", "   ", null, null, AHORA, porNinera: false));
        Assert.Throws<clsValidacionException>(() => t.FirmarConMomento(_recibe, "trazo", new string('x', 1001), null, null, AHORA, porNinera: false));
        Assert.Throws<clsProhibidoException>(() => t.FirmarConMomento(_entrega, "trazo", "no le toca", null, null, AHORA, porNinera: false));
    }

    [Fact]
    public void El_cierre_con_una_sola_firma_guarda_el_motivo_codificado_el_testigo_y_el_momento()
    {
        var t = clsTransferenciaCustodia.CrearSinReceptor(Guid.NewGuid(), _entrega, AHORA);

        t.CerrarConUnaFirma(_entrega, "trazo", "No había nadie para recibir", "10.0.0.1", AHORA, new MomentoDeFirma(MOMENTO, 4.71, -74.07, 5), clsMotivoUnaFirma.NO_HABIA_NADIE, "Andrés Pardo", "79123456");

        Assert.Equal(clsTipoCierre.RECIBO_SIN_ENTREGA, t.TipoCierre);
        Assert.Equal(clsMotivoUnaFirma.NO_HABIA_NADIE, t.MotivoCodigo);
        Assert.Equal("Andrés Pardo", t.TestigoNombre);
        Assert.Equal("79123456", t.TestigoCedula);
        Assert.Equal(MOMENTO, t.FirmaEntregaMomentoAt);
        Assert.Equal(clsParteFirmante.RECIBE, t.NoFirmaParte);
    }

    [Fact]
    public void Los_motivos_codificados_tienen_su_etiqueta_para_el_titulo_del_aviso()
    {
        Assert.Equal(["no_habia_nadie", "se_nego_a_firmar", "sin_inspector_del_punto"], clsMotivoUnaFirma.VALORES.Order(StringComparer.Ordinal));
        Assert.Equal("No había nadie para recibir", clsMotivoUnaFirma.Etiqueta(clsMotivoUnaFirma.NO_HABIA_NADIE));
        Assert.Equal("Quien recibe se negó a firmar", clsMotivoUnaFirma.Etiqueta(clsMotivoUnaFirma.SE_NEGO_A_FIRMAR));
    }
}
