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
/// La lista de pendientes del inspector (cargues de hoy de sus puntos y entregas que llegan a ellos, con su estado), su visibilidad (pendientes de su punto mas lo que reclamo; el conductor no cambia) y el
/// aviso al coordinador de un camion reclamado sin actividad (una vez por reclamo, sin liberarlo).
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class PendientesDelInspectorApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private sealed record Escenario(Guid Envigado, Guid Cartagena, Sesion Esleider, Sesion Andres, Sesion Coordinador, Sesion Conductor);

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

    private Task PuntoAsync(Sesion inspector, Guid lugar) =>
        Sembrador.EjecutarAsync("INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), @u, @l)", ("@u", inspector.UsuarioId), ("@l", lugar));

    private async Task<Escenario> EscenarioAsync()
    {
        var coordinador = await SesionAsync("luisa@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var esleider = await SesionAsync("esleider@ejemplo.com", clsRol.INSPECTOR);
        var andres = await SesionAsync("andres@ejemplo.com", clsRol.INSPECTOR);
        var envigado = await LugarRealAsync(Como(coordinador), "CEDI Envigado", "cedi_1", "Envigado");
        var cartagena = await LugarRealAsync(Como(coordinador), "Puerto Cartagena", "cedi_2", "Cartagena");
        await PuntoAsync(esleider, envigado);
        await PuntoAsync(andres, envigado);
        return new Escenario(envigado, cartagena, esleider, andres, coordinador, conductor);
    }

    private static string Fecha(int dias = 0) =>
        new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-5).Date.AddDays(dias).AddHours(6), DateTimeKind.Unspecified), TimeSpan.FromHours(-5)).ToString("yyyy-MM-ddTHH:mm:sszzz");

    private async Task<Guid> ViajeAsync(Escenario e, string codigo, string placa, Guid lugarCargue, string fecha, Guid? destino = null, string? vin = null, Sesion? conductor = null)
    {
        vin ??= "VIN-" + codigo;
        await Sembrador.VehiculoAsync(vin);
        var tipo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;
        var creado = await Como(e.Coordinador).PostAsync("/api/viajes", Cuerpo(new
        {
            codigo_mtrack = codigo,
            tipo_unidad_id = tipo,
            placa,
            conductor_id = conductor?.UsuarioId,
            unidades = new[] { new { vin, lugar_destino_id = destino ?? lugarCargue } },
        }));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await JsonAsync(creado)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsync($"/api/viajes/{id}", Cuerpo(new { fecha_programada = fecha, lugar_cargue_id = lugarCargue }))).StatusCode);
        return id;
    }

    private Task<HttpResponseMessage> ReclamarAsync(Sesion quien, Guid viaje, string proceso = "cargue") =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/reclamos", Cuerpo(new { proceso }));

    private async Task<List<JsonElement>> PendientesAsync(Sesion quien)
    {
        var r = await Como(quien).GetAsync("/api/viajes/pendientes");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await JsonAsync(r)).EnumerateArray().ToList();
    }

    // ------------------------------------------------------------------------------------------ ESC-01: la lista de pendientes

    [Fact]
    public async Task El_inspector_ve_solo_los_cargues_de_hoy_de_su_punto_como_disponibles()
    {
        var e = await EscenarioAsync();
        await ViajeAsync(e, "MTK-ABC123", "ABC123", e.Envigado, Fecha());
        await ViajeAsync(e, "MTK-DEF456", "DEF456", e.Envigado, Fecha());
        await ViajeAsync(e, "MTK-GHI789", "GHI789", e.Envigado, Fecha(1));
        await ViajeAsync(e, "MTK-JKL012", "JKL012", e.Cartagena, Fecha());
        await ViajeAsync(e, "MTK-AYER", "MNO345", e.Envigado, Fecha(-1));

        var lista = await PendientesAsync(e.Esleider);

        Assert.Equal(["ABC123", "DEF456"], lista.Select(p => p.GetProperty("placa").GetString()).Order().ToList());
        Assert.All(lista, p =>
        {
            Assert.Equal(
                ["viaje_id", "codigo_mtrack", "placa", "proceso", "lugar_id", "lugar", "fecha_programada", "estado", "reclamado_por", "reclamado_por_nombre", "reclamado_at", "unidades"],
                p.EnumerateObject().Select(x => x.Name).ToList());
            Assert.Equal("cargue", p.GetProperty("proceso").GetString());
            Assert.Equal("CEDI Envigado", p.GetProperty("lugar").GetString());
            Assert.Equal("disponible", p.GetProperty("estado").GetString());
            Assert.Equal(JsonValueKind.Null, p.GetProperty("reclamado_por").ValueKind);
            Assert.Equal(1, p.GetProperty("unidades").GetInt32());
        });
    }

    [Fact]
    public async Task Cada_camion_trae_su_estado_disponible_en_revision_por_otro_o_mio_y_liberarlo_lo_vuelve_a_dejar_disponible()
    {
        var e = await EscenarioAsync();
        var abc = await ViajeAsync(e, "MTK-ABC123", "ABC123", e.Envigado, Fecha());
        var def = await ViajeAsync(e, "MTK-DEF456", "DEF456", e.Envigado, Fecha());
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Esleider, abc)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Andres, def)).StatusCode);

        var deAndres = (await PendientesAsync(e.Andres)).ToDictionary(p => p.GetProperty("placa").GetString()!);
        var deEsleider = (await PendientesAsync(e.Esleider)).ToDictionary(p => p.GetProperty("placa").GetString()!);

        // «En revision por Esleider» en la lista de Andres; lo suyo, «mio».
        Assert.Equal("en_revision", deAndres["ABC123"].GetProperty("estado").GetString());
        Assert.Equal(e.Esleider.UsuarioId, deAndres["ABC123"].GetProperty("reclamado_por").GetGuid());
        Assert.Equal("Nombre esleider@ejemplo.com", deAndres["ABC123"].GetProperty("reclamado_por_nombre").GetString());
        Assert.NotEqual(JsonValueKind.Null, deAndres["ABC123"].GetProperty("reclamado_at").ValueKind);
        Assert.Equal("mio", deAndres["DEF456"].GetProperty("estado").GetString());
        Assert.Equal("mio", deEsleider["ABC123"].GetProperty("estado").GetString());
        Assert.Equal("en_revision", deEsleider["DEF456"].GetProperty("estado").GetString());

        var libera = await Como(e.Esleider).DeleteAsync($"/api/viajes/{abc}/reclamos/cargue");
        Assert.Equal(HttpStatusCode.OK, libera.StatusCode);
        var tras = (await PendientesAsync(e.Andres)).Single(p => p.GetProperty("placa").GetString() == "ABC123");
        Assert.Equal("disponible", tras.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, tras.GetProperty("reclamado_por").ValueKind);
    }

    [Fact]
    public async Task La_lista_incluye_las_entregas_en_curso_que_llegan_a_su_punto_hasta_que_se_cierran_y_solo_la_ve_un_inspector()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-2201", "ABC123", e.Cartagena, Fecha(), destino: e.Envigado, conductor: e.Conductor);
        var sinPuntos = await SesionAsync("sin@ejemplo.com", clsRol.INSPECTOR);

        Assert.DoesNotContain(await PendientesAsync(e.Esleider), p => p.GetProperty("proceso").GetString() == "entrega");
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsync($"/api/viajes/{viaje}/estado", Cuerpo(new { estado = "en_curso" }))).StatusCode);

        var entrega = (await PendientesAsync(e.Esleider)).Single(p => p.GetProperty("proceso").GetString() == "entrega");
        Assert.Equal(viaje, entrega.GetProperty("viaje_id").GetGuid());
        Assert.Equal("CEDI Envigado", entrega.GetProperty("lugar").GetString());
        Assert.Equal("disponible", entrega.GetProperty("estado").GetString());
        Assert.Equal(1, entrega.GetProperty("unidades").GetInt32());
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Andres, viaje, "entrega")).StatusCode);
        Assert.Equal("en_revision", (await PendientesAsync(e.Esleider)).Single(p => p.GetProperty("proceso").GetString() == "entrega").GetProperty("estado").GetString());

        // Un inspector sin puntos recibe la lista vacia; el conductor, el coordinador y los demas roles no la usan (403).
        Assert.Empty(await PendientesAsync(sinPuntos));
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Conductor).GetAsync("/api/viajes/pendientes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Coordinador).GetAsync("/api/viajes/pendientes")).StatusCode);
    }

    // ------------------------------------------------------------------------------ ESC-07 y CA-2: la visibilidad del inspector

    [Fact]
    public async Task El_inspector_ve_el_detalle_de_los_pendientes_de_su_punto_aunque_no_los_haya_reclamado_y_el_de_otro_punto_responde_403()
    {
        var e = await EscenarioAsync();
        var deSuPunto = await ViajeAsync(e, "MTK-ABC123", "ABC123", e.Envigado, Fecha());
        var deOtroPunto = await ViajeAsync(e, "MTK-JKL012", "JKL012", e.Cartagena, Fecha());
        var deManana = await ViajeAsync(e, "MTK-GHI789", "GHI789", e.Envigado, Fecha(1));

        var suPunto = await Como(e.Andres).GetAsync($"/api/viajes/{deSuPunto}");
        var otroPunto = await Como(e.Andres).GetAsync($"/api/viajes/{deOtroPunto}");
        var manana = await Como(e.Andres).GetAsync($"/api/viajes/{deManana}");
        var reclamos = await Como(e.Andres).GetAsync($"/api/viajes/{deSuPunto}/reclamos");

        Assert.Equal(HttpStatusCode.OK, suPunto.StatusCode);
        Assert.Equal("MTK-ABC123", (await JsonAsync(suPunto)).GetProperty("codigo_mtrack").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, otroPunto.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, manana.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reclamos.StatusCode);

        // Ver no es actuar: sin reclamar no puede iniciar la inspeccion; al reclamar si.
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-ACTUAR", viajeId: deSuPunto);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Andres).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "salida", fecha = Fecha(), lat = 6.2, lng = -75.5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Andres, deSuPunto)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Como(e.Andres).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "salida", fecha = Fecha(), lat = 6.2, lng = -75.5 })).StatusCode);
    }

    [Fact]
    public async Task La_visibilidad_del_conductor_no_cambia_solo_ve_sus_viajes_aunque_el_cargue_este_en_un_punto_con_inspectores()
    {
        var e = await EscenarioAsync();
        var suyo = await ViajeAsync(e, "MTK-ABC123", "ABC123", e.Envigado, Fecha(), conductor: e.Conductor);
        var ajeno = await ViajeAsync(e, "MTK-DEF456", "DEF456", e.Envigado, Fecha());
        var otroConductor = await SesionAsync("otro@ejemplo.com", clsRol.CONDUCTOR);

        Assert.Equal(HttpStatusCode.OK, (await Como(e.Conductor).GetAsync($"/api/viajes/{suyo}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Conductor).GetAsync($"/api/viajes/{ajeno}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(otroConductor).GetAsync($"/api/viajes/{suyo}")).StatusCode);
        var mios = await JsonAsync(await Como(e.Conductor).GetAsync("/api/viajes/mios"));
        Assert.Equal([suyo], mios.EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToList());
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).GetAsync($"/api/viajes/{ajeno}")).StatusCode);
    }

    // ------------------------------------------------------------------------------------- ESC-06: reclamado sin actividad

    private Task EnvejecerReclamoAsync(Guid viaje, int minutos) =>
        Sembrador.EjecutarAsync("UPDATE dbo.tblReclamos SET ReclamadoAt = DATEADD(MINUTE, @m, ReclamadoAt) WHERE ViajeId = @v", ("@m", -minutos), ("@v", viaje));

    private async Task<int> AvisosAsync() => await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'reclamo_sin_actividad'");

    private async Task<int> EjecutarAvisosAsync()
    {
        using var alcance = _api.Services.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>().EjecutarAvisosAsync();
    }

    [Fact]
    public async Task Un_camion_reclamado_sin_actividad_avisa_una_sola_vez_al_coordinador_y_el_reclamo_sigue_vigente()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-ABC123", "ABC123", e.Envigado, Fecha());
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Esleider, viaje)).StatusCode);

        // Antes de cumplirse el parametro (30 minutos) no hay aviso.
        await EnvejecerReclamoAsync(viaje, 20);
        await EjecutarAvisosAsync();
        Assert.Equal(0, await AvisosAsync());

        // Cumplido el tiempo: un aviso al coordinador (urgencia media) con la placa y el nombre del inspector.
        await EnvejecerReclamoAsync(viaje, 20);
        var emitidos = await EjecutarAvisosAsync();

        Assert.True(emitidos >= 1);
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'reclamo_sin_actividad' AND Urgencia = 'media' AND Canal = 'bandeja' AND UsuarioId = @u AND ViajeId = @v AND Titulo LIKE N'%ABC123%'",
            ("@u", e.Coordinador.UsuarioId), ("@v", viaje)));
        var mensaje = (string)(await Sembrador.EscalarAsync("SELECT TOP 1 Mensaje FROM dbo.tblNotificaciones WHERE Tipo = 'reclamo_sin_actividad' AND UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)))!;
        Assert.Contains("Nombre esleider@ejemplo.com", mensaje);
        Assert.Contains("ABC123", mensaje);
        // El inspector y el conductor no reciben nada, y el reclamo no se libera.
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'reclamo_sin_actividad' AND UsuarioId IN (@a, @b)", ("@a", e.Esleider.UsuarioId), ("@b", e.Conductor.UsuarioId)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND LiberadoAt IS NULL", ("@v", viaje)));

        // Una sola vez por reclamo: volver a correr el proceso no repite el aviso.
        var antes = await AvisosAsync();
        await EjecutarAvisosAsync();
        await EjecutarAvisosAsync();
        Assert.Equal(antes, await AvisosAsync());
    }

    [Fact]
    public async Task Con_actividad_despues_del_reclamo_o_si_el_parametro_es_mayor_no_hay_aviso()
    {
        var e = await EscenarioAsync();
        var conActividad = await ViajeAsync(e, "MTK-ABC123", "ABC123", e.Envigado, Fecha());
        var sinActividad = await ViajeAsync(e, "MTK-DEF456", "DEF456", e.Envigado, Fecha());
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Esleider, conActividad)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Andres, sinActividad)).StatusCode);
        // Una inspeccion iniciada despues del reclamo es actividad.
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-ACTIVO", viajeId: conActividad);
        await EnvejecerReclamoAsync(conActividad, 31);
        await Sembrador.InformeAsync(etapas[0], e.Esleider.UsuarioId, cerrada: false);
        await EnvejecerReclamoAsync(sinActividad, 31);

        await EjecutarAvisosAsync();

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'reclamo_sin_actividad' AND ViajeId = @v", ("@v", conActividad)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'reclamo_sin_actividad' AND Canal = 'bandeja' AND ViajeId = @v AND UsuarioId = @u", ("@v", sinActividad), ("@u", e.Coordinador.UsuarioId)));

        // El parametro se ajusta sin desplegar: con 120 minutos, un reclamo de 31 no avisa.
        var otro = await ViajeAsync(e, "MTK-GHI789", "GHI789", e.Envigado, Fecha());
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Esleider, otro)).StatusCode);
        await EnvejecerReclamoAsync(otro, 31);
        await CambiarParametroAsync("reclamo_sin_actividad_minutos", "120", _api);
        await EjecutarAvisosAsync();
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'reclamo_sin_actividad' AND ViajeId = @v", ("@v", otro)));
    }
}
