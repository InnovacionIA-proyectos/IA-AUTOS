using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La revision de un hallazgo del conductor: el inspector (de un VIN de sus viajes) y los administradores lo ubican en su zona y le asignan clase y nivel V; el coordinador de calidad tambien lo hace con
/// los de la salida de un Cargue sin personal de apoyo. Queda en el historial, el hallazgo pasa al seguimiento y un nivel critico alerta al cliente.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class RevisionHallazgosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-21T08:30:00-05:00";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Un viaje con su conductor, su inspector, un coordinador y un administrador, y una novedad del conductor sin revisar en el Transporte.</summary>
    private sealed record Escenario(Guid Viaje, Guid Novedad, Guid[] Etapas, string Vin, Sesion Conductor, Sesion Inspector, Sesion Coordinador, Sesion Admin);

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

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var r = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" });
        var cuerpo = await ComoJsonAsync(r);
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    private async Task<Escenario> EscenarioAsync(string vin = "VIN-REV-0001")
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        await Sembrador.InformeAsync(etapas[0], conductor.UsuarioId, cerrada: true, momento: "llegada");
        var viaje = await Sembrador.ViajeDeEtapaAsync(etapas[0]);
        var (ruta, hash) = await SubirAsync(conductor);
        var r = await Como(conductor).PostAsJsonAsync($"/api/viajes/{viaje}/vehiculos/{vin}/novedades", new
        {
            fecha = FECHA,
            lat = 4.7,
            lng = -74.2,
            tipo_novedad = "golpe",
            tamano_dano = "1_5cm",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.7, sello_lng = -74.2, hash_sha256 = hash },
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Escenario(viaje, (await ComoJsonAsync(r)).GetProperty("id").GetGuid(), etapas, vin, conductor, inspector, coordinador, admin);
    }

    /// <summary>Una revision completa: el cuadrante 7 del lado izquierdo, golpe y nivel V2 (que no es critico), salvo lo que cada prueba cambie.</summary>
    private static Dictionary<string, object?> Cuerpo(Action<Dictionary<string, object?>>? cambiar = null)
    {
        var cuerpo = new Dictionary<string, object?> { ["zona_provisional"] = "frente", ["clase_dano"] = "golpe", ["nivel_severidad"] = "v2" };
        cambiar?.Invoke(cuerpo);
        return cuerpo;
    }

    private Task<HttpResponseMessage> RevisarAsync(Sesion quien, Guid hallazgo, object cuerpo) =>
        Como(quien).PostAsJsonAsync($"/api/hallazgos/{hallazgo}/revision", cuerpo);

    private async Task<JsonElement> HistorialAsync(Sesion quien, Guid hallazgo) => await ComoJsonAsync(await Como(quien).GetAsync($"/api/hallazgos/{hallazgo}/seguimiento"));

    // ----------------------------------------------------------------------------------- la revision

    [Fact]
    public async Task El_inspector_del_viaje_revisa_la_novedad_la_completa_y_corrige_el_tamano()
    {
        var e = await EscenarioAsync();

        var r = await RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => { c["tamano_dano"] = "5_10cm"; c["nota"] = "  Abolladura sobre el frente  "; }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var h = await ComoJsonAsync(r);
        Assert.Equal(e.Novedad, h.GetProperty("id").GetGuid());
        Assert.Equal("golpe", h.GetProperty("tipo_novedad").GetString());
        Assert.Equal("conductor", h.GetProperty("origen").GetString());
        Assert.Equal("frente", h.GetProperty("zona_provisional").GetString());
        Assert.Equal("Frente", h.GetProperty("zona").GetString());
        Assert.Equal("golpe", h.GetProperty("clase_dano").GetString());
        Assert.Equal("v2", h.GetProperty("nivel_severidad").GetString());
        Assert.Equal("5_10cm", h.GetProperty("tamano_dano").GetString());
        Assert.Equal("Abolladura sobre el frente", h.GetProperty("nota").GetString());
        Assert.Equal(e.Inspector.UsuarioId, h.GetProperty("revisado_por").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, h.GetProperty("revisado_at").ValueKind);
        Assert.Equal("pendiente", h.GetProperty("estado_seguimiento").GetString());
        Assert.Equal(e.Inspector.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT RevisadoPor FROM dbo.tblHallazgos WHERE Id = @h", ("@h", e.Novedad)))!);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'hallazgo.revision'"));
    }

    [Fact]
    public async Task La_revision_queda_en_el_historial_con_lo_asignado_y_quien_la_hizo()
    {
        var e = await EscenarioAsync();

        await RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => { c["tamano_dano"] = "10_20cm"; c["nota"] = "Revisada con la foto"; }));

        var fila = (await HistorialAsync(e.Coordinador, e.Novedad)).EnumerateArray().Single();
        Assert.Equal("revision", fila.GetProperty("accion").GetString());
        Assert.Equal(e.Inspector.UsuarioId, fila.GetProperty("usuario_id").GetGuid());
        Assert.Equal("Nombre inspector@ejemplo.com", fila.GetProperty("usuario_nombre").GetString());
        Assert.Equal("Revisada con la foto", fila.GetProperty("nota").GetString());
        var detalle = fila.GetProperty("detalle");
        Assert.Equal("frente", detalle.GetProperty("zona_provisional").GetString());
        Assert.Equal("golpe", detalle.GetProperty("clase_dano").GetString());
        Assert.Equal("v2", detalle.GetProperty("nivel_severidad").GetString());
        Assert.Equal("10_20cm", detalle.GetProperty("tamano_dano").GetString());
        Assert.Equal("1_5cm", detalle.GetProperty("tamano_dano_anterior").GetString());
    }

    [Fact]
    public async Task Sin_corregir_el_tamano_conserva_el_que_eligio_el_conductor()
    {
        var e = await EscenarioAsync();

        var h = await ComoJsonAsync(await RevisarAsync(e.Inspector, e.Novedad, Cuerpo()));

        Assert.Equal("1_5cm", h.GetProperty("tamano_dano").GetString());
        var detalle = (await HistorialAsync(e.Coordinador, e.Novedad)).EnumerateArray().Single().GetProperty("detalle");
        Assert.Equal(JsonValueKind.Null, detalle.GetProperty("tamano_dano_anterior").ValueKind);
    }

    [Fact]
    public async Task La_zona_numerada_se_ubica_por_cuadrante_y_lado()
    {
        var e = await EscenarioAsync();
        await Sembrador.CuadranteAsync(7, "Puerta delantera");

        var r = await RevisarAsync(e.Admin, e.Novedad, new { cuadrante_numero = 7, lado = "izquierdo", clase_dano = "raya_pintar", nivel_severidad = "v3" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var h = await ComoJsonAsync(r);
        Assert.Equal(7, h.GetProperty("cuadrante_numero").GetInt32());
        Assert.Equal("izquierdo", h.GetProperty("lado").GetString());
        Assert.Equal("Izquierdo 7 · Puerta delantera", h.GetProperty("zona").GetString());
        Assert.Equal(e.Admin.UsuarioId, h.GetProperty("revisado_por").GetGuid());
    }

    [Fact]
    public async Task Desde_la_revision_la_novedad_entra_al_seguimiento_y_antes_no()
    {
        var e = await EscenarioAsync();
        await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/llegada", Json("{\"fecha\":\"2026-10-21T10:00:00-05:00\",\"lat\":4.9,\"lng\":-74.4}"));

        var antes = await Como(e.Coordinador).PostAsJsonAsync($"/api/hallazgos/{e.Novedad}/seguimiento", new { estado = "rechazado" });
        await RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => c["nivel_severidad"] = "v1"));
        var despues = await Como(e.Coordinador).PostAsJsonAsync($"/api/hallazgos/{e.Novedad}/seguimiento", new { estado = "rechazado" });

        Assert.Equal(HttpStatusCode.Conflict, antes.StatusCode);
        Assert.Equal(HttpStatusCode.OK, despues.StatusCode);
        var historial = (await HistorialAsync(e.Coordinador, e.Novedad)).EnumerateArray().Select(f => f.GetProperty("accion").GetString()).ToList();
        Assert.Equal(["revision", "seguimiento"], historial);
    }

    // ---------------------------------------------------------------------------------------- alertas

    [Fact]
    public async Task Un_nivel_critico_alerta_al_coordinador_y_al_administrador_del_cliente()
    {
        var e = await EscenarioAsync();
        await Como(e.Coordinador).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-coord", plataforma = "android" });

        var r = await RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => c["nivel_severidad"] = "v1_plus"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        foreach (var destinatario in new[] { e.Coordinador, e.Admin })
        {
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'hallazgo_critico' AND Urgencia = 'alta' AND Canal = 'bandeja' AND UsuarioId = @u AND HallazgoId = @h AND ViajeId = @v",
                ("@u", destinatario.UsuarioId), ("@h", e.Novedad), ("@v", e.Viaje)));
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'hallazgo_critico' AND UsuarioId IN (@c, @i)", ("@c", e.Conductor.UsuarioId), ("@i", e.Inspector.UsuarioId)));
        Assert.Contains(_push.Enviados, x => x.Token == "tk-coord" && x.Mensaje.Datos["tipo"] == "hallazgo_critico");
    }

    [Fact]
    public async Task Un_nivel_que_no_es_critico_no_alerta()
    {
        var e = await EscenarioAsync();

        await RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => c["nivel_severidad"] = "v2"));

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'hallazgo_critico'"));
    }

    [Fact]
    public async Task Los_niveles_criticos_son_el_parametro_niveles_criticos()
    {
        var e = await EscenarioAsync();
        await CambiarParametroAsync("niveles_criticos", "v1_plus, v2", _api);

        await RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => c["nivel_severidad"] = "v2"));

        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'hallazgo_critico' AND Canal = 'bandeja'"));
    }

    // ------------------------------------------------------------------------------------- conflictos

    [Fact]
    public async Task Una_novedad_ya_revisada_responde_409_hallazgo_ya_revisado_y_conserva_la_primera_revision()
    {
        var e = await EscenarioAsync();
        await RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => c["nivel_severidad"] = "v3"));

        var deAdmin = await RevisarAsync(e.Admin, e.Novedad, Cuerpo(c => c["nivel_severidad"] = "v1"));
        var delMismo = await RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => c["nivel_severidad"] = "v1"));

        foreach (var r in new[] { deAdmin, delMismo })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("HALLAZGO_YA_REVISADO", (await ComoJsonAsync(r)).GetProperty("code").GetString());
        }

        Assert.Equal("v3", (string?)await Sembrador.EscalarAsync("SELECT NivelSeveridad FROM dbo.tblHallazgos WHERE Id = @h", ("@h", e.Novedad)));
        Assert.Equal(1, (await HistorialAsync(e.Coordinador, e.Novedad)).GetArrayLength());
    }

    [Fact]
    public async Task Dos_revisiones_simultaneas_guardan_solo_una()
    {
        var e = await EscenarioAsync();

        var respuestas = await Task.WhenAll(
            RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => c["nivel_severidad"] = "v3")),
            RevisarAsync(e.Admin, e.Novedad, Cuerpo(c => c["nivel_severidad"] = "v1")));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgoSeguimiento WHERE HallazgoId = @h AND Accion = 'revision'", ("@h", e.Novedad)));
    }

    [Fact]
    public async Task Un_hallazgo_que_no_es_del_conductor_responde_409()
    {
        var e = await EscenarioAsync();
        var (_, delInspector) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[0], e.Inspector.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!, nivel: "v2");

        var r = await RevisarAsync(e.Inspector, delInspector, Cuerpo());

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("CONFLICT", (await ComoJsonAsync(r)).GetProperty("code").GetString());
    }

    // ----------------------------------------------------------------------------------- validaciones

    [Theory]
    [InlineData("nivel_severidad", null)]
    [InlineData("clase_dano", null)]
    [InlineData("nivel_severidad", "v9")]
    [InlineData("clase_dano", "inventada")]
    [InlineData("tamano_dano", "enorme")]
    [InlineData("zona_provisional", "techo-volador")]
    public async Task Una_revision_sin_nivel_clase_o_con_valores_invalidos_responde_400_y_no_se_guarda(string campo, string? valor)
    {
        var e = await EscenarioAsync();

        var r = await RevisarAsync(e.Inspector, e.Novedad, Cuerpo(c => c[campo] = valor));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(r)).TryGetProperty(campo, out _), $"Falta el error de {campo}");
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND RevisadoAt IS NOT NULL", ("@h", e.Novedad)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"clase_dano\":\"golpe\",\"nivel_severidad\":\"v2\"}")]
    [InlineData("{\"cuadrante_numero\":7,\"zona_provisional\":\"frente\",\"clase_dano\":\"golpe\",\"nivel_severidad\":\"v2\"}")]
    [InlineData("{\"cuadrante_numero\":7,\"clase_dano\":\"golpe\",\"nivel_severidad\":\"v2\"}")]
    [InlineData("{\"cuadrante_numero\":30,\"lado\":\"izquierdo\",\"clase_dano\":\"golpe\",\"nivel_severidad\":\"v2\"}")]
    public async Task Una_revision_sin_zona_o_con_una_zona_mal_formada_responde_400(string cuerpo)
    {
        var e = await EscenarioAsync();

        var r = await Como(e.Inspector).PostAsync($"/api/hallazgos/{e.Novedad}/revision", Json(cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Un_cuadrante_que_no_existe_o_esta_inactivo_responde_400()
    {
        var e = await EscenarioAsync();

        var r = await RevisarAsync(e.Inspector, e.Novedad, new { cuadrante_numero = 7, lado = "izquierdo", clase_dano = "golpe", nivel_severidad = "v2" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("no existe o está inactivo", Errores(await ComoJsonAsync(r)).GetProperty("cuadrante_numero")[0].GetString());
    }

    [Fact]
    public async Task Un_hallazgo_inexistente_responde_404()
    {
        var e = await EscenarioAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await RevisarAsync(e.Inspector, Guid.NewGuid(), Cuerpo())).StatusCode);
    }

    // ---------------------------------------------------------------------------------------- permisos

    [Theory]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.CONDUCTOR)]
    public async Task Los_roles_que_la_tarea_no_nombra_responden_403(string rol)
    {
        var e = await EscenarioAsync();
        var sesion = rol == clsRol.CONDUCTOR ? e.Conductor : await SesionAsync($"{rol}-x@ejemplo.com", rol);

        var r = await RevisarAsync(sesion, e.Novedad, Cuerpo());

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND RevisadoAt IS NOT NULL", ("@h", e.Novedad)));
    }

    [Fact]
    public async Task Un_inspector_sin_el_vin_en_sus_viajes_responde_403()
    {
        var e = await EscenarioAsync();
        var otro = await SesionAsync("otro-inspector@ejemplo.com", clsRol.INSPECTOR);

        var r = await RevisarAsync(otro, e.Novedad, Cuerpo());

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task El_coordinador_no_revisa_una_novedad_de_transporte_pero_si_la_de_un_cargue_hecho_por_excepcion()
    {
        var e = await EscenarioAsync();
        var deTransporte = await RevisarAsync(e.Coordinador, e.Novedad, Cuerpo());

        // El conductor registra un hallazgo en la salida del Cargue que hizo por la excepcion «No hay inspector»: queda sin zona confirmada ni nivel.
        var inspeccion = await ComoJsonAsync(await Como(e.Conductor).PostAsJsonAsync("/api/inspecciones", new { etapa_id = e.Etapas[0], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1, motivo_sin_inspector = "inspector_no_llego" }));
        var (ruta, hash) = await SubirAsync(e.Conductor);
        var creado = await Como(e.Conductor).PostAsJsonAsync($"/api/inspecciones/{inspeccion.GetProperty("id").GetGuid()}/hallazgos", new
        {
            zona_provisional = "frente",
            clase_dano = "rotura",
            tamano_dano = "1_5cm",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, hash_sha256 = hash },
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var delCargue = (await ComoJsonAsync(creado)).GetProperty("id").GetGuid();

        var aDistancia = await RevisarAsync(e.Coordinador, delCargue, Cuerpo(c => { c["clase_dano"] = "rotura"; c["nivel_severidad"] = "v1_plus"; }));

        Assert.Equal(HttpStatusCode.Forbidden, deTransporte.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aDistancia.StatusCode);
        var h = await ComoJsonAsync(aDistancia);
        Assert.Equal("v1_plus", h.GetProperty("nivel_severidad").GetString());
        Assert.Equal(e.Coordinador.UsuarioId, h.GetProperty("revisado_por").GetGuid());
        // Un V1+ asignado a distancia alerta y deja a la unidad lista para recuperarse o no salir.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'hallazgo_critico' AND Canal = 'bandeja' AND UsuarioId = @u AND HallazgoId = @h", ("@u", e.Admin.UsuarioId), ("@h", delCargue)));
    }

    [Fact]
    public async Task El_inspector_tambien_revisa_el_hallazgo_del_conductor_de_un_cargue_hecho_por_excepcion()
    {
        var e = await EscenarioAsync();
        var (_, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(
            e.Etapas[0], e.Conductor.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!, nivel: null, origen: "conductor");

        var r = await RevisarAsync(e.Inspector, hallazgo, Cuerpo());

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Quien_se_limita_a_su_cliente_no_revisa_lo_de_otro_cliente_y_el_super_administrador_si()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var adminOtro = await SesionAsync("admin-otro@ejemplo.com", clsRol.ADMIN, otroCliente);
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var deAdminOtro = await RevisarAsync(adminOtro, e.Novedad, Cuerpo());
        var deCoordinadorOtro = await RevisarAsync(coordinadorOtro, e.Novedad, Cuerpo());
        var deSuper = await RevisarAsync(superAdmin, e.Novedad, Cuerpo());

        Assert.Equal(HttpStatusCode.Forbidden, deAdminOtro.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deCoordinadorOtro.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deSuper.StatusCode);
    }

    [Fact]
    public async Task La_revision_exige_sesion()
    {
        var e = await EscenarioAsync();

        var r = await Cliente(fabrica: _api).PostAsJsonAsync($"/api/hallazgos/{e.Novedad}/revision", Cuerpo());

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
