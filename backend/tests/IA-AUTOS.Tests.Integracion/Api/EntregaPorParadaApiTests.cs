using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La entrega por parada: quien recibe sale de la parada (el inspector que la tomo o la persona del lugar) y no del viaje, el reclamo de la entrega es por parada, un carro se entrega o se rechaza por una novedad de su
/// revision de llegada, las reservas van por carro, una sola firma lleva su motivo codificado y su evidencia, toda firma guarda la hora y el GPS del momento, el orden del destino se hace cumplir y la firma carro por
/// carro del descargue ya no existe.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class EntregaPorParadaApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string MOMENTO = "2026-11-18T14:10:00-05:00";
    private static readonly byte[] PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>
    /// El viaje MT-5001 en curso, cargado por Ana (su inspectora), con tres carros para el Puerto (que tiene al inspector Pedro), tres para Norte (sin inspectores) y uno para Sur (que tiene a la inspectora Diana; la ninera admite siete). Cada carro
    /// tiene la llegada de Transporte confirmada («Llegue») y la revision de llegada del Descargue cerrada.
    /// </summary>
    private sealed record Esc(Guid Viaje, Guid Puerto, Guid Norte, Guid Sur, Sesion Conductor, Sesion Coordinador, Sesion Pedro, Sesion Diana, Sesion Ana, Dictionary<string, Guid> Llegadas);

    private static readonly string[] VINES_PUERTO = ["VIN-PAR-P1", "VIN-PAR-P2", "VIN-PAR-P3"];
    private static readonly string[] VINES_NORTE = ["VIN-PAR-N1", "VIN-PAR-N2", "VIN-PAR-N3"];
    private static readonly string[] VINES_SUR = ["VIN-PAR-S1"];

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _api = CrearFabrica(servicios: s => s.AddSingleton<IEnviadorPush>(_push));
    }

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await _api.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1", _api), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token, _api);

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static string? Motivo(JsonElement error) => error.GetProperty("details").GetProperty("motivo").GetString();

    private static string Mensaje(JsonElement error) => error.GetProperty("message").GetString()!;

    private async Task<Guid> EtapaAsync(Guid viaje, string vin, string codigo) =>
        (Guid)(await Sembrador.EscalarAsync(
            "SELECT e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId WHERE e.ViajeId = @v AND v.Vin = @vin AND c.Codigo = @c",
            ("@v", viaje), ("@vin", vin), ("@c", codigo)))!;

    private Task AsignarPuntoAsync(Sesion inspector, Guid lugar) =>
        Sembrador.EjecutarAsync("INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), @u, @l)", ("@u", inspector.UsuarioId), ("@l", lugar));

    private async Task<Esc> EscenarioAsync(bool conLlegue = true, string[]? conDano = null)
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var pedro = await SesionAsync("pedro@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.INSPECTOR);
        var ana = await SesionAsync("ana@ejemplo.com", clsRol.INSPECTOR);
        var puerto = await LugarRealAsync(Como(coordinador), "Puerto de Cartagena", "destino", "Cartagena");
        var norte = await LugarRealAsync(Como(coordinador), "Concesionario Norte", "concesionario", "Bogota");
        var sur = await LugarRealAsync(Como(coordinador), "Concesionario Sur", "cedi_2", "Cali");
        var cargue = await LugarRealAsync(Como(coordinador), "CEDI de prueba", "cedi_1");
        await AsignarPuntoAsync(pedro, puerto);
        await AsignarPuntoAsync(diana, sur);
        foreach (var vin in VINES_PUERTO.Concat(VINES_NORTE).Concat(VINES_SUR))
        {
            await Sembrador.VehiculoAsync(vin);
        }

        var unidadTipo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;
        object[] Unidades(Guid lugar, string[] vines) => [.. vines.Select(v => (object)new { vin = v, lugar_destino_id = lugar })];
        var creado = await Como(coordinador).PostAsync("/api/viajes", Cuerpo(new
        {
            codigo_mtrack = "MT-5001",
            tipo_unidad_id = unidadTipo,
            placa = "ABC123",
            conductor_id = conductor.UsuarioId,
            inspector_id = ana.UsuarioId,
            fecha_programada = "2026-11-18T06:00:00-05:00",
            lugar_cargue_id = cargue,
            unidades = Unidades(puerto, VINES_PUERTO).Concat(Unidades(norte, VINES_NORTE)).Concat(Unidades(sur, VINES_SUR)).ToArray(),
        }));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var viaje = (await JsonAsync(creado)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Como(coordinador).PatchAsync($"/api/viajes/{viaje}/estado", Cuerpo(new { estado = "en_curso" }))).StatusCode);

        var zona = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Numero = 50"))!;
        var llegadas = new Dictionary<string, Guid>();
        foreach (var vin in VINES_PUERTO.Concat(VINES_NORTE).Concat(VINES_SUR))
        {
            if (conLlegue)
            {
                await Sembrador.InformeAsync(await EtapaAsync(viaje, vin, "transporte"), conductor.UsuarioId, cerrada: true, momento: "llegada");
            }

            var descargue = await EtapaAsync(viaje, vin, "descargue");
            llegadas[vin] = conDano?.Contains(vin) == true
                ? (await Sembrador.InformeCerradoConHallazgoAsync(descargue, pedro.UsuarioId, zona, "golpe", "llegada", "v2", conFoto: true)).InspeccionId
                : await Sembrador.InformeAsync(descargue, pedro.UsuarioId, cerrada: true, momento: "llegada");
        }

        return new Esc(viaje, puerto, norte, sur, conductor, coordinador, pedro, diana, ana, llegadas);
    }

    private Task<HttpResponseMessage> FirmarAsync(Sesion quien, Guid viaje, Guid lugar, object? cuerpo = null) =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/entregas/{lugar}/firma", Cuerpo(cuerpo ?? new { firma = "trazo-" + quien.UsuarioId.ToString("N")[..6] }));

    private Task<HttpResponseMessage> UnaFirmaAsync(Sesion quien, Guid viaje, Guid lugar, object cuerpo) =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/entregas/{lugar}/una-firma", Cuerpo(cuerpo));

    private Task<HttpResponseMessage> ReclamarEntregaAsync(Sesion quien, Guid viaje, Guid? lugar) =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/reclamos", Cuerpo(lugar is null ? new { proceso = "entrega" } : new { proceso = "entrega", lugar_id = lugar }));

    private static readonly object EXTERNA = new { recibe_nombre = "Rosa Ríos", recibe_cedula = "52 123 456", firma_recibe = "trazo-rosa" };

    private Task<HttpResponseMessage> FirmarConExternaAsync(Sesion conductor, Guid viaje, Guid lugar, IReadOnlyList<object>? rechazos = null, IReadOnlyList<object>? reservas = null, object? momento = null) =>
        Como(conductor).PostAsync($"/api/viajes/{viaje}/entregas/{lugar}/firma", Cuerpo(new Dictionary<string, object?>
        {
            ["firma"] = "trazo-conductor",
            ["recibe_nombre"] = "Rosa Ríos",
            ["recibe_cedula"] = "52 123 456",
            ["firma_recibe"] = "trazo-rosa",
            ["rechazos"] = rechazos,
            ["reservas"] = reservas,
            ["fecha_momento"] = momento is null ? null : MOMENTO,
            ["lat"] = momento is null ? null : 10.39,
            ["lng"] = momento is null ? null : -75.48,
        }));

    private Task<object?> TransferenciaAsync(Guid inspeccion, string columna) =>
        Sembrador.EscalarAsync($"SELECT {columna} FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @i", ("@i", inspeccion));

    private async Task<Guid> HallazgoDeAsync(Guid inspeccion) =>
        (Guid)(await Sembrador.EscalarAsync("SELECT TOP 1 Id FROM dbo.tblHallazgos WHERE InspeccionId = @i", ("@i", inspeccion)))!;

    private static JsonElement Unidad(JsonElement punto, string vin) => punto.GetProperty("unidades").EnumerateArray().Single(u => u.GetProperty("vin").GetString() == vin);

    private async Task<JsonElement> PuntoAsync(Sesion quien, Guid viaje, Guid lugar) => await JsonAsync(await Como(quien).GetAsync($"/api/viajes/{viaje}/entregas/{lugar}"));

    // ------------------------------------------------------------------------------------------------ quien recibe

    [Fact]
    public async Task Recibe_el_inspector_que_tomo_la_parada_y_el_inspector_que_cargo_no_figura_en_ninguna_firma()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, e.Puerto)).StatusCode);
        var firmada = await FirmarAsync(e.Pedro, e.Viaje, e.Puerto);

        Assert.Equal(HttpStatusCode.OK, firmada.StatusCode);
        var punto = await JsonAsync(firmada);
        Assert.Equal("entregada", punto.GetProperty("estado_parada").GetString());
        Assert.Equal("inspector_del_punto", punto.GetProperty("receptor").GetString());
        Assert.Equal("Nombre pedro@ejemplo.com", punto.GetProperty("receptor_nombre").GetString());
        foreach (var vin in VINES_PUERTO)
        {
            Assert.Equal("firmada", await TransferenciaAsync(e.Llegadas[vin], "TipoCierre"));
            Assert.Equal(e.Pedro.UsuarioId, await TransferenciaAsync(e.Llegadas[vin], "RecibeUsuarioId"));
            Assert.Equal(e.Conductor.UsuarioId, await TransferenciaAsync(e.Llegadas[vin], "EntregaUsuarioId"));
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE RecibeUsuarioId = @a OR EntregaUsuarioId = @a", ("@a", e.Ana.UsuarioId)));
    }

    [Fact]
    public async Task Una_parada_sin_inspectores_la_recibe_la_persona_del_lugar_aunque_el_viaje_tenga_inspector_y_nunca_se_cae_al_inspector_del_viaje()
    {
        var e = await EscenarioAsync();

        var sinDatos = await FirmarAsync(e.Conductor, e.Viaje, e.Norte);
        var conExterna = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte);

        Assert.Equal(HttpStatusCode.BadRequest, sinDatos.StatusCode);
        Assert.Equal(["firma_recibe", "recibe_cedula", "recibe_nombre"], (await JsonAsync(sinDatos)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToList());
        Assert.Equal(HttpStatusCode.OK, conExterna.StatusCode);
        var punto = await JsonAsync(conExterna);
        Assert.Equal("persona_del_lugar", punto.GetProperty("receptor").GetString());
        Assert.Equal("Rosa Ríos", punto.GetProperty("receptor_nombre").GetString());
        foreach (var vin in VINES_NORTE)
        {
            Assert.Equal("firmada", await TransferenciaAsync(e.Llegadas[vin], "TipoCierre"));
            Assert.Null(await TransferenciaAsync(e.Llegadas[vin], "RecibeUsuarioId"));
            Assert.Equal("Rosa Ríos", await TransferenciaAsync(e.Llegadas[vin], "RecibeExternoNombre"));
        }
    }

    [Fact]
    public async Task Nadie_tomo_la_entrega_en_una_parada_con_inspectores_responde_sin_receptor_sin_avisos_y_sin_guardar_nada()
    {
        var e = await EscenarioAsync();

        var r = await FirmarAsync(e.Conductor, e.Viaje, e.Puerto);
        var unaFirma = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Puerto, new { firma = "f", motivo_codigo = "no_habia_nadie" });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var error = await JsonAsync(r);
        Assert.Equal("sin_receptor", Motivo(error));
        Assert.Equal("Este punto tiene inspector: pídele a un inspector del punto que tome la entrega en su app", Mensaje(error));
        Assert.Equal(HttpStatusCode.Conflict, unaFirma.StatusCode);
        Assert.Equal("sin_receptor", Motivo(await JsonAsync(unaFirma)));
        var punto = await PuntoAsync(e.Conductor, e.Viaje, e.Puerto);
        Assert.Equal("sin_receptor", punto.GetProperty("receptor").GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones"));
    }

    [Fact]
    public async Task El_inspector_que_cargo_solo_toma_y_firma_una_parada_si_esta_asignado_a_ella_como_cualquier_inspector()
    {
        var e = await EscenarioAsync();

        // Ana cargo el viaje pero no esta asignada al Puerto: no la toma ni la firma (403) y tampoco una parada sin inspectores.
        var tomar = await ReclamarEntregaAsync(e.Ana, e.Viaje, e.Puerto);
        var firmar = await FirmarAsync(e.Ana, e.Viaje, e.Puerto);
        var firmarNorte = await FirmarAsync(e.Ana, e.Viaje, e.Norte);
        Assert.Equal(HttpStatusCode.Forbidden, tomar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, firmar.StatusCode);
        Assert.Equal("no_es_su_punto", Motivo(await JsonAsync(firmar)));
        Assert.Equal(HttpStatusCode.Forbidden, firmarNorte.StatusCode);

        // Asignada al Puerto, la toma y firma como Pedro lo haria.
        await AsignarPuntoAsync(e.Ana, e.Puerto);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Ana, e.Viaje, e.Puerto)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, e.Puerto)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Ana, e.Viaje, e.Puerto)).StatusCode);
        Assert.Equal(e.Ana.UsuarioId, await TransferenciaAsync(e.Llegadas["VIN-PAR-P1"], "RecibeUsuarioId"));
        // Pedro, que tambien es del Puerto, ya no puede firmarla: la tomo Ana.
        var otraVez = await FirmarAsync(e.Pedro, e.Viaje, e.Puerto);
        Assert.Equal(HttpStatusCode.Forbidden, otraVez.StatusCode);
    }

    [Fact]
    public async Task Admin_coordinador_y_super_admin_no_firman_la_entrega_pero_la_leen_como_siempre()
    {
        var e = await EscenarioAsync();
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        foreach (var quien in new[] { e.Coordinador, admin, superAdmin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await FirmarAsync(quien, e.Viaje, e.Norte)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await UnaFirmaAsync(quien, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "no_habia_nadie" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Como(quien).GetAsync($"/api/viajes/{e.Viaje}/entregas")).StatusCode);
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Los_campos_de_antes_ya_no_se_aceptan_en_la_firma_por_parada()
    {
        var e = await EscenarioAsync();

        var motivoParcial = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Norte}/firma", Cuerpo(new { firma = "f", motivo_parcial = "falta espacio" }));
        var firmantes = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Norte}/firma", Cuerpo(new { firma = "f", recibe_usuario_id = e.Pedro.UsuarioId }));
        var rechazosEnUnaFirma = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "no_habia_nadie", rechazos = new[] { new { vin = "VIN-PAR-N1" } } });

        foreach (var r in new[] { motivoParcial, firmantes, rechazosEnUnaFirma })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        Assert.Equal("Campo no admitido", (await JsonAsync(motivoParcial)).GetProperty("details").GetProperty("fieldErrors").GetProperty("motivo_parcial")[0].GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    // ------------------------------------------------------------------------------------------------ el reclamo por parada

    [Fact]
    public async Task La_entrega_se_reclama_por_parada_cada_inspector_toma_la_suya_y_se_libera_por_parada()
    {
        var e = await EscenarioAsync();

        var sinParada = await ReclamarEntregaAsync(e.Pedro, e.Viaje, null);
        var deOtro = await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Sur);
        var paradaSinCarros = await ReclamarEntregaAsync(e.Pedro, e.Viaje, Guid.NewGuid());
        var puerto = await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto);
        var sur = await ReclamarEntregaAsync(e.Diana, e.Viaje, e.Sur);
        var otraVez = await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto);

        Assert.Equal(HttpStatusCode.BadRequest, sinParada.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deOtro.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, paradaSinCarros.StatusCode);
        Assert.Equal(HttpStatusCode.Created, puerto.StatusCode);
        var reclamo = await JsonAsync(puerto);
        Assert.Equal(e.Puerto, reclamo.GetProperty("lugar_id").GetGuid());
        Assert.Equal("Puerto de Cartagena", reclamo.GetProperty("lugar").GetString());
        Assert.Equal(HttpStatusCode.Created, sur.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otraVez.StatusCode);

        // Otro inspector de la misma parada recibe el 409 de siempre con su motivo.
        await AsignarPuntoAsync(e.Diana, e.Puerto);
        var tomada = await ReclamarEntregaAsync(e.Diana, e.Viaje, e.Puerto);
        Assert.Equal(HttpStatusCode.Conflict, tomada.StatusCode);
        var error = await JsonAsync(tomada);
        Assert.Equal("entrega_tomada", Motivo(error));
        Assert.Equal("Ya fue tomado por otro inspector", Mensaje(error));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND Proceso = 'entrega' AND LiberadoAt IS NULL", ("@v", e.Viaje)));

        // Liberar es por parada: sin ella 400; liberar el Puerto no toca el Sur.
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(e.Pedro).DeleteAsync($"/api/viajes/{e.Viaje}/reclamos/entrega")).StatusCode);
        var liberado = await Como(e.Pedro).DeleteAsync($"/api/viajes/{e.Viaje}/reclamos/entrega?lugar_id={e.Puerto}");
        Assert.Equal(HttpStatusCode.OK, liberado.StatusCode);
        Assert.False((await JsonAsync(liberado)).GetProperty("activo").GetBoolean());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND Proceso = 'entrega' AND LiberadoAt IS NULL AND LugarId = @l", ("@v", e.Viaje), ("@l", e.Sur)));
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Diana, e.Viaje, e.Puerto)).StatusCode);
    }

    [Fact]
    public async Task Dos_inspectores_que_toman_a_la_vez_la_misma_parada_dejan_exactamente_un_reclamo_activo()
    {
        var e = await EscenarioAsync();
        await AsignarPuntoAsync(e.Diana, e.Puerto);

        var respuestas = await Task.WhenAll(ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto), ReclamarEntregaAsync(e.Diana, e.Viaje, e.Puerto));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], respuestas.Select(r => r.StatusCode).Order().ToList());
        var perdedora = respuestas.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("entrega_tomada", Motivo(await JsonAsync(perdedora)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND Proceso = 'entrega' AND LiberadoAt IS NULL AND LugarId = @l", ("@v", e.Viaje), ("@l", e.Puerto)));
    }

    [Fact]
    public async Task Los_pendientes_del_inspector_salen_por_parada_con_el_estado_del_reclamo_de_esa_parada_y_el_reclamo_de_una_no_da_acceso_a_otra()
    {
        var e = await EscenarioAsync();
        await AsignarPuntoAsync(e.Pedro, e.Sur);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Diana, e.Viaje, e.Sur)).StatusCode);

        var pendientes = await JsonAsync(await Como(e.Pedro).GetAsync("/api/viajes/pendientes"));
        var puerto = pendientes.EnumerateArray().Single(p => p.GetProperty("lugar_id").GetGuid() == e.Puerto);
        var sur = pendientes.EnumerateArray().Single(p => p.GetProperty("lugar_id").GetGuid() == e.Sur);

        Assert.Equal("mio", puerto.GetProperty("estado").GetString());
        Assert.Equal("en_revision", sur.GetProperty("estado").GetString());
        Assert.Equal("Nombre diana@ejemplo.com", sur.GetProperty("reclamado_por_nombre").GetString());
        // El reclamo del Puerto vale solo para firmar el Puerto: la del Sur la tomo Diana.
        Assert.Equal(HttpStatusCode.Forbidden, (await FirmarAsync(e.Pedro, e.Viaje, e.Sur)).StatusCode);
    }

    [Fact]
    public async Task El_aviso_de_reclamo_sin_actividad_nombra_la_parada_y_sale_una_vez_por_reclamo()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblReclamos SET ReclamadoAt = DATEADD(HOUR, -3, SYSDATETIMEOFFSET()) WHERE LugarId IS NOT NULL");
        await Sembrador.EjecutarAsync("ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL; UPDATE dbo.tblInspecciones SET IniciadaAt = DATEADD(DAY, -2, IniciadaAt), CerradaAt = DATEADD(DAY, -2, CerradaAt); ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;");

        var servicio = _api.Services.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider.GetRequiredService<IAAutos.Aplicacion.Alertas.clsAlertasPeriodicas>();
        await servicio.EjecutarReclamosSinActividadAsync();
        await servicio.EjecutarReclamosSinActividadAsync();

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'reclamo_sin_actividad' AND Canal = 'bandeja' AND UsuarioId = @u AND Mensaje LIKE N'%entrega en Puerto de Cartagena%'", ("@u", e.Coordinador.UsuarioId)));
    }

    // ------------------------------------------------------------------------------------------------ el orden del destino

    [Fact]
    public async Task La_firma_del_inspector_espera_el_llegue_del_conductor_y_la_parada_lo_dice()
    {
        var e = await EscenarioAsync(conLlegue: false);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        Assert.False((await PuntoAsync(e.Pedro, e.Viaje, e.Puerto)).GetProperty("conductor_llego").GetBoolean());

        var antes = await FirmarAsync(e.Pedro, e.Viaje, e.Puerto);

        Assert.Equal(HttpStatusCode.Conflict, antes.StatusCode);
        var error = await JsonAsync(antes);
        Assert.Equal("conductor_sin_llegada", Motivo(error));
        Assert.Equal("El conductor aún no marca «Llegué» en esta parada", Mensaje(error));

        await Sembrador.InformeAsync(await EtapaAsync(e.Viaje, "VIN-PAR-P1", "transporte"), e.Conductor.UsuarioId, cerrada: true, momento: "llegada");

        Assert.True((await PuntoAsync(e.Pedro, e.Viaje, e.Puerto)).GetProperty("conductor_llego").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
    }

    [Fact]
    public async Task Firmar_con_la_revision_de_llegada_de_un_carro_abierta_o_sin_crear_responde_llegada_abierta_nombrando_el_vin_y_no_entrega_ninguno()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync("ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL; UPDATE dbo.tblInspecciones SET Estado = 'borrador', CerradaAt = NULL WHERE Id = @i; ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;", ("@i", e.Llegadas["VIN-PAR-N3"]));

        var abierta = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte);
        var cierre = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "no_habia_nadie" });

        foreach (var r in new[] { abierta, cierre })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("llegada_abierta", Motivo(error));
            Assert.Equal("Termina la revisión de llegada del VIN VIN-PAR-N3 antes de firmar", Mensaje(error));
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Crear_la_revision_de_llegada_del_descargue_sin_el_llegue_del_conductor_responde_sin_llegue_para_cualquier_rol()
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var pedro = await SesionAsync("pedro@ejemplo.com", clsRol.INSPECTOR);
        var norte = await LugarRealAsync(Como(coordinador), "Concesionario Norte", "concesionario", "Bogota");
        await AsignarPuntoAsync(pedro, norte);
        await Sembrador.VehiculoAsync("VIN-LLE-1");
        var cargue = await LugarRealAsync(Como(coordinador), "CEDI de prueba", "cedi_1");
        var tipo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;
        var creado = await Como(coordinador).PostAsync("/api/viajes", Cuerpo(new
        {
            codigo_mtrack = "MT-5002", tipo_unidad_id = tipo, placa = "ABC123", conductor_id = conductor.UsuarioId, inspector_id = pedro.UsuarioId, fecha_programada = "2026-11-18T06:00:00-05:00", lugar_cargue_id = cargue,
            unidades = new[] { new { vin = "VIN-LLE-1", lugar_destino_id = norte } },
        }));
        var viaje = (await JsonAsync(creado)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Como(coordinador).PatchAsync($"/api/viajes/{viaje}/estado", Cuerpo(new { estado = "en_curso" }))).StatusCode);
        var descargue = await EtapaAsync(viaje, "VIN-LLE-1", "descargue");

        foreach (var quien in new[] { conductor, pedro })
        {
            var r = await Como(quien).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = descargue, momento = "llegada", fecha = MOMENTO, lat = 4.6, lng = -74.1 }));
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("sin_llegue", Motivo(error));
            Assert.Equal("El conductor aún no marca «Llegué» para el VIN VIN-LLE-1", Mensaje(error));
        }

        // Con el llegue confirmado la revision se crea (un ciclo 1): en una parada con inspectores la hace el inspector y el conductor no.
        await Sembrador.InformeAsync(await EtapaAsync(viaje, "VIN-LLE-1", "transporte"), conductor.UsuarioId, cerrada: true, momento: "llegada");
        var delConductor = await Como(conductor).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = descargue, momento = "llegada", fecha = MOMENTO, lat = 4.6, lng = -74.1 }));
        Assert.Equal(HttpStatusCode.Conflict, delConductor.StatusCode);
        Assert.Equal("llegada_del_inspector_del_punto", Motivo(await JsonAsync(delConductor)));
        var creada = await Como(pedro).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = descargue, momento = "llegada", fecha = MOMENTO, lat = 4.6, lng = -74.1 }));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Ciclo = 1", ("@e", descargue)));
    }

    [Fact]
    public async Task La_transferencia_carro_por_carro_del_descargue_responde_entrega_por_parada_para_todos_los_roles_y_lo_cerrado_se_sigue_leyendo()
    {
        var e = await EscenarioAsync();
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var inspeccion = e.Llegadas["VIN-PAR-N1"];

        foreach (var quien in new[] { e.Conductor, e.Pedro, e.Coordinador, admin })
        {
            foreach (var (ruta, cuerpo) in new (string, object)[]
            {
                ("transferencia", new { entrega_usuario_id = e.Conductor.UsuarioId, recibe_usuario_id = e.Pedro.UsuarioId, firma = "f" }),
                ("transferencia/reservas", new { entrega_usuario_id = e.Conductor.UsuarioId, recibe_usuario_id = e.Pedro.UsuarioId, firma = "f", reservas = "sucio" }),
                ("transferencia/no-firma", new { entrega_usuario_id = e.Conductor.UsuarioId, recibe_usuario_id = e.Pedro.UsuarioId, parte = "recibe", motivo = "no quiso" }),
            })
            {
                var r = await Como(quien).PostAsync($"/api/inspecciones/{inspeccion}/{ruta}", Cuerpo(cuerpo));
                Assert.True(r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.Forbidden || r.StatusCode == HttpStatusCode.BadRequest, $"{ruta} dio {(int)r.StatusCode}");
                if (r.StatusCode == HttpStatusCode.Conflict)
                {
                    var error = await JsonAsync(r);
                    Assert.Equal("entrega_por_parada", Motivo(error));
                    Assert.Equal("En el destino la entrega se firma por parada: usa «Entregar en esta parada»", Mensaje(error));
                }
            }
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(HttpStatusCode.Conflict, (await Como(e.Conductor).PostAsync($"/api/inspecciones/{inspeccion}/transferencia", Cuerpo(new { entrega_usuario_id = e.Conductor.UsuarioId, recibe_usuario_id = e.Pedro.UsuarioId, firma = "f" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte)).StatusCode);
        // Lo ya cerrado se sigue leyendo: la constancia en PDF de una transferencia del descargue sale.
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).GetAsync($"/api/inspecciones/{inspeccion}/transferencia/constancia/pdf")).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------ el rechazo por novedad

    [Fact]
    public async Task Un_carro_se_rechaza_por_el_dano_de_su_revision_y_los_otros_se_entregan_con_aviso_urgente_al_coordinador()
    {
        var e = await EscenarioAsync(conDano: ["VIN-PAR-N2"]);
        var hallazgo = await HallazgoDeAsync(e.Llegadas["VIN-PAR-N2"]);

        var r = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, rechazos: [new { vin = "VIN-PAR-N2", hallazgo_id = hallazgo }]);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var punto = await JsonAsync(r);
        Assert.Equal("entregada_con_rechazos", punto.GetProperty("estado_parada").GetString());
        Assert.Equal(0, punto.GetProperty("unidades_pendientes").GetInt32());
        Assert.True(punto.GetProperty("entregada").GetBoolean());
        Assert.Equal("cerrada", Unidad(punto, "VIN-PAR-N1").GetProperty("estado").GetString());
        var rechazado = Unidad(punto, "VIN-PAR-N2");
        Assert.Equal("rechazado", rechazado.GetProperty("estado").GetString());
        Assert.Equal("rechazado", rechazado.GetProperty("tipo_cierre").GetString());
        var rechazo = rechazado.GetProperty("rechazo");
        Assert.Equal(hallazgo, rechazo.GetProperty("hallazgo_id").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, rechazo.GetProperty("evidencia_id").ValueKind);
        Assert.Equal(e.Conductor.UsuarioId, rechazo.GetProperty("marcado_por").GetGuid());
        Assert.Equal("rechazado_por_novedad", rechazo.GetProperty("motivo").GetString());
        Assert.Equal(1, rechazado.GetProperty("danos_nuevos").GetInt32());
        // La foto del daño llega con su enlace firmado y su sello, sin leer el informe del carro; el conteo y el identificador se conservan.
        var fotoDelRechazo = rechazo.GetProperty("foto");
        Assert.StartsWith("http", fotoDelRechazo.GetProperty("url").GetString());
        Assert.NotEqual(JsonValueKind.Null, fotoDelRechazo.GetProperty("url_expira_at").ValueKind);
        Assert.Equal(
            ["sello_fecha", "sello_lat", "sello_lng", "url", "url_expira_at", "hash_sha256", "precision", "dispositivo_id", "sin_ubicacion"],
            fotoDelRechazo.EnumerateObject().Select(p => p.Name).ToList());
        Assert.False(fotoDelRechazo.GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal(0, rechazado.GetProperty("fotos_cierre").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, Unidad(punto, "VIN-PAR-N1").GetProperty("rechazo").ValueKind);

        // Sigue activa en el viaje, a cargo del conductor, y el viaje sigue en curso aunque se entregue el resto.
        Assert.Equal("activa", await Sembrador.EscalarAsync("SELECT vv.EstadoUnidad FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId WHERE vv.ViajeId = @v AND v.Vin = 'VIN-PAR-N2'", ("@v", e.Viaje)));
        Assert.Equal(hallazgo, await TransferenciaAsync(e.Llegadas["VIN-PAR-N2"], "RechazoHallazgoId"));
        Assert.Equal(e.Conductor.UsuarioId, await TransferenciaAsync(e.Llegadas["VIN-PAR-N2"], "RechazoMarcadoPor"));
        Assert.Equal("en_curso", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
        // El daño conserva la gravedad que tenia (no se recalifica).
        Assert.Equal("v2", await Sembrador.EscalarAsync("SELECT NivelSeveridad FROM dbo.tblHallazgos WHERE Id = @h", ("@h", hallazgo)));

        // La auditoria guarda quien lo marco, el dano, la hora y el GPS.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'entrega.rechazar' AND UsuarioId = @u AND Detalle LIKE @h AND Detalle LIKE N'%VIN-PAR-N2%'", ("@u", e.Conductor.UsuarioId), ("@h", $"%{hallazgo}%")));

        // El aviso: urgente, al coordinador, con el lugar y los datos propios y ninguna cedula.
        var aviso = Assert.Single((await JsonAsync(await Como(e.Coordinador).GetAsync("/api/notificaciones?tipo=carro_rechazado_en_destino")))
            .GetProperty("items").EnumerateArray());
        Assert.Equal("alta", aviso.GetProperty("urgencia").GetString());
        Assert.Equal("Carro rechazado en destino: Concesionario Norte", aviso.GetProperty("titulo").GetString());
        var datos = aviso.GetProperty("datos");
        Assert.Equal("VIN-PAR-N2", datos.GetProperty("vin").GetString());
        Assert.Equal(e.Norte, datos.GetProperty("lugar_id").GetGuid());
        Assert.Equal(hallazgo, datos.GetProperty("hallazgo_id").GetGuid());
        Assert.Equal("entrega", datos.GetProperty("parte").GetString());
        // El aviso dice que dano motivo el rechazo, con los mismos valores del hallazgo.
        Assert.Equal("golpe", datos.GetProperty("clase_dano").GetString());
        Assert.Equal("5_10cm", datos.GetProperty("tamano_dano").GetString());
        Assert.Contains("50", datos.GetProperty("zona").GetString());
        Assert.Equal(JsonValueKind.Null, datos.GetProperty("parte_vehiculo").ValueKind);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Mensaje LIKE N'%52 123 456%' OR Datos LIKE N'%52 123 456%'"));
    }

    [Fact]
    public async Task El_aviso_del_rechazo_de_un_dano_sin_zona_trae_la_zona_nula_y_el_nombre_de_su_parte()
    {
        var e = await EscenarioAsync(conDano: ["VIN-PAR-N2"]);
        var hallazgo = await HallazgoDeAsync(e.Llegadas["VIN-PAR-N2"]);
        await Sembrador.EjecutarAsync(
            "ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL; UPDATE dbo.tblHallazgos SET ZonaId = NULL, ParteVehiculo = N'Platón' WHERE Id = @h; ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL", ("@h", hallazgo));

        var r = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, rechazos: [new { vin = "VIN-PAR-N2", hallazgo_id = hallazgo }]);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var datos = Assert.Single((await JsonAsync(await Como(e.Coordinador).GetAsync("/api/notificaciones?tipo=carro_rechazado_en_destino"))).GetProperty("items").EnumerateArray()).GetProperty("datos");
        Assert.Equal(JsonValueKind.Null, datos.GetProperty("zona").ValueKind);
        Assert.Equal("Platón", datos.GetProperty("parte_vehiculo").GetString());
        Assert.Equal("golpe", datos.GetProperty("clase_dano").GetString());
        Assert.Equal("5_10cm", datos.GetProperty("tamano_dano").GetString());
    }

    [Fact]
    public async Task El_inspector_de_la_parada_marca_el_rechazo_y_el_conductor_no_puede_donde_hay_inspectores()
    {
        var e = await EscenarioAsync(conDano: ["VIN-PAR-P3"]);
        var hallazgo = await HallazgoDeAsync(e.Llegadas["VIN-PAR-P3"]);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        var rechazos = new[] { new { vin = "VIN-PAR-P3", hallazgo_id = hallazgo } };

        var delConductor = await FirmarAsync(e.Conductor, e.Viaje, e.Puerto, new { firma = "f", rechazos });
        Assert.Equal(HttpStatusCode.Forbidden, delConductor.StatusCode);

        var delInspector = await FirmarAsync(e.Pedro, e.Viaje, e.Puerto, new { firma = "f", rechazos });
        Assert.Equal(HttpStatusCode.OK, delInspector.StatusCode);
        // Con la firma de Pedro el carro rechazado espera la del conductor; los otros tambien.
        Assert.Equal("pendiente", Unidad(await JsonAsync(delInspector), "VIN-PAR-P3").GetProperty("estado").GetString());
        var cierre = await FirmarAsync(e.Conductor, e.Viaje, e.Puerto);
        var punto = await JsonAsync(cierre);

        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal("entregada_con_rechazos", punto.GetProperty("estado_parada").GetString());
        Assert.Equal(e.Pedro.UsuarioId, Unidad(punto, "VIN-PAR-P3").GetProperty("rechazo").GetProperty("marcado_por").GetGuid());
        Assert.Equal("rechazado", await TransferenciaAsync(e.Llegadas["VIN-PAR-P3"], "TipoCierre"));
        var aviso = Assert.Single((await JsonAsync(await Como(e.Coordinador).GetAsync("/api/notificaciones?tipo=carro_rechazado_en_destino"))).GetProperty("items").EnumerateArray());
        Assert.Equal("recibe", aviso.GetProperty("datos").GetProperty("parte").GetString());
    }

    [Fact]
    public async Task Un_carro_no_se_deja_fuera_sin_un_dano_que_lo_justifique_y_el_rechazo_exige_el_dano_de_su_propia_revision_con_foto()
    {
        var e = await EscenarioAsync(conDano: ["VIN-PAR-N2", "VIN-PAR-N3"]);
        var hallazgoDeN3 = await HallazgoDeAsync(e.Llegadas["VIN-PAR-N3"]);

        // El carro N1 no tiene ningun dano: no se rechaza.
        var sinDano = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, rechazos: [new { vin = "VIN-PAR-N1", hallazgo_id = hallazgoDeN3 }]);
        Assert.Equal(HttpStatusCode.Conflict, sinDano.StatusCode);
        var error = await JsonAsync(sinDano);
        Assert.Equal("sin_novedad_para_rechazar", Motivo(error));
        Assert.Equal("El VIN VIN-PAR-N1 no tiene un daño que justifique el rechazo: entrégalo en esta parada", Mensaje(error));

        // N3 tiene dano con foto: sin el dano elegido, con el de otro carro (otra revision) o con un VIN ajeno a la parada es 400.
        var sinElegir = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, rechazos: [new { vin = "VIN-PAR-N3" }]);
        var deOtraRevision = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, rechazos: [new { vin = "VIN-PAR-N3", hallazgo_id = await HallazgoDeAsync(e.Llegadas["VIN-PAR-N2"]) }]);
        var ajeno = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, rechazos: [new { vin = "VIN-OTRO-9", hallazgo_id = hallazgoDeN3 }]);
        foreach (var r in new[] { sinElegir, deOtraRevision, ajeno })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        Assert.Contains("Para no entregar un carro elige el daño de su revisión de llegada que lo motiva", (await JsonAsync(sinElegir)).GetProperty("details").GetProperty("fieldErrors").GetProperty("rechazos")[0].GetString());
        Assert.Contains("VIN-OTRO-9", (await JsonAsync(ajeno)).GetProperty("details").GetProperty("fieldErrors").GetProperty("rechazos")[0].GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Si_se_rechazan_todos_los_carros_la_parada_igual_se_firma_como_constancia_y_cada_rechazo_avisa()
    {
        var e = await EscenarioAsync(conDano: VINES_PUERTO);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        var rechazos = new List<object>();
        foreach (var vin in VINES_PUERTO)
        {
            rechazos.Add(new { vin, hallazgo_id = await HallazgoDeAsync(e.Llegadas[vin]) });
        }

        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Pedro, e.Viaje, e.Puerto, new { firma = "f-pedro", rechazos })).StatusCode);
        var cierre = await FirmarAsync(e.Conductor, e.Viaje, e.Puerto);

        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        var punto = await JsonAsync(cierre);
        Assert.Equal("firmada_todos_rechazados", punto.GetProperty("estado_parada").GetString());
        Assert.False(punto.GetProperty("entregada").GetBoolean());
        Assert.All(punto.GetProperty("unidades").EnumerateArray(), u => Assert.Equal("rechazado", u.GetProperty("estado").GetString()));
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'rechazado' AND FirmaEntrega IS NOT NULL AND FirmaRecibe IS NOT NULL"));
        Assert.Equal(3, (await JsonAsync(await Como(e.Coordinador).GetAsync("/api/notificaciones?tipo=carro_rechazado_en_destino"))).GetProperty("items").GetArrayLength());
        Assert.Equal("en_curso", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
        // Una parada con todos rechazados no se vuelve a firmar.
        Assert.Equal(HttpStatusCode.Conflict, (await FirmarAsync(e.Conductor, e.Viaje, e.Puerto)).StatusCode);
    }

    [Fact]
    public async Task El_coordinador_le_cambia_el_destino_al_carro_rechazado_aunque_su_revision_este_cerrada_y_el_viaje_sigue_en_curso_hasta_que_se_entregue_en_el_destino_nuevo()
    {
        var e = await EscenarioAsync(conDano: ["VIN-PAR-N2"]);
        var hallazgo = await HallazgoDeAsync(e.Llegadas["VIN-PAR-N2"]);
        Assert.Equal(HttpStatusCode.OK, (await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, rechazos: [new { vin = "VIN-PAR-N2", hallazgo_id = hallazgo }])).StatusCode);

        // Un carro entregado conserva el 409 de siempre; el rechazado cambia de destino.
        var entregado = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-PAR-N1", Cuerpo(new { lugar_destino_id = e.Sur }));
        Assert.Equal(HttpStatusCode.Conflict, entregado.StatusCode);
        Assert.Equal("descargue_con_informe_cerrado", Motivo(await JsonAsync(entregado)));
        var cambio = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-PAR-N2", Cuerpo(new { lugar_destino_id = e.Sur }));
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);

        // La revision del rechazo y su transferencia se conservan como evidencia; el carro queda pendiente en el destino nuevo.
        Assert.Equal("rechazado", await TransferenciaAsync(e.Llegadas["VIN-PAR-N2"], "TipoCierre"));
        var sur = await PuntoAsync(e.Conductor, e.Viaje, e.Sur);
        var enSur = Unidad(sur, "VIN-PAR-N2");
        Assert.Equal("sin_inspeccion", enSur.GetProperty("estado").GetString());
        Assert.Equal(2, sur.GetProperty("unidades_total").GetInt32());
        Assert.Equal(2, (await PuntoAsync(e.Conductor, e.Viaje, e.Norte)).GetProperty("unidades_total").GetInt32());

        // Su revision nueva es otro ciclo de la misma etapa (la del rechazo no se toca) y se entrega con las reglas de siempre.
        // El Sur tiene inspectora: la revision de llegada la hace Diana, que toma la entrega de esa parada.
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Diana, e.Viaje, e.Sur)).StatusCode);
        var descargue = await EtapaAsync(e.Viaje, "VIN-PAR-N2", "descargue");
        var creada = await Como(e.Diana).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = descargue, momento = "llegada", fecha = MOMENTO, lat = 4.6, lng = -74.1 }));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'llegada'", ("@e", descargue)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'llegada' AND Ciclo = 2", ("@e", descargue)));
        Assert.Equal("en_curso", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
        // Un segundo ciclo igual en la misma etapa no se admite.
        Assert.Equal(HttpStatusCode.Conflict, (await Como(e.Diana).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = descargue, momento = "llegada", fecha = MOMENTO, lat = 4.6, lng = -74.1 }))).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------ las reservas

    [Fact]
    public async Task Un_carro_recibido_con_reservas_cierra_con_las_dos_firmas_y_las_observaciones_y_los_demas_firmada()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);

        // El conductor no deja reservas donde recibe un inspector.
        var delConductor = await FirmarAsync(e.Conductor, e.Viaje, e.Puerto, new { firma = "f", reservas = new[] { new { vin = "VIN-PAR-P2", observaciones = "no le toca" } } });
        Assert.Equal(HttpStatusCode.Forbidden, delConductor.StatusCode);
        var ajeno = await FirmarAsync(e.Pedro, e.Viaje, e.Puerto, new { firma = "f", reservas = new[] { new { vin = "VIN-PAR-N1", observaciones = "de otra parada" } } });
        Assert.Equal(HttpStatusCode.BadRequest, ajeno.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Pedro, e.Viaje, e.Puerto, new { firma = "f-pedro", reservas = new[] { new { vin = "VIN-PAR-P2", observaciones = "Llegó sucio y sin manual" } } })).StatusCode);
        var cierre = await JsonAsync(await FirmarAsync(e.Conductor, e.Viaje, e.Puerto));

        var p2 = Unidad(cierre, "VIN-PAR-P2");
        Assert.Equal("con_reservas", p2.GetProperty("tipo_cierre").GetString());
        Assert.Equal("Llegó sucio y sin manual", p2.GetProperty("reservas").GetString());
        Assert.Equal("firmada", Unidad(cierre, "VIN-PAR-P1").GetProperty("tipo_cierre").GetString());
        Assert.Equal(JsonValueKind.Null, Unidad(cierre, "VIN-PAR-P1").GetProperty("reservas").ValueKind);
    }

    [Fact]
    public async Task Si_quien_entrega_ya_firmo_cuando_aparecen_reservas_ese_carro_espera_su_firma_otra_vez_y_los_otros_cierran()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, e.Puerto, new { firma = "f-conductor" })).StatusCode);

        var punto = await JsonAsync(await FirmarAsync(e.Pedro, e.Viaje, e.Puerto, new { firma = "f-pedro", reservas = new[] { new { vin = "VIN-PAR-P3", observaciones = "Rayón en la puerta" } } }));

        Assert.Equal("cerrada", Unidad(punto, "VIN-PAR-P1").GetProperty("estado").GetString());
        var p3 = Unidad(punto, "VIN-PAR-P3");
        Assert.Equal("pendiente", p3.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, p3.GetProperty("entrega_firmada_at").ValueKind);
        Assert.Equal(1, punto.GetProperty("unidades_pendientes").GetInt32());

        var final = await JsonAsync(await FirmarAsync(e.Conductor, e.Viaje, e.Puerto, new { firma = "f-conductor-2" }));
        Assert.Equal("con_reservas", Unidad(final, "VIN-PAR-P3").GetProperty("tipo_cierre").GetString());
        Assert.Equal("entregada", final.GetProperty("estado_parada").GetString());
    }

    [Fact]
    public async Task Las_reservas_de_la_persona_del_lugar_no_se_borran_al_firmar_y_las_observaciones_vacias_o_largas_se_rechazan()
    {
        var e = await EscenarioAsync();

        var vacia = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, reservas: [new { vin = "VIN-PAR-N1", observaciones = "  " }]);
        var larga = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, reservas: [new { vin = "VIN-PAR-N1", observaciones = new string('x', 1001) }]);
        var repetida = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, reservas: [new { vin = "VIN-PAR-N1", observaciones = "a" }, new { vin = "vin-par-n1", observaciones = "b" }]);
        foreach (var r in new[] { vacia, larga, repetida })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        var ok = await JsonAsync(await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte, reservas: [new { vin = "VIN-PAR-N1", observaciones = "Falta el gato" }]));
        Assert.Equal("con_reservas", Unidad(ok, "VIN-PAR-N1").GetProperty("tipo_cierre").GetString());
        Assert.Equal("Falta el gato", Unidad(ok, "VIN-PAR-N1").GetProperty("reservas").GetString());
        Assert.Equal("firmada", Unidad(ok, "VIN-PAR-N2").GetProperty("tipo_cierre").GetString());
    }

    // ------------------------------------------------------------------------------------------------ hora y GPS del momento

    [Fact]
    public async Task Cada_firma_guarda_la_hora_y_el_gps_del_momento_aparte_de_la_hora_de_envio_y_sin_gps_queda_sin_ubicacion()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, e.Puerto, new { firma = "f-1", fecha_momento = MOMENTO, lat = 10.39, lng = -75.48, precision_m = 6.5 })).StatusCode);
        var punto = await JsonAsync(await FirmarAsync(e.Pedro, e.Viaje, e.Puerto, new { firma = "f-2", fecha_momento = MOMENTO }));

        var u = Unidad(punto, "VIN-PAR-P1");
        var entrega = u.GetProperty("entrega_momento");
        Assert.Equal(DateTimeOffset.Parse(MOMENTO), entrega.GetProperty("momento").GetDateTimeOffset());
        Assert.Equal(10.39, entrega.GetProperty("lat").GetDouble());
        Assert.Equal(6.5, entrega.GetProperty("precision_m").GetDouble());
        Assert.False(entrega.GetProperty("sin_ubicacion").GetBoolean());
        var recibe = u.GetProperty("recibe_momento");
        Assert.Equal(DateTimeOffset.Parse(MOMENTO), recibe.GetProperty("momento").GetDateTimeOffset());
        Assert.True(recibe.GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal(JsonValueKind.Null, recibe.GetProperty("lat").ValueKind);
        // La hora de envio es otra: la del servidor, no la del momento.
        Assert.NotEqual(DateTimeOffset.Parse(MOMENTO), u.GetProperty("entrega_firmada_at").GetDateTimeOffset());
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE FirmaEntregaLat = 10.39 AND FirmaRecibeLat IS NULL AND FirmaRecibeMomentoAt IS NOT NULL"));
    }

    [Fact]
    public async Task Una_firma_sin_hora_ni_gps_se_acepta_y_una_hora_o_un_gps_invalidos_se_rechazan_con_400()
    {
        var e = await EscenarioAsync();

        var horaMala = await FirmarConExternaAsync(e.Conductor, e.Viaje, e.Norte);
        var fecha = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Norte}/firma", Cuerpo(new { firma = "f", recibe_nombre = "Rosa Ríos", recibe_cedula = "52123456", firma_recibe = "fr", fecha_momento = "ayer" }));
        var gps = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Sur}/una-firma", Cuerpo(new { firma = "f", motivo_codigo = "no_habia_nadie", lat = 95, lng = -75 }));
        var solaLat = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Sur}/una-firma", Cuerpo(new { firma = "f", motivo_codigo = "no_habia_nadie", lat = 4.5 }));

        // La primera firma (sin hora ni GPS) se acepto; las otras se rechazan nombrando el campo.
        Assert.Equal(HttpStatusCode.OK, horaMala.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fecha.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, gps.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, solaLat.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @i AND FirmaEntregaMomentoAt IS NOT NULL", ("@i", e.Llegadas["VIN-PAR-N1"])));
        var invalida = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Sur}/una-firma", Cuerpo(new { firma = "f", motivo_codigo = "no_habia_nadie", fecha_momento = "ayer" }));
        Assert.Equal("fecha_momento", (await JsonAsync(invalida)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Single().Name);
    }

    // ------------------------------------------------------------------------------------------------ el cierre con una sola firma

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var bytes = PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var r = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await JsonAsync(r);
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    private async Task<object> FotoAsync(Sesion quien, string vin)
    {
        var (ruta, hash) = await SubirAsync(quien);
        return new { vin, storage_path = ruta, sello_fecha = MOMENTO, sello_lat = 4.6, sello_lng = -74.1, precision = 5.0, hash_sha256 = hash };
    }

    [Fact]
    public async Task El_cierre_con_una_sola_firma_exige_su_motivo_codificado_y_se_nego_a_firmar_exige_al_menos_una_foto()
    {
        var e = await EscenarioAsync();

        var sinMotivo = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f" });
        var otroMotivo = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "falta_espacio" });
        var motivoViejo = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo = "texto libre" });
        var sinFoto = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "se_nego_a_firmar" });
        var textoLargo = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "no_habia_nadie", motivo_texto = new string('x', 301) });
        var soloNombre = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "no_habia_nadie", testigo_nombre = "Andrés Pardo" });
        var fotoAjena = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "se_nego_a_firmar", fotos = new[] { await FotoAsync(e.Conductor, "VIN-PAR-P1") } });

        foreach (var r in new[] { sinMotivo, otroMotivo, motivoViejo, sinFoto, textoLargo, soloNombre, fotoAjena })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        Assert.Equal("Con este motivo la foto es obligatoria", (await JsonAsync(sinFoto)).GetProperty("details").GetProperty("fieldErrors").GetProperty("fotos")[0].GetString());
        Assert.Contains("testigo_cedula", (await JsonAsync(soloNombre)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Quien_recibe_se_nego_a_firmar_cierra_con_su_foto_y_el_aviso_dice_el_motivo_sin_cedulas()
    {
        var e = await EscenarioAsync();
        var foto = await FotoAsync(e.Conductor, "VIN-PAR-N1");

        var r = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new
        {
            firma = "f-conductor", motivo_codigo = "se_nego_a_firmar", motivo_texto = "Dijo que no recibía sin el manual", fotos = new[] { foto },
            testigo_nombre = "Andrés Pardo", testigo_cedula = "79 123 456", fecha_momento = MOMENTO, lat = 4.71, lng = -74.07,
        });

        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var punto = await JsonAsync(r);
        var n1 = Unidad(punto, "VIN-PAR-N1");
        Assert.Equal("recibo_sin_entrega", n1.GetProperty("tipo_cierre").GetString());
        Assert.Equal("se_nego_a_firmar", n1.GetProperty("motivo_codigo").GetString());
        Assert.Equal("Dijo que no recibía sin el manual", n1.GetProperty("motivo_cierre").GetString());
        Assert.Equal(1, n1.GetProperty("fotos_de_cierre").GetInt32());
        Assert.Equal(0, Unidad(punto, "VIN-PAR-N2").GetProperty("fotos_de_cierre").GetInt32());
        // Las fotos del cierre llegan como lista, con el enlace firmado y el sello de cada una.
        var fotosDelCierre = n1.GetProperty("fotos_cierre").EnumerateArray().ToList();
        var fotoDelCierre = Assert.Single(fotosDelCierre);
        Assert.StartsWith("http", fotoDelCierre.GetProperty("url").GetString());
        Assert.Equal(4.6, fotoDelCierre.GetProperty("sello_lat").GetDouble());
        Assert.Equal(-74.1, fotoDelCierre.GetProperty("sello_lng").GetDouble());
        Assert.Equal(5.0, fotoDelCierre.GetProperty("precision").GetDouble());
        Assert.False(fotoDelCierre.GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal(0, Unidad(punto, "VIN-PAR-N2").GetProperty("fotos_cierre").GetArrayLength());
        Assert.Equal("Andrés Pardo", n1.GetProperty("testigo_nombre").GetString());
        Assert.Equal(DateTimeOffset.Parse(MOMENTO), n1.GetProperty("entrega_momento").GetProperty("momento").GetDateTimeOffset());
        Assert.Equal(4.71, n1.GetProperty("entrega_momento").GetProperty("lat").GetDouble());
        Assert.Equal("79 123 456", await TransferenciaAsync(e.Llegadas["VIN-PAR-N1"], "TestigoCedula"));
        Assert.Equal("se_nego_a_firmar", await Sembrador.EscalarAsync("SELECT TOP 1 MotivoCodigo FROM dbo.tblNovedadesProceso WHERE ViajeId = @v AND Tipo = 'recibo_sin_entrega'", ("@v", e.Viaje)));
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE ViajeId = @v AND MotivoCodigo = 'se_nego_a_firmar'", ("@v", e.Viaje)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsConstancia = 1 AND InspeccionId = @i", ("@i", e.Llegadas["VIN-PAR-N1"])));

        var aviso = Assert.Single((await JsonAsync(await Como(e.Coordinador).GetAsync("/api/notificaciones?tipo=novedad_proceso"))).GetProperty("items").EnumerateArray());
        Assert.Equal("alta", aviso.GetProperty("urgencia").GetString());
        Assert.Equal("Quien recibe se negó a firmar: Concesionario Norte", aviso.GetProperty("titulo").GetString());
        var datos = aviso.GetProperty("datos");
        Assert.Equal("entrega_con_una_firma", datos.GetProperty("hecho").GetString());
        Assert.Equal("se_nego_a_firmar", datos.GetProperty("motivo_codigo").GetString());
        Assert.Equal(1, datos.GetProperty("fotos").GetInt32());
        Assert.Equal("Andrés Pardo", datos.GetProperty("testigo").GetString());
        Assert.Equal(e.Norte, datos.GetProperty("lugar_id").GetGuid());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Mensaje LIKE N'%79 123 456%' OR Datos LIKE N'%79 123 456%' OR Titulo LIKE N'%79 123 456%'"));
    }

    [Fact]
    public async Task No_habia_nadie_cierra_sin_fotos_propias_donde_no_hay_inspectores_y_responde_sin_receptor_donde_los_hay()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);

        var conInspector = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Puerto, new { firma = "f", motivo_codigo = "no_habia_nadie" });
        Assert.Equal(HttpStatusCode.Conflict, conInspector.StatusCode);
        Assert.Equal("sin_receptor", Motivo(await JsonAsync(conInspector)));

        var r = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "no_habia_nadie", testigo_nombre = "Andrés Pardo", testigo_cedula = "79123456" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var punto = await JsonAsync(r);
        Assert.All(punto.GetProperty("unidades").EnumerateArray(), u =>
        {
            Assert.Equal("recibo_sin_entrega", u.GetProperty("tipo_cierre").GetString());
            Assert.Equal("no_habia_nadie", u.GetProperty("motivo_codigo").GetString());
            Assert.Equal("No había nadie para recibir", u.GetProperty("motivo_cierre").GetString());
            Assert.Equal(0, u.GetProperty("fotos_de_cierre").GetInt32());
        });
        Assert.Equal("recibe", Unidad(punto, "VIN-PAR-N1").GetProperty("no_firma_parte").GetString());
        var aviso = Assert.Single((await JsonAsync(await Como(e.Coordinador).GetAsync("/api/notificaciones?tipo=novedad_proceso"))).GetProperty("items").EnumerateArray());
        Assert.Equal("No había nadie para recibir: Concesionario Norte", aviso.GetProperty("titulo").GetString());
        Assert.Equal(0, aviso.GetProperty("datos").GetProperty("fotos").GetInt32());
        // El lugar y el nombre del testigo viajan con cada motivo; la cedula, nunca.
        Assert.Equal(e.Norte, aviso.GetProperty("datos").GetProperty("lugar_id").GetGuid());
        Assert.Equal("Andrés Pardo", aviso.GetProperty("datos").GetProperty("testigo").GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Mensaje LIKE N'%79123456%' OR Datos LIKE N'%79123456%'"));
    }

    [Fact]
    public async Task El_motivo_sin_inspector_del_punto_no_aplica_en_una_parada_sin_inspectores_y_el_cierre_de_la_otra_parte_ya_firmada_sigue_siendo_409()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);

        var plan = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte, new { firma = "f", motivo_codigo = "sin_inspector_del_punto" });
        Assert.Equal(HttpStatusCode.Conflict, plan.StatusCode);
        Assert.Equal("motivo_no_aplica", Motivo(await JsonAsync(plan)));

        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        var yaFirmo = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Puerto, new { firma = "f", motivo_codigo = "se_nego_a_firmar", fotos = new[] { await FotoAsync(e.Conductor, "VIN-PAR-P1") } });
        Assert.Equal(HttpStatusCode.Conflict, yaFirmo.StatusCode);
    }

    [Fact]
    public async Task El_inspector_de_la_parada_tambien_cierra_con_su_sola_firma_y_el_que_no_es_de_la_parada_recibe_403()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReclamarEntregaAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);

        var ajeno = await UnaFirmaAsync(e.Diana, e.Viaje, e.Puerto, new { firma = "f", motivo_codigo = "se_nego_a_firmar", fotos = new[] { await FotoAsync(e.Diana, "VIN-PAR-P1") } });
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);

        var r = await UnaFirmaAsync(e.Pedro, e.Viaje, e.Puerto, new { firma = "f-pedro", motivo_codigo = "se_nego_a_firmar", fotos = new[] { await FotoAsync(e.Pedro, "VIN-PAR-P1") } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("entrega", Unidad(await JsonAsync(r), "VIN-PAR-P1").GetProperty("no_firma_parte").GetString());
        Assert.Equal(e.Pedro.UsuarioId, await TransferenciaAsync(e.Llegadas["VIN-PAR-P1"], "RecibeUsuarioId"));
        Assert.Null(await TransferenciaAsync(e.Llegadas["VIN-PAR-P1"], "EntregaUsuarioId"));
    }

    [Fact]
    public async Task Las_entregas_cerradas_con_una_sola_firma_antes_del_cambio_se_leen_con_su_motivo_como_otro_y_sin_foto()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaRecibe, FirmaRecibeAt, TipoCierre, NoFirmaParte, MotivoNoFirma) VALUES (NEWID(), @i, NULL, @r, N'trazo', SYSDATETIMEOFFSET(), 'recibo_sin_entrega', 'entrega', N'El conductor no llego')",
            ("@i", e.Llegadas["VIN-PAR-N1"]), ("@r", e.Pedro.UsuarioId));

        var punto = await PuntoAsync(e.Coordinador, e.Viaje, e.Norte);

        var u = Unidad(punto, "VIN-PAR-N1");
        Assert.Equal("cerrada", u.GetProperty("estado").GetString());
        Assert.Equal("otro", u.GetProperty("motivo_codigo").GetString());
        Assert.Equal("El conductor no llego", u.GetProperty("motivo_cierre").GetString());
        Assert.Equal(0, u.GetProperty("fotos_de_cierre").GetInt32());
        Assert.Equal(JsonValueKind.Null, u.GetProperty("testigo_nombre").ValueKind);
        Assert.Equal(JsonValueKind.Null, u.GetProperty("rechazo").ValueKind);
        Assert.Equal(JsonValueKind.Null, Unidad(punto, "VIN-PAR-N2").GetProperty("motivo_codigo").ValueKind);
    }
}
