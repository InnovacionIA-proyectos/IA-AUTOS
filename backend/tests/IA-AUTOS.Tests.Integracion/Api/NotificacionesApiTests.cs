using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La bandeja de notificaciones: cada usuario ve y lee las suyas, el coordinador de calidad y los administradores responden la novedad del conductor (solo la primera respuesta se guarda y le
/// llega al conductor del viaje) y cada usuario registra los dispositivos donde recibe el push.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class NotificacionesApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-21T08:30:00-05:00";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>El viaje del conductor con una novedad ya registrada y alertados el coordinador y el administrador del cliente.</summary>
    private sealed record Escenario(Guid Viaje, Guid Hallazgo, string Vin, Sesion Conductor, Sesion Coordinador, Sesion Admin);

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

    private async Task<Escenario> EscenarioAsync()
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-NTF-0001", conductorId: conductor.UsuarioId);
        await Sembrador.InformeAsync(etapas[0], conductor.UsuarioId, cerrada: true, momento: "llegada");
        var viaje = await Sembrador.ViajeDeEtapaAsync(etapas[0]);

        var subida = await Como(conductor).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" });
        var foto = await ComoJsonAsync(subida);
        var novedad = await Como(conductor).PostAsJsonAsync($"/api/viajes/{viaje}/vehiculos/VIN-NTF-0001/novedades", new
        {
            fecha = FECHA,
            lat = 4.7,
            lng = -74.2,
            tipo_novedad = "golpe",
            tamano_dano = "5_10cm",
            evidencia = new { storage_path = foto.GetProperty("storage_path").GetString(), sello_fecha = FECHA, sello_lat = 4.7, sello_lng = -74.2, hash_sha256 = foto.GetProperty("hash_sha256").GetString() },
        });
        Assert.Equal(HttpStatusCode.Created, novedad.StatusCode);
        return new Escenario(viaje, (await ComoJsonAsync(novedad)).GetProperty("id").GetGuid(), "VIN-NTF-0001", conductor, coordinador, admin);
    }

    private static JsonElement Errores(JsonElement cuerpo) => cuerpo.GetProperty("details").GetProperty("fieldErrors");

    private async Task<JsonElement> BandejaAsync(Sesion quien, string consulta = "")
    {
        var r = await Como(quien).GetAsync("/api/notificaciones" + consulta);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await ComoJsonAsync(r);
    }

    private async Task<Guid> NotificacionDeAsync(Sesion quien, string tipo = "novedad_conductor")
    {
        var bandeja = await BandejaAsync(quien);
        return bandeja.GetProperty("items").EnumerateArray().First(n => n.GetProperty("tipo").GetString() == tipo).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> ResponderAsync(Sesion quien, Guid notificacion, object cuerpo) =>
        Como(quien).PostAsJsonAsync($"/api/notificaciones/{notificacion}/respuesta", cuerpo);

    // ---------------------------------------------------------------------------------------- bandeja

    [Fact]
    public async Task Cada_usuario_ve_en_su_bandeja_solo_sus_notificaciones_con_el_viaje_y_el_vin_de_la_novedad()
    {
        var e = await EscenarioAsync();

        var coordinador = await BandejaAsync(e.Coordinador);
        var conductor = await BandejaAsync(e.Conductor);

        Assert.Equal(1, coordinador.GetProperty("total").GetInt32());
        Assert.Equal(1, coordinador.GetProperty("no_leidas").GetInt32());
        Assert.Equal(1, coordinador.GetProperty("pagina").GetInt32());
        Assert.Equal(50, coordinador.GetProperty("tamano").GetInt32());
        var n = coordinador.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("novedad_conductor", n.GetProperty("tipo").GetString());
        Assert.Equal("alta", n.GetProperty("urgencia").GetString());
        Assert.Equal(e.Viaje, n.GetProperty("viaje_id").GetGuid());
        Assert.StartsWith("MTK-", n.GetProperty("codigo_mtrack").GetString());
        Assert.Equal(e.Vin, n.GetProperty("vin").GetString());
        Assert.Equal(e.Hallazgo, n.GetProperty("hallazgo_id").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, n.GetProperty("inspeccion_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, n.GetProperty("leida_at").ValueKind);
        Assert.Equal(JsonValueKind.Null, n.GetProperty("respuesta").ValueKind);
        Assert.Contains(e.Vin, n.GetProperty("titulo").GetString());
        // El conductor no recibe la alerta de su propia novedad, y la fila de push (solo el registro del envio) no se lista.
        Assert.Equal(0, conductor.GetProperty("total").GetInt32());
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)));
    }

    [Fact]
    public async Task La_bandeja_se_filtra_por_leida_tipo_y_urgencia_y_se_pagina()
    {
        var e = await EscenarioAsync();
        // Otras alertas del mismo destinatario, ya creadas en la base.
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, EnviadaAt, ClaveDedupe, CreatedAt) "
            + "VALUES (NEWID(), 'umbral_patron', 'media', @u, N'Patron', N'Patron repetido', 'bandeja', SYSDATETIMEOFFSET(), 'zz:patron', DATEADD(HOUR, -1, SYSDATETIMEOFFSET())), "
            + "(NEWID(), 'captura_incompleta', 'media', @u, N'Captura', N'Falta captura', 'bandeja', SYSDATETIMEOFFSET(), 'zz:captura', DATEADD(HOUR, -2, SYSDATETIMEOFFSET()))",
            ("@u", e.Coordinador.UsuarioId));
        var novedad = await NotificacionDeAsync(e.Coordinador);
        await Como(e.Coordinador).PostAsync($"/api/notificaciones/{novedad}/leida", null);

        var todas = await BandejaAsync(e.Coordinador);
        var sinLeer = await BandejaAsync(e.Coordinador, "?leida=false");
        var leidas = await BandejaAsync(e.Coordinador, "?leida=true");
        var altas = await BandejaAsync(e.Coordinador, "?urgencia=alta");
        var patrones = await BandejaAsync(e.Coordinador, "?tipo=umbral_patron");
        var paginaDos = await BandejaAsync(e.Coordinador, "?tamano=1&pagina=2");
        var tamanoExcesivo = await BandejaAsync(e.Coordinador, "?tamano=5000");

        Assert.Equal(3, todas.GetProperty("total").GetInt32());
        // Las mas recientes primero.
        Assert.Equal(["novedad_conductor", "umbral_patron", "captura_incompleta"], todas.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("tipo").GetString()).ToList());
        Assert.Equal(2, sinLeer.GetProperty("total").GetInt32());
        Assert.Equal(1, leidas.GetProperty("total").GetInt32());
        // El conteo de no leidas no cambia con los filtros.
        Assert.Equal(2, leidas.GetProperty("no_leidas").GetInt32());
        Assert.Equal(1, altas.GetProperty("total").GetInt32());
        Assert.Equal("umbral_patron", patrones.GetProperty("items").EnumerateArray().Single().GetProperty("tipo").GetString());
        Assert.Equal(3, paginaDos.GetProperty("total").GetInt32());
        Assert.Equal(2, paginaDos.GetProperty("pagina").GetInt32());
        Assert.Equal("umbral_patron", paginaDos.GetProperty("items").EnumerateArray().Single().GetProperty("tipo").GetString());
        Assert.Equal(200, tamanoExcesivo.GetProperty("tamano").GetInt32());
    }

    [Theory]
    [InlineData("?leida=quizas", "leida")]
    [InlineData("?tipo=inventado", "tipo")]
    [InlineData("?urgencia=baja", "urgencia")]
    public async Task Un_filtro_invalido_de_la_bandeja_responde_400_con_su_campo(string consulta, string campo)
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var r = await Como(coordinador).GetAsync("/api/notificaciones" + consulta);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(r)).TryGetProperty(campo, out _));
    }

    [Fact]
    public async Task El_super_administrador_ve_las_notificaciones_de_todos_y_quien_no_recibe_alertas_responde_403()
    {
        var e = await EscenarioAsync();
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL);

        var bandeja = await BandejaAsync(superAdmin);
        var deComercial = await Como(comercial).GetAsync("/api/notificaciones");
        var anonimo = await Cliente(fabrica: _api).GetAsync("/api/notificaciones");

        Assert.Equal(2, bandeja.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, deComercial.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    // ------------------------------------------------------------------------------------------ leida

    [Fact]
    public async Task El_destinatario_marca_leida_su_notificacion_y_leerla_otra_vez_no_cambia_la_fecha()
    {
        var e = await EscenarioAsync();
        var id = await NotificacionDeAsync(e.Coordinador);

        var primera = await Como(e.Coordinador).PostAsync($"/api/notificaciones/{id}/leida", null);
        var primeraFecha = (await ComoJsonAsync(primera)).GetProperty("leida_at").GetString();
        var segunda = await Como(e.Coordinador).PostAsync($"/api/notificaciones/{id}/leida", null);

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.NotNull(primeraFecha);
        Assert.Equal(primeraFecha, (await ComoJsonAsync(segunda)).GetProperty("leida_at").GetString());
        Assert.Equal(0, (await BandejaAsync(e.Coordinador)).GetProperty("no_leidas").GetInt32());
        // La otra persona sigue con la suya sin leer.
        Assert.Equal(1, (await BandejaAsync(e.Admin)).GetProperty("no_leidas").GetInt32());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'notificacion.leer'"));
    }

    [Fact]
    public async Task Nadie_marca_leida_la_notificacion_de_otro_y_una_que_no_existe_responde_404()
    {
        var e = await EscenarioAsync();
        var id = await NotificacionDeAsync(e.Coordinador);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var push = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblNotificaciones WHERE Canal = 'push' AND UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)))!;

        var deOtro = await Como(e.Admin).PostAsync($"/api/notificaciones/{id}/leida", null);
        var deSuperAdmin = await Como(superAdmin).PostAsync($"/api/notificaciones/{id}/leida", null);
        var inexistente = await Como(e.Coordinador).PostAsync($"/api/notificaciones/{Guid.NewGuid()}/leida", null);
        var filaDePush = await Como(e.Coordinador).PostAsync($"/api/notificaciones/{push}/leida", null);

        Assert.Equal(HttpStatusCode.Forbidden, deOtro.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deSuperAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, filaDePush.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE LeidaAt IS NOT NULL"));
    }

    // --------------------------------------------------------------------------------------- respuesta

    [Fact]
    public async Task El_coordinador_responde_la_novedad_y_la_respuesta_le_llega_al_conductor_del_viaje()
    {
        var e = await EscenarioAsync();
        await Como(e.Conductor).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-conductor", plataforma = "android", modelo = "Galaxy" });
        var id = await NotificacionDeAsync(e.Coordinador);

        var r = await ResponderAsync(e.Coordinador, id, new { texto = "  Detente en el siguiente peaje y espera al inspector  ", medio = "app" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var respuesta = (await ComoJsonAsync(r)).GetProperty("respuesta");
        Assert.Equal("Detente en el siguiente peaje y espera al inspector", respuesta.GetProperty("texto").GetString());
        Assert.Equal("app", respuesta.GetProperty("medio").GetString());
        Assert.Equal("Nombre coord@ejemplo.com", respuesta.GetProperty("usuario_nombre").GetString());
        Assert.NotNull(respuesta.GetProperty("fecha").GetString());
        // La novedad queda atendida para todos: tambien el administrador la ve respondida.
        var deAdmin = (await BandejaAsync(e.Admin)).GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Detente en el siguiente peaje y espera al inspector", deAdmin.GetProperty("respuesta").GetProperty("texto").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(DISTINCT RespondidaPor) FROM dbo.tblNotificaciones WHERE RespondidaPor = @u", ("@u", e.Coordinador.UsuarioId)));
        // El conductor recibe la respuesta, de urgencia alta, por bandeja y push.
        var delConductor = (await BandejaAsync(e.Conductor)).GetProperty("items").EnumerateArray().Single();
        Assert.Equal("respuesta_conductor", delConductor.GetProperty("tipo").GetString());
        Assert.Equal("alta", delConductor.GetProperty("urgencia").GetString());
        Assert.Equal("Detente en el siguiente peaje y espera al inspector", delConductor.GetProperty("mensaje").GetString());
        Assert.Equal(e.Hallazgo, delConductor.GetProperty("hallazgo_id").GetGuid());
        Assert.Equal(e.Vin, delConductor.GetProperty("vin").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'respuesta_conductor' AND Canal = 'push' AND UsuarioId = @u AND EnviadaAt IS NOT NULL", ("@u", e.Conductor.UsuarioId)));
        Assert.Contains(_push.Enviados, x => x.Token == "tk-conductor" && x.Mensaje.Datos["tipo"] == "respuesta_conductor");
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'notificacion.responder'"));
    }

    [Fact]
    public async Task La_respuesta_por_llamada_tambien_deja_atendida_la_alerta()
    {
        var e = await EscenarioAsync();
        var id = await NotificacionDeAsync(e.Admin);

        var r = await ResponderAsync(e.Admin, id, new { texto = "Hable con el conductor por teléfono: sin riesgo", medio = "llamada" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("llamada", (await ComoJsonAsync(r)).GetProperty("respuesta").GetProperty("medio").GetString());
        Assert.Equal("llamada", (await BandejaAsync(e.Coordinador)).GetProperty("items").EnumerateArray().Single().GetProperty("respuesta").GetProperty("medio").GetString());
    }

    [Fact]
    public async Task La_segunda_respuesta_a_la_misma_novedad_responde_409_y_conserva_la_primera()
    {
        var e = await EscenarioAsync();
        var segundoCoordinador = await SesionAsync("coord2@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        // El segundo coordinador tambien recibe la alerta si la novedad se registra despues de que existe.
        var primera = await ResponderAsync(e.Coordinador, await NotificacionDeAsync(e.Coordinador), new { texto = "Primera respuesta", medio = "app" });

        var segunda = await ResponderAsync(e.Admin, await NotificacionDeAsync(e.Admin), new { texto = "Segunda respuesta", medio = "llamada" });
        var delMismo = await ResponderAsync(e.Coordinador, await NotificacionDeAsync(e.Coordinador), new { texto = "Otra vez", medio = "app" });

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delMismo.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE RespuestaTexto IN (N'Segunda respuesta', N'Otra vez')"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'respuesta_conductor' AND Canal = 'bandeja'"));
        Assert.Equal(0, (await BandejaAsync(segundoCoordinador)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Dos_respuestas_simultaneas_guardan_solo_una()
    {
        var e = await EscenarioAsync();
        var coordinador = await NotificacionDeAsync(e.Coordinador);
        var admin = await NotificacionDeAsync(e.Admin);

        var respuestas = await Task.WhenAll(
            ResponderAsync(e.Coordinador, coordinador, new { texto = "Desde el coordinador", medio = "app" }),
            ResponderAsync(e.Admin, admin, new { texto = "Desde el administrador", medio = "app" }));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(DISTINCT RespuestaTexto) FROM dbo.tblNotificaciones WHERE RespuestaTexto IS NOT NULL"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'respuesta_conductor' AND Canal = 'bandeja'"));
    }

    [Theory]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    public async Task Un_rol_sin_permiso_no_responde_la_novedad(string rol)
    {
        var e = await EscenarioAsync();
        var sesion = await SesionAsync($"{rol}-x@ejemplo.com", rol);

        var r = await ResponderAsync(sesion, await NotificacionDeAsync(e.Coordinador), new { texto = "Respuesta", medio = "app" });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE RespuestaTexto IS NOT NULL"));
    }

    [Fact]
    public async Task El_coordinador_de_otro_cliente_no_responde_la_novedad_pero_el_super_administrador_si()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var id = await NotificacionDeAsync(e.Coordinador);

        var ajeno = await ResponderAsync(coordinadorOtro, id, new { texto = "No es mi cliente", medio = "app" });
        var delSuper = await ResponderAsync(superAdmin, id, new { texto = "Atendida por el super administrador", medio = "llamada" });

        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delSuper.StatusCode);
        Assert.Equal("Nombre super@ejemplo.com", (await ComoJsonAsync(delSuper)).GetProperty("respuesta").GetProperty("usuario_nombre").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"texto\":\"   \",\"medio\":\"app\"}")]
    [InlineData("{\"texto\":\"Hola\"}")]
    [InlineData("{\"texto\":\"Hola\",\"medio\":\"paloma\"}")]
    public async Task Una_respuesta_sin_texto_o_con_un_medio_invalido_responde_400(string cuerpo)
    {
        var e = await EscenarioAsync();

        var r = await Como(e.Coordinador).PostAsync($"/api/notificaciones/{await NotificacionDeAsync(e.Coordinador)}/respuesta", Json(cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE RespuestaTexto IS NOT NULL"));
    }

    [Fact]
    public async Task La_respuesta_admite_mil_caracteres_y_no_mil_uno()
    {
        var e = await EscenarioAsync();
        var id = await NotificacionDeAsync(e.Coordinador);

        var larga = await ResponderAsync(e.Coordinador, id, new { texto = new string('r', 1001), medio = "app" });
        var justa = await ResponderAsync(e.Coordinador, id, new { texto = new string('r', 1000), medio = "app" });

        Assert.Equal(HttpStatusCode.BadRequest, larga.StatusCode);
        Assert.Equal(HttpStatusCode.OK, justa.StatusCode);
    }

    [Fact]
    public async Task Solo_la_novedad_del_conductor_se_responde_y_la_notificacion_inexistente_responde_404()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, ClienteId, Titulo, Mensaje, Canal, EnviadaAt, ClaveDedupe) "
            + "SELECT NEWID(), 'umbral_patron', 'media', @u, ClienteId, N'Patron', N'Patron repetido', 'bandeja', SYSDATETIMEOFFSET(), 'zz:patron' FROM dbo.tblUsuarios WHERE Id = @u",
            ("@u", e.Coordinador.UsuarioId));
        var patron = await NotificacionDeAsync(e.Coordinador, "umbral_patron");

        var deOtroTipo = await ResponderAsync(e.Coordinador, patron, new { texto = "Respuesta", medio = "app" });
        var inexistente = await ResponderAsync(e.Coordinador, Guid.NewGuid(), new { texto = "Respuesta", medio = "app" });

        Assert.Equal(HttpStatusCode.Conflict, deOtroTipo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    // ------------------------------------------------------------------------------------ dispositivos

    [Fact]
    public async Task El_usuario_registra_su_dispositivo_y_registrarlo_de_nuevo_lo_actualiza()
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);

        var alta = await Como(conductor).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-1", plataforma = "android", modelo = "Pixel 8" });
        var cambio = await Como(conductor).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-1", plataforma = "ios", modelo = "iPhone 15" });

        Assert.Equal(HttpStatusCode.OK, alta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        var cuerpo = await ComoJsonAsync(cambio);
        Assert.Equal("ios", cuerpo.GetProperty("plataforma").GetString());
        Assert.Equal("iPhone 15", cuerpo.GetProperty("modelo").GetString());
        Assert.True(cuerpo.GetProperty("activo").GetBoolean());
        Assert.Equal((await ComoJsonAsync(alta)).GetProperty("id").GetGuid(), cuerpo.GetProperty("id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblDispositivos WHERE TokenFcm = 'tk-1' AND Plataforma = 'ios' AND UsuarioId = @u", ("@u", conductor.UsuarioId)));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'dispositivo.registrar'"));
    }

    [Fact]
    public async Task El_celular_que_cambia_de_usuario_pasa_a_nombre_de_quien_lo_registra()
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        await Como(conductor).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-compartido", plataforma = "android" });

        var r = await Como(inspector).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-compartido", plataforma = "android" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblDispositivos WHERE TokenFcm = 'tk-compartido' AND UsuarioId = @u", ("@u", inspector.UsuarioId)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token_fcm\":\"tk\",\"plataforma\":\"symbian\"}")]
    [InlineData("{\"plataforma\":\"android\"}")]
    [InlineData("{\"token_fcm\":\"   \",\"plataforma\":\"android\"}")]
    public async Task Un_dispositivo_sin_token_o_con_una_plataforma_invalida_responde_400(string cuerpo)
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);

        var r = await Como(conductor).PostAsync("/api/dispositivos", Json(cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblDispositivos"));
    }

    [Fact]
    public async Task Al_cerrar_sesion_el_dispositivo_se_retira_y_ya_no_recibe_push()
    {
        var e = await EscenarioAsync();
        await Como(e.Coordinador).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-coord", plataforma = "web" });

        var r = await Como(e.Coordinador).DeleteAsync("/api/dispositivos/tk-coord");
        var repetido = await Como(e.Coordinador).DeleteAsync("/api/dispositivos/tk-coord");
        var inexistente = await Como(e.Coordinador).DeleteAsync("/api/dispositivos/tk-que-no-existe");

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, inexistente.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblDispositivos WHERE TokenFcm = 'tk-coord' AND Activo = 0"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'dispositivo.retirar'"));
        // Una alerta nueva a ese usuario ya no le envia nada a ese celular.
        var antes = _push.Enviados.Count;
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = 'en_curso' WHERE Id = @v", ("@v", e.Viaje));
        Assert.Equal(antes, _push.Enviados.Count);
    }

    [Fact]
    public async Task Nadie_retira_el_dispositivo_de_otro_usuario()
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        await Como(conductor).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-conductor", plataforma = "android" });

        var r = await Como(inspector).DeleteAsync("/api/dispositivos/tk-conductor");

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblDispositivos WHERE TokenFcm = 'tk-conductor' AND Activo = 1"));
    }

    [Fact]
    public async Task Los_dispositivos_exigen_sesion()
    {
        var registrar = await Cliente(fabrica: _api).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk", plataforma = "android" });
        var retirar = await Cliente(fabrica: _api).DeleteAsync("/api/dispositivos/tk");

        Assert.Equal(HttpStatusCode.Unauthorized, registrar.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, retirar.StatusCode);
    }
}
