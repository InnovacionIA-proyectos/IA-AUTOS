using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Alertas;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El inspector reclama su camion: reclama el cargue de HOY de uno de sus puntos (ESC-02) o la entrega de un viaje que llega a un punto suyo (ESC-08); el segundo en reclamar recibe 409 (ESC-03) y, si
/// dos lo hacen a la vez, gana exactamente uno; lo libera el propio inspector, el coordinador o un administrador y lo capturado conserva su autor (ESC-04, ESC-05); no hay caducidad automatica.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class ReclamosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Dos puntos (CEDI Envigado y Puerto Cartagena), tres inspectores (Esleider y Andres en Envigado, Paula en Cartagena), un coordinador, un administrador y un conductor.</summary>
    private sealed record Escenario(Guid Envigado, Guid Cartagena, Sesion Esleider, Sesion Andres, Sesion Paula, Sesion Coordinador, Sesion Admin, Sesion Conductor);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task PuntoAsync(Sesion inspector, Guid lugar) =>
        await Sembrador.EjecutarAsync("INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), @u, @l)", ("@u", inspector.UsuarioId), ("@l", lugar));

    private async Task<Escenario> EscenarioAsync()
    {
        var coordinador = await SesionAsync("luisa@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var esleider = await SesionAsync("esleider@ejemplo.com", clsRol.INSPECTOR);
        var andres = await SesionAsync("andres@ejemplo.com", clsRol.INSPECTOR);
        var paula = await SesionAsync("paula@ejemplo.com", clsRol.INSPECTOR);
        var envigado = await LugarRealAsync(Como(coordinador), "CEDI Envigado", "cedi_1", "Envigado");
        var cartagena = await LugarRealAsync(Como(coordinador), "Puerto Cartagena", "cedi_2", "Cartagena");
        await PuntoAsync(esleider, envigado);
        await PuntoAsync(andres, envigado);
        await PuntoAsync(paula, cartagena);
        return new Escenario(envigado, cartagena, esleider, andres, paula, coordinador, admin, conductor);
    }

    /// <summary>Las 06:00 de hoy en Colombia (UTC-5) mas los dias indicados, en ISO 8601 con zona.</summary>
    private static string FechaDeHoy(int dias = 0) =>
        new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-5).Date.AddDays(dias).AddHours(6), DateTimeKind.Unspecified), TimeSpan.FromHours(-5)).ToString("yyyy-MM-ddTHH:mm:sszzz");

    /// <summary>Un cargue programado (con su lugar real de cargue y su fecha) y una unidad con destino al lugar indicado.</summary>
    private async Task<Guid> ViajeAsync(Escenario e, string codigo, Guid lugarCargue, string fecha, Guid? destino = null, string vin = "VIN-REC-0001", Sesion? conductor = null)
    {
        await Sembrador.VehiculoAsync(vin);
        var tipo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;
        var creado = await Como(e.Coordinador).PostAsync("/api/viajes", Cuerpo(new
        {
            codigo_mtrack = codigo,
            tipo_unidad_id = tipo,
            placa = "ABC123",
            conductor_id = conductor?.UsuarioId,
            fecha_programada = fecha,
            lugar_cargue_id = lugarCargue,
            unidades = new[] { new { vin, lugar_destino_id = destino ?? lugarCargue } },
        }));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await JsonAsync(creado)).GetProperty("id").GetGuid();
        var programado = await Como(e.Coordinador).PatchAsync($"/api/viajes/{id}", Cuerpo(new { fecha_programada = fecha, lugar_cargue_id = lugarCargue }));
        Assert.Equal(HttpStatusCode.OK, programado.StatusCode);
        return id;
    }

    private Task<HttpResponseMessage> ReclamarAsync(Sesion quien, Guid viaje, string proceso = "cargue", Guid? lugar = null) =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/reclamos", Cuerpo(lugar is null ? new { proceso } : new { proceso, lugar_id = lugar }));

    private Task<HttpResponseMessage> LiberarAsync(Sesion quien, Guid viaje, string proceso = "cargue", Guid? lugar = null) =>
        Como(quien).DeleteAsync($"/api/viajes/{viaje}/reclamos/{proceso}" + (lugar is null ? string.Empty : $"?lugar_id={lugar}"));

    private Task<object?> InspectorDelViajeAsync(Guid viaje) => Sembrador.EscalarAsync("SELECT InspectorId FROM dbo.tblViajes WHERE Id = @v", ("@v", viaje));

    // ------------------------------------------------------------------------- ESC-02 y CA-1: reclamar un cargue de hoy de mi punto

    [Fact]
    public async Task El_inspector_reclama_un_cargue_de_hoy_de_su_punto_y_queda_como_inspector_del_viaje()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-ABC123", e.Envigado, FechaDeHoy());

        var r = await ReclamarAsync(e.Esleider, viaje);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var reclamo = await JsonAsync(r);
        Assert.Equal(["id", "viaje_id", "proceso", "inspector_id", "inspector_nombre", "reclamado_at", "activo", "liberado_at", "liberado_por", "liberado_por_nombre", "lugar_id", "lugar"], reclamo.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(viaje, reclamo.GetProperty("viaje_id").GetGuid());
        Assert.Equal("cargue", reclamo.GetProperty("proceso").GetString());
        Assert.Equal(e.Esleider.UsuarioId, reclamo.GetProperty("inspector_id").GetGuid());
        Assert.Equal("Nombre esleider@ejemplo.com", reclamo.GetProperty("inspector_nombre").GetString());
        Assert.True(reclamo.GetProperty("activo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reclamo.GetProperty("liberado_at").ValueKind);
        // Esleider queda como el inspector del viaje (las firmas de la transferencia salen de ahi) y el reclamo activo queda registrado.
        Assert.Equal(e.Esleider.UsuarioId, await InspectorDelViajeAsync(viaje));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND InspectorId = @i AND Proceso = 'cargue' AND LiberadoAt IS NULL", ("@v", viaje), ("@i", e.Esleider.UsuarioId)));
        var firmantes = await JsonAsync(await Como(e.Esleider).GetAsync($"/api/viajes/{viaje}/firmantes"));
        Assert.Contains(e.Esleider.UsuarioId, firmantes.EnumerateArray().Select(f => f.GetProperty("id").GetGuid()));
        // CA-6: queda en la auditoria con el usuario, la fecha y el viaje.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.reclamar' AND UsuarioId = @u AND ViajeId = @v AND CreatedAt IS NOT NULL", ("@u", e.Esleider.UsuarioId), ("@v", viaje)));

        // Reclamar de nuevo lo que ya es suyo no cambia nada (200).
        var otraVez = await ReclamarAsync(e.Esleider, viaje);
        Assert.Equal(HttpStatusCode.OK, otraVez.StatusCode);
        Assert.Equal(reclamo.GetProperty("id").GetGuid(), (await JsonAsync(otraVez)).GetProperty("id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos"));
    }

    [Fact]
    public async Task Reclamar_un_cargue_de_otro_punto_responde_403_y_uno_que_no_es_de_hoy_o_un_proceso_invalido_no_se_reclama()
    {
        var e = await EscenarioAsync();
        var deCartagena = await ViajeAsync(e, "MTK-JKL012", e.Cartagena, FechaDeHoy(), vin: "VIN-REC-0002");
        var deManana = await ViajeAsync(e, "MTK-GHI789", e.Envigado, FechaDeHoy(1), vin: "VIN-REC-0003");
        var sinProgramar = await Sembrador.ViajeAsync(codigoMtrack: "MTK-SIN");
        var deHoy = await ViajeAsync(e, "MTK-DEF456", e.Envigado, FechaDeHoy(), vin: "VIN-REC-0004");

        var otroPunto = await ReclamarAsync(e.Esleider, deCartagena);
        var manana = await ReclamarAsync(e.Esleider, deManana);
        var sinLugar = await ReclamarAsync(e.Esleider, sinProgramar);
        var invalido = await ReclamarAsync(e.Esleider, deHoy, "descargue");
        var sinProceso = await Como(e.Esleider).PostAsync($"/api/viajes/{deHoy}/reclamos", Cuerpo(new { }));
        var inexistente = await ReclamarAsync(e.Esleider, Guid.NewGuid());
        var comoConductor = await ReclamarAsync(e.Conductor, deHoy);
        var comoCoordinador = await ReclamarAsync(e.Coordinador, deHoy);

        Assert.Equal(HttpStatusCode.Forbidden, otroPunto.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, manana.StatusCode);
        Assert.Equal("El cargue no está programado para hoy", (await JsonAsync(manana)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, sinLugar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinProceso.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comoConductor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comoCoordinador.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos"));
        Assert.Null(await InspectorDelViajeAsync(deCartagena));
    }

    // ------------------------------------------------------------------------------------------- ESC-03 y CA-2: el segundo recibe 409

    [Fact]
    public async Task El_segundo_en_reclamar_recibe_409_y_el_camion_sigue_a_nombre_del_primero()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-ABC123", e.Envigado, FechaDeHoy());
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Esleider, viaje)).StatusCode);

        var andres = await ReclamarAsync(e.Andres, viaje);

        Assert.Equal(HttpStatusCode.Conflict, andres.StatusCode);
        Assert.Equal("Ya fue tomado por otro inspector", (await JsonAsync(andres)).GetProperty("message").GetString());
        Assert.Equal(e.Esleider.UsuarioId, await InspectorDelViajeAsync(viaje));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND LiberadoAt IS NULL", ("@v", viaje)));
    }

    [Fact]
    public async Task Si_varios_inspectores_reclaman_el_mismo_cargue_a_la_vez_gana_exactamente_uno_y_los_demas_reciben_409()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-ABC123", e.Envigado, FechaDeHoy());
        var aspirantes = new List<Sesion> { e.Esleider, e.Andres };
        for (var i = 1; i <= 4; i++)
        {
            var otro = await SesionAsync($"inspector{i}@ejemplo.com", clsRol.INSPECTOR);
            await PuntoAsync(otro, e.Envigado);
            aspirantes.Add(otro);
        }

        // Cada uno con su propio cliente HTTP, todos disparados juntos.
        var respuestas = await Task.WhenAll(aspirantes.Select(a => Task.Run(() => ReclamarAsync(a, viaje))));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(aspirantes.Count - 1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var perdedor in respuestas.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal("Ya fue tomado por otro inspector", (await JsonAsync(perdedor)).GetProperty("message").GetString());
        }

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND LiberadoAt IS NULL", ("@v", viaje)));
        var ganador = aspirantes[Array.FindIndex(respuestas, r => r.StatusCode == HttpStatusCode.Created)];
        Assert.Equal(ganador.UsuarioId, await InspectorDelViajeAsync(viaje));
    }

    // ---------------------------------------------------------------------------------------- ESC-04, ESC-05 y CA-3: liberar

    [Fact]
    public async Task El_inspector_libera_su_reclamo_el_viaje_queda_sin_inspector_el_cargue_vuelve_a_estar_disponible_y_lo_capturado_conserva_su_autor()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-ABC123", e.Envigado, FechaDeHoy());
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Esleider, viaje)).StatusCode);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-REC-9999", viajeId: viaje);
        var inspeccion = await Sembrador.InformeAsync(etapas[0], e.Esleider.UsuarioId, cerrada: true);

        var ajeno = await LiberarAsync(e.Andres, viaje);
        var r = await LiberarAsync(e.Esleider, viaje);

        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var liberado = await JsonAsync(r);
        Assert.False(liberado.GetProperty("activo").GetBoolean());
        Assert.Equal(e.Esleider.UsuarioId, liberado.GetProperty("liberado_por").GetGuid());
        Assert.Equal("Nombre esleider@ejemplo.com", liberado.GetProperty("liberado_por_nombre").GetString());
        Assert.NotEqual(JsonValueKind.Null, liberado.GetProperty("liberado_at").ValueKind);
        Assert.Null(await InspectorDelViajeAsync(viaje));
        // La inspeccion que Esleider cerro conserva a Esleider como autor.
        Assert.Equal(e.Esleider.UsuarioId, await Sembrador.EscalarAsync("SELECT UsuarioId FROM dbo.tblInspecciones WHERE Id = @i", ("@i", inspeccion)));
        Assert.Equal("cerrada", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblInspecciones WHERE Id = @i", ("@i", inspeccion)));
        // CA-6: quedo en la auditoria con quien libero.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.liberar_reclamo' AND UsuarioId = @u AND ViajeId = @v", ("@u", e.Esleider.UsuarioId), ("@v", viaje)));

        // Ya nadie lo tiene: liberar otra vez es 404 y Andres puede reclamarlo; el historial conserva los dos reclamos.
        Assert.Equal(HttpStatusCode.NotFound, (await LiberarAsync(e.Esleider, viaje)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Andres, viaje)).StatusCode);
        Assert.Equal(e.Andres.UsuarioId, await InspectorDelViajeAsync(viaje));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v", ("@v", viaje)));
    }

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    [InlineData(clsRol.ADMIN)]
    public async Task El_coordinador_o_un_administrador_liberan_el_reclamo_y_queda_registrado_quien_lo_libero(string rol)
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-DEF456", e.Envigado, FechaDeHoy());
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Esleider, viaje)).StatusCode);
        var quien = rol == clsRol.ADMIN ? e.Admin : e.Coordinador;

        var r = await LiberarAsync(quien, viaje);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Null(await InspectorDelViajeAsync(viaje));
        var historial = await JsonAsync(await Como(quien).GetAsync($"/api/viajes/{viaje}/reclamos"));
        Assert.Equal(1, historial.GetArrayLength());
        Assert.Equal(quien.UsuarioId, historial[0].GetProperty("liberado_por").GetGuid());
        Assert.Equal("Nombre " + (rol == clsRol.ADMIN ? "admin@ejemplo.com" : "luisa@ejemplo.com"), historial[0].GetProperty("liberado_por_nombre").GetString());
        Assert.Equal(e.Esleider.UsuarioId, historial[0].GetProperty("inspector_id").GetGuid());
    }

    [Fact]
    public async Task Nadie_mas_libera_un_reclamo_y_el_historial_solo_lo_ven_quienes_participan()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-ABC123", e.Envigado, FechaDeHoy(), conductor: e.Conductor);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Esleider, viaje)).StatusCode);

        var comoConductor = await LiberarAsync(e.Conductor, viaje);
        var comoOtroInspector = await LiberarAsync(e.Paula, viaje);
        var procesoInvalido = await LiberarAsync(e.Coordinador, viaje, "descargue");
        var delConductor = await Como(e.Conductor).GetAsync($"/api/viajes/{viaje}/reclamos");
        var delOtroInspector = await Como(e.Paula).GetAsync($"/api/viajes/{viaje}/reclamos");
        var delPropio = await Como(e.Esleider).GetAsync($"/api/viajes/{viaje}/reclamos");

        Assert.Equal(HttpStatusCode.Forbidden, comoConductor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comoOtroInspector.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, procesoInvalido.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delConductor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delOtroInspector.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delPropio.StatusCode);
        Assert.Equal(e.Esleider.UsuarioId, await InspectorDelViajeAsync(viaje));
    }

    // ------------------------------------------------------------------------------------------------- CA-4: sin caducidad automatica

    [Fact]
    public async Task Un_reclamo_no_caduca_ningun_proceso_periodico_lo_libera()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-ABC123", e.Envigado, FechaDeHoy());
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(e.Esleider, viaje)).StatusCode);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblReclamos SET ReclamadoAt = DATEADD(DAY, -30, SYSDATETIMEOFFSET()) WHERE ViajeId = @v", ("@v", viaje));

        using (var alcance = Fabrica.Services.CreateScope())
        {
            var procesos = alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>();
            await procesos.EjecutarUmbralesAsync();
            await procesos.EjecutarAvisosAsync();
            await procesos.EjecutarCapturasIncompletasAsync();
            await procesos.EjecutarTransferenciasPendientesAsync();
        }

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND LiberadoAt IS NULL AND LiberadoPor IS NULL", ("@v", viaje)));
        Assert.Equal(e.Esleider.UsuarioId, await InspectorDelViajeAsync(viaje));
        Assert.Equal(HttpStatusCode.Conflict, (await ReclamarAsync(e.Andres, viaje)).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------- ESC-08 y CA-5: la entrega

    [Fact]
    public async Task La_inspectora_del_punto_de_entrega_reclama_la_revision_de_la_entrega_y_los_demas_del_punto_la_ven_tomada()
    {
        var e = await EscenarioAsync();
        var otraDeCartagena = await SesionAsync("otra@ejemplo.com", clsRol.INSPECTOR);
        await PuntoAsync(otraDeCartagena, e.Cartagena);
        var viaje = await ViajeAsync(e, "MTK-2201", e.Envigado, FechaDeHoy(), destino: e.Cartagena, conductor: e.Conductor);

        var deEnvigado = await ReclamarAsync(e.Esleider, viaje, "entrega", e.Cartagena);
        var paula = await ReclamarAsync(e.Paula, viaje, "entrega", e.Cartagena);
        var otra = await ReclamarAsync(otraDeCartagena, viaje, "entrega", e.Cartagena);

        Assert.Equal(HttpStatusCode.Forbidden, deEnvigado.StatusCode);
        Assert.Equal(HttpStatusCode.Created, paula.StatusCode);
        Assert.Equal("entrega", (await JsonAsync(paula)).GetProperty("proceso").GetString());
        Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
        Assert.Equal("Ya fue tomado por otro inspector", (await JsonAsync(otra)).GetProperty("message").GetString());
        // Reclamar la entrega no cambia el inspector del cargue.
        Assert.Null(await InspectorDelViajeAsync(viaje));
        // La revision de la entrega queda a nombre de Paula: ve las entregas del viaje; los demas inspectores no.
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Paula).GetAsync($"/api/viajes/{viaje}/entregas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(otraDeCartagena).GetAsync($"/api/viajes/{viaje}/entregas")).StatusCode);

        // La libera ella: otra del punto puede tomarla.
        Assert.Equal(HttpStatusCode.OK, (await LiberarAsync(e.Paula, viaje, "entrega", e.Cartagena)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Paula).GetAsync($"/api/viajes/{viaje}/entregas")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReclamarAsync(otraDeCartagena, viaje, "entrega", e.Cartagena)).StatusCode);
    }

    [Fact]
    public async Task El_cargue_y_la_entrega_del_mismo_viaje_se_reclaman_por_separado()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-2201", e.Envigado, FechaDeHoy(), destino: e.Cartagena);

        var cargue = await ReclamarAsync(e.Esleider, viaje, "cargue");
        var entrega = await ReclamarAsync(e.Paula, viaje, "entrega", e.Cartagena);
        var liberaCargue = await LiberarAsync(e.Esleider, viaje, "cargue");

        Assert.Equal(HttpStatusCode.Created, cargue.StatusCode);
        Assert.Equal(HttpStatusCode.Created, entrega.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liberaCargue.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND Proceso = 'entrega' AND LiberadoAt IS NULL", ("@v", viaje)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND Proceso = 'cargue' AND LiberadoAt IS NULL", ("@v", viaje)));
    }

    // ------------------------------------------------------------------------------------------- el inspector ya no se asigna por PATCH

    [Fact]
    public async Task El_patch_del_viaje_ya_no_acepta_el_inspector_porque_lo_llena_el_reclamo()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-ABC123", e.Envigado, FechaDeHoy());

        var r = await Como(e.Coordinador).PatchAsync($"/api/viajes/{viaje}", Cuerpo(new { inspector_id = e.Andres.UsuarioId }));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.StartsWith("Campo no admitido", (await JsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").GetProperty("inspector_id")[0].GetString());
        Assert.Null(await InspectorDelViajeAsync(viaje));
    }

    // ------------------------------------------------------------------------------------------------- la ubicacion al tomar la entrega

    private const double LAT = 10.3910;
    private const double LNG = -75.4794;

    private static double AlNorte(double metros) => LAT + (metros / clsConfirmacionSitio.RADIO_TIERRA_M * 180 / Math.PI);

    /// <summary>La ubicacion confirmada del lugar con su radio (la carga manual del catalogo).</summary>
    private async Task UbicarAsync(Escenario e, Guid lugar, int radio = 500)
    {
        var r = await Como(e.Coordinador).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { latitud = LAT, longitud = LNG, radio_metros = radio }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    /// <summary>Toma la entrega de la parada (o el cargue) con la posicion a esos metros al norte del lugar; sin metros no manda posicion.</summary>
    private Task<HttpResponseMessage> TomarAsync(Sesion quien, Guid viaje, Guid lugar, double? metros, double? precision = 10, string proceso = "entrega")
    {
        var cuerpo = new Dictionary<string, object?> { ["proceso"] = proceso };
        if (proceso == "entrega")
        {
            cuerpo["lugar_id"] = lugar;
        }

        if (metros is { } m)
        {
            cuerpo["lat"] = AlNorte(m);
            cuerpo["lng"] = LNG;
        }

        if (precision is { } p && metros is not null)
        {
            cuerpo["precision"] = p;
        }

        return Como(quien).PostAsync($"/api/viajes/{viaje}/reclamos", Cuerpo(cuerpo));
    }

    private Task<int> AvisosFueraDeRangoAsync(Guid usuario, string canal = "bandeja") =>
        Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Urgencia = 'alta' AND Canal = @c AND UsuarioId = @u AND Datos LIKE N'%reclamo_entrega%'", ("@c", canal), ("@u", usuario));

    [Fact]
    public async Task Tomar_la_entrega_a_2300_metros_del_lugar_crea_el_reclamo_y_avisa_al_coordinador_sin_bloquear()
    {
        var e = await EscenarioAsync();
        await UbicarAsync(e, e.Cartagena);
        var viaje = await ViajeAsync(e, "MTK-2301", e.Envigado, FechaDeHoy(), destino: e.Cartagena, conductor: e.Conductor);

        var r = await TomarAsync(e.Paula, viaje, e.Cartagena, 2300);

        // El reclamo queda creado y la entrega tomada por Paula.
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(e.Paula.UsuarioId, (await JsonAsync(r)).GetProperty("inspector_id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v AND Proceso = 'entrega' AND LiberadoAt IS NULL", ("@v", viaje)));
        // Le llega al coordinador (bandeja y push, urgencia alta) con el viaje, quien, la distancia y el rango; al administrador y a la inspectora no.
        Assert.Equal(1, await AvisosFueraDeRangoAsync(e.Coordinador.UsuarioId));
        Assert.Equal(1, await AvisosFueraDeRangoAsync(e.Coordinador.UsuarioId, "push"));
        Assert.Equal(0, await AvisosFueraDeRangoAsync(e.Admin.UsuarioId));
        Assert.Equal(0, await AvisosFueraDeRangoAsync(e.Paula.UsuarioId));
        var titulo = (string)(await Sembrador.EscalarAsync("SELECT Titulo FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Canal = 'bandeja' AND UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)))!;
        var mensaje = (string)(await Sembrador.EscalarAsync("SELECT Mensaje FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Canal = 'bandeja' AND UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)))!;
        var json = (string)(await Sembrador.EscalarAsync("SELECT Datos FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Canal = 'bandeja' AND UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)))!;
        Assert.Equal("Fuera del rango: Puerto Cartagena", titulo);
        Assert.Contains("MTK-2301", mensaje);
        Assert.Contains("Nombre paula@ejemplo.com", mensaje);
        Assert.Contains("rango 500 m", mensaje);
        var datos = JsonDocument.Parse(json).RootElement;
        Assert.Equal("reclamo_entrega", datos.GetProperty("hecho").GetString());
        Assert.Equal(viaje, datos.GetProperty("viaje_id").GetGuid());
        Assert.Equal(e.Cartagena, datos.GetProperty("lugar_id").GetGuid());
        Assert.Equal("Puerto Cartagena", datos.GetProperty("lugar").GetString());
        Assert.Equal("Nombre paula@ejemplo.com", datos.GetProperty("usuario_nombre").GetString());
        Assert.Equal(2300, datos.GetProperty("distancia_m").GetDouble(), 0);
        Assert.Equal(500, datos.GetProperty("radio_m").GetDouble());
        Assert.Equal(10, datos.GetProperty("precision_m").GetDouble());
        // El resultado queda en la auditoria del reclamo.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.reclamar' AND ViajeId = @v AND Detalle LIKE N'%fuera_de_rango%'", ("@v", viaje)));
        // Volver a tomar lo que ya es suyo (200) no repite el aviso.
        Assert.Equal(HttpStatusCode.OK, (await TomarAsync(e.Paula, viaje, e.Cartagena, 2300)).StatusCode);
        Assert.Equal(1, await AvisosFueraDeRangoAsync(e.Coordinador.UsuarioId));
    }

    [Fact]
    public async Task Tomar_la_entrega_dentro_del_rango_no_avisa_y_deja_el_resultado_en_la_auditoria()
    {
        var e = await EscenarioAsync();
        await UbicarAsync(e, e.Cartagena);
        var viaje = await ViajeAsync(e, "MTK-2302", e.Envigado, FechaDeHoy(), destino: e.Cartagena);

        var r = await TomarAsync(e.Paula, viaje, e.Cartagena, 300);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(0, await AvisosFueraDeRangoAsync(e.Coordinador.UsuarioId));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.reclamar' AND ViajeId = @v AND Detalle LIKE N'%confirmado_en_sitio%'", ("@v", viaje)));
    }

    [Fact]
    public async Task Sin_posicion_con_precision_de_la_valvula_o_sin_ubicacion_confirmada_del_lugar_el_reclamo_no_avisa()
    {
        var e = await EscenarioAsync();
        await UbicarAsync(e, e.Cartagena);
        var sinPosicion = await ViajeAsync(e, "MTK-2303", e.Envigado, FechaDeHoy(), destino: e.Cartagena);
        var imprecisa = await ViajeAsync(e, "MTK-2304", e.Envigado, FechaDeHoy(), destino: e.Cartagena, vin: "VIN-REC-0002");
        var sinUbicar = await ViajeAsync(e, "MTK-2305", e.Envigado, FechaDeHoy(), destino: e.Envigado, vin: "VIN-REC-0003");

        // Sin posicion; con la precision en la valvula (500 m), que ni confirma ni acusa; y en un lugar sin ubicacion confirmada.
        Assert.Equal(HttpStatusCode.Created, (await TomarAsync(e.Paula, sinPosicion, e.Cartagena, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await TomarAsync(e.Paula, imprecisa, e.Cartagena, 2300, precision: 500)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await TomarAsync(e.Esleider, sinUbicar, e.Envigado, 9000)).StatusCode);

        Assert.Equal(0, await AvisosFueraDeRangoAsync(e.Coordinador.UsuarioId));
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE Proceso = 'entrega' AND LiberadoAt IS NULL"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.reclamar' AND (Detalle LIKE N'%fuera_de_rango%' OR Detalle LIKE N'%confirmado_en_sitio%')"));
    }

    [Fact]
    public async Task La_posicion_del_reclamo_se_valida_y_un_dato_invalido_no_crea_el_reclamo()
    {
        var e = await EscenarioAsync();
        await UbicarAsync(e, e.Cartagena);
        var viaje = await ViajeAsync(e, "MTK-2306", e.Envigado, FechaDeHoy(), destino: e.Cartagena);

        var soloLat = await Como(e.Paula).PostAsync($"/api/viajes/{viaje}/reclamos", Cuerpo(new { proceso = "entrega", lugar_id = e.Cartagena, lat = LAT }));
        var latFuera = await Como(e.Paula).PostAsync($"/api/viajes/{viaje}/reclamos", Cuerpo(new { proceso = "entrega", lugar_id = e.Cartagena, lat = 95, lng = LNG }));
        var precisionNegativa = await Como(e.Paula).PostAsync($"/api/viajes/{viaje}/reclamos", Cuerpo(new { proceso = "entrega", lugar_id = e.Cartagena, lat = LAT, lng = LNG, precision = -1 }));

        Assert.Equal(HttpStatusCode.BadRequest, soloLat.StatusCode);
        Assert.Contains("lng", (await JsonAsync(soloLat)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(x => x.Name));
        Assert.Equal(HttpStatusCode.BadRequest, latFuera.StatusCode);
        Assert.Contains("lat", (await JsonAsync(latFuera)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(x => x.Name));
        Assert.Equal(HttpStatusCode.BadRequest, precisionNegativa.StatusCode);
        Assert.Contains("precision", (await JsonAsync(precisionNegativa)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(x => x.Name));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = @v", ("@v", viaje)));
    }

    [Fact]
    public async Task El_reclamo_del_cargue_no_cambia_aunque_traiga_posicion()
    {
        var e = await EscenarioAsync();
        await UbicarAsync(e, e.Envigado);
        var viaje = await ViajeAsync(e, "MTK-2307", e.Envigado, FechaDeHoy());

        var r = await TomarAsync(e.Esleider, viaje, e.Envigado, 9000, proceso: "cargue");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(e.Esleider.UsuarioId, await InspectorDelViajeAsync(viaje));
        Assert.Equal(0, await AvisosFueraDeRangoAsync(e.Coordinador.UsuarioId));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.reclamar' AND ViajeId = @v AND (Detalle LIKE N'%fuera_de_rango%' OR Detalle LIKE N'%confirmado_en_sitio%')", ("@v", viaje)));
    }
}
