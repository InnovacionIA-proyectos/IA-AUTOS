using System.Net;
using System.Text.Json;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Sesion del portal: login-portal, refresh rotativo y logout, con access y refresh en el cuerpo (mismo esquema que EmbarQA). La
/// base de TI se simula (no hay credenciales de TI en estas pruebas); todo lo demas es la API real contra SQL Server 2019.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class SesionPortalApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private sealed class PortalFalso : IPortalUsuarios
    {
        public bool Disponible { get; set; } = true;

        public Dictionary<long, clsUsuarioPortal> Usuarios { get; } = [];

        public Task<clsUsuarioPortal?> ObtenerAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(Usuarios.GetValueOrDefault(id));
    }

    private (HttpClient Cliente, clsFabricaApi Fabrica, PortalFalso Portal) Preparar(Dictionary<string, string?>? extra = null)
    {
        var portal = new PortalFalso();
        portal.Usuarios[123] = new clsUsuarioPortal(123, "jperez", "jperez@empresa.com", "Juan Pérez", ["AdminIAAUTOS"]);
        portal.Usuarios[777] = new clsUsuarioPortal(777, "sinrol", "sinrol@empresa.com", "Sin Rol", ["AdminEmbarQA"]);

        // Sin gracia: un refresh rotado que se presenta de nuevo se toma como robo de inmediato.
        var valores = new Dictionary<string, string?> { ["Sesion:RefreshGraciaSegundos"] = "0" };
        foreach (var (clave, valor) in extra ?? [])
        {
            valores[clave] = valor;
        }

        var fabrica = CrearFabrica(valores, servicios =>
        {
            servicios.RemoveAll<IPortalUsuarios>();
            servicios.AddSingleton<IPortalUsuarios>(portal);
        });
        return (fabrica.CreateClient(), fabrica, portal);
    }

    /// <summary>El arreglo tal como lo guarda el portal: permisos como arreglo, "idOcion" (sin la p) y ids de permiso como texto.</summary>
    private static StringContent CuerpoPortal(long id = 123, string opcion = "AdminIAAUTOS") =>
        Json("[{\"id\":" + id + ",\"nombreUsuario\":\"jperez\",\"permisos\":[{\"id\":\"53\",\"descripcion\":\"" + opcion
            + "\",\"idOcion\":\"56\",\"opcion\":\"" + opcion + "\"}]}]");

    private static StringContent CuerpoRefresh(string refresh) => Json("{\"refresh\":\"" + refresh + "\"}");

    private async Task<(string Access, string Refresh, JsonDocument Cuerpo)> IniciarSesionAsync(HttpClient cliente, long id = 123)
    {
        var respuesta = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(id));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var doc = await LeerAsync(respuesta);
        return (doc.RootElement.GetProperty("access").GetString()!, doc.RootElement.GetProperty("refresh").GetString()!, doc);
    }

    private static async Task<HttpStatusCode> LlamarMeAsync(HttpClient cliente, string access)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        solicitud.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        return (await cliente.SendAsync(solicitud)).StatusCode;
    }

    private static Task<HttpResponseMessage> RefrescarAsync(HttpClient cliente, string refresh) =>
        cliente.PostAsync("/api/auth/refresh", CuerpoRefresh(refresh));

    // ------------------------------------------------------------------------------------------- login-portal

    [Fact]
    public async Task Login_portal_entra_sin_segundo_login_y_responde_access_refresh_y_user()
    {
        var (cliente, _, _) = Preparar();

        var respuesta = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(respuesta.Headers.Contains("Set-Cookie"), "La sesion no usa cookies: access y refresh van en el cuerpo");
        using var doc = await LeerAsync(respuesta);
        var raiz = doc.RootElement;
        Assert.False(string.IsNullOrEmpty(raiz.GetProperty("access").GetString()));
        Assert.False(string.IsNullOrEmpty(raiz.GetProperty("refresh").GetString()));
        Assert.False(raiz.TryGetProperty("usuario", out _), "La clave es user, no usuario");
        var usuario = raiz.GetProperty("user");
        Assert.Equal("jperez@empresa.com", usuario.GetProperty("email").GetString());
        Assert.Equal("Juan Pérez", usuario.GetProperty("nombre").GetString());
        Assert.Equal(clsRol.ADMIN, usuario.GetProperty("rol").GetString());
        Assert.True(Guid.TryParse(usuario.GetProperty("id").GetString(), out _));
        Assert.Equal(HttpStatusCode.OK, await LlamarMeAsync(cliente, raiz.GetProperty("access").GetString()!));
    }

    [Fact]
    public async Task El_access_token_dura_60_minutos_por_defecto_y_lleva_jti_y_sesion()
    {
        var (cliente, _, _) = Preparar();

        var (access, _, _) = await IniciarSesionAsync(cliente);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(access);
        Assert.Equal(TimeSpan.FromMinutes(60), token.ValidTo - token.ValidFrom);
        Assert.False(string.IsNullOrEmpty(token.Id));
        Assert.True(token.TryGetClaim("sid", out _));
    }

    [Fact]
    public async Task Los_tiempos_de_la_sesion_son_configurables()
    {
        var (cliente, _, _) = Preparar(new() { ["Sesion:AccessMinutos"] = "15" });

        var (access, _, _) = await IniciarSesionAsync(cliente);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(access);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.ValidFrom);
    }

    [Fact]
    public async Task Usuario_del_portal_sin_rol_de_IA_Autos_recibe_403_PORTAL_USER_NOT_LINKED()
    {
        var (cliente, _, _) = Preparar();

        var respuesta = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(777));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("PORTAL_USER_NOT_LINKED", doc.RootElement.GetProperty("code").GetString());
        Assert.Contains("acceso", doc.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task El_403_de_acceso_no_habilitado_es_distinto_del_FORBIDDEN_de_rol_insuficiente()
    {
        var (cliente, fabrica, _) = Preparar();
        await fabrica.SembrarUsuarioAsync("inspector@empresa.com", "ClaveSegura1", clsRol.INSPECTOR);
        var login = await cliente.PostAsync("/api/auth/login", Json("""{"email":"inspector@empresa.com","password":"ClaveSegura1"}"""));
        using var docLogin = await LeerAsync(login);
        var token = docLogin.RootElement.GetProperty("token").GetString()!;

        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/api/usuarios") { Content = Json("{}") };
        solicitud.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var rolInsuficiente = await cliente.SendAsync(solicitud);
        var noVinculado = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(777));

        using var doc1 = await LeerAsync(rolInsuficiente);
        using var doc2 = await LeerAsync(noVinculado);
        Assert.Equal(HttpStatusCode.Forbidden, rolInsuficiente.StatusCode);
        Assert.Equal("FORBIDDEN", doc1.RootElement.GetProperty("code").GetString());
        Assert.Equal("PORTAL_USER_NOT_LINKED", doc2.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Un_id_inexistente_en_el_portal_recibe_403_y_no_se_crea_ningun_usuario()
    {
        var (cliente, fabrica, _) = Preparar();

        var respuesta = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(999999));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        using var alcance = fabrica.Services.CreateScope();
        Assert.Null(await alcance.ServiceProvider.GetRequiredService<IUsuarioRepository>().ObtenerPorPortalIdAsync(999999));
    }

    [Fact]
    public async Task Si_la_persona_no_existe_en_IA_Autos_pero_tiene_rol_en_el_portal_se_registra_con_ese_rol()
    {
        var (cliente, fabrica, portal) = Preparar();
        portal.Usuarios[55] = new clsUsuarioPortal(55, "mgomez", "mgomez@empresa.com", "María Gómez", ["ComercialIAAUTOS", "InspectorIAAUTOS"]);

        var (access, _, doc) = await IniciarSesionAsync(cliente, 55);

        Assert.Equal(clsRol.COMERCIAL, doc.RootElement.GetProperty("user").GetProperty("rol").GetString());
        Assert.Equal(HttpStatusCode.OK, await LlamarMeAsync(cliente, access));
        using var alcance = fabrica.Services.CreateScope();
        var creado = await alcance.ServiceProvider.GetRequiredService<IUsuarioRepository>().ObtenerPorPortalIdAsync(55);
        Assert.NotNull(creado);
        Assert.Equal("mgomez@empresa.com", creado.Email);
    }

    [Fact]
    public async Task Los_permisos_que_manda_el_cliente_no_dan_acceso()
    {
        var (cliente, _, _) = Preparar();

        // El navegador dice que tiene la opcion AdminIAAUTOS, pero el id es de alguien sin rol de IA Autos en TI.
        var respuesta = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(777, opcion: "AdminIAAUTOS"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Una_cuenta_existente_con_el_mismo_correo_se_reutiliza_y_toma_el_rol_del_portal()
    {
        var (cliente, fabrica, _) = Preparar();
        var existente = await fabrica.SembrarUsuarioAsync("jperez@empresa.com", "ClaveSegura1", clsRol.INSPECTOR);

        var (_, _, doc) = await IniciarSesionAsync(cliente);

        var usuario = doc.RootElement.GetProperty("user");
        Assert.Equal(existente.Id, usuario.GetProperty("id").GetGuid());
        Assert.Equal(clsRol.ADMIN, usuario.GetProperty("rol").GetString());
    }

    [Fact]
    public async Task Cambio_de_usuario_del_portal_con_el_mismo_correo_no_relinkea_la_cuenta()
    {
        var (cliente, fabrica, portal) = Preparar();
        // Misma persona, pero TI le asigno un id de portal nuevo (p. ej. recontratacion) con el mismo correo.
        portal.Usuarios[456] = new clsUsuarioPortal(456, "jperez2", "jperez@empresa.com", "Juan Pérez", ["AdminIAAUTOS"]);

        await IniciarSesionAsync(cliente, 123);
        var segunda = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(456));

        Assert.Equal(HttpStatusCode.Forbidden, segunda.StatusCode);
        using var docRechazo = await LeerAsync(segunda);
        Assert.Equal("PORTAL_USER_NOT_LINKED", docRechazo.RootElement.GetProperty("code").GetString());

        // El vinculo original (123) sigue funcionando: no quedo reasignado al nuevo id.
        var relogin = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(123));
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        using var alcance = fabrica.Services.CreateScope();
        Assert.Null(await alcance.ServiceProvider.GetRequiredService<IUsuarioRepository>().ObtenerPorPortalIdAsync(456));
    }

    [Fact]
    public async Task Sin_base_de_TI_configurada_solo_entran_los_ids_ya_vinculados()
    {
        var (cliente, fabrica, portal) = Preparar();
        portal.Disponible = false;
        var vinculado = await fabrica.SembrarUsuarioAsync("jperez@empresa.com", "ClaveSegura1", clsRol.ADMIN);
        using (var alcance = fabrica.Services.CreateScope())
        {
            var repo = alcance.ServiceProvider.GetRequiredService<IUsuarioRepository>();
            var usuario = await repo.ObtenerPorIdAsync(vinculado.Id);
            usuario!.VincularPortal(123, DateTimeOffset.UtcNow);
            await alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        }

        var conVinculo = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(123));
        var sinVinculo = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(456));

        Assert.Equal(HttpStatusCode.OK, conVinculo.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sinVinculo.StatusCode);
    }

    // ----------------------------------------------------------------- lectura estricta del cuerpo del portal

    [Fact]
    public async Task Solo_se_usa_el_primer_elemento_del_arreglo()
    {
        var (cliente, _, _) = Preparar();

        var respuesta = await cliente.PostAsync(
            "/api/auth/login-portal",
            Json("[{\"id\":123,\"nombreUsuario\":\"a\",\"permisos\":[]},{\"id\":777,\"nombreUsuario\":\"b\",\"permisos\":[]}]"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("jperez@empresa.com", doc.RootElement.GetProperty("user").GetProperty("email").GetString());
    }

    [Theory]
    [InlineData("""{"id":123,"nombreUsuario":"x","permisos":[]}""", "la raiz debe ser un arreglo, no un objeto")]
    [InlineData("""[]""", "arreglo vacio")]
    [InlineData("""[{"nombreUsuario":"x","permisos":[]}]""", "falta el id")]
    [InlineData("""[{"id":null,"nombreUsuario":"x"}]""", "id nulo")]
    [InlineData("""[{"id":"123","nombreUsuario":"x","permisos":[]}]""", "el id del usuario debe ser un numero, no texto")]
    [InlineData("""[{"id":123.5,"nombreUsuario":"x","permisos":[]}]""", "el id del usuario debe ser un entero")]
    [InlineData("""[{"id":123,"nombreUsuario":"x","permisos":{"id":"53","opcion":"AdminIAAUTOS"}}]""", "permisos llega como arreglo, no como objeto")]
    [InlineData("""[{"id":123,"nombreUsuario":"x","permisos":[{"id":53,"opcion":"AdminIAAUTOS"}]}]""", "el id del permiso es texto, no numero")]
    [InlineData("""[{"id":123,"nombreUsuario":"x","permisos":[{"id":"53","idOcion":56,"opcion":"AdminIAAUTOS"}]}]""", "idOcion es texto, no numero")]
    [InlineData("""[{"id":123,"nombreUsuario":42,"permisos":[]}]""", "nombreUsuario es texto")]
    [InlineData("""[123]""", "los elementos deben ser objetos")]
    [InlineData("no es json", "JSON invalido")]
    public async Task Un_cuerpo_que_no_respeta_el_formato_del_portal_responde_400(string cuerpo, string motivo)
    {
        var (cliente, _, _) = Preparar();

        var respuesta = await cliente.PostAsync("/api/auth/login-portal", Json(cuerpo));

        Assert.True(respuesta.StatusCode == HttpStatusCode.BadRequest, $"Se esperaba 400 ({motivo}) y fue {(int)respuesta.StatusCode}");
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("VALIDATION_ERROR", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Los_nombres_de_campo_del_portal_son_camelCase_y_distinguen_mayusculas()
    {
        var (cliente, _, _) = Preparar();

        // Con "ID" en mayusculas el campo no se enlaza: no hay id valido.
        var mayusculas = await cliente.PostAsync("/api/auth/login-portal", Json("""[{"ID":123,"nombreUsuario":"x","permisos":[]}]"""));
        // Un campo desconocido (por ejemplo "idOpcion" con p) se ignora sin romper la lectura.
        var conPDeMas = await cliente.PostAsync(
            "/api/auth/login-portal",
            Json("""[{"id":123,"nombreUsuario":"x","permisos":[{"id":"53","idOpcion":"56","opcion":"AdminIAAUTOS"}]}]"""));

        Assert.Equal(HttpStatusCode.BadRequest, mayusculas.StatusCode);
        Assert.Equal(HttpStatusCode.OK, conPDeMas.StatusCode);
    }

    [Fact]
    public async Task Login_portal_tiene_limite_de_intentos_por_minuto()
    {
        var (cliente, _, _) = Preparar(new() { ["Seguridad:LoginPortalMaxIntentosPorMinuto"] = "3" });

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(999))).StatusCode);
        }

        var excedido = await cliente.PostAsync("/api/auth/login-portal", CuerpoPortal(999));

        Assert.Equal(HttpStatusCode.TooManyRequests, excedido.StatusCode);
        using var doc = await LeerAsync(excedido);
        Assert.Equal("RATE_LIMITED", doc.RootElement.GetProperty("code").GetString());
    }

    // ----------------------------------------------------------------------------------------------- refresh

    [Fact]
    public async Task Refresh_entrega_access_y_refresh_nuevos_y_el_anterior_queda_revocado()
    {
        var (cliente, _, _) = Preparar();
        var (accessInicial, refreshInicial, _) = await IniciarSesionAsync(cliente);

        var renovada = await RefrescarAsync(cliente, refreshInicial);

        Assert.Equal(HttpStatusCode.OK, renovada.StatusCode);
        using var doc = await LeerAsync(renovada);
        var accessNuevo = doc.RootElement.GetProperty("access").GetString()!;
        var refreshNuevo = doc.RootElement.GetProperty("refresh").GetString()!;
        Assert.NotEqual(accessInicial, accessNuevo);
        Assert.NotEqual(refreshInicial, refreshNuevo);
        Assert.False(doc.RootElement.TryGetProperty("user", out _), "refresh responde solo access y refresh");
        Assert.Equal(HttpStatusCode.OK, await LlamarMeAsync(cliente, accessNuevo));

        var reuso = await RefrescarAsync(cliente, refreshInicial);

        Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);
        using var docReuso = await LeerAsync(reuso);
        Assert.Equal("UNAUTHORIZED", docReuso.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task El_refresh_nuevo_tambien_se_puede_rotar()
    {
        var (cliente, _, _) = Preparar();
        var (_, refresh1, _) = await IniciarSesionAsync(cliente);
        using var doc1 = await LeerAsync(await RefrescarAsync(cliente, refresh1));
        var refresh2 = doc1.RootElement.GetProperty("refresh").GetString()!;

        var segunda = await RefrescarAsync(cliente, refresh2);

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
    }

    [Fact]
    public async Task Reusar_un_refresh_revoca_toda_la_sesion_incluido_el_access_vigente()
    {
        var (cliente, _, _) = Preparar();
        var (_, refreshInicial, _) = await IniciarSesionAsync(cliente);
        using var doc = await LeerAsync(await RefrescarAsync(cliente, refreshInicial));
        var accessVigente = doc.RootElement.GetProperty("access").GetString()!;
        var refreshVigente = doc.RootElement.GetProperty("refresh").GetString()!;

        // Alguien presenta el refresh viejo (robado): toda la sesion se revoca.
        await RefrescarAsync(cliente, refreshInicial);

        Assert.Equal(HttpStatusCode.Unauthorized, await LlamarMeAsync(cliente, accessVigente));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefrescarAsync(cliente, refreshVigente)).StatusCode);
    }

    [Fact]
    public async Task Doble_refresh_concurrente_con_el_mismo_token_no_revoca_la_sesion_ganadora()
    {
        // Gracia por defecto (10s): dos pestañas que refrescan casi a la vez con el mismo refresh no deben tumbar la sesión.
        var (cliente, _, _) = Preparar(new() { ["Sesion:RefreshGraciaSegundos"] = "10" });
        var (_, refreshInicial, _) = await IniciarSesionAsync(cliente);

        var ganadora = await RefrescarAsync(cliente, refreshInicial);
        Assert.Equal(HttpStatusCode.OK, ganadora.StatusCode);
        using var docGanadora = await LeerAsync(ganadora);
        var accessGanador = docGanadora.RootElement.GetProperty("access").GetString()!;
        var refreshGanador = docGanadora.RootElement.GetProperty("refresh").GetString()!;

        // La pestaña que perdió la carrera presenta el mismo refresh ya rotado, dentro de la ventana de gracia.
        var perdedora = await RefrescarAsync(cliente, refreshInicial);

        Assert.Equal(HttpStatusCode.Unauthorized, perdedora.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await LlamarMeAsync(cliente, accessGanador));
        Assert.Equal(HttpStatusCode.OK, (await RefrescarAsync(cliente, refreshGanador)).StatusCode);
    }

    [Fact]
    public async Task Pasada_la_gracia_reusar_el_refresh_rotado_si_revoca_toda_la_sesion()
    {
        var (cliente, _, _) = Preparar(new() { ["Sesion:RefreshGraciaSegundos"] = "10" });
        var (_, refreshInicial, _) = await IniciarSesionAsync(cliente);
        using var docRotado = await LeerAsync(await RefrescarAsync(cliente, refreshInicial));
        var refreshVigente = docRotado.RootElement.GetProperty("refresh").GetString()!;

        // Simula que paso mas tiempo que la ventana de gracia desde la rotacion: ya no es una carrera, es reuso real.
        // El margen (10 min) es deliberadamente mucho mayor que la gracia (10s) para no depender de que el reloj del
        // contenedor de SQL Server y el del proceso de pruebas esten perfectamente sincronizados.
        await BaseDatos.EjecutarAsync("UPDATE dbo.tblRefreshTokens SET RevocadoAt = DATEADD(minute, -10, RevocadoAt) WHERE RevocadoAt IS NOT NULL");

        var reusoTardio = await RefrescarAsync(cliente, refreshInicial);

        Assert.Equal(HttpStatusCode.Unauthorized, reusoTardio.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefrescarAsync(cliente, refreshVigente)).StatusCode);
    }

    [Fact]
    public async Task Un_refresh_desconocido_responde_401_y_uno_ausente_responde_400()
    {
        var (cliente, _, _) = Preparar();

        var desconocido = await RefrescarAsync(cliente, "valor-que-no-existe");
        var sinCampo = await cliente.PostAsync("/api/auth/refresh", Json("{}"));
        var enBlanco = await RefrescarAsync(cliente, "  ");
        var sinCuerpo = await cliente.PostAsync("/api/auth/refresh", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.Unauthorized, desconocido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinCampo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, enBlanco.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinCuerpo.StatusCode);
    }

    [Fact]
    public async Task El_refresh_no_se_acepta_en_una_cookie()
    {
        var (cliente, _, _) = Preparar();
        var (_, refresh, _) = await IniciarSesionAsync(cliente);

        var porCookie = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = Json("{}") };
        porCookie.Headers.Add("Cookie", "ia_autos_refresh=" + refresh);

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.SendAsync(porCookie)).StatusCode);
    }

    [Fact]
    public async Task Un_refresh_vencido_responde_401()
    {
        var (cliente, _, _) = Preparar();
        var (_, refresh, _) = await IniciarSesionAsync(cliente);
        await BaseDatos.EjecutarAsync("UPDATE dbo.tblRefreshTokens SET ExpiraAt = DATEADD(day, -1, SYSDATETIMEOFFSET())");

        var respuesta = await RefrescarAsync(cliente, refresh);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Los_refresh_se_guardan_solo_como_hash_con_ip_y_dispositivo()
    {
        var (cliente, _, _) = Preparar();
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("PruebasIntegracion/1.0");
        var (_, refresh, _) = await IniciarSesionAsync(cliente);

        await using var conexion = new Microsoft.Data.SqlClient.SqlConnection(BaseDatos.CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new Microsoft.Data.SqlClient.SqlCommand(
            "SELECT TokenHash, Dispositivo FROM dbo.tblRefreshTokens", conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        Assert.True(await lector.ReadAsync());
        Assert.NotEqual(refresh, lector.GetString(0));
        Assert.Equal(64, lector.GetString(0).Length);
        Assert.Contains("PruebasIntegracion", lector.GetString(1));
    }

    // ------------------------------------------------------------------------------------------------ logout

    [Fact]
    public async Task Logout_revoca_de_verdad_el_access_y_el_refresh_y_responde_204_vacio()
    {
        var (cliente, _, _) = Preparar();
        var (access, refresh, _) = await IniciarSesionAsync(cliente);

        var cierre = await cliente.PostAsync("/api/auth/logout", CuerpoRefresh(refresh));

        Assert.Equal(HttpStatusCode.NoContent, cierre.StatusCode);
        Assert.Equal(string.Empty, await cierre.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, await LlamarMeAsync(cliente, access));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefrescarAsync(cliente, refresh)).StatusCode);
    }

    [Fact]
    public async Task Logout_con_el_bearer_y_sin_cuerpo_tambien_cierra_la_sesion_del_access()
    {
        var (cliente, _, _) = Preparar();
        var (access, refresh, _) = await IniciarSesionAsync(cliente);
        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        solicitud.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);

        var cierre = await cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.NoContent, cierre.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await LlamarMeAsync(cliente, access));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefrescarAsync(cliente, refresh)).StatusCode);
    }

    [Fact]
    public async Task Logout_sin_sesion_o_con_un_refresh_desconocido_responde_204_igual()
    {
        var (cliente, _, _) = Preparar();

        var sinNada = await cliente.PostAsync("/api/auth/logout", new StringContent(string.Empty));
        var desconocido = await cliente.PostAsync("/api/auth/logout", CuerpoRefresh("no-existe"));
        var sinCampo = await cliente.PostAsync("/api/auth/logout", Json("{}"));

        Assert.Equal(HttpStatusCode.NoContent, sinNada.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, desconocido.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, sinCampo.StatusCode);
    }

    [Fact]
    public async Task Cerrar_una_sesion_no_afecta_a_otra_del_mismo_usuario()
    {
        var (cliente, _, _) = Preparar();
        var (accessA, refreshA, _) = await IniciarSesionAsync(cliente);
        var (accessB, _, _) = await IniciarSesionAsync(cliente);

        await cliente.PostAsync("/api/auth/logout", CuerpoRefresh(refreshA));

        Assert.Equal(HttpStatusCode.Unauthorized, await LlamarMeAsync(cliente, accessA));
        Assert.Equal(HttpStatusCode.OK, await LlamarMeAsync(cliente, accessB));
    }

    // ------------------------------------------------------------------------------- issuer y audience

    [Fact]
    public async Task Con_issuer_y_audience_configurados_los_tokens_los_llevan_y_se_rechazan_los_que_no()
    {
        var (cliente, fabrica, _) = Preparar(new() { ["Jwt:Issuer"] = "IA-AUTOS", ["Jwt:Audience"] = "IA-AUTOS.Clientes" });
        var (access, _, _) = await IniciarSesionAsync(cliente);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(access);
        Assert.Equal("IA-AUTOS", token.Issuer);
        Assert.Contains("IA-AUTOS.Clientes", token.Audiences);
        Assert.Equal(HttpStatusCode.OK, await LlamarMeAsync(cliente, access));

        // Un token sin emisor ni destinatario (aunque este bien firmado) ya no vale.
        var usuario = await fabrica.SembrarUsuarioAsync("otro@empresa.com", "ClaveSegura1");
        var sinEmisor = CrearToken(usuario.Id, usuario.Email, usuario.Rol);
        Assert.Equal(HttpStatusCode.Unauthorized, await LlamarMeAsync(cliente, sinEmisor));

        // El login directo (app movil) tambien emite con emisor y destinatario y conserva sus 30 dias.
        var login = await cliente.PostAsync("/api/auth/login", Json("""{"email":"otro@empresa.com","password":"ClaveSegura1"}"""));
        using var doc = await LeerAsync(login);
        var tokenMovil = new JsonWebTokenHandler().ReadJsonWebToken(doc.RootElement.GetProperty("token").GetString()!);
        Assert.Equal("IA-AUTOS", tokenMovil.Issuer);
        Assert.Equal(TimeSpan.FromDays(30), tokenMovil.ValidTo - tokenMovil.ValidFrom);
    }

    [Fact]
    public async Task Sin_issuer_ni_audience_configurados_los_tokens_no_los_exigen()
    {
        var (cliente, _, _) = Preparar();

        var (access, _, _) = await IniciarSesionAsync(cliente);

        Assert.True(string.IsNullOrEmpty(new JsonWebTokenHandler().ReadJsonWebToken(access).Issuer));
    }

    // ------------------------------------------------------------------------------------------------- CORS

    [Fact]
    public async Task Cors_acepta_solo_origenes_exactos_sin_credenciales_y_expone_Content_Disposition()
    {
        var (cliente, _, _) = Preparar();

        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login-portal");
        preflight.Headers.Add("Origin", clsFabricaApi.ORIGEN_PERMITIDO);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
        var respuesta = await cliente.SendAsync(preflight);
        Assert.Equal(clsFabricaApi.ORIGEN_PERMITIDO, respuesta.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(respuesta.Headers.Contains("Access-Control-Allow-Credentials"), "La sesion va en el cuerpo: no se piden credenciales");

        var simple = new HttpRequestMessage(HttpMethod.Get, "/health");
        simple.Headers.Add("Origin", clsFabricaApi.ORIGEN_PERMITIDO);
        var conOrigen = await cliente.SendAsync(simple);
        Assert.Contains("Content-Disposition", string.Join(",", conOrigen.Headers.GetValues("Access-Control-Expose-Headers")));

        var ajeno = new HttpRequestMessage(HttpMethod.Get, "/health");
        ajeno.Headers.Add("Origin", "https://sitio-ajeno.com");
        Assert.False((await cliente.SendAsync(ajeno)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task El_comodin_en_los_origenes_se_ignora_y_los_demas_origenes_siguen_funcionando()
    {
        var (cliente, _, _) = Preparar(new() { ["Cors:Origins"] = $"*,{clsFabricaApi.ORIGEN_PERMITIDO}" });

        var cualquiera = new HttpRequestMessage(HttpMethod.Get, "/health");
        cualquiera.Headers.Add("Origin", "https://cualquiera.com");
        var exacto = new HttpRequestMessage(HttpMethod.Get, "/health");
        exacto.Headers.Add("Origin", clsFabricaApi.ORIGEN_PERMITIDO);

        Assert.False((await cliente.SendAsync(cualquiera)).Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(clsFabricaApi.ORIGEN_PERMITIDO, (await cliente.SendAsync(exacto)).Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
