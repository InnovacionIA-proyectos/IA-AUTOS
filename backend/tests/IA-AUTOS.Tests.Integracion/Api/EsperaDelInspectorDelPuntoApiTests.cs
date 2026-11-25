using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Alertas;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La espera del inspector del punto: el conductor la activa en una parada con inspectores donde nadie tomo la entrega; desde su hora corren los plazos que ajusta TI (el aviso al coordinador y el plan B, evaluados dentro de cada
/// peticion), un inspector que toma la entrega la termina, el cierre sin inspector del punto es definitivo, la revision de llegada del conductor solo existe con el plan B, el inspector que llega califica los danos que marco
/// el conductor antes de firmar y, con el plan B, el conductor puede rechazar un carro por una novedad.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class EsperaDelInspectorDelPuntoApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private static readonly byte[] PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Quien hizo la revision de llegada de los carros del Puerto al preparar el escenario.</summary>
    private enum Llegadas
    {
        Ninguna,
        Inspector,
        Conductor,
    }

    private sealed record Esc(Guid Viaje, Guid Puerto, Guid Norte, Sesion Conductor, Sesion Coordinador, Sesion Pedro, Sesion Diana, Sesion Ana, Dictionary<string, Guid> Llegadas, Dictionary<string, Guid> Danos);

    private static readonly string[] VINES_PUERTO = ["VIN-ESP-P1", "VIN-ESP-P2", "VIN-ESP-P3"];
    private const string VIN_NORTE = "VIN-ESP-N1";

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

    /// <summary>
    /// El viaje MT-6001 en curso, cargado por Ana, con tres carros para el Puerto (que tiene a Pedro y a Diana como inspectores) y uno para Norte (sin inspectores). Cada carro tiene la llegada de Transporte confirmada
    /// («Llegue») salvo que <paramref name="conLlegue"/> sea falso; la revision de llegada del Descargue de los carros del Puerto la hizo quien diga <paramref name="llegadas"/> (con un dano del conductor sin nivel y con foto en
    /// el VIN indicado en <paramref name="conDanoEn"/>).
    /// </summary>
    private async Task<Esc> EscenarioAsync(Llegadas llegadas = Llegadas.Ninguna, bool conLlegue = true, string? conDanoEn = null, bool norteSinRevision = false)
    {
        var conductor = await SesionAsync("luis@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("carlos@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var pedro = await SesionAsync("pedro@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.INSPECTOR);
        var ana = await SesionAsync("ana@ejemplo.com", clsRol.INSPECTOR);
        var puerto = await LugarRealAsync(Como(coordinador), "Puerto de Cartagena", "destino", "Cartagena");
        var norte = await LugarRealAsync(Como(coordinador), "Concesionario Norte", "concesionario", "Bogota");
        var cargue = await LugarRealAsync(Como(coordinador), "CEDI de prueba", "cedi_1");
        await AsignarPuntoAsync(pedro, puerto);
        await AsignarPuntoAsync(diana, puerto);
        foreach (var vin in VINES_PUERTO.Append(VIN_NORTE))
        {
            await Sembrador.VehiculoAsync(vin);
        }

        var unidadTipo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;
        object[] Unidades(Guid lugar, IEnumerable<string> vines) => [.. vines.Select(v => (object)new { vin = v, lugar_destino_id = lugar })];
        var creado = await Como(coordinador).PostAsync("/api/viajes", Cuerpo(new
        {
            codigo_mtrack = "MT-6001",
            tipo_unidad_id = unidadTipo,
            placa = "ABC123",
            conductor_id = conductor.UsuarioId,
            inspector_id = ana.UsuarioId,
            fecha_programada = "2026-11-18T06:00:00-05:00",
            lugar_cargue_id = cargue,
            unidades = Unidades(puerto, VINES_PUERTO).Concat(Unidades(norte, [VIN_NORTE])).ToArray(),
        }));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var viaje = (await JsonAsync(creado)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Como(coordinador).PatchAsync($"/api/viajes/{viaje}/estado", Cuerpo(new { estado = "en_curso" }))).StatusCode);

        var zona = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Numero = 50"))!;
        var revisiones = new Dictionary<string, Guid>();
        var danos = new Dictionary<string, Guid>();
        foreach (var vin in VINES_PUERTO.Append(VIN_NORTE))
        {
            if (conLlegue)
            {
                await Sembrador.InformeAsync(await EtapaAsync(viaje, vin, "transporte"), conductor.UsuarioId, cerrada: true, momento: "llegada");
            }

            var descargue = await EtapaAsync(viaje, vin, "descargue");
            if (vin == VIN_NORTE)
            {
                if (!norteSinRevision)
                {
                    revisiones[vin] = await Sembrador.InformeAsync(descargue, pedro.UsuarioId, cerrada: true, momento: "llegada");
                }
            }
            else if (llegadas == Llegadas.Inspector)
            {
                revisiones[vin] = await Sembrador.InformeAsync(descargue, pedro.UsuarioId, cerrada: true, momento: "llegada");
            }
            else if (llegadas == Llegadas.Conductor)
            {
                if (vin == conDanoEn)
                {
                    var (inspeccion, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(descargue, conductor.UsuarioId, zona, "golpe", "llegada", nivel: null, conFoto: true, origen: "conductor");
                    revisiones[vin] = inspeccion;
                    danos[vin] = hallazgo;
                }
                else
                {
                    revisiones[vin] = await Sembrador.InformeAsync(descargue, conductor.UsuarioId, cerrada: true, momento: "llegada");
                }
            }
        }

        return new Esc(viaje, puerto, norte, conductor, coordinador, pedro, diana, ana, revisiones, danos);
    }

    private static string AhoraIso(int minutosAtras = 0) => DateTimeOffset.UtcNow.AddMinutes(-minutosAtras).ToString("O");

    private Task<HttpResponseMessage> ActivarAsync(Sesion quien, Guid viaje, Guid lugar, object? cuerpo = null) =>
        Como(quien).PostAsync(
            $"/api/viajes/{viaje}/entregas/{lugar}/sin-inspector",
            Cuerpo(cuerpo ?? new { motivo_sin_inspector = "inspector_no_llego", fecha_momento = AhoraIso(), lat = 10.39, lng = -75.48, precision_m = 12 }));

    /// <summary>Activa la espera y la envejece: como si el conductor la hubiera activado hace <paramref name="minutos"/> minutos.</summary>
    private async Task EsperaActivadaHaceAsync(Esc e, int minutos)
    {
        Assert.Equal(HttpStatusCode.Created, (await ActivarAsync(e.Conductor, e.Viaje, e.Puerto)).StatusCode);
        await EnvejecerEsperaAsync(e, minutos);
    }

    private Task EnvejecerEsperaAsync(Esc e, int minutos) =>
        Sembrador.EjecutarAsync(
            "UPDATE dbo.tblEsperasInspectorPunto SET ActivadaAt = DATEADD(MINUTE, -@m, ActivadaAt), CreatedAt = DATEADD(MINUTE, -@m, CreatedAt) WHERE ViajeId = @v AND LugarId = @l AND Estado = 'esperando'",
            ("@m", minutos), ("@v", e.Viaje), ("@l", e.Puerto));

    private Task<HttpResponseMessage> FirmarConPersonaAsync(Sesion conductor, Guid viaje, Guid lugar, IReadOnlyList<object>? rechazos = null) =>
        Como(conductor).PostAsync($"/api/viajes/{viaje}/entregas/{lugar}/firma", Cuerpo(new Dictionary<string, object?>
        {
            ["firma"] = "trazo-conductor",
            ["recibe_nombre"] = "Andrés Pardo",
            ["recibe_cedula"] = "52 123 456",
            ["firma_recibe"] = "trazo-andres",
            ["rechazos"] = rechazos,
            ["fecha_momento"] = AhoraIso(),
            ["lat"] = 10.39,
            ["lng"] = -75.48,
        }));

    private Task<HttpResponseMessage> UnaFirmaAsync(Sesion quien, Guid viaje, Guid lugar, string codigo = "sin_inspector_del_punto") =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/entregas/{lugar}/una-firma", Cuerpo(new { firma = "trazo-conductor", motivo_codigo = codigo, fecha_momento = AhoraIso(), lat = 10.39, lng = -75.48 }));

    private Task<HttpResponseMessage> ReclamarAsync(Sesion quien, Guid viaje, Guid lugar) =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/reclamos", Cuerpo(new { proceso = "entrega", lugar_id = lugar }));

    private async Task<JsonElement> PuntoAsync(Sesion quien, Guid viaje, Guid lugar) => await JsonAsync(await Como(quien).GetAsync($"/api/viajes/{viaje}/entregas/{lugar}"));

    private static JsonElement Unidad(JsonElement punto, string vin) => punto.GetProperty("unidades").EnumerateArray().Single(u => u.GetProperty("vin").GetString() == vin);

    private Task<int> NotificacionesAsync(string tipo, Guid? usuario = null) =>
        Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = @t AND Canal = 'bandeja' AND (@u IS NULL OR UsuarioId = @u)", ("@t", tipo), ("@u", (object?)usuario ?? DBNull.Value));

    private async Task<T> ConProcesosAsync<T>(Func<clsAlertasPeriodicas, Task<T>> accion)
    {
        using var alcance = _api.Services.CreateScope();
        return await accion(alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>());
    }

    private async Task<string?> EstadoDeLaEsperaAsync(Guid viaje, Guid lugar) =>
        (string?)await Sembrador.EscalarAsync("SELECT TOP 1 Estado FROM dbo.tblEsperasInspectorPunto WHERE ViajeId = @v AND LugarId = @l ORDER BY ActivadaAt DESC, CreatedAt DESC", ("@v", viaje), ("@l", lugar));

    private Task<int> AuditoriaAsync(string accion) => Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = @a", ("@a", accion));

    // ------------------------------------------------------------------------------------------ la activacion

    [Fact]
    public async Task El_conductor_activa_la_espera_con_su_motivo_su_hora_y_su_gps_y_activarla_de_nuevo_devuelve_la_misma_sin_reiniciar_el_reloj()
    {
        var e = await EscenarioAsync();

        var creada = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto);
        var espera = await JsonAsync(creada);
        await EnvejecerEsperaAsync(e, 7);
        var repetida = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto);
        var segunda = await JsonAsync(repetida);

        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        Assert.Equal("esperando", espera.GetProperty("estado").GetString());
        Assert.Equal("inspector_no_llego", espera.GetProperty("motivo_sin_inspector").GetString());
        Assert.Equal(10.39, espera.GetProperty("lat").GetDouble());
        Assert.Equal(12, espera.GetProperty("precision_m").GetDouble());
        Assert.True(espera.GetProperty("activada_at").GetDateTimeOffset() <= espera.GetProperty("recibida_at").GetDateTimeOffset());
        Assert.False(espera.GetProperty("plan_b_habilitado").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(espera.GetProperty("id").GetGuid(), segunda.GetProperty("id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto"));
        // El reloj sigue contando desde la primera activacion (la envejecida), no desde la repetida.
        Assert.True(segunda.GetProperty("activada_at").GetDateTimeOffset() < DateTimeOffset.UtcNow.AddMinutes(-6));
        Assert.Equal(1, await AuditoriaAsync("espera_inspector_punto.activar"));
    }

    [Fact]
    public async Task La_hora_del_celular_posterior_a_la_del_servidor_se_guarda_como_la_del_servidor()
    {
        var e = await EscenarioAsync();

        var r = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto, new { motivo_sin_inspector = "inspector_no_llego", fecha_momento = AhoraIso(-90) });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto WHERE ActivadaAt <= CreatedAt"));
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(r)).GetProperty("lat").ValueKind);
    }

    [Fact]
    public async Task Solo_el_conductor_del_viaje_activa_la_espera_y_nadie_mas_la_activa()
    {
        var e = await EscenarioAsync();
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var otroConductor = await SesionAsync("otro@ejemplo.com", clsRol.CONDUCTOR);

        foreach (var quien in new[] { e.Pedro, e.Coordinador, admin, superAdmin, otroConductor })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ActivarAsync(quien, e.Viaje, e.Puerto)).StatusCode);
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto"));
    }

    [Fact]
    public async Task La_activacion_rechaza_con_su_motivo_una_parada_sin_inspectores_y_una_entrega_tomada()
    {
        var e = await EscenarioAsync();

        var norte = await ActivarAsync(e.Conductor, e.Viaje, e.Norte);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        var tomada = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto);

        Assert.Equal((HttpStatusCode.Conflict, "parada_sin_inspectores"), (norte.StatusCode, Motivo(await JsonAsync(norte))));
        Assert.Equal((HttpStatusCode.Conflict, "entrega_tomada"), (tomada.StatusCode, Motivo(await JsonAsync(tomada))));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto"));
    }

    [Fact]
    public async Task La_activacion_rechaza_una_parada_donde_el_conductor_aun_no_marco_llegue()
    {
        var e = await EscenarioAsync(conLlegue: false);

        var r = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto);

        Assert.Equal((HttpStatusCode.Conflict, "sin_llegue_en_parada"), (r.StatusCode, Motivo(await JsonAsync(r))));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto"));
    }

    [Fact]
    public async Task La_activacion_rechaza_una_entrega_ya_cerrada()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 50);
        Assert.Equal(HttpStatusCode.OK, (await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto)).StatusCode);

        var cerrada = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto);

        Assert.Equal((HttpStatusCode.Conflict, "entrega_cerrada"), (cerrada.StatusCode, Motivo(await JsonAsync(cerrada))));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto"));
    }

    [Fact]
    public async Task Un_motivo_que_no_existe_o_un_texto_que_el_motivo_no_admite_se_rechazan_con_su_campo()
    {
        var e = await EscenarioAsync();

        var sinMotivo = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto, new { fecha_momento = AhoraIso() });
        var inventado = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto, new { motivo_sin_inspector = "inventado" });
        var conTexto = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto, new { motivo_sin_inspector = "inspector_no_llego", motivo_sin_inspector_texto = "no estaba" });
        var gpsIncompleto = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto, new { motivo_sin_inspector = "inspector_no_llego", lat = 10.4 });

        foreach (var (r, campo) in new[] { (sinMotivo, "motivo_sin_inspector"), (inventado, "motivo_sin_inspector"), (conTexto, "motivo_sin_inspector_texto"), (gpsIncompleto, "lat") })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True((await JsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty(campo, out _), campo);
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto"));
    }

    // ------------------------------------------------------------------------------------------ «Llegue» y el aviso

    [Fact]
    public async Task Marcar_llegue_no_crea_ninguna_espera_ni_ningun_aviso_por_mucho_tiempo_que_pase_ni_habilita_el_plan_b()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await Sembrador.EjecutarAsync("ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL; UPDATE dbo.tblInspecciones SET IniciadaAt = DATEADD(HOUR, -3, IniciadaAt), CerradaAt = DATEADD(HOUR, -3, CerradaAt); ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL");

        var avisos = await ConProcesosAsync(p => p.EjecutarEsperasDelDestinoAsync());
        var firma = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Puerto}/firma", Cuerpo(new { firma = "trazo-conductor" }));
        var unaFirma = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Puerto);

        Assert.Equal(0, avisos);
        Assert.Equal(0, await NotificacionesAsync("espera_inspector_punto"));
        Assert.Equal("sin_receptor", Motivo(await JsonAsync(firma)));
        Assert.Equal("sin_receptor", Motivo(await JsonAsync(unaFirma)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto"));
    }

    [Fact]
    public async Task A_los_15_minutos_de_activar_la_espera_el_coordinador_recibe_un_solo_aviso_urgente_con_sus_datos()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await Como(e.Coordinador).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-coord", plataforma = "android" });
        await EsperaActivadaHaceAsync(e, 14);
        await ConProcesosAsync(p => p.EjecutarEsperasDelDestinoAsync());
        Assert.Equal(0, await NotificacionesAsync("espera_inspector_punto"));

        await EnvejecerEsperaAsync(e, 2);
        await ConProcesosAsync(p => p.EjecutarEsperasDelDestinoAsync());
        await ConProcesosAsync(p => p.EjecutarEsperasDelDestinoAsync());

        Assert.Equal(1, await NotificacionesAsync("espera_inspector_punto", e.Coordinador.UsuarioId));
        Assert.Equal(1, await NotificacionesAsync("espera_inspector_punto"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'espera_inspector_punto' AND Urgencia = 'alta' AND Canal = 'bandeja' AND Titulo = N'Nadie ha tomado la entrega: Puerto de Cartagena' AND Mensaje LIKE N'%Puerto de Cartagena%'"));
        // El push sale una sola vez (urgencia alta) y los campos propios del aviso van en sus datos.
        Assert.Single(_push.Enviados, x => x.Token == "tk-coord" && x.Mensaje.Datos["tipo"] == "espera_inspector_punto");
        var datos = JsonDocument.Parse((string)(await Sembrador.EscalarAsync("SELECT TOP 1 Datos FROM dbo.tblNotificaciones WHERE Tipo = 'espera_inspector_punto' AND Canal = 'bandeja'"))!).RootElement;
        Assert.Equal("espera_inspector_punto", datos.GetProperty("hecho").GetString());
        Assert.Equal(e.Viaje, datos.GetProperty("viaje_id").GetGuid());
        Assert.Equal(e.Puerto, datos.GetProperty("lugar_id").GetGuid());
        Assert.Equal("Puerto de Cartagena", datos.GetProperty("lugar").GetString());
        Assert.Equal(3, datos.GetProperty("unidades").GetInt32());
        Assert.True(datos.GetProperty("minutos_esperando").GetInt32() >= 16);
        Assert.True(datos.TryGetProperty("activada_at", out _) && datos.TryGetProperty("plan_b_desde", out _));
        Assert.NotNull(await Sembrador.EscalarAsync("SELECT AvisoAt FROM dbo.tblEsperasInspectorPunto"));
    }

    [Fact]
    public async Task Activada_sin_senal_el_aviso_sale_en_la_misma_peticion_si_ya_pasaron_los_minutos_y_la_espera_cuenta_desde_la_hora_del_celular()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);

        var r = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto, new { motivo_sin_inspector = "inspector_no_llego", fecha_momento = AhoraIso(20), lat = 10.39, lng = -75.48 });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(1, await NotificacionesAsync("espera_inspector_punto", e.Coordinador.UsuarioId));
        var espera = await JsonAsync(r);
        Assert.True(espera.GetProperty("activada_at").GetDateTimeOffset() < DateTimeOffset.UtcNow.AddMinutes(-19));
        Assert.NotEqual(JsonValueKind.Null, espera.GetProperty("aviso_at").ValueKind);
        Assert.Equal(espera.GetProperty("activada_at").GetDateTimeOffset().AddMinutes(15), espera.GetProperty("aviso_desde").GetDateTimeOffset());
        // Una activacion repetida no repite el aviso.
        await ActivarAsync(e.Conductor, e.Viaje, e.Puerto);
        await ConProcesosAsync(p => p.EjecutarEsperasDelDestinoAsync());
        Assert.Equal(1, await NotificacionesAsync("espera_inspector_punto"));
    }

    [Fact]
    public async Task Si_un_inspector_toma_la_entrega_antes_de_los_15_minutos_el_aviso_no_sale()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 5);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblEsperasInspectorPunto SET ActivadaAt = DATEADD(MINUTE, -35, ActivadaAt), CreatedAt = DATEADD(MINUTE, -35, CreatedAt)");

        var avisos = await ConProcesosAsync(p => p.EjecutarEsperasDelDestinoAsync());

        Assert.Equal(0, avisos);
        Assert.Equal(0, await NotificacionesAsync("espera_inspector_punto"));
    }

    // ------------------------------------------------------------------------------------------ el plan B

    [Fact]
    public async Task Antes_de_los_45_minutos_el_cierre_sin_inspector_se_rechaza_con_lo_que_falta_y_a_los_45_se_acepta_en_la_misma_peticion()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 40);

        var antesPersona = await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto);
        var antesUna = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Puerto);
        await EnvejecerEsperaAsync(e, 6);
        var despues = await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto);

        foreach (var r in new[] { antesPersona, antesUna })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("plan_b_no_habilitado", Motivo(error));
            Assert.Matches(@"^Todavía no puedes cerrar sin inspector del punto: faltan [1-5] minutos$", Mensaje(error));
        }

        Assert.Equal(HttpStatusCode.OK, despues.StatusCode);
        Assert.Equal(0, await NotificacionesAsync("novedad_proceso"));
    }

    [Fact]
    public async Task El_cierre_con_la_persona_del_lugar_deja_los_carros_entregados_sin_inspector_del_punto_la_espera_cerrada_y_la_auditoria_con_las_tres_horas()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 50);

        var r = await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var punto = await JsonAsync(r);
        Assert.Equal("entregada", punto.GetProperty("estado_parada").GetString());
        Assert.Equal("persona_del_lugar", punto.GetProperty("receptor").GetString());
        Assert.Equal("cerrada_sin_inspector_del_punto", punto.GetProperty("espera").GetProperty("estado").GetString());
        Assert.NotEqual(JsonValueKind.Null, punto.GetProperty("espera").GetProperty("cerrada_at").ValueKind);
        foreach (var vin in VINES_PUERTO)
        {
            Assert.True(Unidad(punto, vin).GetProperty("entregada_sin_inspector_del_punto").GetBoolean());
            Assert.Equal("firmada", await Sembrador.EscalarAsync("SELECT TipoCierre FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @i", ("@i", e.Llegadas[vin])));
            Assert.Equal(true, await Sembrador.EscalarAsync("SELECT SinInspectorDelPunto FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @i", ("@i", e.Llegadas[vin])));
        }

        Assert.Equal("cerrada_sin_inspector_del_punto", await EstadoDeLaEsperaAsync(e.Viaje, e.Puerto));
        Assert.Equal(
            1,
            await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'espera_inspector_punto.cerrar' AND ViajeId = @v AND UsuarioId = @u AND JSON_VALUE(Detalle, '$.despues.activada_at') IS NOT NULL "
                + "AND JSON_VALUE(Detalle, '$.despues.habilitada_at') IS NOT NULL AND JSON_VALUE(Detalle, '$.despues.cerrada_at') IS NOT NULL AND JSON_VALUE(Detalle, '$.despues.recibio') = N'Andrés Pardo'",
                ("@v", e.Viaje), ("@u", e.Conductor.UsuarioId)));
        // El punto sin inspectores y el cierre normal no llevan la marca.
        var norte = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Norte}/firma", Cuerpo(new { firma = "f", recibe_nombre = "Rosa Ríos", recibe_cedula = "52 123 456", firma_recibe = "r" }));
        Assert.Equal(HttpStatusCode.OK, norte.StatusCode);
        Assert.False(Unidad(await JsonAsync(norte), VIN_NORTE).GetProperty("entregada_sin_inspector_del_punto").GetBoolean());
    }

    [Fact]
    public async Task El_cierre_con_su_sola_firma_y_el_motivo_sin_inspector_del_punto_entrega_todos_los_carros_con_la_marca_y_no_admite_rechazos()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 50);

        var conRechazos = await Como(e.Conductor).PostAsync(
            $"/api/viajes/{e.Viaje}/entregas/{e.Puerto}/una-firma", Cuerpo(new { firma = "f", motivo_codigo = "sin_inspector_del_punto", rechazos = new[] { new { vin = VINES_PUERTO[0] } } }));
        var r = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Puerto);

        Assert.Equal(HttpStatusCode.BadRequest, conRechazos.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var punto = await JsonAsync(r);
        Assert.Equal("entregada", punto.GetProperty("estado_parada").GetString());
        foreach (var vin in VINES_PUERTO)
        {
            var unidad = Unidad(punto, vin);
            Assert.Equal("cerrada", unidad.GetProperty("estado").GetString());
            Assert.Equal("recibo_sin_entrega", unidad.GetProperty("tipo_cierre").GetString());
            Assert.Equal("sin_inspector_del_punto", unidad.GetProperty("motivo_codigo").GetString());
            Assert.True(unidad.GetProperty("entregada_sin_inspector_del_punto").GetBoolean());
        }

        Assert.Equal("cerrada_sin_inspector_del_punto", await EstadoDeLaEsperaAsync(e.Viaje, e.Puerto));
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE MotivoCodigo = 'sin_inspector_del_punto'"));
    }

    [Fact]
    public async Task Sin_la_espera_el_motivo_sin_inspector_del_punto_no_se_acepta_ni_en_una_parada_con_inspectores_ni_en_una_sin_ellos()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);

        var conInspectores = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Puerto);
        var sinInspectores = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Norte);

        Assert.Equal((HttpStatusCode.Conflict, "sin_receptor"), (conInspectores.StatusCode, Motivo(await JsonAsync(conInspectores))));
        Assert.Equal((HttpStatusCode.Conflict, "motivo_no_aplica"), (sinInspectores.StatusCode, Motivo(await JsonAsync(sinInspectores))));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    // ------------------------------------------------------------------------------------------ el inspector llega

    [Fact]
    public async Task El_inspector_que_toma_la_entrega_aunque_ya_paso_el_tiempo_termina_la_espera_y_el_plan_b_responde_entrega_tomada()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 50);

        var tomar = await ReclamarAsync(e.Pedro, e.Viaje, e.Puerto);
        var persona = await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto);
        var unaFirma = await UnaFirmaAsync(e.Conductor, e.Viaje, e.Puerto);

        Assert.Equal(HttpStatusCode.Created, tomar.StatusCode);
        Assert.Equal("inspector_llego", await EstadoDeLaEsperaAsync(e.Viaje, e.Puerto));
        Assert.Equal(e.Pedro.UsuarioId, await Sembrador.EscalarAsync("SELECT InspectorId FROM dbo.tblEsperasInspectorPunto"));
        Assert.NotNull(await Sembrador.EscalarAsync("SELECT InspectorLlegoAt FROM dbo.tblEsperasInspectorPunto"));
        Assert.Equal(1, await AuditoriaAsync("espera_inspector_punto.inspector_llego"));
        foreach (var r in new[] { persona, unaFirma })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("entrega_tomada", Motivo(await JsonAsync(r)));
        }

        var punto = await PuntoAsync(e.Conductor, e.Viaje, e.Puerto);
        Assert.Equal("inspector_llego", punto.GetProperty("espera").GetProperty("estado").GetString());
        Assert.Equal("Nombre pedro@ejemplo.com", punto.GetProperty("espera").GetProperty("inspector_nombre").GetString());
        Assert.Equal("inspector_del_punto", punto.GetProperty("receptor").GetString());
    }

    [Fact]
    public async Task El_inspector_que_llega_despues_del_cierre_sin_inspector_recibe_el_rechazo_con_la_hora_y_nada_se_reabre()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 50);
        Assert.Equal(HttpStatusCode.OK, (await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto)).StatusCode);

        var r = await ReclamarAsync(e.Pedro, e.Viaje, e.Puerto);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var error = await JsonAsync(r);
        Assert.Equal("entrega_cerrada_sin_inspector", Motivo(error));
        Assert.Matches(@"^Esta entrega se cerró sin inspector del punto a las \d{2}:\d{2}: el cierre es definitivo$", Mensaje(error));
        Assert.True(error.GetProperty("details").TryGetProperty("cerrada_at", out _));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND LugarId = @l", ("@v", e.Viaje), ("@l", e.Puerto)));
        Assert.Equal("cerrada_sin_inspector_del_punto", await EstadoDeLaEsperaAsync(e.Viaje, e.Puerto));
    }

    [Fact]
    public async Task Si_el_inspector_suelta_la_entrega_la_espera_anterior_queda_en_el_historial_y_la_nueva_cuenta_desde_su_propia_hora()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 30);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Pedro).DeleteAsync($"/api/viajes/{e.Viaje}/reclamos/entrega?lugar_id={e.Puerto}")).StatusCode);

        var nueva = await ActivarAsync(e.Conductor, e.Viaje, e.Puerto);
        var plazoAntes = await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto);

        Assert.Equal(HttpStatusCode.Created, nueva.StatusCode);
        var espera = await JsonAsync(nueva);
        Assert.InRange(Math.Abs((espera.GetProperty("plan_b_desde").GetDateTimeOffset() - DateTimeOffset.UtcNow.AddMinutes(45)).TotalSeconds), 0, 30);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto WHERE ViajeId = @v", ("@v", e.Viaje)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto WHERE Estado = 'inspector_llego'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto WHERE Estado = 'esperando'"));
        Assert.Equal("plan_b_no_habilitado", Motivo(await JsonAsync(plazoAntes)));
    }

    [Fact]
    public async Task Si_el_plan_B_y_el_reclamo_llegan_a_la_vez_gana_exactamente_uno_y_el_otro_recibe_su_409()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 50);

        var respuestas = await Task.WhenAll(FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto), ReclamarAsync(e.Pedro, e.Viaje, e.Puerto));

        var firma = respuestas[0];
        var reclamo = respuestas[1];
        var ganaElPlanB = firma.StatusCode == HttpStatusCode.OK;
        Assert.True(ganaElPlanB ^ reclamo.StatusCode == HttpStatusCode.Created, $"firma {(int)firma.StatusCode}, reclamo {(int)reclamo.StatusCode}");
        if (ganaElPlanB)
        {
            Assert.Equal((HttpStatusCode.Conflict, "entrega_cerrada_sin_inspector"), (reclamo.StatusCode, Motivo(await JsonAsync(reclamo))));
            Assert.Equal("cerrada_sin_inspector_del_punto", await EstadoDeLaEsperaAsync(e.Viaje, e.Puerto));
            Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND LugarId = @l", ("@v", e.Viaje), ("@l", e.Puerto)));
        }
        else
        {
            Assert.Equal((HttpStatusCode.Conflict, "entrega_tomada"), (firma.StatusCode, Motivo(await JsonAsync(firma))));
            Assert.Equal("inspector_llego", await EstadoDeLaEsperaAsync(e.Viaje, e.Puerto));
            Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE SinInspectorDelPunto = 1"));
        }
    }

    // ------------------------------------------------------------------------------------------ la consulta y el viaje

    [Fact]
    public async Task La_entrega_de_la_parada_trae_la_espera_con_sus_plazos_y_los_minutos_vigentes_de_los_parametros()
    {
        await Sembrador.EjecutarAsync("UPDATE dbo.tblParametros SET Valor = N'5' WHERE Clave = 'espera_inspector_aviso_minutos'; UPDATE dbo.tblParametros SET Valor = N'10' WHERE Clave = 'espera_inspector_plan_b_minutos'");
        var e = await EscenarioAsync(Llegadas.Inspector);
        var sinEspera = await PuntoAsync(e.Conductor, e.Viaje, e.Puerto);
        Assert.Equal(JsonValueKind.Null, sinEspera.GetProperty("espera").ValueKind);
        Assert.Equal((5, 10), (sinEspera.GetProperty("espera_aviso_minutos").GetInt32(), sinEspera.GetProperty("espera_plan_b_minutos").GetInt32()));

        await EsperaActivadaHaceAsync(e, 16);
        var conEspera = (await PuntoAsync(e.Conductor, e.Viaje, e.Puerto)).GetProperty("espera");
        var activada = conEspera.GetProperty("activada_at").GetDateTimeOffset();

        Assert.Equal("esperando", conEspera.GetProperty("estado").GetString());
        Assert.Equal(activada.AddMinutes(5), conEspera.GetProperty("aviso_desde").GetDateTimeOffset());
        Assert.Equal(activada.AddMinutes(15), conEspera.GetProperty("plan_b_desde").GetDateTimeOffset());
        Assert.True(conEspera.GetProperty("plan_b_habilitado").GetBoolean());
        // Con los parametros en 5 y 10, a los 16 minutos el plan B ya esta habilitado: el cierre se acepta.
        Assert.Equal(HttpStatusCode.OK, (await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto)).StatusCode);
    }

    [Fact]
    public async Task Si_el_viaje_se_cancela_o_se_finaliza_a_mano_la_espera_abierta_queda_anulada()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        await EsperaActivadaHaceAsync(e, 5);

        var r = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/estado", Cuerpo(new { estado = "cancelado" }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("anulada", await EstadoDeLaEsperaAsync(e.Viaje, e.Puerto));
        Assert.Equal(1, await AuditoriaAsync("espera_inspector_punto.anular"));
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarEsperasDelDestinoAsync()));
    }

    // ------------------------------------------------------------------------------------------ C10: la revision de llegada

    private Task<HttpResponseMessage> CrearLlegadaAsync(Sesion quien, Guid etapa) =>
        Como(quien).PostAsJsonAsync("/api/inspecciones", new Dictionary<string, object?> { ["etapa_id"] = etapa, ["momento"] = "llegada", ["fecha"] = "2026-11-18T14:00:00-05:00", ["lat"] = 10.39, ["lng"] = -75.48 });

    [Fact]
    public async Task En_una_parada_con_inspectores_el_conductor_solo_crea_la_revision_de_llegada_con_el_plan_b_habilitado_y_el_inspector_siempre_puede()
    {
        var e = await EscenarioAsync();
        var p1 = await EtapaAsync(e.Viaje, VINES_PUERTO[0], "descargue");
        var p2 = await EtapaAsync(e.Viaje, VINES_PUERTO[1], "descargue");
        var p3 = await EtapaAsync(e.Viaje, VINES_PUERTO[2], "descargue");

        // Sin espera: lo que debe hacer.
        var sinEspera = await CrearLlegadaAsync(e.Conductor, p1);
        var errorSinEspera = await JsonAsync(sinEspera);
        Assert.Equal((HttpStatusCode.Conflict, "llegada_del_inspector_del_punto"), (sinEspera.StatusCode, Motivo(errorSinEspera)));
        Assert.Equal("La revisión de llegada en esta parada la hace el inspector del punto: si no llega, activa «No hay inspector»", Mensaje(errorSinEspera));

        // Con la espera a los 40 minutos: faltan 5.
        await EsperaActivadaHaceAsync(e, 40);
        var antes = await CrearLlegadaAsync(e.Conductor, p1);
        var errorAntes = await JsonAsync(antes);
        Assert.Equal(HttpStatusCode.Conflict, antes.StatusCode);
        Assert.Equal("llegada_del_inspector_del_punto", Motivo(errorAntes));
        Assert.Matches(@"^La revisión de llegada en esta parada la hace el inspector del punto: podrás hacerla sin él en [1-5] minutos$", Mensaje(errorAntes));

        // A los 46 el servidor habilita el plan B en esa misma peticion y la acepta.
        await EnvejecerEsperaAsync(e, 6);
        var despues = await CrearLlegadaAsync(e.Conductor, p1);
        Assert.Equal(HttpStatusCode.Created, despues.StatusCode);

        // El inspector que tomo la entrega la crea siempre, y el conductor ya no cuando hay inspector.
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CrearLlegadaAsync(e.Pedro, p2)).StatusCode);
        var conductorConInspector = await CrearLlegadaAsync(e.Conductor, p3);
        Assert.Equal((HttpStatusCode.Conflict, "llegada_del_inspector_del_punto"), (conductorConInspector.StatusCode, Motivo(await JsonAsync(conductorConInspector))));
    }

    [Fact]
    public async Task En_una_parada_sin_inspectores_el_conductor_crea_la_revision_de_llegada_como_siempre()
    {
        var e = await EscenarioAsync(norteSinRevision: true);
        var norte = await EtapaAsync(e.Viaje, VIN_NORTE, "descargue");

        var r = await CrearLlegadaAsync(e.Conductor, norte);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    // ------------------------------------------------------------------------------------------ C19: el inspector que llega

    private Task<HttpResponseMessage> CalificarAsync(Sesion quien, Guid hallazgo, string nivel = "v2") =>
        Como(quien).PostAsJsonAsync($"/api/hallazgos/{hallazgo}/revision", new { clase_dano = "golpe", nivel_severidad = nivel });

    [Fact]
    public async Task El_inspector_que_toma_la_parada_califica_los_danos_del_conductor_antes_de_firmar_y_las_revisiones_del_conductor_cuentan_sin_repetirse()
    {
        var e = await EscenarioAsync(Llegadas.Conductor, conDanoEn: VINES_PUERTO[1]);
        await EsperaActivadaHaceAsync(e, 50);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);
        var llegadasAntes = await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Momento = 'llegada'");

        // Sin calificar el dano: ni la firma por parada ni el cierre con una sola firma de la parada tomada.
        var firmaConductor = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Puerto}/firma", Cuerpo(new { firma = "trazo-conductor" }));
        var firmaPedro = await Como(e.Pedro).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Puerto}/firma", Cuerpo(new { firma = "trazo-pedro" }));
        var unaFirma = await Como(e.Pedro).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Puerto}/una-firma", Cuerpo(new { firma = "trazo-pedro", motivo_codigo = "no_habia_nadie" }));
        foreach (var r in new[] { firmaConductor, firmaPedro, unaFirma })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("danos_sin_calificar", Motivo(error));
            Assert.Equal("Antes de firmar, califica los daños que marcó el conductor en esta parada: falta 1", Mensaje(error));
        }

        // La parada trae el dano por calificar con su VIN y su foto.
        var punto = await PuntoAsync(e.Pedro, e.Viaje, e.Puerto);
        var pendiente = punto.GetProperty("danos_por_calificar").EnumerateArray().Single();
        Assert.Equal(e.Danos[VINES_PUERTO[1]], pendiente.GetProperty("hallazgo_id").GetGuid());
        Assert.Equal(VINES_PUERTO[1], pendiente.GetProperty("vin").GetString());
        Assert.NotEqual(JsonValueKind.Null, pendiente.GetProperty("evidencia_id").ValueKind);

        // Otro inspector sin reclamo no califica; Pedro, que tiene la parada aunque no cargo el viaje, si.
        Assert.Equal(HttpStatusCode.Forbidden, (await CalificarAsync(e.Diana, e.Danos[VINES_PUERTO[1]])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CalificarAsync(e.Pedro, e.Danos[VINES_PUERTO[1]])).StatusCode);
        Assert.Equal(0, (await PuntoAsync(e.Pedro, e.Viaje, e.Puerto)).GetProperty("danos_por_calificar").GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Puerto}/firma", Cuerpo(new { firma = "trazo-conductor" }))).StatusCode);
        var firmada = await Como(e.Pedro).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Puerto}/firma", Cuerpo(new { firma = "trazo-pedro" }));
        Assert.Equal(HttpStatusCode.OK, firmada.StatusCode);
        Assert.Equal("entregada", (await JsonAsync(firmada)).GetProperty("estado_parada").GetString());
        Assert.Equal(llegadasAntes, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Momento = 'llegada'"));
        // Con el inspector presente ningun carro se marca como entregado sin inspector del punto.
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE SinInspectorDelPunto = 1"));
    }

    [Fact]
    public async Task Una_parada_sin_revisiones_del_conductor_firma_como_antes_sin_danos_por_calificar()
    {
        var e = await EscenarioAsync(Llegadas.Inspector);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Pedro, e.Viaje, e.Puerto)).StatusCode);

        var punto = await PuntoAsync(e.Pedro, e.Viaje, e.Puerto);
        var conductor = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Puerto}/firma", Cuerpo(new { firma = "trazo-conductor" }));
        var pedro = await Como(e.Pedro).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Puerto}/firma", Cuerpo(new { firma = "trazo-pedro" }));

        Assert.Equal(0, punto.GetProperty("danos_por_calificar").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, conductor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pedro.StatusCode);
    }

    // ------------------------------------------------------------------------------------------ C25: rechazar con el plan B

    [Fact]
    public async Task Con_el_plan_b_el_conductor_rechaza_un_carro_por_su_novedad_y_los_demas_quedan_entregados_sin_inspector_del_punto()
    {
        var e = await EscenarioAsync(Llegadas.Conductor, conDanoEn: VINES_PUERTO[1]);
        await EsperaActivadaHaceAsync(e, 50);
        var hallazgo = e.Danos[VINES_PUERTO[1]];

        var r = await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto, [new { vin = VINES_PUERTO[1], hallazgo_id = hallazgo }]);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var punto = await JsonAsync(r);
        Assert.Equal("entregada_con_rechazos", punto.GetProperty("estado_parada").GetString());
        var rechazado = Unidad(punto, VINES_PUERTO[1]);
        Assert.Equal("rechazado", rechazado.GetProperty("estado").GetString());
        Assert.False(rechazado.GetProperty("entregada_sin_inspector_del_punto").GetBoolean());
        Assert.Equal(e.Conductor.UsuarioId, rechazado.GetProperty("rechazo").GetProperty("marcado_por").GetGuid());
        foreach (var vin in new[] { VINES_PUERTO[0], VINES_PUERTO[2] })
        {
            Assert.True(Unidad(punto, vin).GetProperty("entregada_sin_inspector_del_punto").GetBoolean());
        }

        // El dano queda sin calificar, el carro sigue activo a cargo del conductor y el coordinador recibe el aviso del rechazo.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND NivelSeveridad IS NULL", ("@h", hallazgo)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId WHERE v.Vin = @v AND vv.EstadoUnidad = 'activa'", ("@v", VINES_PUERTO[1])));
        Assert.Equal(1, await NotificacionesAsync("carro_rechazado_en_destino", e.Coordinador.UsuarioId));
        Assert.Equal("cerrada_sin_inspector_del_punto", await EstadoDeLaEsperaAsync(e.Viaje, e.Puerto));
    }

    [Fact]
    public async Task Con_el_plan_b_un_carro_sin_dano_no_se_puede_rechazar_y_antes_del_plazo_los_rechazos_tampoco_se_aceptan()
    {
        var e = await EscenarioAsync(Llegadas.Conductor, conDanoEn: VINES_PUERTO[1]);
        await EsperaActivadaHaceAsync(e, 30);

        var antes = await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto, [new { vin = VINES_PUERTO[1], hallazgo_id = e.Danos[VINES_PUERTO[1]] }]);
        await EnvejecerEsperaAsync(e, 20);
        var sinDano = await FirmarConPersonaAsync(e.Conductor, e.Viaje, e.Puerto, [new { vin = VINES_PUERTO[0] }]);

        Assert.Equal("plan_b_no_habilitado", Motivo(await JsonAsync(antes)));
        Assert.Equal(HttpStatusCode.Conflict, sinDano.StatusCode);
        Assert.Equal("sin_novedad_para_rechazar", Motivo(await JsonAsync(sinDano)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal("esperando", await EstadoDeLaEsperaAsync(e.Viaje, e.Puerto));
    }
}
