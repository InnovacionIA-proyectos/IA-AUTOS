using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// «Ya estaba registrado» al revisar: quien revisa el hallazgo del conductor puede confirmar que el dano ya venia (<c>es_preexistente</c> y <c>hallazgo_origen_id</c>) con la misma validacion de cuando se crea un
/// hallazgo; el coordinador de calidad del cliente tambien lo hace a distancia y pide los hallazgos previos, y revisa los del conductor en la llegada del Descargue que el hizo sin inspector en destino.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_1)]
public sealed class RevisionPreexistenteApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>
    /// Un VIN con su ruta: en el Cargue, una salida cerrada del inspector con un golpe en el frente (el hallazgo previo) y, despues, la llegada del Descargue que hizo el conductor con el mismo golpe anotado
    /// otra vez (el hallazgo del conductor que se revisa).
    /// </summary>
    private sealed record Escenario(Guid Vehiculo, string Vin, Guid[] Etapas, Sesion Conductor, Sesion Inspector, Sesion Coordinador, Sesion Admin, Guid Frente, Guid InspeccionPrevia, Guid HallazgoPrevio, Guid InspeccionActual, Guid HallazgoActual);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _api = CrearFabrica(servicios: s => s.AddSingleton<IEnviadorPush>(_push));
    }

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await _api.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1", _api), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token, _api);

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static JsonElement Errores(JsonElement cuerpo) => cuerpo.GetProperty("details").GetProperty("fieldErrors");

    private async Task<Guid> ZonaAsync(int numero) => (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Numero = @n", ("@n", numero)))!;

    /// <param name="antesDelActual">Siembra lo que debe estar cerrado ANTES de que empiece la inspeccion que se revisa (recibe las etapas, el inspector y la zona del frente).</param>
    private async Task<Escenario> EscenarioAsync(string vin = "VIN-PRE-0001", Func<Guid[], Sesion, Guid, Task>? antesDelActual = null)
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (vehiculo, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        var frente = await ZonaAsync(50);
        var (previa, hallazgoPrevio) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], inspector.UsuarioId, frente, "golpe", nivel: "v2", conFoto: true);
        if (antesDelActual is not null)
        {
            await antesDelActual(etapas, inspector, frente);
        }

        await Task.Delay(50);
        var (actual, hallazgoActual) = await Sembrador.InformeCerradoConHallazgoAsync(
            etapas[2], conductor.UsuarioId, frente, "golpe", momento: "llegada", nivel: null, origen: "conductor", cerrada: false);
        return new Escenario(vehiculo, vin, etapas, conductor, inspector, coordinador, admin, frente, previa, hallazgoPrevio, actual, hallazgoActual);
    }

    private static Dictionary<string, object?> Cuerpo(Action<Dictionary<string, object?>>? cambiar = null)
    {
        var cuerpo = new Dictionary<string, object?> { ["cuadrante_numero"] = 50, ["clase_dano"] = "golpe", ["nivel_severidad"] = "v2" };
        cambiar?.Invoke(cuerpo);
        return cuerpo;
    }

    private Task<HttpResponseMessage> RevisarAsync(Sesion quien, Guid hallazgo, Dictionary<string, object?> cuerpo) =>
        Como(quien).PostAsJsonAsync($"/api/hallazgos/{hallazgo}/revision", cuerpo);

    private static Dictionary<string, object?> ComoPreexistente(Guid origen) => Cuerpo(c => { c["es_preexistente"] = true; c["hallazgo_origen_id"] = origen; });

    // ----------------------------------------------------------------------------------------------- la revision

    [Fact]
    public async Task El_coordinador_revisa_a_distancia_el_hallazgo_del_conductor_y_lo_confirma_como_ya_registrado()
    {
        var e = await EscenarioAsync();

        var r = await RevisarAsync(e.Coordinador, e.HallazgoActual, ComoPreexistente(e.HallazgoPrevio));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var h = await ComoJsonAsync(r);
        Assert.True(h.GetProperty("es_preexistente").GetBoolean());
        Assert.Equal(e.HallazgoPrevio, h.GetProperty("hallazgo_origen_id").GetGuid());
        Assert.Equal(e.Coordinador.UsuarioId, h.GetProperty("confirmado_por").GetGuid());
        Assert.Equal(e.Coordinador.UsuarioId, h.GetProperty("revisado_por").GetGuid());
        Assert.Equal("v2", h.GetProperty("nivel_severidad").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND EsPreexistente = 1 AND HallazgoOrigenId = @o AND ConfirmadoPor = @u", ("@h", e.HallazgoActual), ("@o", e.HallazgoPrevio), ("@u", e.Coordinador.UsuarioId)));
        // Queda en el historial de la revision, con el hallazgo al que se unio y quien lo confirmo, y en la auditoria.
        var historial = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/hallazgos/{e.HallazgoActual}/seguimiento"));
        var revision = historial.EnumerateArray().Single(p => p.GetProperty("accion").GetString() == "revision");
        var detalle = revision.GetProperty("detalle");
        Assert.True(detalle.GetProperty("es_preexistente").GetBoolean());
        Assert.Equal(e.HallazgoPrevio, detalle.GetProperty("hallazgo_origen_id").GetGuid());
        Assert.Equal(e.Coordinador.UsuarioId, detalle.GetProperty("confirmado_por").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'hallazgo.revision' AND EntidadId = @h AND JSON_VALUE(Detalle, '$.despues.es_preexistente') IS NOT NULL", ("@h", e.HallazgoActual)));
    }

    [Fact]
    public async Task El_inspector_y_el_administrador_tambien_lo_confirman_y_una_revision_sin_origen_no_cambia()
    {
        var e = await EscenarioAsync();
        var (_, otroActual) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[1], e.Conductor.UsuarioId, e.Frente, "golpe", momento: "llegada", nivel: null, origen: "conductor", cerrada: false);
        var (_, tercero) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2], e.Inspector.UsuarioId, e.Frente, "golpe", momento: "salida", nivel: null, origen: "conductor", cerrada: false);

        var delInspector = await RevisarAsync(e.Inspector, otroActual, ComoPreexistente(e.HallazgoPrevio));
        var delAdmin = await RevisarAsync(e.Admin, tercero, Cuerpo());

        Assert.Equal(HttpStatusCode.OK, delInspector.StatusCode);
        Assert.Equal(e.Inspector.UsuarioId, (await ComoJsonAsync(delInspector)).GetProperty("confirmado_por").GetGuid());
        Assert.Equal(HttpStatusCode.OK, delAdmin.StatusCode);
        var sinOrigen = await ComoJsonAsync(delAdmin);
        Assert.False(sinOrigen.GetProperty("es_preexistente").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sinOrigen.GetProperty("hallazgo_origen_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, sinOrigen.GetProperty("confirmado_por").ValueKind);
    }

    [Fact]
    public async Task Un_origen_que_no_corresponde_responde_400_y_no_deja_nada_a_medias()
    {
        Guid otroVin = Guid.Empty, otraZonaPrevia = Guid.Empty, otraClasePrevia = Guid.Empty;
        var e = await EscenarioAsync(antesDelActual: async (etapas, inspector, frente) =>
        {
            // Todo esto se cierra antes de empezar la inspeccion que se revisa: lo unico que falla en cada caso es la coincidencia con el VIN, la zona o la clase.
            var otraZona = await ZonaAsync(51);
            var (_, ajenas) = await Sembrador.VehiculoConRutaAsync("VIN-PRE-0099", inspectorId: inspector.UsuarioId);
            otroVin = (await Sembrador.InformeCerradoConHallazgoAsync(ajenas[0], inspector.UsuarioId, frente, "golpe")).HallazgoId;
            otraZonaPrevia = (await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], inspector.UsuarioId, otraZona, "golpe", momento: "llegada")).HallazgoId;
            otraClasePrevia = (await Sembrador.InformeCerradoConHallazgoAsync(etapas[1], inspector.UsuarioId, frente, "rotura")).HallazgoId;
        });
        // Una inspeccion que aun no se cierra y otra que se cierra DESPUES de empezar la que se revisa.
        var (_, sinCerrar) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2], e.Inspector.UsuarioId, e.Frente, "golpe", cerrada: false);
        var (_, posterior) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[1], e.Inspector.UsuarioId, e.Frente, "golpe", momento: "llegada");

        var casos = new[]
        {
            ComoPreexistente(otroVin),
            ComoPreexistente(otraZonaPrevia),
            ComoPreexistente(otraClasePrevia),
            ComoPreexistente(sinCerrar),
            ComoPreexistente(posterior),
            ComoPreexistente(e.HallazgoActual),
            ComoPreexistente(Guid.NewGuid()),
        };
        foreach (var cuerpo in casos)
        {
            var r = await RevisarAsync(e.Coordinador, e.HallazgoActual, cuerpo);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(
                "El hallazgo de origen debe ser de una inspección cerrada anterior del mismo vehículo, con la misma zona y la misma clase de daño",
                Errores(await ComoJsonAsync(r)).GetProperty("hallazgo_origen_id")[0].GetString());
        }

        // Una revision rechazada no guarda nada: el hallazgo sigue sin revisar y se puede revisar bien despues.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND RevisadoAt IS NULL AND EsPreexistente = 0", ("@h", e.HallazgoActual)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'hallazgo.revision'"));
        Assert.Equal(HttpStatusCode.OK, (await RevisarAsync(e.Coordinador, e.HallazgoActual, ComoPreexistente(e.HallazgoPrevio))).StatusCode);
    }

    [Fact]
    public async Task Es_preexistente_exige_el_origen_y_el_origen_exige_es_preexistente()
    {
        var e = await EscenarioAsync();

        var sinOrigen = await RevisarAsync(e.Coordinador, e.HallazgoActual, Cuerpo(c => c["es_preexistente"] = true));
        var sinBandera = await RevisarAsync(e.Coordinador, e.HallazgoActual, Cuerpo(c => c["hallazgo_origen_id"] = e.HallazgoPrevio));
        var enFalso = await RevisarAsync(e.Coordinador, e.HallazgoActual, Cuerpo(c => { c["es_preexistente"] = false; c["hallazgo_origen_id"] = e.HallazgoPrevio; }));
        var mal = await RevisarAsync(e.Coordinador, e.HallazgoActual, Cuerpo(c => { c["es_preexistente"] = true; c["hallazgo_origen_id"] = "no-es-un-uuid"; }));

        foreach (var r in new[] { sinOrigen, sinBandera, enFalso, mal })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        Assert.Contains("exige hallazgo_origen_id", Errores(await ComoJsonAsync(sinOrigen)).GetProperty("hallazgo_origen_id")[0].GetString());
        Assert.Contains("solo se envía con es_preexistente verdadero", Errores(await ComoJsonAsync(sinBandera)).GetProperty("hallazgo_origen_id")[0].GetString());
        Assert.True(Errores(await ComoJsonAsync(enFalso)).TryGetProperty("hallazgo_origen_id", out _));
        // Si es_preexistente es falso y no hay origen, la revision es la de siempre.
        var normal = await RevisarAsync(e.Coordinador, e.HallazgoActual, Cuerpo(c => c["es_preexistente"] = false));
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
        Assert.False((await ComoJsonAsync(normal)).GetProperty("es_preexistente").GetBoolean());
    }

    // ----------------------------------------------------------------------------------------------- quien revisa a distancia

    [Fact]
    public async Task El_coordinador_revisa_la_llegada_del_descargue_que_hizo_el_conductor_y_no_la_de_otro_ni_otra_salida_ni_otro_cliente()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        // Otra unidad: la llegada del Descargue que hizo el inspector y la salida del Descargue que hizo el conductor. El coordinador no las revisa.
        var (_, otras) = await Sembrador.VehiculoConRutaAsync("VIN-PRE-0002", conductorId: e.Conductor.UsuarioId, inspectorId: e.Inspector.UsuarioId);
        var (_, llegadaDelInspector) = await Sembrador.InformeCerradoConHallazgoAsync(otras[2], e.Inspector.UsuarioId, e.Frente, "golpe", momento: "llegada", nivel: null, origen: "conductor", cerrada: false);
        var (_, salidaDelConductor) = await Sembrador.InformeCerradoConHallazgoAsync(otras[2], e.Conductor.UsuarioId, e.Frente, "golpe", momento: "salida", nivel: null, origen: "conductor", cerrada: false);

        var deOtroCliente = await RevisarAsync(coordinadorOtro, e.HallazgoActual, Cuerpo());
        var delInspector = await RevisarAsync(e.Coordinador, llegadaDelInspector, Cuerpo());
        var salidaDeDescargue = await RevisarAsync(e.Coordinador, salidaDelConductor, Cuerpo());
        var delConductor = await RevisarAsync(e.Coordinador, e.HallazgoActual, Cuerpo());

        Assert.Equal(HttpStatusCode.Forbidden, deOtroCliente.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salidaDeDescargue.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delConductor.StatusCode);
    }

    [Fact]
    public async Task Los_hallazgos_previos_los_pide_el_coordinador_de_su_cliente_pero_no_el_de_otro_ni_el_conductor()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL);
        var ruta = $"/api/inspecciones/{e.InspeccionActual}/hallazgos-previos";

        var delCoordinador = await Como(e.Coordinador).GetAsync(ruta);
        var delInspector = await Como(e.Inspector).GetAsync(ruta);

        Assert.Equal(HttpStatusCode.OK, delCoordinador.StatusCode);
        var lista = (await ComoJsonAsync(delCoordinador)).EnumerateArray().ToList();
        Assert.Equal(e.HallazgoPrevio, lista.Single().GetProperty("id").GetGuid());
        Assert.Equal("cargue", lista.Single().GetProperty("proceso").GetString());
        Assert.NotEmpty(lista.Single().GetProperty("evidencias").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, delInspector.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Admin).GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(coordinadorOtro).GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Conductor).GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(comercial).GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync(ruta)).StatusCode);
    }
}
