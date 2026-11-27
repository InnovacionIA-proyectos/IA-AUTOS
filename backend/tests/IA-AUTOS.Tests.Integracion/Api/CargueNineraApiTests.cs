using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IAAutos.Aplicacion.Alertas;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El cargue se firma por NINERA: una sola firma de cada parte (el inspector del viaje entrega, el conductor recibe) para todos los carros activos, con la hora y el GPS del momento; las reservas de quien
/// recibe van por carro; si una parte se niega, la otra deja la constancia de toda la niñera; exige el cargue completo de cada carro (409 cargue_incompleto); la firma carro por carro del cargue se rechaza en sus tres rutas
/// (409 firma_por_ninera); y un aviso recuerda la firma que falta pasados los minutos del parametro.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed partial class CargueNineraApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string MOMENTO = "2026-11-18T14:09:00-05:00";

    private static readonly string[] VINES = ["9FB45RFL8NM301001", "9FB45RFL8NM301002"];

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>El viaje con dos carros activos, su inspector y su conductor, con el cargue de cada carro completo (llegada y salida cerradas).</summary>
    private sealed record Escenario(Guid Viaje, Guid[] Vehiculos, Guid[][] Etapas, Guid[] Salidas, Guid[] Llegadas, Sesion Inspector, Sesion Conductor, Sesion Coordinador, Sesion Admin);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync(bool cargueCompleto = true)
    {
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var viaje = await Sembrador.ViajeAsync(conductor.UsuarioId, inspector.UsuarioId, estado: "en_curso", codigoMtrack: "M-NINERA");
        var vehiculos = new List<Guid>();
        var etapas = new List<Guid[]>();
        var salidas = new List<Guid>();
        var llegadas = new List<Guid>();
        foreach (var vin in VINES)
        {
            var (vehiculo, ruta) = await Sembrador.VehiculoConRutaAsync(vin, viajeId: viaje);
            vehiculos.Add(vehiculo);
            etapas.Add(ruta);
            llegadas.Add(await Sembrador.InformeAsync(ruta[0], inspector.UsuarioId, cerrada: cargueCompleto, momento: "llegada"));
            salidas.Add(await Sembrador.InformeAsync(ruta[0], inspector.UsuarioId, cerrada: cargueCompleto, momento: "salida"));
        }

        return new Escenario(viaje, [.. vehiculos], [.. etapas], [.. salidas], [.. llegadas], inspector, conductor, coordinador, admin);
    }

    private Task<HttpResponseMessage> FirmarAsync(Sesion quien, Guid viaje, string parte, IEnumerable<object>? reservas = null, string? fecha = MOMENTO, double? lat = 6.2, string firma = "M0 0 L10 10") =>
        Como(quien).PostAsJsonAsync($"/api/viajes/{viaje}/cargue/firma", new Dictionary<string, object?>
        {
            ["firma"] = firma, ["parte"] = parte, ["fecha_momento"] = fecha, ["lat"] = lat, ["lng"] = -75.5, ["precision_m"] = 7.5, ["reservas"] = reservas?.ToList(),
        });

    private static object Reserva(string vin, string observaciones) => new { vin, observaciones };

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var r = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await JsonAsync(r);
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    private async Task<HttpResponseMessage> NoFirmaAsync(Sesion quien, Guid viaje, string parteQueSeNego, Guid testigo, string motivo = "Se niega a firmar el cargue")
    {
        var (ruta, hash) = await SubirAsync(quien);
        return await Como(quien).PostAsJsonAsync($"/api/viajes/{viaje}/cargue/no-firma", new Dictionary<string, object?>
        {
            ["parte"] = parteQueSeNego, ["motivo"] = motivo, ["testigo_usuario_id"] = testigo,
            ["evidencia"] = new { storage_path = ruta, sello_fecha = MOMENTO, sello_lat = 6.2, sello_lng = -75.5, precision = 4.5, dispositivo_id = "tablet-1", hash_sha256 = hash },
            ["fecha_momento"] = MOMENTO, ["lat"] = 6.2, ["lng"] = -75.5, ["precision_m"] = 7.5,
        });
    }

    private Task<string?> TipoDeCierreAsync(string vin) =>
        Sembrador.EscalarAsync(
            "SELECT t.TipoCierre FROM dbo.tblTransferenciasCustodia t JOIN dbo.tblInspecciones i ON i.Id = t.InspeccionId JOIN dbo.tblEtapas e ON e.Id = i.EtapaId JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = @v AND i.Momento = 'salida'",
            ("@v", vin)).ContinueWith(t => t.Result as string);

    private async Task<JsonElement> ViajeAsync(Sesion quien, Guid viaje) => await JsonAsync(await Como(quien).GetAsync($"/api/viajes/{viaje}"));

    // ------------------------------------------------------------------------------------------------ la firma

    [Fact]
    public async Task Las_dos_partes_firman_una_vez_y_cada_carro_cierra_firmado_con_el_momento_y_el_gps_de_cada_firma()
    {
        var e = await EscenarioAsync();

        var entrega = await FirmarAsync(e.Inspector, e.Viaje, "entrega");

        Assert.Equal(HttpStatusCode.OK, entrega.StatusCode);
        var primera = await JsonAsync(entrega);
        Assert.Equal(2, primera.GetProperty("unidades").GetInt32());
        Assert.Equal(0, primera.GetProperty("cerradas").GetInt32());
        Assert.Equal(JsonValueKind.Null, primera.GetProperty("cargue_firmado_por_ninera_at").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("cargue_firmado_por_ninera_at").ValueKind);

        var recibe = await FirmarAsync(e.Conductor, e.Viaje, "recibe");

        Assert.Equal(HttpStatusCode.OK, recibe.StatusCode);
        var segunda = await JsonAsync(recibe);
        Assert.Equal(2, segunda.GetProperty("cerradas").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, segunda.GetProperty("cargue_firmado_por_ninera_at").ValueKind);
        Assert.All(segunda.GetProperty("detalle").EnumerateArray(), d =>
        {
            Assert.Equal("firmada", d.GetProperty("tipo_cierre").GetString());
            Assert.True(d.GetProperty("por_ninera").GetBoolean());
        });
        Assert.Equal(2, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada' AND FirmaPorNinera = 1 AND FirmaEntregaAt IS NOT NULL AND FirmaRecibeAt IS NOT NULL "
            + "AND FirmaEntregaMomentoAt IS NOT NULL AND FirmaRecibeMomentoAt IS NOT NULL AND FirmaEntregaLat = 6.2 AND FirmaEntregaLng = -75.5 AND FirmaRecibePrecisionM = 7.5 AND ReservasTexto IS NULL"));
        // El momento es el que envio el cliente, no el de llegada al servidor.
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE CONVERT(varchar(19), SWITCHOFFSET(FirmaEntregaMomentoAt, '-05:00'), 126) = '2026-11-18T14:09:00'"));
        var viaje = await ViajeAsync(e.Coordinador, e.Viaje);
        Assert.NotEqual(JsonValueKind.Null, viaje.GetProperty("cargue_firmado_por_ninera_at").ValueKind);
        Assert.Equal(JsonValueKind.Null, viaje.GetProperty("cargue_no_firmado_at").ValueKind);
        Assert.Equal(4, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'transferencia.firmar_ninera'"));
    }

    [Fact]
    public async Task Las_reservas_van_por_carro_y_reabren_la_firma_de_quien_entrega_solo_en_los_carros_con_reservas()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);

        var recibe = await FirmarAsync(e.Conductor, e.Viaje, "recibe", [Reserva(VINES[0], "Rayón en la puerta izquierda")]);

        Assert.Equal(HttpStatusCode.OK, recibe.StatusCode);
        var dto = await JsonAsync(recibe);
        Assert.Equal(1, dto.GetProperty("cerradas").GetInt32());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("cargue_firmado_por_ninera_at").ValueKind);
        // El carro con reservas pide otra vez la firma de quien entrega; el otro ya cerro firmado.
        Assert.Equal("abierta", dto.GetProperty("detalle").EnumerateArray().Single(d => d.GetProperty("vin").GetString() == VINES[0]).GetProperty("tipo_cierre").GetString());
        Assert.Equal("firmada", dto.GetProperty("detalle").EnumerateArray().Single(d => d.GetProperty("vin").GetString() == VINES[1]).GetProperty("tipo_cierre").GetString());

        var otraVez = await FirmarAsync(e.Inspector, e.Viaje, "entrega", fecha: "2026-11-18T14:20:00-05:00");

        Assert.Equal(HttpStatusCode.OK, otraVez.StatusCode);
        var cierre = await JsonAsync(otraVez);
        Assert.Equal(2, cierre.GetProperty("cerradas").GetInt32());
        Assert.Equal("con_reservas", await TipoDeCierreAsync(VINES[0]));
        Assert.Equal("firmada", await TipoDeCierreAsync(VINES[1]));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE ReservasTexto = N'Rayón en la puerta izquierda'"));
        Assert.NotEqual(JsonValueKind.Null, cierre.GetProperty("cargue_firmado_por_ninera_at").ValueKind);
    }

    [Fact]
    public async Task Reenviar_la_misma_firma_no_duplica_nada_aunque_ya_este_cerrado()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, "recibe")).StatusCode);
        var auditoria = await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'transferencia.firmar_ninera'");

        var igual = await FirmarAsync(e.Inspector, e.Viaje, "entrega");
        var igualRecibe = await FirmarAsync(e.Conductor, e.Viaje, "recibe");
        // Quien entrega firma otra vez con otro trazo y otra hora: el cargue ya esta cerrado y no cambia.
        var otroTrazo = await FirmarAsync(e.Inspector, e.Viaje, "entrega", fecha: "2026-11-18T15:00:00-05:00", firma: "M5 5 L20 20");

        Assert.Equal(HttpStatusCode.OK, igual.StatusCode);
        Assert.Equal(HttpStatusCode.OK, igualRecibe.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otroTrazo.StatusCode);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE FirmaEntrega = N'M0 0 L10 10'"));
        Assert.Equal(auditoria, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'transferencia.firmar_ninera'"));
    }

    [Fact]
    public async Task Si_falta_la_llegada_o_la_salida_del_cargue_de_un_carro_no_se_firma_y_el_mensaje_dice_cual()
    {
        var e = await EscenarioAsync(cargueCompleto: false);
        // El primer carro completo; al segundo le falta la salida (la acomodacion).
        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id IN (@a, @b, @c)", ("@a", e.Llegadas[0]), ("@b", e.Salidas[0]), ("@c", e.Llegadas[1]));

        var r = await FirmarAsync(e.Inspector, e.Viaje, "entrega");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var error = await JsonAsync(r);
        Assert.Equal("cargue_incompleto", error.GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal([$"Falta la salida del cargue del VIN {VINES[1]}"], error.GetProperty("details").GetProperty("faltantes").EnumerateArray().Select(x => x.GetString()).ToList());
        Assert.Contains(VINES[1], error.GetProperty("message").GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Un_carro_que_no_sale_queda_fuera_de_la_firma_y_de_lo_que_se_exige()
    {
        var e = await EscenarioAsync(cargueCompleto: false);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id IN (@a, @b)", ("@a", e.Llegadas[0]), ("@b", e.Salidas[0]));
        var zona = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Numero = 50"))!;
        var (_, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[1][1], e.Inspector.UsuarioId, zona, nivel: "v1_plus");
        await Sembrador.UnidadNoSaleAsync(e.Viaje, e.Vehiculos[1], hallazgo, e.Coordinador.UsuarioId, "Golpe en el capó");

        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);
        var recibe = await FirmarAsync(e.Conductor, e.Viaje, "recibe");

        Assert.Equal(HttpStatusCode.OK, recibe.StatusCode);
        var dto = await JsonAsync(recibe);
        Assert.Equal(1, dto.GetProperty("unidades").GetInt32());
        Assert.Equal(1, dto.GetProperty("cerradas").GetInt32());
        Assert.Equal("firmada", await TipoDeCierreAsync(VINES[0]));
        Assert.Null(await TipoDeCierreAsync(VINES[1]));
    }

    [Fact]
    public async Task Cada_parte_firma_la_suya_y_nadie_firma_por_otro_ni_firma_quien_no_es_del_viaje()
    {
        var e = await EscenarioAsync();
        var otroConductor = await SesionAsync("otro-conductor@ejemplo.com", clsRol.CONDUCTOR);

        var inspectorComoRecibe = await FirmarAsync(e.Inspector, e.Viaje, "recibe");
        var conductorComoEntrega = await FirmarAsync(e.Conductor, e.Viaje, "entrega");
        var deAjeno = await FirmarAsync(otroConductor, e.Viaje, "recibe");
        var deAdmin = await FirmarAsync(e.Admin, e.Viaje, "entrega");
        var deCoordinador = await FirmarAsync(e.Coordinador, e.Viaje, "entrega");
        var sinToken = await Cliente().PostAsJsonAsync($"/api/viajes/{e.Viaje}/cargue/firma", new { firma = "x" });
        var inexistente = await FirmarAsync(e.Inspector, Guid.NewGuid(), "entrega");

        Assert.Equal(HttpStatusCode.Forbidden, inspectorComoRecibe.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, conductorComoEntrega.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deCoordinador.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task La_firma_exige_la_parte_el_trazo_la_hora_y_el_gps_del_momento_y_las_reservas_validas()
    {
        var e = await EscenarioAsync();

        var sinHora = await FirmarAsync(e.Inspector, e.Viaje, "entrega", fecha: null);
        var horaSinZona = await FirmarAsync(e.Inspector, e.Viaje, "entrega", fecha: "2026-11-18T14:09:00");
        var sinGps = await FirmarAsync(e.Inspector, e.Viaje, "entrega", lat: null);
        var gpsFuera = await FirmarAsync(e.Inspector, e.Viaje, "entrega", lat: 95);
        var sinParte = await FirmarAsync(e.Inspector, e.Viaje, "intruso");
        var sinFirma = await FirmarAsync(e.Inspector, e.Viaje, "entrega", firma: "");
        var reservaDeQuienEntrega = await FirmarAsync(e.Inspector, e.Viaje, "entrega", [Reserva(VINES[0], "Algo")]);
        var reservaVacia = await FirmarAsync(e.Conductor, e.Viaje, "recibe", [Reserva(VINES[0], "  ")]);
        var reservaAjena = await FirmarAsync(e.Conductor, e.Viaje, "recibe", [Reserva("VINQUENOESDELVIAJE", "Rayón")]);
        var reservaRepetida = await FirmarAsync(e.Conductor, e.Viaje, "recibe", [Reserva(VINES[0], "Uno"), Reserva(VINES[0], "Dos")]);

        foreach (var (r, campo) in new[] { (sinHora, "fecha_momento"), (horaSinZona, "fecha_momento"), (sinGps, "lat"), (gpsFuera, "lat"), (sinParte, "parte"), (sinFirma, "firma"), (reservaVacia, "reservas"), (reservaAjena, "reservas"), (reservaRepetida, "reservas") })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True((await JsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty(campo, out _), campo);
        }

        Assert.Equal(HttpStatusCode.Forbidden, reservaDeQuienEntrega.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    // ------------------------------------------------------------------------------------------------ la constancia de no firma

    [Fact]
    public async Task Si_una_parte_se_niega_la_otra_deja_la_constancia_de_toda_la_ninera_y_se_avisa_al_coordinador_una_vez()
    {
        var e = await EscenarioAsync();
        var testigo = await SesionAsync("testigo@ejemplo.com", clsRol.INSPECTOR);

        var r = await NoFirmaAsync(e.Inspector, e.Viaje, "recibe", testigo.UsuarioId);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var dto = await JsonAsync(r);
        Assert.Equal(2, dto.GetProperty("cerradas").GetInt32());
        Assert.All(dto.GetProperty("detalle").EnumerateArray(), d => Assert.Equal("no_firma", d.GetProperty("tipo_cierre").GetString()));
        Assert.Equal(2, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'no_firma' AND FirmaPorNinera = 1 AND NoFirmaParte = 'recibe' AND TestigoUsuarioId IS NOT NULL AND EvidenciaConstanciaId IS NOT NULL AND FirmaEntregaMomentoAt IS NOT NULL"));
        // Una sola foto de la constancia para toda la niñera.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsConstancia = 1"));
        var viaje = await ViajeAsync(e.Coordinador, e.Viaje);
        Assert.NotEqual(JsonValueKind.Null, viaje.GetProperty("cargue_no_firmado_at").ValueKind);
        Assert.Equal("recibe", viaje.GetProperty("cargue_no_firma_parte").GetString());
        Assert.Equal(JsonValueKind.Null, viaje.GetProperty("cargue_firmado_por_ninera_at").ValueKind);
        // El coordinador y el administrador del cliente reciben un solo aviso.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_proceso' AND Canal = 'bandeja' AND UsuarioId = @u AND ViajeId = @v", ("@u", e.Coordinador.UsuarioId), ("@v", e.Viaje)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_proceso' AND Canal = 'bandeja' AND UsuarioId = @u AND ViajeId = @v", ("@u", e.Admin.UsuarioId), ("@v", e.Viaje)));

        // Reenviar la misma constancia no crea otra foto, otro cierre ni otro aviso.
        var repetida = await NoFirmaAsync(e.Inspector, e.Viaje, "recibe", testigo.UsuarioId);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsConstancia = 1"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_proceso' AND Canal = 'bandeja' AND UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)));
    }

    [Fact]
    public async Task La_constancia_la_deja_la_otra_parte_con_testigo_valido_y_el_cargue_completo_y_no_si_ya_firmo_la_que_se_nego()
    {
        var e = await EscenarioAsync();
        var testigo = await SesionAsync("testigo@ejemplo.com", clsRol.INSPECTOR);

        var propiaNegativa = await NoFirmaAsync(e.Inspector, e.Viaje, "entrega", testigo.UsuarioId);
        var deAjeno = await NoFirmaAsync(e.Admin, e.Viaje, "recibe", testigo.UsuarioId);
        var testigoInexistente = await NoFirmaAsync(e.Inspector, e.Viaje, "recibe", Guid.NewGuid());
        var sinMotivo = await NoFirmaAsync(e.Inspector, e.Viaje, "recibe", testigo.UsuarioId, motivo: "");

        Assert.Equal(HttpStatusCode.Forbidden, propiaNegativa.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, testigoInexistente.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));

        // Si quien se niega ya firmo, ya no hay negativa que registrar.
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, "recibe")).StatusCode);
        var yaFirmo = await NoFirmaAsync(e.Inspector, e.Viaje, "recibe", testigo.UsuarioId);
        Assert.Equal(HttpStatusCode.Conflict, yaFirmo.StatusCode);

        // Y con el cargue incompleto tampoco hay constancia.
        var incompleto = await EscenarioIncompletoAsync();
        var sinCargue = await NoFirmaAsync(incompleto.Inspector, incompleto.Viaje, "recibe", testigo.UsuarioId);
        Assert.Equal(HttpStatusCode.Conflict, sinCargue.StatusCode);
        Assert.Equal("cargue_incompleto", (await JsonAsync(sinCargue)).GetProperty("details").GetProperty("motivo").GetString());
    }

    // Si los dos carros ya se cerraron carro por carro antes del cambio, no queda nada pendiente de firmar: la
    // constancia de la niñera no tiene sobre qué carro colgarse y no debe reventar (hallazgo de QA en #448).
    [Fact]
    public async Task Si_todos_los_carros_ya_se_cerraron_antes_la_constancia_no_crea_nada_y_no_revienta()
    {
        var e = await EscenarioAsync();
        await Sembrador.TransferenciaAsync(e.Llegadas[0], e.Inspector.UsuarioId, e.Conductor.UsuarioId, "firmada");
        await Sembrador.TransferenciaAsync(e.Llegadas[1], e.Inspector.UsuarioId, e.Conductor.UsuarioId, "firmada");
        var testigo = await SesionAsync("testigo@ejemplo.com", clsRol.INSPECTOR);

        var r = await NoFirmaAsync(e.Inspector, e.Viaje, "recibe", testigo.UsuarioId);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsConstancia = 1"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE FirmaPorNinera = 1"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_proceso'"));
    }

    private async Task<Escenario> EscenarioIncompletoAsync()
    {
        var inspector = await SesionAsync("inspector2@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor2@ejemplo.com", clsRol.CONDUCTOR);
        var viaje = await Sembrador.ViajeAsync(conductor.UsuarioId, inspector.UsuarioId, estado: "en_curso", codigoMtrack: "M-INCOMPLETO");
        var (vehiculo, ruta) = await Sembrador.VehiculoConRutaAsync("9FB45RFL8NM309999", viajeId: viaje);
        var llegada = await Sembrador.InformeAsync(ruta[0], inspector.UsuarioId, cerrada: true, momento: "llegada");
        return new Escenario(viaje, [vehiculo], [ruta], [], [llegada], inspector, conductor, inspector, inspector);
    }

    // ------------------------------------------------------------------------------------------------ el cargue no se firma carro por carro

    [Theory]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    [InlineData(clsRol.ADMIN)]
    public async Task La_firma_carro_por_carro_del_cargue_se_rechaza_en_sus_tres_rutas_para_todos_los_roles(string rol)
    {
        var e = await EscenarioAsync();
        var quien = rol switch { clsRol.INSPECTOR => e.Inspector, clsRol.CONDUCTOR => e.Conductor, _ => e.Admin };
        var testigo = await SesionAsync("testigo@ejemplo.com", clsRol.INSPECTOR);
        var (ruta, hash) = await SubirAsync(quien);
        var evidencia = new { storage_path = ruta, sello_fecha = MOMENTO, sello_lat = 6.2, sello_lng = -75.5, precision = 4.5, dispositivo_id = "tablet-1", hash_sha256 = hash };

        foreach (var inspeccion in new[] { e.Salidas[0], e.Llegadas[0] })
        {
            var firma = await Como(quien).PostAsJsonAsync($"/api/inspecciones/{inspeccion}/transferencia", new { entrega_usuario_id = e.Inspector.UsuarioId, recibe_usuario_id = e.Conductor.UsuarioId, firma = "A" });
            var reservas = await Como(quien).PostAsJsonAsync($"/api/inspecciones/{inspeccion}/transferencia/reservas", new { firma = "B", reservas = "Rayón" });
            var noFirma = await Como(quien).PostAsJsonAsync($"/api/inspecciones/{inspeccion}/transferencia/no-firma", new { parte = "recibe", motivo = "No quiso", testigo_usuario_id = testigo.UsuarioId, evidencia });

            foreach (var r in new[] { firma, reservas, noFirma })
            {
                // El admin ni siquiera es parte de la transferencia: para el el rechazo puede ser el de roles; para el inspector y el conductor es el del cargue por niñera.
                if (rol == clsRol.ADMIN && r.StatusCode == HttpStatusCode.Forbidden)
                {
                    continue;
                }

                Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
                Assert.Equal("firma_por_ninera", (await JsonAsync(r)).GetProperty("details").GetProperty("motivo").GetString());
            }
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Lo_que_se_firmo_carro_por_carro_antes_se_sigue_leyendo_y_la_firma_por_ninera_no_lo_toca()
    {
        var e = await EscenarioAsync();
        // El cargue del primer carro ya se cerro carro por carro, antes del cambio (su transferencia cuelga de la llegada).
        await Sembrador.TransferenciaAsync(e.Llegadas[0], e.Inspector.UsuarioId, e.Conductor.UsuarioId, "firmada");

        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);
        var cierre = await FirmarAsync(e.Conductor, e.Viaje, "recibe");

        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        var dto = await JsonAsync(cierre);
        Assert.Equal(2, dto.GetProperty("cerradas").GetInt32());
        var anterior = dto.GetProperty("detalle").EnumerateArray().Single(d => d.GetProperty("vin").GetString() == VINES[0]);
        Assert.False(anterior.GetProperty("por_ninera").GetBoolean());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE FirmaPorNinera = 1"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @i", ("@i", e.Llegadas[0])));
    }

    // ------------------------------------------------------------------------------------------------ la etapa firmada ya no se corrige

    [Fact]
    public async Task Con_la_acomodacion_firmada_ya_no_se_corrige_la_llegada_del_cargue_aunque_la_llegada_no_tenga_firma_propia()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);

        var llegada = await Como(e.Inspector).PostAsync($"/api/inspecciones/{e.Llegadas[0]}/corregir", null);
        var salida = await Como(e.Inspector).PostAsync($"/api/inspecciones/{e.Salidas[0]}/corregir", null);
        var admin = await Como(e.Admin).PostAsync($"/api/inspecciones/{e.Llegadas[0]}/corregir", null);

        foreach (var r in new[] { llegada, salida, admin })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("INSPECCION_YA_FIRMADA", error.GetProperty("code").GetString());
            Assert.Contains("Esta etapa ya fue firmada", error.GetProperty("message").GetString());
        }
    }

    // ------------------------------------------------------------------------------------------------ los avisos

    private async Task<T> ConProcesosAsync<T>(Func<clsAlertasPeriodicas, Task<T>> accion)
    {
        using var alcance = Fabrica.Services.CreateScope();
        return await accion(alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>());
    }

    private Task EnvejecerAsync(Guid inspeccion, int minutos) =>
        Sembrador.EjecutarAsync(
            "ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL; UPDATE dbo.tblInspecciones SET CerradaAt = DATEADD(MINUTE, -@m, SYSDATETIMEOFFSET()) WHERE Id = @i; ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL",
            ("@m", minutos), ("@i", inspeccion));

    private Task<int> AvisosAsync(string tipo, Guid? usuario = null) =>
        Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = @t AND Canal = 'bandeja' AND (@u IS NULL OR UsuarioId = @u)", ("@t", tipo), ("@u", (object?)usuario ?? DBNull.Value));

    [Fact]
    public async Task Pasados_los_minutos_del_parametro_cada_parte_que_no_ha_firmado_recibe_un_aviso_con_sus_carros_y_no_se_repite()
    {
        var e = await EscenarioAsync();
        foreach (var salida in e.Salidas)
        {
            await EnvejecerAsync(salida, 40);
        }

        // La llegada del cargue sin transferencia propia ya no avisa como «transferencia pendiente»: de eso avisa la firma del cargue.
        foreach (var llegada in e.Llegadas)
        {
            await EnvejecerAsync(llegada, 400);
        }

        var primera = await ConProcesosAsync(p => p.EjecutarAvisosAsync());
        var segunda = await ConProcesosAsync(p => p.EjecutarAvisosAsync());

        // Cada parte avisa a quien le falta firmar y al coordinador y al administrador del cliente: 2 partes por 3 destinatarios.
        Assert.Equal(6, primera);
        Assert.Equal(6, segunda);
        Assert.Equal(1, await AvisosAsync("firma_cargue_pendiente", e.Inspector.UsuarioId));
        Assert.Equal(1, await AvisosAsync("firma_cargue_pendiente", e.Conductor.UsuarioId));
        Assert.Equal(2, await AvisosAsync("firma_cargue_pendiente", e.Coordinador.UsuarioId));
        Assert.Equal(2, await AvisosAsync("firma_cargue_pendiente", e.Admin.UsuarioId));
        Assert.Equal(0, await AvisosAsync("transferencia_pendiente"));
        var mensaje = (string)(await Sembrador.EscalarAsync("SELECT TOP 1 Mensaje FROM dbo.tblNotificaciones WHERE Tipo = 'firma_cargue_pendiente' AND UsuarioId = @u", ("@u", e.Inspector.UsuarioId)))!;
        Assert.Contains(VINES[0], mensaje);
        Assert.Contains(VINES[1], mensaje);

        // Cuando el inspector firma, deja de avisarsele; al conductor sigue faltandole.
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);
        await Sembrador.EjecutarAsync("ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL; DELETE FROM dbo.tblNotificaciones; ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL");
        await ConProcesosAsync(p => p.EjecutarAvisosAsync());
        Assert.Equal(0, await AvisosAsync("firma_cargue_pendiente", e.Inspector.UsuarioId));
        Assert.Equal(1, await AvisosAsync("firma_cargue_pendiente", e.Conductor.UsuarioId));
        Assert.Equal(1, await AvisosAsync("firma_cargue_pendiente", e.Coordinador.UsuarioId));

        // Con las dos firmas no avisa a nadie.
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, "recibe")).StatusCode);
        await Sembrador.EjecutarAsync("ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL; DELETE FROM dbo.tblNotificaciones; ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL");
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));
    }

    [Fact]
    public async Task Antes_de_los_minutos_del_parametro_no_avisa_y_el_valor_se_ajusta_sin_desplegar()
    {
        var e = await EscenarioAsync();
        foreach (var salida in e.Salidas)
        {
            await EnvejecerAsync(salida, 10);
        }

        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));

        await CambiarParametroAsync("firma_cargue_pendiente_minutos", "5");
        Assert.Equal(6, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));
    }

    [Fact]
    public async Task Con_la_firma_de_quien_recibe_pendiente_coordinacion_y_administracion_reciben_un_aviso_del_viaje_con_todos_los_carros_y_no_se_repite()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        foreach (var salida in e.Salidas)
        {
            await EnvejecerAsync(salida, 40);
        }

        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);
        await ConProcesosAsync(p => p.EjecutarFirmasDelCarguePendientesAsync());
        await ConProcesosAsync(p => p.EjecutarFirmasDelCarguePendientesAsync());

        foreach (var destinatario in new[] { e.Coordinador, e.Admin, e.Conductor })
        {
            Assert.Equal(1, await AvisosAsync("firma_cargue_pendiente", destinatario.UsuarioId));
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'firma_cargue_pendiente' AND Canal = 'bandeja' AND UsuarioId = @u AND ViajeId = @v AND JSON_VALUE(Datos, '$.parte_que_falta') = 'recibe' AND JSON_QUERY(Datos, '$.vines') IS NOT NULL AND (SELECT COUNT(*) FROM OPENJSON(Datos, '$.vines')) = 2",
                ("@u", destinatario.UsuarioId), ("@v", e.Viaje)));
        }

        Assert.Equal(0, await AvisosAsync("firma_cargue_pendiente", e.Inspector.UsuarioId));
        Assert.Equal(0, await AvisosAsync("firma_cargue_pendiente", coordinadorOtro.UsuarioId));
    }

    [Fact]
    public async Task Un_viaje_sin_inspector_asignado_tambien_avisa_a_coordinacion_y_administracion()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET InspectorId = NULL WHERE Id = @v", ("@v", e.Viaje));
        foreach (var salida in e.Salidas)
        {
            await EnvejecerAsync(salida, 40);
        }

        await ConProcesosAsync(p => p.EjecutarFirmasDelCarguePendientesAsync());

        foreach (var destinatario in new[] { e.Coordinador, e.Admin })
        {
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'firma_cargue_pendiente' AND Canal = 'bandeja' AND UsuarioId = @u AND JSON_VALUE(Datos, '$.parte_que_falta') = 'entrega'", ("@u", destinatario.UsuarioId)));
        }

        Assert.Equal(0, await AvisosAsync("firma_cargue_pendiente", e.Inspector.UsuarioId));
    }

    // ------------------------------------------------------------------------------------------------ el informe de la niñera en PDF

    /// <summary>El texto del PDF en una sola linea (las lineas que se parten por el ancho se vuelven a unir).</summary>
    private static string Plano(byte[] pdf) =>
        string.Join(' ', TextoDeLinea().Matches(Encoding.Latin1.GetString(pdf)).Select(m => Desescapar(m.Groups[1].Value)));

    /// <summary>El texto de una linea del PDF: los acentos y simbolos llegan como secuencias octales (Windows-1252) y los parentesis y la barra, con barra invertida.</summary>
    private static string Desescapar(string linea) =>
        Regex.Replace(linea, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString())
            .Replace("\u0097", "—").Replace(@"\(", "(").Replace(@"\)", ")").Replace(@"\\", @"\");

    [GeneratedRegex(@"\(((?:\\.|[^\\)])*)\) Tj")]
    private static partial Regex TextoDeLinea();

    private async Task<byte[]> PdfAsync(Sesion quien, Guid viaje)
    {
        var r = await Como(quien).GetAsync($"/api/viajes/{viaje}/ninera/pdf");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        return bytes;
    }

    /// <summary>
    /// El viaje con dos carros y el cargue completo: el primero con un daño en su llegada (con la nota del inspector), su puesto y una foto de la acomodacion; el segundo no sale del viaje por su propio daño.
    /// Las inspecciones del cargue quedan cerradas.
    /// </summary>
    private async Task<Escenario> EscenarioConDanosAsync()
    {
        var e = await EscenarioAsync(cargueCompleto: false);
        var dano = await Sembrador.HallazgoEnFrenteAsync(e.Llegadas[0], "v2");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblHallazgos SET Nota = N'Rayón profundo en el capó' WHERE Id = @h", ("@h", dano));
        var causa = await Sembrador.HallazgoEnFrenteAsync(e.Llegadas[1], "v1_plus", "mancha");
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), @i, 'acomodacion-1.jpg', SYSDATETIMEOFFSET(), 6.2, -75.5)", ("@i", e.Salidas[0]));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE ViajeId = @v", ("@v", e.Viaje));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajeVehiculos SET Posicion = 3 WHERE ViajeId = @v AND VehiculoId = @x", ("@v", e.Viaje), ("@x", e.Vehiculos[0]));
        await Sembrador.UnidadNoSaleAsync(e.Viaje, e.Vehiculos[1], causa, e.Coordinador.UsuarioId, "Abolladura en la puerta");
        return e;
    }

    [Fact]
    public async Task El_informe_de_la_ninera_reune_los_puestos_los_danos_las_fotos_la_firma_del_cargue_y_el_carro_que_no_sale()
    {
        var e = await EscenarioConDanosAsync();
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, "recibe")).StatusCode);

        var texto = Plano(await PdfAsync(e.Coordinador, e.Viaje));

        Assert.Contains("Informe de la niñera", texto);
        Assert.Contains("Viaje M-NINERA", texto);
        Assert.Contains($"Puesto 3 — VIN {VINES[0]}", texto);
        Assert.Contains("Puesto 1 — libre", texto);
        Assert.Contains($"NO SALE — VIN {VINES[1]}: Daño V1+: Abolladura en la puerta", texto);
        Assert.Contains("Clase: golpe", texto);
        Assert.Contains("Tamaño: 5_10cm", texto);
        Assert.Contains("Clase: mancha", texto);
        Assert.Contains("Fotos de la niñera", texto);
        Assert.DoesNotContain("Sin fotos de la niñera", texto);
        Assert.Contains("Entrega (inspector): Nombre inspector@ejemplo.com", texto);
        Assert.Contains("Recibe (conductor): Nombre conductor@ejemplo.com", texto);
        Assert.Contains("hora del momento de la firma", texto);
        // El momento enviado (14:09 en -05:00) sale en UTC, y el GPS del momento.
        Assert.Contains("2026-11-18 19:09:00 UTC", texto);
        Assert.Contains("Ubicación: 6.2, -75.5 (± 7.5 m)", texto);
        Assert.DoesNotContain("Cargue sin firmar", texto);
        Assert.DoesNotContain("Acomodación sin cerrar", texto);
    }

    [Fact]
    public async Task El_conductor_recibe_el_informe_sin_el_nivel_ni_la_nota_de_ningun_dano_y_el_coordinador_y_el_inspector_con_ellos()
    {
        var e = await EscenarioConDanosAsync();

        var delConductor = Plano(await PdfAsync(e.Conductor, e.Viaje));
        var delCoordinador = Plano(await PdfAsync(e.Coordinador, e.Viaje));
        var delInspector = Plano(await PdfAsync(e.Inspector, e.Viaje));

        Assert.Contains("Clase: golpe", delConductor);
        Assert.DoesNotContain("Nivel:", delConductor);
        Assert.DoesNotContain("Rayón profundo", delConductor);
        foreach (var texto in new[] { delCoordinador, delInspector })
        {
            Assert.Contains("Nivel: ", texto);
            Assert.Contains("Nota: Rayón profundo en el capó", texto);
        }
    }

    [Fact]
    public async Task Con_la_acomodacion_sin_cerrar_y_el_cargue_sin_firmar_el_informe_sale_con_lo_que_hay_y_lo_dice()
    {
        var e = await EscenarioAsync(cargueCompleto: false);

        var texto = Plano(await PdfAsync(e.Coordinador, e.Viaje));

        Assert.Contains("Acomodación sin cerrar", texto);
        Assert.Contains("Cargue sin firmar", texto);
        Assert.Contains($"VIN {VINES[0]}", texto);
        Assert.Contains("Sin fotos de la niñera", texto);
    }

    [Fact]
    public async Task Con_solo_una_firma_el_informe_dice_que_la_otra_parte_no_ha_firmado()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);

        var texto = Plano(await PdfAsync(e.Coordinador, e.Viaje));

        Assert.Contains("Entrega (inspector): Nombre inspector@ejemplo.com", texto);
        Assert.Contains("Cargue sin firmar por esta parte", texto);
        Assert.DoesNotContain("Cargue sin firmar.", texto);
    }

    [Fact]
    public async Task Con_la_constancia_de_no_firma_el_informe_trae_la_parte_que_se_nego_el_motivo_y_el_testigo()
    {
        var e = await EscenarioAsync();
        var testigo = await SesionAsync("testigo@ejemplo.com", clsRol.INSPECTOR);
        Assert.Equal(HttpStatusCode.OK, (await NoFirmaAsync(e.Inspector, e.Viaje, "recibe", testigo.UsuarioId, "El conductor se niega a firmar el cargue")).StatusCode);

        var texto = Plano(await PdfAsync(e.Coordinador, e.Viaje));

        Assert.Contains("una parte se negó a firmar", texto);
        Assert.Contains("Parte que se negó: quien recibe (el conductor)", texto);
        Assert.Contains("Motivo: El conductor se niega a firmar el cargue", texto);
        Assert.Contains("Testigo: Nombre testigo@ejemplo.com", texto);
        Assert.Contains("Se negó a firmar (ver la constancia de no firma)", texto);
    }

    [Fact]
    public async Task Un_carro_que_ya_no_va_en_el_viaje_deja_de_salir_en_el_siguiente_informe()
    {
        var e = await EscenarioAsync(cargueCompleto: false);
        Assert.Contains($"VIN {VINES[1]}", Plano(await PdfAsync(e.Coordinador, e.Viaje)));

        // El carro sale del viaje: el informe no se guarda, se arma de nuevo con los que quedan.
        await Sembrador.EjecutarAsync(
            "DELETE FROM dbo.tblInspecciones WHERE EtapaId IN (SELECT Id FROM dbo.tblEtapas WHERE VehiculoId = @x); DELETE FROM dbo.tblEtapas WHERE VehiculoId = @x; DELETE FROM dbo.tblViajeVehiculos WHERE VehiculoId = @x",
            ("@x", e.Vehiculos[1]));

        var texto = Plano(await PdfAsync(e.Coordinador, e.Viaje));
        Assert.DoesNotContain(VINES[1], texto);
        Assert.Contains($"VIN {VINES[0]}", texto);
    }

    [Fact]
    public async Task Solo_lo_ve_quien_puede_ver_el_viaje()
    {
        var e = await EscenarioAsync();
        var ajeno = await SesionAsync("ajeno@ejemplo.com", clsRol.INSPECTOR);
        var otroConductor = await SesionAsync("otro-conductor@ejemplo.com", clsRol.CONDUCTOR);

        Assert.Equal(HttpStatusCode.Forbidden, (await Como(ajeno).GetAsync($"/api/viajes/{e.Viaje}/ninera/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(otroConductor).GetAsync($"/api/viajes/{e.Viaje}/ninera/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Como(e.Coordinador).GetAsync($"/api/viajes/{Guid.NewGuid()}/ninera/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync($"/api/viajes/{e.Viaje}/ninera/pdf")).StatusCode);
        foreach (var quien in new[] { e.Inspector, e.Conductor, e.Coordinador, e.Admin })
        {
            Assert.Equal(HttpStatusCode.OK, (await Como(quien).GetAsync($"/api/viajes/{e.Viaje}/ninera/pdf")).StatusCode);
        }
    }
}
