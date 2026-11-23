using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>La firma del cargue por niñera en la transferencia de cada carro: el momento y el GPS de cada firma, las reservas de quien recibe, los reenvios y la constancia de no firma de toda la niñera.</summary>
public class FirmaPorNineraTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 18, 19, 30, 0, TimeSpan.Zero);
    private static readonly MomentoDeFirma MOMENTO = new(new DateTimeOffset(2026, 11, 18, 14, 9, 0, TimeSpan.FromHours(-5)), 6.2, -75.5, 7.5);
    private readonly Guid _entrega = Guid.NewGuid();
    private readonly Guid _recibe = Guid.NewGuid();

    private clsTransferenciaCustodia Nueva() => clsTransferenciaCustodia.Crear(Guid.NewGuid(), _entrega, _recibe, AHORA);

    [Fact]
    public void Cada_firma_guarda_su_propio_momento_y_gps_ademas_de_cuando_llego_y_con_las_dos_cierra_firmada()
    {
        var t = Nueva();
        var otro = new MomentoDeFirma(MOMENTO.Momento.AddMinutes(5), 6.21, -75.51, null);

        t.FirmarPorNinera(_entrega, "trazo-a", null, MOMENTO, "10.0.0.1", AHORA);
        t.FirmarPorNinera(_recibe, "trazo-b", null, otro, "10.0.0.2", AHORA.AddMinutes(3));

        Assert.True(t.FirmaPorNinera);
        Assert.Equal(clsTipoCierre.FIRMADA, t.TipoCierre);
        Assert.Equal(MOMENTO.Momento, t.FirmaEntregaMomentoAt);
        Assert.Equal((6.2, -75.5, 7.5), (t.FirmaEntregaLat, t.FirmaEntregaLng, t.FirmaEntregaPrecisionM));
        Assert.Equal(otro.Momento, t.FirmaRecibeMomentoAt);
        Assert.Equal((6.21, -75.51), (t.FirmaRecibeLat, t.FirmaRecibeLng));
        Assert.Null(t.FirmaRecibePrecisionM);
        // El momento no es la hora de llegada al servidor: esa se guarda aparte.
        Assert.Equal(AHORA, t.FirmaEntregaAt);
        Assert.Equal(AHORA.AddMinutes(3), t.FirmaRecibeAt);
    }

    [Fact]
    public void Quien_recibe_con_reservas_cierra_con_reservas_y_solo_el_las_deja()
    {
        var t = Nueva();
        t.FirmarPorNinera(_entrega, "a", null, MOMENTO, null, AHORA);

        Assert.Throws<clsProhibidoException>(() => Nueva().FirmarPorNinera(_entrega, "a", "Algo", MOMENTO, null, AHORA));
        t.FirmarPorNinera(_recibe, "b", "Rayón en la puerta", MOMENTO, null, AHORA);

        // Las reservas son nuevas: la firma de quien entrega se retiro y el carro sigue abierto hasta que firme otra vez.
        Assert.False(t.EstaCerrada);
        Assert.Equal("Rayón en la puerta", t.ReservasTexto);
        Assert.Null(t.FirmaEntrega);
        Assert.Null(t.FirmaEntregaMomentoAt);
        t.FirmarPorNinera(_entrega, "a2", null, MOMENTO with { Momento = MOMENTO.Momento.AddMinutes(10) }, null, AHORA.AddMinutes(10));
        Assert.Equal(clsTipoCierre.CON_RESERVAS, t.TipoCierre);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Las_reservas_sin_texto_no_se_admiten(string reservas)
    {
        var ex = Assert.Throws<clsValidacionException>(() => Nueva().FirmarPorNinera(_recibe, "b", reservas, MOMENTO, null, AHORA));

        Assert.Contains("reservas", ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys);
    }

    [Fact]
    public void Nadie_firma_por_otro()
    {
        Assert.Throws<clsProhibidoException>(() => Nueva().FirmarPorNinera(Guid.NewGuid(), "x", null, MOMENTO, null, AHORA));
    }

    [Fact]
    public void Reenviar_la_misma_firma_no_cambia_nada_aunque_ya_este_cerrada_y_una_firma_distinta_sobre_una_cerrada_no_cambia_el_trazo()
    {
        var t = Nueva();
        t.FirmarPorNinera(_entrega, "a", null, MOMENTO, "10.0.0.1", AHORA);
        t.FirmarPorNinera(_recibe, "b", null, MOMENTO, "10.0.0.2", AHORA);
        var cierre = t.UpdatedAt;

        t.FirmarPorNinera(_entrega, "a", null, MOMENTO, "10.0.0.1", AHORA.AddHours(1));
        t.FirmarPorNinera(_entrega, "otro-trazo", null, MOMENTO with { Momento = MOMENTO.Momento.AddHours(1) }, "10.0.0.9", AHORA.AddHours(1));

        Assert.Equal(cierre, t.UpdatedAt);
        Assert.Equal("a", t.FirmaEntrega);
        Assert.Equal("10.0.0.1", t.FirmaEntregaIp);
        Assert.Equal(clsTipoCierre.FIRMADA, t.TipoCierre);
    }

    [Fact]
    public void Quien_recibe_no_cambia_las_reservas_de_un_carro_ya_cerrado()
    {
        var t = Nueva();
        t.FirmarPorNinera(_entrega, "a", null, MOMENTO, null, AHORA);
        t.FirmarPorNinera(_recibe, "b", null, MOMENTO, null, AHORA);

        Assert.Throws<clsConflictoException>(() => t.FirmarPorNinera(_recibe, "b2", "Ahora si hay un golpe", MOMENTO with { Momento = MOMENTO.Momento.AddMinutes(1) }, null, AHORA));
        Assert.Null(t.ReservasTexto);
    }

    [Fact]
    public void La_constancia_de_no_firma_de_la_ninera_guarda_el_momento_de_quien_la_deja_y_marca_la_ninera()
    {
        var t = Nueva();

        t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "Se niega a firmar", Guid.NewGuid(), Guid.NewGuid(), AHORA, MOMENTO, porNinera: true);

        Assert.Equal(clsTipoCierre.NO_FIRMA, t.TipoCierre);
        Assert.True(t.FirmaPorNinera);
        Assert.Equal(clsParteFirmante.RECIBE, t.NoFirmaParte);
        Assert.Equal(MOMENTO.Momento, t.FirmaEntregaMomentoAt);
        Assert.Null(t.FirmaRecibeMomentoAt);
    }

    [Fact]
    public void La_constancia_sin_ninera_no_marca_la_ninera_ni_guarda_momento()
    {
        var t = Nueva();

        t.RegistrarNoFirma(_entrega, clsParteFirmante.RECIBE, "Se niega a firmar", Guid.NewGuid(), Guid.NewGuid(), AHORA);

        Assert.False(t.FirmaPorNinera);
        Assert.Null(t.FirmaEntregaMomentoAt);
    }
}
