using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

[Collection(clsColecciones.BASE_DE_DATOS_1)]
public sealed class UsuariosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private async Task<string> TokenSuperAdminAsync(string email = "super@ejemplo.com")
    {
        await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", clsRol.SUPER_ADMIN, nombre: "Super Admin");
        return await LoginAsync(email, "Clave-Segura-1");
    }

    [Fact]
    public async Task Lista_para_administradores_trae_datos_completos_ordenados_por_nombre_y_con_fechas_iso()
    {
        await Fabrica.SembrarUsuarioAsync("zeta@ejemplo.com", "x", clsRol.ADMIN, nombre: "Zeta");
        await Fabrica.SembrarUsuarioAsync("alfa@ejemplo.com", "x", clsRol.CONDUCTOR, nombre: "Alfa");
        var token = await LoginAsync("zeta@ejemplo.com", "x");

        var respuesta = await Cliente(token).GetAsync("/api/usuarios");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal(["items", "total", "pagina", "tamano"], doc.RootElement.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(2, doc.RootElement.GetProperty("total").GetInt32());
        var items = doc.RootElement.GetProperty("items");
        var nombres = items.EnumerateArray().Select(u => u.GetProperty("nombre").GetString()).ToList();
        Assert.Equal(["Alfa", "Zeta"], nombres);
        var primero = items[0];
        Assert.Equal(
            ["id", "nombre", "email", "rol", "subtipo_inspector", "cedula", "activo", "tipo_credencial", "debe_cambiar_pin", "bloqueado_hasta", "ultimo_acceso_at", "cliente_id", "created_at", "updated_at", "pin_temporal"],
            primero.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Null(primero.GetProperty("pin_temporal").GetString());
        Assert.False(primero.TryGetProperty("pin_hash", out _));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", primero.GetProperty("created_at").GetString()!);
        Assert.Equal(JsonValueKind.Null, primero.GetProperty("subtipo_inspector").ValueKind);
        Assert.False(primero.TryGetProperty("password_hash", out _));
    }

    [Fact]
    public async Task Lista_para_los_demas_roles_es_reducida_sin_correos_y_solo_con_usuarios_activos()
    {
        await Fabrica.SembrarUsuarioAsync("inspector@ejemplo.com", "x", clsRol.INSPECTOR, nombre: "Inspector");
        await Fabrica.SembrarUsuarioAsync("conductor@ejemplo.com", "x", clsRol.CONDUCTOR, nombre: "Conductor");
        await Fabrica.SembrarUsuarioAsync("retirado@ejemplo.com", "x", clsRol.CONDUCTOR, activo: false, nombre: "Retirado");
        var token = await LoginAsync("inspector@ejemplo.com", "x");

        var respuesta = await Cliente(token).GetAsync("/api/usuarios");

        using var doc = await LeerAsync(respuesta);
        Assert.Equal(["Conductor", "Inspector"], doc.RootElement.EnumerateArray().Select(u => u.GetProperty("nombre").GetString()).ToList());
        Assert.Equal(["id", "nombre", "rol"], doc.RootElement[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.DoesNotContain("@", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Super_admin_crea_usuarios_y_el_nuevo_usuario_puede_ingresar()
    {
        var token = await TokenSuperAdminAsync();

        var respuesta = await Cliente(token).PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Luis Gomez",
            email = "Luis.Gomez@Ejemplo.com",
            rol = "inspector",
            password = "Clave-Nueva-22",
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("luis.gomez@ejemplo.com", doc.RootElement.GetProperty("email").GetString());
        Assert.True(doc.RootElement.GetProperty("activo").GetBoolean());
        await LoginAsync("luis.gomez@ejemplo.com", "Clave-Nueva-22");
    }

    [Theory]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    public async Task Solo_admin_coordinador_y_super_admin_gestionan_usuarios(string rol)
    {
        await Fabrica.SembrarUsuarioAsync("otro@ejemplo.com", "x", rol);
        var token = await LoginAsync("otro@ejemplo.com", "x");

        var respuesta = await Cliente(token).PostAsync("/api/usuarios", Json("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("FORBIDDEN", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("No tiene permisos para esta acción (rol requerido: admin, super_admin, coordinador_calidad)", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Correo_repetido_responde_409()
    {
        var token = await TokenSuperAdminAsync();
        await Fabrica.SembrarUsuarioAsync("existe@ejemplo.com", "x");

        var respuesta = await Cliente(token).PostAsJsonAsync("/api/usuarios", new
        {
            nombre = "Repetido",
            email = "EXISTE@ejemplo.com",
            rol = "inspector",
            password = "Clave-Nueva-22",
        });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("CONFLICT", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Ya existe un usuario con ese correo", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Datos_invalidos_responden_400_con_detalle_por_campo()
    {
        var token = await TokenSuperAdminAsync();

        var respuesta = await Cliente(token).PostAsJsonAsync("/api/usuarios", new
        {
            nombre = " ",
            email = "no-es-correo",
            rol = "gerente",
            password = "corta",
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("VALIDATION_ERROR", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Datos inválidos", doc.RootElement.GetProperty("message").GetString());
        var detalle = doc.RootElement.GetProperty("details");
        Assert.Equal(0, detalle.GetProperty("formErrors").GetArrayLength());
        var campos = detalle.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["nombre", "rol", "email", "password"], campos);
    }

    [Fact]
    public async Task Tipos_de_dato_incorrectos_responden_400()
    {
        var token = await TokenSuperAdminAsync();

        var respuesta = await Cliente(token).PostAsync("/api/usuarios", Json("{\"nombre\":123,\"email\":\"a@b.co\"}"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("Datos inválidos", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Actualiza_solo_los_campos_enviados_y_cambia_la_clave()
    {
        var token = await TokenSuperAdminAsync();
        var usuario = await Fabrica.SembrarUsuarioAsync("ana@ejemplo.com", "Clave-Vieja-1", clsRol.INSPECTOR, nombre: "Ana");

        var respuesta = await Cliente(token).PatchAsync($"/api/usuarios/{usuario.Id}", JsonContent.Create(new { rol = "comercial", password = "Clave-Nueva-22" }));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("comercial", doc.RootElement.GetProperty("rol").GetString());
        Assert.Equal("Ana", doc.RootElement.GetProperty("nombre").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsJsonAsync("/api/auth/login", new { email = "ana@ejemplo.com", password = "Clave-Vieja-1" })).StatusCode);
        await LoginAsync("ana@ejemplo.com", "Clave-Nueva-22");
    }

    [Fact]
    public async Task Un_usuario_dado_de_baja_con_patch_ya_no_ingresa()
    {
        var token = await TokenSuperAdminAsync();
        var usuario = await Fabrica.SembrarUsuarioAsync("ana@ejemplo.com", "Clave-Segura-1");

        var respuesta = await Cliente(token).PatchAsync($"/api/usuarios/{usuario.Id}", JsonContent.Create(new { activo = false }));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsJsonAsync("/api/auth/login", new { email = "ana@ejemplo.com", password = "Clave-Segura-1" })).StatusCode);
    }

    [Fact]
    public async Task Actualizar_un_usuario_inexistente_responde_404()
    {
        var token = await TokenSuperAdminAsync();

        var respuesta = await Cliente(token).PatchAsync($"/api/usuarios/{Guid.NewGuid()}", JsonContent.Create(new { nombre = "X" }));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("Usuario no encontrado", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task No_puede_eliminarse_a_si_mismo()
    {
        await Fabrica.SembrarUsuarioAsync("super@ejemplo.com", "Clave-Segura-1", clsRol.SUPER_ADMIN);
        var token = await LoginAsync("super@ejemplo.com", "Clave-Segura-1");
        var yo = await (await Cliente(token).GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();

        var respuesta = await Cliente(token).DeleteAsync($"/api/usuarios/{yo.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal("No puedes eliminar tu propio usuario", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Sin_historial_se_elimina_y_con_historial_se_retira()
    {
        var token = await TokenSuperAdminAsync();
        var libre = await Fabrica.SembrarUsuarioAsync("libre@ejemplo.com", "x");
        var conHistorial = await Fabrica.SembrarUsuarioAsync("activo@ejemplo.com", "Clave-Segura-1");
        await LoginAsync("activo@ejemplo.com", "Clave-Segura-1"); // el login deja registro de auditoria

        var r1 = await Cliente(token).DeleteAsync($"/api/usuarios/{libre.Id}");
        var r2 = await Cliente(token).DeleteAsync($"/api/usuarios/{conHistorial.Id}");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        using var d1 = await LeerAsync(r1);
        Assert.Equal("eliminado", d1.RootElement.GetProperty("modo").GetString());
        using var d2 = await LeerAsync(r2);
        Assert.Equal("retirado", d2.RootElement.GetProperty("modo").GetString());

        var lista = await (await Cliente(token).GetAsync("/api/usuarios")).Content.ReadFromJsonAsync<JsonElement>();
        var correos = lista.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("email").GetString()).ToList();
        Assert.DoesNotContain("libre@ejemplo.com", correos);
        var retirado = lista.GetProperty("items").EnumerateArray().Single(u => u.GetProperty("email").GetString() == "activo@ejemplo.com");
        Assert.False(retirado.GetProperty("activo").GetBoolean());
    }

    [Fact]
    public async Task Un_identificador_que_no_es_guid_no_coincide_con_ninguna_ruta()
    {
        var token = await TokenSuperAdminAsync();

        var respuesta = await Cliente(token).DeleteAsync("/api/usuarios/no-es-guid");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
