using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Auditoria: cada accion de escritura queda con quien la hizo, la IP y el dispositivo, el detalle antes y despues (sin contrasenas, PIN ni tokens), el viaje y el cliente; el administrador
/// consulta la de su cliente y el super administrador la de todos, y nadie la modifica ni la borra.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class AuditoriaApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-07-17T12:30:15.123Z";

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    /// <summary>Los registros de la auditoria que ve quien consulta, con la consulta indicada (<c>?entidad=...</c>), de la mas reciente a la mas antigua.</summary>
    private async Task<List<JsonElement>> ConsultarAsync(Sesion quien, string consulta = "")
    {
        var r = await Cliente(quien.Token).GetAsync("/api/auditoria" + consulta);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await ComoJsonAsync(r)).GetProperty("items").EnumerateArray().ToList();
    }

    private async Task<Guid> ClienteOtroAsync()
    {
        var otro = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otro));
        return otro;
    }

    // ------------------------------------------------------------------------------------ IP y dispositivo

    [Fact]
    public async Task Cada_registro_guarda_la_ip_y_el_dispositivo_de_la_solicitud()
    {
        var detras = CrearFabrica(new Dictionary<string, string?> { ["Proxy:ConfiarEnEncabezadosReenviados"] = "true" });
        await Fabrica.SembrarUsuarioAsync("super@ejemplo.com", "Clave-Segura-1", clsRol.SUPER_ADMIN, nombre: "Super");
        await Fabrica.SembrarUsuarioAsync("persona@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR, nombre: "Persona");

        var conDispositivo = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = Cuerpo(new { email = "persona@ejemplo.com", password = "Clave-Segura-1" }) };
        conDispositivo.Headers.Add("X-Forwarded-For", "203.0.113.7");
        conDispositivo.Headers.Add("X-Dispositivo-Id", "tablet-bodega-3");
        conDispositivo.Headers.Add("User-Agent", "IAAutosApp/2.1");
        Assert.Equal(HttpStatusCode.OK, (await Cliente(fabrica: detras).SendAsync(conDispositivo)).StatusCode);

        var soloAgente = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = Cuerpo(new { email = "persona@ejemplo.com", password = "Clave-Segura-1" }) };
        soloAgente.Headers.Add("X-Forwarded-For", "198.51.100.9");
        soloAgente.Headers.Add("User-Agent", "Mozilla/5.0 (portal web)");
        Assert.Equal(HttpStatusCode.OK, (await Cliente(fabrica: detras).SendAsync(soloAgente)).StatusCode);

        var super = new Sesion(await LoginAsync("super@ejemplo.com", "Clave-Segura-1"), Guid.Empty);
        var filas = await ConsultarAsync(super, "?accion=auth.login&entidad=usuarios");
        var primera = filas.Single(f => f.GetProperty("ip").GetString() == "203.0.113.7");
        var segunda = filas.Single(f => f.GetProperty("ip").GetString() == "198.51.100.9");

        Assert.Equal("tablet-bodega-3", primera.GetProperty("dispositivo").GetString());
        Assert.Equal("Mozilla/5.0 (portal web)", segunda.GetProperty("dispositivo").GetString());
        Assert.Equal("Persona", primera.GetProperty("usuario_nombre").GetString());
        Assert.Equal("inspector", primera.GetProperty("rol").GetString());
    }

    [Fact]
    public async Task Un_dispositivo_largo_se_recorta_y_una_accion_del_sistema_no_tiene_ip_ni_dispositivo()
    {
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = Cuerpo(new { email = "super@ejemplo.com", password = "Clave-Segura-1" }) };
        request.Headers.Add("X-Dispositivo-Id", new string('d', 500));
        await Cliente().SendAsync(request);

        var filas = await ConsultarAsync(super, "?accion=auth.login");

        Assert.Contains(filas, f => f.GetProperty("dispositivo").GetString() == new string('d', 200));
        Assert.All(filas.Where(f => f.GetProperty("dispositivo").ValueKind == JsonValueKind.String), f => Assert.True(f.GetProperty("dispositivo").GetString()!.Length <= 200));
    }

    // ------------------------------------------------------------------------------------ detalle, viaje y cliente

    [Fact]
    public async Task Crear_una_inspeccion_guarda_el_detalle_el_viaje_y_el_cliente()
    {
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-AUD-0001", inspectorId: inspector.UsuarioId);
        var viaje = await Sembrador.ViajeDeEtapaAsync(etapas[0]);
        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;

        var creada = await Cliente(inspector.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1 });
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var id = (await ComoJsonAsync(creada)).GetProperty("id").GetGuid();

        var fila = Assert.Single(await ConsultarAsync(super, "?accion=inspeccion.crear"));

        Assert.Equal(id, fila.GetProperty("entidad_id").GetGuid());
        Assert.Equal("inspecciones", fila.GetProperty("entidad").GetString());
        Assert.Equal(inspector.UsuarioId, fila.GetProperty("usuario_id").GetGuid());
        Assert.Equal(viaje, fila.GetProperty("viaje_id").GetGuid());
        Assert.Equal(sofasa, fila.GetProperty("cliente_id").GetGuid());
        Assert.Equal(await Sembrador.EscalarAsync("SELECT CodigoMtrack FROM dbo.tblViajes WHERE Id = @v", ("@v", viaje)), fila.GetProperty("codigo_mtrack").GetString());
        var detalle = fila.GetProperty("detalle");
        Assert.Equal(["antes", "despues"], detalle.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(JsonValueKind.Null, detalle.GetProperty("antes").ValueKind);
        Assert.Equal("salida", detalle.GetProperty("despues").GetProperty("momento").GetString());
        Assert.Equal("borrador", detalle.GetProperty("despues").GetProperty("estado").GetString());
        Assert.Equal(4.6, detalle.GetProperty("despues").GetProperty("lat").GetDouble());
    }

    [Fact]
    public async Task El_super_administrador_que_actua_sobre_un_viaje_queda_en_la_auditoria_del_cliente_del_viaje()
    {
        var otro = await ClienteOtroAsync();
        var adminOtro = await SesionAsync("admin-otro@ejemplo.com", clsRol.ADMIN, otro);
        var adminSofasa = await SesionAsync("admin-sofasa@ejemplo.com", clsRol.ADMIN);
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-AUD-0002");
        var viaje = await Sembrador.ViajeDeEtapaAsync(etapas[0]);

        var r = await Cliente(super.Token).PatchAsync($"/api/viajes/{viaje}", Cuerpo(new { placa = "NUEVA1" }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var delCliente = Assert.Single(await ConsultarAsync(adminSofasa, "?accion=viaje.actualizar"));
        Assert.Equal(super.UsuarioId, delCliente.GetProperty("usuario_id").GetGuid());
        Assert.Equal(viaje, delCliente.GetProperty("viaje_id").GetGuid());
        Assert.Equal("NUEVA1", delCliente.GetProperty("detalle").GetProperty("despues").GetProperty("placa").GetString());
        Assert.NotEqual("NUEVA1", delCliente.GetProperty("detalle").GetProperty("antes").GetProperty("placa").GetString());
        Assert.Empty(await ConsultarAsync(adminOtro, "?accion=viaje.actualizar"));
    }

    [Fact]
    public async Task Crear_y_actualizar_un_usuario_guardan_el_antes_y_el_despues_sin_contrasenas_ni_pin_ni_tokens()
    {
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var cliente = Cliente(super.Token);

        var creado = await cliente.PostAsJsonAsync("/api/usuarios", new { nombre = "Luis Gomez", email = "luis@ejemplo.com", rol = "inspector", password = "Clave-Nueva-22" });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await ComoJsonAsync(creado)).GetProperty("id").GetGuid();
        var pin = await cliente.PostAsJsonAsync("/api/usuarios", new { nombre = "Carla Ruiz", rol = "conductor", cedula = "1032456789" });
        Assert.Equal(HttpStatusCode.Created, pin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.PatchAsync($"/api/usuarios/{id}", Cuerpo(new { nombre = "Luis Alberto Gomez", password = "Otra-Clave-33" }))).StatusCode);

        var filas = await ConsultarAsync(super, $"?entidad=usuarios&entidad_id={id}");
        var actualizar = filas.Single(f => f.GetProperty("accion").GetString() == "usuario.actualizar");
        var crear = filas.Single(f => f.GetProperty("accion").GetString() == "usuario.crear");

        Assert.Equal("Luis Gomez", actualizar.GetProperty("detalle").GetProperty("antes").GetProperty("nombre").GetString());
        Assert.Equal("Luis Alberto Gomez", actualizar.GetProperty("detalle").GetProperty("despues").GetProperty("nombre").GetString());
        Assert.Equal(JsonValueKind.Null, crear.GetProperty("detalle").GetProperty("antes").ValueKind);
        Assert.Equal("inspector", crear.GetProperty("detalle").GetProperty("despues").GetProperty("rol").GetString());
        foreach (var prohibido in new[] { "\"password\":", "password_hash", "pin_hash", "\"pin\":", "token", "Clave-Nueva-22", "Otra-Clave-33" })
        {
            Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Detalle LIKE @t", ("@t", $"%{prohibido}%")));
        }
    }

    [Fact]
    public async Task El_pin_errado_y_la_asignacion_de_un_pin_quedan_auditados_sin_el_valor_del_pin()
    {
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var conductor = await Fabrica.SembrarUsuarioConPinAsync("1032456789", "4821", nombre: "Conductor Uno");

        var mal = await Cliente().PostAsJsonAsync("/api/auth/login-conductor", new { cedula = "1032456789", pin = "0000" });
        Assert.Equal(HttpStatusCode.Unauthorized, mal.StatusCode);
        var asignado = await Cliente(super.Token).PostAsJsonAsync($"/api/usuarios/{conductor.Id}/pin", new { pin = "7391" });
        Assert.Equal(HttpStatusCode.OK, asignado.StatusCode);

        var filas = await ConsultarAsync(super, $"?entidad_id={conductor.Id}");

        Assert.Contains(filas, f => f.GetProperty("accion").GetString() == "auth.login_conductor_fallido");
        var asignacion = filas.Single(f => f.GetProperty("accion").GetString() == "usuario.pin_asignar");
        var detalle = asignacion.GetProperty("detalle").GetRawText();
        Assert.DoesNotContain("\"7391\"", detalle);
        Assert.DoesNotContain("pin_hash", detalle);
        Assert.DoesNotContain("\"pin\":", detalle);
        Assert.True(asignacion.GetProperty("detalle").GetProperty("despues").GetProperty("debe_cambiar_pin").GetBoolean());
    }

    [Fact]
    public async Task Cambiar_un_catalogo_guarda_el_valor_antes_y_despues()
    {
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var cliente = Cliente(super.Token);
        var creada = await cliente.PostAsJsonAsync("/api/catalogos/clases-dano", new { codigo = "oxidacion", nombre = "Oxidacion", orden = 90 });
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var id = (await ComoJsonAsync(creada)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await cliente.PatchAsync($"/api/catalogos/clases-dano/{id}", Cuerpo(new { nombre = "Oxidacion superficial" }))).StatusCode);

        var filas = await ConsultarAsync(super, $"?entidad=clases-dano&entidad_id={id}");

        var actualizar = filas.Single(f => f.GetProperty("accion").GetString() == "catalogo.clases-dano.actualizar");
        Assert.Equal("Oxidacion", actualizar.GetProperty("detalle").GetProperty("antes").GetProperty("nombre").GetString());
        Assert.Equal("Oxidacion superficial", actualizar.GetProperty("detalle").GetProperty("despues").GetProperty("nombre").GetString());
        Assert.Contains(filas, f => f.GetProperty("accion").GetString() == "catalogo.clases-dano.crear");
    }

    // ------------------------------------------------------------------------------------ consulta

    private async Task SembrarAuditoriaAsync(Guid? clienteId, string accion, DateTimeOffset creada, Guid? viajeId = null, Guid? usuarioId = null, string? detalle = null)
    {
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblAuditLog (Id, UsuarioId, Accion, Entidad, ViajeId, ClienteId, Detalle, CreatedAt, UpdatedAt) VALUES (NEWID(), @u, @a, N'pruebas', @v, @c, @d, @f, @f)",
            ("@u", usuarioId ?? (object)DBNull.Value), ("@a", accion), ("@v", viajeId ?? (object)DBNull.Value), ("@c", clienteId ?? (object)DBNull.Value),
            ("@d", detalle ?? (object)DBNull.Value), ("@f", creada));
    }

    [Fact]
    public async Task El_administrador_ve_solo_la_auditoria_de_su_cliente_y_el_super_administrador_la_de_todos()
    {
        var otro = await ClienteOtroAsync();
        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var adminOtro = await SesionAsync("admin-otro@ejemplo.com", clsRol.ADMIN, otro);
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var ahora = DateTimeOffset.UtcNow;
        await SembrarAuditoriaAsync(sofasa, "prueba.sofasa", ahora);
        await SembrarAuditoriaAsync(otro, "prueba.otro", ahora);
        await SembrarAuditoriaAsync(null, "prueba.sistema", ahora);

        var delAdmin = (await ConsultarAsync(admin, "?entidad=pruebas")).Select(f => f.GetProperty("accion").GetString()).ToList();
        var delOtro = (await ConsultarAsync(adminOtro, "?entidad=pruebas")).Select(f => f.GetProperty("accion").GetString()).ToList();
        var deTodos = (await ConsultarAsync(super, "?entidad=pruebas")).Select(f => f.GetProperty("accion").GetString()).Order().ToList();

        Assert.Equal(["prueba.sofasa"], delAdmin);
        Assert.Equal(["prueba.otro"], delOtro);
        Assert.Equal(["prueba.otro", "prueba.sistema", "prueba.sofasa"], deTodos);
    }

    [Fact]
    public async Task El_listado_pagina_de_la_mas_reciente_a_la_mas_antigua_y_filtra_por_cada_campo()
    {
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var otroUsuario = await Fabrica.SembrarUsuarioAsync("otro-usuario@ejemplo.com", "x", clsRol.INSPECTOR);
        var viaje = await Sembrador.ViajeAsync();
        var base1 = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
        {
            await SembrarAuditoriaAsync(null, $"prueba.paso{i}", base1.AddHours(i), viajeId: i == 3 ? viaje : null, usuarioId: i == 2 ? otroUsuario.Id : null);
        }

        var r = await Cliente(super.Token).GetAsync("/api/auditoria?entidad=pruebas&pagina=2&tamano=2");
        var pagina = await ComoJsonAsync(r);

        Assert.Equal(["items", "total", "pagina", "tamano"], pagina.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(5, pagina.GetProperty("total").GetInt32());
        Assert.Equal(2, pagina.GetProperty("pagina").GetInt32());
        Assert.Equal(2, pagina.GetProperty("tamano").GetInt32());
        Assert.Equal(["prueba.paso2", "prueba.paso1"], pagina.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("accion").GetString()).ToList());
        Assert.Equal(["prueba.paso3"], (await ConsultarAsync(super, $"?viaje_id={viaje}")).Select(f => f.GetProperty("accion").GetString()).ToList());
        Assert.Equal(["prueba.paso2"], (await ConsultarAsync(super, $"?usuario_id={otroUsuario.Id}&entidad=pruebas")).Select(f => f.GetProperty("accion").GetString()).ToList());
        Assert.Equal(["prueba.paso1"], (await ConsultarAsync(super, "?accion=prueba.paso1")).Select(f => f.GetProperty("accion").GetString()).ToList());
        Assert.Equal(
            ["prueba.paso3", "prueba.paso2"],
            (await ConsultarAsync(super, "?entidad=pruebas&desde=2026-07-01T12:00:00Z&hasta=2026-07-01T13:00:00Z")).Select(f => f.GetProperty("accion").GetString()).ToList());
        Assert.Equal(5, (await ConsultarAsync(super, "?entidad=pruebas&tamano=1000")).Count);
    }

    [Theory]
    [InlineData("usuario_id=no-es-uuid", "usuario_id")]
    [InlineData("entidad_id=123", "entidad_id")]
    [InlineData("viaje_id=x", "viaje_id")]
    [InlineData("desde=ayer", "desde")]
    [InlineData("hasta=2026-07-01", "hasta")]
    public async Task Un_filtro_mal_formado_responde_400_con_el_campo(string consulta, string campo)
    {
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var r = await Cliente(super.Token).GetAsync("/api/auditoria?" + consulta);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await ComoJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty(campo, out _));
    }

    [Fact]
    public async Task Un_registro_se_consulta_por_id_y_el_de_otro_cliente_responde_403()
    {
        var otro = await ClienteOtroAsync();
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        await SembrarAuditoriaAsync(otro, "prueba.otro", DateTimeOffset.UtcNow, detalle: "{\"antes\":{\"a\":1},\"despues\":{\"a\":2}}");
        var idOtro = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblAuditLog WHERE Accion = 'prueba.otro'"))!;

        var comoSuper = await Cliente(super.Token).GetAsync($"/api/auditoria/{idOtro}");
        var comoAdmin = await Cliente(admin.Token).GetAsync($"/api/auditoria/{idOtro}");
        var inexistente = await Cliente(admin.Token).GetAsync($"/api/auditoria/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, comoSuper.StatusCode);
        var registro = await ComoJsonAsync(comoSuper);
        Assert.Equal(
            ["id", "usuario_id", "usuario_nombre", "rol", "accion", "entidad", "entidad_id", "viaje_id", "codigo_mtrack", "cliente_id", "ip", "dispositivo", "detalle", "created_at"],
            registro.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(2, registro.GetProperty("detalle").GetProperty("despues").GetProperty("a").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, comoAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    public async Task Los_demas_roles_y_quien_no_inicio_sesion_no_consultan_la_auditoria(string rol)
    {
        var sesion = await SesionAsync($"{rol}@ejemplo.com", rol);

        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(sesion.Token).GetAsync("/api/auditoria")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(sesion.Token).GetAsync($"/api/auditoria/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync("/api/auditoria")).StatusCode);
    }

    [Fact]
    public async Task La_auditoria_no_se_modifica_ni_se_borra_por_la_api_ni_por_la_base()
    {
        var super = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        await SembrarAuditoriaAsync(null, "prueba.fija", DateTimeOffset.UtcNow);
        var id = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblAuditLog WHERE Accion = 'prueba.fija'"))!;
        var cliente = Cliente(super.Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await cliente.DeleteAsync($"/api/auditoria/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await cliente.PatchAsync($"/api/auditoria/{id}", Cuerpo(new { accion = "x" }))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await cliente.PutAsync($"/api/auditoria/{id}", Cuerpo(new { accion = "x" }))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await cliente.PostAsync("/api/auditoria", Cuerpo(new { accion = "x" }))).StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(() => Sembrador.EjecutarAsync("UPDATE dbo.tblAuditLog SET Accion = 'otra' WHERE Id = @i", ("@i", id)));
        await Assert.ThrowsAnyAsync<Exception>(() => Sembrador.EjecutarAsync("DELETE dbo.tblAuditLog WHERE Id = @i", ("@i", id)));
        Assert.Equal("prueba.fija", await Sembrador.EscalarAsync("SELECT Accion FROM dbo.tblAuditLog WHERE Id = @i", ("@i", id)));
    }

    [Fact]
    public async Task La_capacidad_ver_auditoria_la_tienen_el_administrador_y_el_super_administrador()
    {
        foreach (var (rol, esperado) in new[] { (clsRol.ADMIN, true), (clsRol.SUPER_ADMIN, true), (clsRol.COORDINADOR_CALIDAD, false), (clsRol.INSPECTOR, false), (clsRol.CONDUCTOR, false), (clsRol.COMERCIAL, false) })
        {
            var sesion = await SesionAsync($"{rol}-capacidad@ejemplo.com", rol);

            var me = await ComoJsonAsync(await Cliente(sesion.Token).GetAsync("/api/auth/me"));

            var capacidades = me.GetProperty("capacidades").EnumerateArray().Select(c => c.GetString()).ToList();
            Assert.Equal(esperado, capacidades.Contains("ver_auditoria"));
        }
    }
}
