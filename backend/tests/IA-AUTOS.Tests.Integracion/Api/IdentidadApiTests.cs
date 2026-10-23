using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>Cedula y PIN, bloqueo, cambio obligatorio del PIN, permisos por rol y clientes, contra la API y la base reales.</summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class IdentidadApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string CEDULA = "1234567";
    private const string PIN = "482915";

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private async Task<string> TokenAsync(string rol, string? email = null)
    {
        email ??= $"{rol}@ejemplo.com";
        await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol);
        return await LoginAsync(email, "Clave-Segura-1");
    }

    private Task<HttpResponseMessage> LoginConductorAsync(string cedula, string pin) =>
        Cliente().PostAsJsonAsync("/api/auth/login-conductor", new { cedula, pin });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<Guid> OtroClienteAsync()
    {
        var id = Guid.NewGuid();
        await BaseDatos.EjecutarAsync($"INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES ('{id}', 'otro', N'Otro cliente', 2)");
        return id;
    }

    // ------------------------------------------------------------------------------------------ login del conductor

    [Fact]
    public async Task Cedula_y_pin_correctos_dan_un_token_de_conductor_que_sirve_en_la_api()
    {
        var conductor = await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN);

        var r = await LoginConductorAsync("1.234.567", PIN);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await JsonAsync(r);
        var usuario = cuerpo.GetProperty("usuario");
        Assert.Equal(conductor.Id, usuario.GetProperty("id").GetGuid());
        Assert.Equal("conductor", usuario.GetProperty("rol").GetString());
        Assert.Equal(CEDULA, usuario.GetProperty("cedula").GetString());
        Assert.Equal(JsonValueKind.Null, usuario.GetProperty("email").ValueKind);
        Assert.False(usuario.GetProperty("debe_cambiar_pin").GetBoolean());

        var me = await Cliente(cuerpo.GetProperty("token").GetString()).GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'auth.login_conductor'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE Cedula = @c AND UltimoAccesoAt IS NOT NULL", ("@c", CEDULA)));
    }

    [Fact]
    public async Task Un_inspector_conserva_su_correo_y_contrasena_y_otro_sin_correo_entra_con_cedula_y_pin()
    {
        await Fabrica.SembrarUsuarioAsync("ines@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        await Fabrica.SembrarUsuarioConPinAsync("7654321", PIN, clsRol.INSPECTOR, nombre: "Inspector de campo");

        var conCorreo = await Cliente().PostAsJsonAsync("/api/auth/login", new { email = "ines@ejemplo.com", password = "Clave-Segura-1" });
        var conPin = await LoginConductorAsync("7654321", PIN);

        Assert.Equal(HttpStatusCode.OK, conCorreo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, conPin.StatusCode);
        Assert.Equal("inspector", (await JsonAsync(conPin)).GetProperty("usuario").GetProperty("rol").GetString());
    }

    [Theory]
    [InlineData("1234567", "000999")]
    [InlineData("9999999", "482915")]
    public async Task Cedula_o_pin_incorrectos_responden_401_con_el_mismo_mensaje(string cedula, string pin)
    {
        await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN);

        var r = await LoginConductorAsync(cedula, pin);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("Cédula o PIN incorrectos", (await JsonAsync(r)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Cuerpo_incompleto_es_400()
    {
        var r = await Cliente().PostAsJsonAsync("/api/auth/login-conductor", new { cedula = CEDULA });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Cédula y PIN son obligatorios", (await JsonAsync(r)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Cinco_pines_errados_bloquean_la_cuenta_quince_minutos_y_los_intentos_quedan_en_la_auditoria()
    {
        await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginConductorAsync(CEDULA, "000999")).StatusCode);
        }

        var quinto = await LoginConductorAsync(CEDULA, "000999");

        Assert.Equal(HttpStatusCode.Locked, quinto.StatusCode);
        var error = await JsonAsync(quinto);
        Assert.Equal("ACCOUNT_LOCKED", error.GetProperty("code").GetString());
        var hasta = DateTimeOffset.Parse(error.GetProperty("details").GetProperty("bloqueado_hasta").GetString()!);
        Assert.InRange(hasta, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        Assert.Equal(5, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'auth.login_conductor_fallido'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'auth.cuenta_bloqueada'"));

        // Bloqueada, ni el PIN correcto entra.
        var conPinCorrecto = await LoginConductorAsync(CEDULA, PIN);
        Assert.Equal(HttpStatusCode.Locked, conPinCorrecto.StatusCode);
    }

    [Fact]
    public async Task Pasado_el_bloqueo_la_cuenta_vuelve_a_servir()
    {
        await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN);
        for (var i = 0; i < 5; i++)
        {
            await LoginConductorAsync(CEDULA, "000999");
        }

        await BaseDatos.EjecutarAsync("UPDATE dbo.tblUsuarios SET BloqueadoHasta = DATEADD(MINUTE, -1, SYSDATETIMEOFFSET()) WHERE Cedula = '" + CEDULA + "'");

        Assert.Equal(HttpStatusCode.OK, (await LoginConductorAsync(CEDULA, PIN)).StatusCode);
    }

    [Fact]
    public async Task Un_administrador_puede_desbloquear_la_cuenta_antes_de_tiempo()
    {
        var conductor = await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN);
        for (var i = 0; i < 5; i++)
        {
            await LoginConductorAsync(CEDULA, "000999");
        }

        var token = await TokenAsync(clsRol.COORDINADOR_CALIDAD);
        var r = await Cliente(token).PostAsync($"/api/usuarios/{conductor.Id}/desbloquear", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(r)).GetProperty("bloqueado_hasta").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await LoginConductorAsync(CEDULA, PIN)).StatusCode);
    }

    [Fact]
    public async Task Un_usuario_retirado_no_entra_con_su_pin()
    {
        await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN, activo: false);

        var r = await LoginConductorAsync(CEDULA, PIN);

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.StartsWith("Usuario retirado", (await JsonAsync(r)).GetProperty("message").GetString());
    }

    // ------------------------------------------------------------------------------------------ PIN temporal

    [Fact]
    public async Task Un_pin_temporal_obliga_a_cambiarlo_antes_de_usar_el_resto_de_la_api()
    {
        await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN, debeCambiarPin: true);
        var login = await JsonAsync(await LoginConductorAsync(CEDULA, PIN));
        Assert.True(login.GetProperty("usuario").GetProperty("debe_cambiar_pin").GetBoolean());
        var cliente = Cliente(login.GetProperty("token").GetString());

        var bloqueada = await cliente.GetAsync("/api/viajes/mios");
        Assert.Equal(HttpStatusCode.Forbidden, bloqueada.StatusCode);
        Assert.Equal("PIN_CHANGE_REQUIRED", (await JsonAsync(bloqueada)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/usuarios")).StatusCode);

        var me = await JsonAsync(await cliente.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("debe_cambiar_pin").GetBoolean());

        var cambio = await cliente.PostAsJsonAsync("/api/auth/cambiar-pin", new { pin_actual = PIN, pin_nuevo = "730194" });
        Assert.Equal(HttpStatusCode.NoContent, cambio.StatusCode);

        // El mismo token ya sirve, y el PIN nuevo es el vigente.
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/viajes/mios")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginConductorAsync(CEDULA, PIN)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginConductorAsync(CEDULA, "730194")).StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'auth.cambiar_pin'"));
    }

    [Fact]
    public async Task Cambiar_el_pin_valida_el_actual_y_el_formato_del_nuevo()
    {
        await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN, debeCambiarPin: true);
        var cliente = Cliente((await JsonAsync(await LoginConductorAsync(CEDULA, PIN))).GetProperty("token").GetString());

        var actualErrado = await cliente.PostAsJsonAsync("/api/auth/cambiar-pin", new { pin_actual = "000999", pin_nuevo = "730194" });
        var igual = await cliente.PostAsJsonAsync("/api/auth/cambiar-pin", new { pin_actual = PIN, pin_nuevo = PIN });
        var trivial = await cliente.PostAsJsonAsync("/api/auth/cambiar-pin", new { pin_actual = PIN, pin_nuevo = "1111" });

        Assert.Equal(HttpStatusCode.BadRequest, actualErrado.StatusCode);
        Assert.True((await JsonAsync(actualErrado)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("pin_actual", out _));
        Assert.Equal(HttpStatusCode.BadRequest, igual.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, trivial.StatusCode);
        Assert.True((await JsonAsync(trivial)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("pin_nuevo", out _));
    }

    [Fact]
    public async Task Quien_entra_con_contrasena_no_puede_cambiar_un_pin_que_no_tiene()
    {
        var token = await TokenAsync(clsRol.INSPECTOR);

        var r = await Cliente(token).PostAsJsonAsync("/api/auth/cambiar-pin", new { pin_actual = PIN, pin_nuevo = "730194" });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task El_administrador_asigna_reinicia_y_fuerza_el_cambio_de_pin()
    {
        var conductor = await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN);
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));

        var asignado = await admin.PostAsync($"/api/usuarios/{conductor.Id}/pin", null);

        Assert.Equal(HttpStatusCode.OK, asignado.StatusCode);
        var dto = await JsonAsync(asignado);
        var temporal = dto.GetProperty("pin_temporal").GetString()!;
        Assert.Equal(6, temporal.Length);
        Assert.True(dto.GetProperty("debe_cambiar_pin").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginConductorAsync(CEDULA, PIN)).StatusCode);
        var login = await JsonAsync(await LoginConductorAsync(CEDULA, temporal));
        Assert.True(login.GetProperty("usuario").GetProperty("debe_cambiar_pin").GetBoolean());

        // Con un PIN elegido por el administrador, la respuesta no lo repite.
        var elegido = await JsonAsync(await admin.PostAsJsonAsync($"/api/usuarios/{conductor.Id}/pin", new { pin = "730194" }));
        Assert.Equal(JsonValueKind.Null, elegido.GetProperty("pin_temporal").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await LoginConductorAsync(CEDULA, "730194")).StatusCode);

        // Forzar el cambio sin cambiar el PIN.
        await Fabrica.SembrarUsuarioConPinAsync("7654321", PIN, nombre: "Otro conductor");
        var otroId = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblUsuarios WHERE Cedula = '7654321'"))!;
        var forzado = await JsonAsync(await admin.PostAsync($"/api/usuarios/{otroId}/pin/forzar-cambio", null));
        Assert.True(forzado.GetProperty("debe_cambiar_pin").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await LoginConductorAsync("7654321", PIN)).StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'usuario.pin_forzar_cambio'"));
    }

    [Fact]
    public async Task Asignar_un_pin_a_un_administrador_o_uno_trivial_es_400()
    {
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));
        var conductor = await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN);
        var comercial = await Fabrica.SembrarUsuarioAsync("com@ejemplo.com", "x", clsRol.COMERCIAL);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/usuarios/{comercial.Id}/pin", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/usuarios/{conductor.Id}/pin", new { pin = "1234" })).StatusCode);
    }

    // ------------------------------------------------------------------------------------------ perfil y menu

    [Theory]
    [InlineData(clsRol.CONDUCTOR, "registrar_inspecciones", "gestionar_usuarios")]
    [InlineData(clsRol.INSPECTOR, "registrar_inspecciones", "gestionar_viajes")]
    [InlineData(clsRol.COORDINADOR_CALIDAD, "gestionar_usuarios", "registrar_inspecciones")]
    [InlineData(clsRol.ADMIN, "gestionar_usuarios", "ver_inventario_secreto")]
    [InlineData(clsRol.COMERCIAL, "ver_reportes", "gestionar_usuarios")]
    public async Task El_perfil_trae_las_capacidades_del_rol_para_armar_el_menu(string rol, string tiene, string noTiene)
    {
        var token = await TokenAsync(rol);

        var me = await JsonAsync(await Cliente(token).GetAsync("/api/auth/me"));

        var capacidades = me.GetProperty("capacidades").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Contains(tiene, capacidades);
        Assert.DoesNotContain(noTiene, capacidades);
        Assert.Equal(rol, me.GetProperty("rol").GetString());
    }

    [Fact]
    public async Task El_perfil_trae_la_cedula_y_el_cliente_y_el_super_administrador_no_tiene_cliente()
    {
        await Fabrica.SembrarUsuarioConPinAsync(CEDULA, PIN);
        var token = (await JsonAsync(await LoginConductorAsync(CEDULA, PIN))).GetProperty("token").GetString();

        var me = await JsonAsync(await Cliente(token).GetAsync("/api/auth/me"));

        Assert.Equal(CEDULA, me.GetProperty("cedula").GetString());
        Assert.Equal("pin", me.GetProperty("tipo_credencial").GetString());
        Assert.Equal("sofasa", me.GetProperty("cliente").GetProperty("codigo").GetString());
        Assert.Equal("Sofasa", me.GetProperty("cliente").GetProperty("nombre").GetString());

        var super = await JsonAsync(await Cliente(await TokenAsync(clsRol.SUPER_ADMIN)).GetAsync("/api/auth/me"));
        Assert.Equal(JsonValueKind.Null, super.GetProperty("cliente").ValueKind);
    }

    // ------------------------------------------------------------------------------------------ administracion de usuarios

    [Theory]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task Admin_y_coordinador_crean_editan_y_desactivan_usuarios_con_auditoria(string rolActor)
    {
        var token = await TokenAsync(rolActor);
        var cliente = Cliente(token);

        var creado = await cliente.PostAsJsonAsync("/api/usuarios", new { nombre = "Luis Gomez", rol = "conductor", cedula = "1.234.567" });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var dto = await JsonAsync(creado);
        var id = dto.GetProperty("id").GetGuid();
        var pin = dto.GetProperty("pin_temporal").GetString()!;
        Assert.Equal("1234567", dto.GetProperty("cedula").GetString());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("email").ValueKind);
        Assert.Equal("pin", dto.GetProperty("tipo_credencial").GetString());
        Assert.NotEqual(JsonValueKind.Null, dto.GetProperty("cliente_id").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await LoginConductorAsync(CEDULA, pin)).StatusCode);

        var editado = await cliente.PatchAsync($"/api/usuarios/{id}", JsonContent.Create(new { nombre = "Luis Fernando Gomez" }));
        Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
        Assert.Equal("Luis Fernando Gomez", (await JsonAsync(editado)).GetProperty("nombre").GetString());

        var desactivado = await cliente.PatchAsync($"/api/usuarios/{id}", JsonContent.Create(new { activo = false }));
        Assert.Equal(HttpStatusCode.OK, desactivado.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginConductorAsync(CEDULA, pin)).StatusCode);

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'usuario.crear' AND EntidadId = @i", ("@i", id)));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'usuario.actualizar' AND EntidadId = @i", ("@i", id)));
    }

    [Theory]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task Solo_el_super_administrador_asigna_el_nivel_super_administrador(string rolActor)
    {
        var cliente = Cliente(await TokenAsync(rolActor));
        var inspector = await Fabrica.SembrarUsuarioAsync("ines@ejemplo.com", "x", clsRol.INSPECTOR);

        var crear = await cliente.PostAsJsonAsync("/api/usuarios", new { nombre = "Root", email = "root@ejemplo.com", rol = "super_admin", password = "Clave-Segura-1" });
        var ascender = await cliente.PatchAsync($"/api/usuarios/{inspector.Id}", JsonContent.Create(new { rol = "super_admin" }));

        Assert.Equal(HttpStatusCode.Forbidden, crear.StatusCode);
        Assert.Equal("Solo un super administrador puede asignar el nivel super administrador", (await JsonAsync(crear)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, ascender.StatusCode);

        var super = Cliente(await TokenAsync(clsRol.SUPER_ADMIN));
        var ok = await super.PostAsJsonAsync("/api/usuarios", new { nombre = "Root", email = "root@ejemplo.com", rol = "super_admin", password = "Clave-Segura-1" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(ok)).GetProperty("cliente_id").ValueKind);
    }

    [Fact]
    public async Task Un_admin_no_ve_ni_puede_modificar_a_un_super_administrador()
    {
        var super = await Fabrica.SembrarUsuarioAsync("root@ejemplo.com", "x", clsRol.SUPER_ADMIN);
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));

        var patch = await admin.PatchAsync($"/api/usuarios/{super.Id}", JsonContent.Create(new { password = "Otra-Clave-22" }));
        var lista = await JsonAsync(await admin.GetAsync("/api/usuarios"));

        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.DoesNotContain(lista.GetProperty("items").EnumerateArray(), u => u.GetProperty("rol").GetString() == "super_admin");
    }

    [Fact]
    public async Task Cada_usuario_ve_y_administra_solo_a_los_de_su_cliente()
    {
        var otro = await OtroClienteAsync();
        await Fabrica.SembrarUsuarioAsync("propio@ejemplo.com", "x", clsRol.INSPECTOR, nombre: "Propio");
        var ajeno = await Fabrica.SembrarUsuarioAsync("ajeno@ejemplo.com", "x", clsRol.INSPECTOR, nombre: "Ajeno", clienteId: otro);
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));

        var lista = await JsonAsync(await admin.GetAsync("/api/usuarios"));
        var basica = await JsonAsync(await Cliente(await LoginAsync("propio@ejemplo.com", "x")).GetAsync("/api/usuarios"));

        var nombres = lista.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("nombre").GetString()).ToList();
        Assert.Contains("Propio", nombres);
        Assert.DoesNotContain("Ajeno", nombres);
        Assert.DoesNotContain(basica.EnumerateArray(), u => u.GetProperty("nombre").GetString() == "Ajeno");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsync($"/api/usuarios/{ajeno.Id}", JsonContent.Create(new { nombre = "X" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/usuarios/{ajeno.Id}")).StatusCode);

        // El super administrador ve todos y puede filtrar por cliente.
        var super = Cliente(await TokenAsync(clsRol.SUPER_ADMIN));
        var todos = await JsonAsync(await super.GetAsync("/api/usuarios"));
        Assert.Contains(todos.GetProperty("items").EnumerateArray(), u => u.GetProperty("nombre").GetString() == "Ajeno");
        var soloOtro = await JsonAsync(await super.GetAsync($"/api/usuarios?cliente_id={otro}"));
        Assert.Equal(["Ajeno"], soloOtro.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("nombre").GetString()).ToList());
    }

    [Fact]
    public async Task El_admin_no_puede_crear_en_otro_cliente_y_el_super_administrador_si()
    {
        var otro = await OtroClienteAsync();
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));
        var cuerpo = new { nombre = "Luis", rol = "conductor", cedula = "1234567", cliente_id = otro };

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/usuarios", cuerpo)).StatusCode);

        var creado = await Cliente(await TokenAsync(clsRol.SUPER_ADMIN)).PostAsJsonAsync("/api/usuarios", cuerpo);
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        Assert.Equal(otro, (await JsonAsync(creado)).GetProperty("cliente_id").GetGuid());
    }

    [Fact]
    public async Task Patch_con_subtipo_nulo_lo_limpia_y_sin_el_campo_lo_conserva()
    {
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));
        var creado = await JsonAsync(await admin.PostAsJsonAsync(
            "/api/usuarios",
            new { nombre = "Ines", email = "ines@ejemplo.com", rol = "inspector", subtipo_inspector = "nacional", password = "Clave-Segura-1" }));
        var id = creado.GetProperty("id").GetGuid();
        Assert.Equal("nacional", creado.GetProperty("subtipo_inspector").GetString());

        var sinCampo = await JsonAsync(await admin.PatchAsync($"/api/usuarios/{id}", JsonContent.Create(new { nombre = "Ines Perez" })));
        Assert.Equal("nacional", sinCampo.GetProperty("subtipo_inspector").GetString());

        var limpio = await JsonAsync(await admin.PatchAsync($"/api/usuarios/{id}", Json("{\"subtipo_inspector\":null}")));
        Assert.Equal(JsonValueKind.Null, limpio.GetProperty("subtipo_inspector").ValueKind);
        Assert.Equal("Ines Perez", limpio.GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task Cambiar_el_rol_a_uno_que_no_es_inspector_limpia_el_subtipo()
    {
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));
        var creado = await JsonAsync(await admin.PostAsJsonAsync(
            "/api/usuarios",
            new { nombre = "Ines", email = "ines@ejemplo.com", rol = "inspector", subtipo_inspector = "importacion", password = "Clave-Segura-1" }));

        var cambiado = await JsonAsync(await admin.PatchAsync($"/api/usuarios/{creado.GetProperty("id").GetGuid()}", JsonContent.Create(new { rol = "comercial" })));

        Assert.Equal(JsonValueKind.Null, cambiado.GetProperty("subtipo_inspector").ValueKind);
    }

    [Fact]
    public async Task Varios_usuarios_sin_correo_conviven_y_el_correo_sigue_siendo_unico_entre_quienes_lo_tienen()
    {
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));

        var a = await admin.PostAsJsonAsync("/api/usuarios", new { nombre = "A", rol = "conductor", cedula = "1111111" });
        var b = await admin.PostAsJsonAsync("/api/usuarios", new { nombre = "B", rol = "conductor", cedula = "2222222" });
        var c = await admin.PostAsJsonAsync("/api/usuarios", new { nombre = "C", rol = "conductor", cedula = "3333333", email = "c@ejemplo.com" });
        var repetido = await admin.PostAsJsonAsync("/api/usuarios", new { nombre = "D", rol = "conductor", cedula = "4444444", email = "C@ejemplo.com" });
        var cedulaRepetida = await admin.PostAsJsonAsync("/api/usuarios", new { nombre = "E", rol = "conductor", cedula = "1.111.111" });

        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        Assert.Equal(HttpStatusCode.Created, c.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("Ya existe un usuario con ese correo", (await JsonAsync(repetido)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, cedulaRepetida.StatusCode);
        Assert.Equal("Ya existe un usuario con esa cédula", (await JsonAsync(cedulaRepetida)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Los_validadores_piden_cedula_o_correo_segun_el_rol()
    {
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));

        var conductorSinCedula = await admin.PostAsJsonAsync("/api/usuarios", new { nombre = "Luis", rol = "conductor" });
        var comercialSinClave = await admin.PostAsJsonAsync("/api/usuarios", new { nombre = "Ana", rol = "comercial", email = "ana@ejemplo.com" });
        var pinAAdmin = await admin.PostAsJsonAsync("/api/usuarios", new { nombre = "Ana", rol = "admin", cedula = "1234567" });

        Assert.Equal(HttpStatusCode.BadRequest, conductorSinCedula.StatusCode);
        Assert.True((await JsonAsync(conductorSinCedula)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("cedula", out _));
        Assert.Equal(HttpStatusCode.BadRequest, comercialSinClave.StatusCode);
        Assert.True((await JsonAsync(comercialSinClave)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("password", out _));
        Assert.Equal(HttpStatusCode.BadRequest, pinAAdmin.StatusCode);
    }

    [Fact]
    public async Task La_busqueda_filtra_por_texto_rol_y_estado_y_pagina()
    {
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));
        for (var i = 1; i <= 5; i++)
        {
            await Fabrica.SembrarUsuarioConPinAsync($"100000{i}", PIN, nombre: $"Conductor {i}");
        }

        await Fabrica.SembrarUsuarioConPinAsync("2000001", PIN, nombre: "Retirado", activo: false);

        var porCedula = await JsonAsync(await admin.GetAsync("/api/usuarios?q=1000003"));
        var porNombre = await JsonAsync(await admin.GetAsync("/api/usuarios?q=conductor"));
        var activos = await JsonAsync(await admin.GetAsync("/api/usuarios?rol=conductor&activo=true"));
        var pagina = await JsonAsync(await admin.GetAsync("/api/usuarios?rol=conductor&pagina=2&tamano=4"));

        Assert.Equal(["Conductor 3"], porCedula.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("nombre").GetString()).ToList());
        Assert.Equal(5, porNombre.GetProperty("total").GetInt32());
        Assert.Equal(5, activos.GetProperty("total").GetInt32());
        Assert.Equal(6, pagina.GetProperty("total").GetInt32());
        Assert.Equal(2, pagina.GetProperty("items").GetArrayLength());
        Assert.Equal(2, pagina.GetProperty("pagina").GetInt32());
        Assert.Equal(4, pagina.GetProperty("tamano").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/usuarios?rol=gerente")).StatusCode);
    }

    [Fact]
    public async Task Patch_rechaza_campos_desconocidos_y_nulos_donde_no_caben()
    {
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));
        var u = await Fabrica.SembrarUsuarioAsync("ines@ejemplo.com", "x", clsRol.INSPECTOR);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsync($"/api/usuarios/{u.Id}", Json("{\"pin_hash\":\"x\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsync($"/api/usuarios/{u.Id}", Json("{\"nombre\":null}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PatchAsync($"/api/usuarios/{u.Id}", Json("{\"cliente_id\":null}"))).StatusCode);
        var super = Cliente(await TokenAsync(clsRol.SUPER_ADMIN));
        Assert.Equal(HttpStatusCode.BadRequest, (await super.PatchAsync($"/api/usuarios/{u.Id}", Json("{\"cliente_id\":null}"))).StatusCode);
    }

    [Fact]
    public async Task No_se_puede_desactivar_el_propio_usuario()
    {
        var token = await TokenAsync(clsRol.ADMIN);
        var cliente = Cliente(token);
        var yo = await JsonAsync(await cliente.GetAsync("/api/auth/me"));

        var r = await cliente.PatchAsync($"/api/usuarios/{yo.GetProperty("id").GetGuid()}", JsonContent.Create(new { activo = false }));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    // ------------------------------------------------------------------------------------------ carga masiva de conductores

    [Fact]
    public async Task La_vista_previa_de_conductores_no_guarda_y_la_confirmacion_crea_los_validos_con_su_pin()
    {
        var admin = Cliente(await TokenAsync(clsRol.COORDINADOR_CALIDAD));
        await Fabrica.SembrarUsuarioConPinAsync("1000001", PIN);
        var cuerpo = new
        {
            filas = new object[]
            {
                new { fila = 2, nombre = "Ana Ruiz", cedula = "2.000.001", email = "ana@ejemplo.com" },
                new { fila = 3, nombre = "Luis Gomez", cedula = "2000002" },
                new { fila = 4, nombre = "Repetido", cedula = "1000001" },
                new { fila = 5, nombre = "", cedula = "2000003" },
            },
        };

        var previa = await admin.PostAsJsonAsync("/api/usuarios/cargar/previsualizar", cuerpo);

        Assert.Equal(HttpStatusCode.OK, previa.StatusCode);
        var p = await JsonAsync(previa);
        Assert.False(p.GetProperty("confirmado").GetBoolean());
        Assert.Equal(2, p.GetProperty("validas").GetInt32());
        Assert.Equal(2, p.GetProperty("invalidas").GetInt32());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE Rol = 'conductor'"));
        Assert.Contains("Ya existe", p.GetProperty("filas")[2].GetProperty("errores")[0].GetString());

        var confirmada = await admin.PostAsJsonAsync("/api/usuarios/cargar", cuerpo);

        Assert.Equal(HttpStatusCode.Created, confirmada.StatusCode);
        var r = await JsonAsync(confirmada);
        Assert.Equal(2, r.GetProperty("creados").GetInt32());
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE Rol = 'conductor'"));
        var ana = r.GetProperty("filas")[0];
        Assert.True(ana.GetProperty("valida").GetBoolean());
        var pinAna = ana.GetProperty("pin_temporal").GetString()!;
        var login = await JsonAsync(await LoginConductorAsync("2000001", pinAna));
        Assert.True(login.GetProperty("usuario").GetProperty("debe_cambiar_pin").GetBoolean());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("filas")[2].GetProperty("pin_temporal").ValueKind);
        Assert.Equal("ana@ejemplo.com", await Sembrador.EscalarAsync("SELECT Email FROM dbo.tblUsuarios WHERE Cedula = '2000001'"));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE Cedula IN ('2000001','2000002') AND DebeCambiarPin = 1 AND ClienteId IS NOT NULL"));
    }

    [Fact]
    public async Task La_carga_de_conductores_es_solo_de_quien_administra_usuarios()
    {
        var inspector = Cliente(await TokenAsync(clsRol.INSPECTOR));

        var r = await inspector.PostAsJsonAsync("/api/usuarios/cargar", new { filas = new[] { new { nombre = "A", cedula = "2000001" } } });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ------------------------------------------------------------------------------------------ clientes

    [Fact]
    public async Task Los_vehiculos_cargados_quedan_en_el_cliente_de_quien_los_carga()
    {
        var otro = await OtroClienteAsync();
        await Fabrica.SembrarUsuarioAsync("admin-otro@ejemplo.com", "Clave-Segura-1", clsRol.ADMIN, clienteId: otro);
        var token = await LoginAsync("admin-otro@ejemplo.com", "Clave-Segura-1");

        var carga = await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = new[] { new { vin = "VINOTRO0001", marca = "Renault", operacion = "nacional" } } });

        Assert.Equal(HttpStatusCode.Created, carga.StatusCode);
        Assert.Equal(otro, (Guid)(await Sembrador.EscalarAsync("SELECT ClienteId FROM dbo.tblVehiculos WHERE Vin = 'VINOTRO0001'"))!);
    }

    [Fact]
    public async Task Los_clientes_se_administran_como_catalogo_y_uno_en_uso_no_se_borra()
    {
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));
        var creado = await admin.PostAsJsonAsync("/api/catalogos/clientes", new { codigo = "nuevo", nombre = "Nuevo cliente", orden = 5 });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await JsonAsync(creado)).GetProperty("id").GetGuid();

        var vacio = await admin.DeleteAsync($"/api/catalogos/clientes/{id}");
        Assert.Equal(HttpStatusCode.NoContent, vacio.StatusCode);

        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;
        var enUso = await admin.DeleteAsync($"/api/catalogos/clientes/{sofasa}");
        Assert.Equal(HttpStatusCode.Conflict, enUso.StatusCode);
        Assert.Equal("El valor está en uso y no se puede borrar: solo puede inactivarlo", (await JsonAsync(enUso)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Un_cliente_inactivo_no_se_ofrece_pero_no_rompe_a_sus_usuarios()
    {
        await Fabrica.SembrarUsuarioAsync("propio@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        var admin = Cliente(await TokenAsync(clsRol.ADMIN));
        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;
        var otro = await OtroClienteAsync();

        var inactivar = await admin.PatchAsync($"/api/catalogos/clientes/{sofasa}", JsonContent.Create(new { activo = false }));
        Assert.Equal(HttpStatusCode.OK, inactivar.StatusCode);

        // Los usuarios del cliente inactivo siguen entrando, y los nuevos sin cliente explicito van al primero activo.
        Assert.Equal(HttpStatusCode.OK, (await Cliente().PostAsJsonAsync("/api/auth/login", new { email = "propio@ejemplo.com", password = "Clave-Segura-1" })).StatusCode);
        var super = Cliente(await TokenAsync(clsRol.SUPER_ADMIN));
        var nuevo = await JsonAsync(await super.PostAsJsonAsync("/api/usuarios", new { nombre = "Luis", rol = "conductor", cedula = "1234567" }));
        Assert.Equal(otro, nuevo.GetProperty("cliente_id").GetGuid());
    }
}
