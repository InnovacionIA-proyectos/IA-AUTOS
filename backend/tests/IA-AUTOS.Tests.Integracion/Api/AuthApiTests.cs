using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Infraestructura.Persistencia;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class AuthApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    // Token generado con la libreria jose (la del sistema anterior): HS256, claims user_role/email/sub, vigencia 3650 dias.
    private const string TOKEN_JOSE =
        "eyJhbGciOiJIUzI1NiJ9.eyJ1c2VyX3JvbGUiOiJpbnNwZWN0b3IiLCJlbWFpbCI6InZpZWpvQGVqZW1wbG8uY29tIiwic3ViIjoiMTExMTExMTEtMjIyMi0zMzMzLTQ0NDQtNTU1NTU1NTU1NTU1IiwiaWF0IjoxNzkwODc2NDg0LCJleHAiOjIxMDYyMzY0ODR9.0KuJWVOAL4bsdRDyPNZv5H2NYoWWZl0Y2VeLBOtBwds";

    private static readonly Guid SUB_JOSE = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static void AfirmarCuerpoDeError(JsonElement raiz, string codigo, string mensaje)
    {
        Assert.Equal(codigo, raiz.GetProperty("code").GetString());
        Assert.Equal(mensaje, raiz.GetProperty("message").GetString());
        Assert.Equal(mensaje, raiz.GetProperty("error").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", raiz.GetProperty("timestamp").GetString()!);
    }

    [Fact]
    public async Task Login_correcto_devuelve_token_y_usuario()
    {
        var usuario = await Fabrica.SembrarUsuarioAsync("Ana@Ejemplo.com", "Clave-Segura-1", clsRol.ADMIN, nombre: "Ana Perez");

        var respuesta = await Cliente().PostAsJsonAsync("/api/auth/login", new { email = " ana@ejemplo.com ", password = "Clave-Segura-1" });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("token").GetString()));
        var u = doc.RootElement.GetProperty("usuario");
        Assert.Equal(usuario.Id, u.GetProperty("id").GetGuid());
        Assert.Equal("Ana Perez", u.GetProperty("nombre").GetString());
        Assert.Equal("ana@ejemplo.com", u.GetProperty("email").GetString());
        Assert.Equal("admin", u.GetProperty("rol").GetString());
        Assert.Equal(["id", "nombre", "email", "rol", "cedula", "debe_cambiar_pin"], u.EnumerateObject().Select(p => p.Name).ToList());
        Assert.False(u.GetProperty("debe_cambiar_pin").GetBoolean());
    }

    [Fact]
    public async Task Login_registra_auditoria()
    {
        var usuario = await Fabrica.SembrarUsuarioAsync("ana@ejemplo.com", "Clave-Segura-1");
        await LoginAsync("ana@ejemplo.com", "Clave-Segura-1");

        using var alcance = Fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<clsAppDbContext>();
        var registro = await contexto.AuditLog.SingleAsync();
        Assert.Equal("auth.login", registro.Accion);
        Assert.Equal(usuario.Id, registro.UsuarioId);
    }

    [Theory]
    [InlineData("ana@ejemplo.com", "incorrecta")]
    [InlineData("otro@ejemplo.com", "Clave-Segura-1")]
    public async Task Credenciales_invalidas_responden_401_con_mensaje_generico(string email, string password)
    {
        await Fabrica.SembrarUsuarioAsync("ana@ejemplo.com", "Clave-Segura-1");

        var respuesta = await Cliente().PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        AfirmarCuerpoDeError(doc.RootElement, "UNAUTHORIZED", "Correo o contraseña incorrectos");
    }

    [Fact]
    public async Task Usuario_retirado_no_puede_ingresar()
    {
        await Fabrica.SembrarUsuarioAsync("retirado@ejemplo.com", "Clave-Segura-1", activo: false);

        var respuesta = await Cliente().PostAsJsonAsync("/api/auth/login", new { email = "retirado@ejemplo.com", password = "Clave-Segura-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        AfirmarCuerpoDeError(doc.RootElement, "UNAUTHORIZED", "Usuario retirado: no tiene acceso. Contacta al administrador.");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"email\":\"a@b.co\"}")]
    [InlineData("{\"email\":\"\",\"password\":\"x\"}")]
    public async Task Login_sin_correo_o_clave_responde_400(string cuerpo)
    {
        var respuesta = await Cliente().PostAsync("/api/auth/login", Json(cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        AfirmarCuerpoDeError(doc.RootElement, "VALIDATION_ERROR", "Correo y contraseña son obligatorios");
        Assert.False(doc.RootElement.TryGetProperty("details", out _));
    }

    [Theory]
    [InlineData("esto no es json", "El cuerpo debe ser JSON válido")]
    [InlineData("", "El cuerpo debe ser JSON válido")]
    [InlineData("[1,2]", "El cuerpo debe ser un objeto JSON")]
    [InlineData("\"texto\"", "El cuerpo debe ser un objeto JSON")]
    public async Task Cuerpo_invalido_responde_400_con_el_mensaje_original(string cuerpo, string mensaje)
    {
        var respuesta = await Cliente().PostAsync("/api/auth/login", Json(cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        AfirmarCuerpoDeError(doc.RootElement, "VALIDATION_ERROR", mensaje);
    }

    [Fact]
    public async Task Me_devuelve_el_usuario_del_token()
    {
        var usuario = await Fabrica.SembrarUsuarioAsync("ana@ejemplo.com", "Clave-Segura-1", clsRol.CONDUCTOR);
        var token = await LoginAsync("ana@ejemplo.com", "Clave-Segura-1");

        var respuesta = await Cliente(token).GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal(usuario.Id, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("conductor", doc.RootElement.GetProperty("rol").GetString());
    }

    [Fact]
    public async Task Los_acentos_salen_como_utf8_directo_y_no_escapados()
    {
        var respuesta = await Cliente().GetAsync("/api/auth/me");

        var texto = await respuesta.Content.ReadAsStringAsync();

        Assert.Contains("Falta el token de autenticación (Bearer)", texto);
        Assert.DoesNotContain("\\u00F3", texto);
    }

    [Fact]
    public async Task Sin_token_responde_401_con_el_mensaje_original()
    {
        var respuesta = await Cliente().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        AfirmarCuerpoDeError(doc.RootElement, "UNAUTHORIZED", "Falta el token de autenticación (Bearer)");
        Assert.Contains("application/json", respuesta.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Token_basura_vencido_o_firmado_con_otro_secreto_responde_401_invalido()
    {
        var usuario = await Fabrica.SembrarUsuarioAsync("ana@ejemplo.com", "Clave-Segura-1");
        var casos = new[]
        {
            "no.es.un.token",
            CrearToken(usuario.Id, usuario.Email, usuario.Rol, expira: DateTime.UtcNow.AddHours(-2)),
            CrearToken(usuario.Id, usuario.Email, usuario.Rol, secreto: "otro-secreto-completamente-distinto-de-32-caracteres"),
        };

        foreach (var token in casos)
        {
            var respuesta = await Cliente(token).GetAsync("/api/auth/me");

            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            using var doc = await LeerAsync(respuesta);
            AfirmarCuerpoDeError(doc.RootElement, "UNAUTHORIZED", "Token de autenticación inválido o expirado");
        }
    }

    [Fact]
    public async Task Acepta_un_token_emitido_por_el_sistema_anterior_con_jose()
    {
        await Fabrica.SembrarUsuarioConIdAsync(SUB_JOSE, "viejo@ejemplo.com", "x", clsRol.INSPECTOR);

        var respuesta = await Cliente(TOKEN_JOSE).GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        Assert.Equal(SUB_JOSE, doc.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Un_usuario_retirado_pierde_el_acceso_aunque_su_token_siga_vigente()
    {
        var usuario = await Fabrica.SembrarUsuarioAsync("ana@ejemplo.com", "Clave-Segura-1");
        var token = await LoginAsync("ana@ejemplo.com", "Clave-Segura-1");
        Assert.Equal(HttpStatusCode.OK, (await Cliente(token).GetAsync("/api/auth/me")).StatusCode);

        await EjecutarSqlAsync($"UPDATE dbo.tblUsuarios SET Activo = 0 WHERE Id = '{usuario.Id}'");

        var respuesta = await Cliente(token).GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        AfirmarCuerpoDeError(doc.RootElement, "UNAUTHORIZED", "Usuario retirado: no tiene acceso. Contacta al administrador.");
    }

    [Fact]
    public async Task Un_token_de_un_usuario_eliminado_es_sesion_no_valida()
    {
        var token = CrearToken(Guid.NewGuid(), "fantasma@ejemplo.com", clsRol.ADMIN);

        var respuesta = await Cliente(token).GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        using var doc = await LeerAsync(respuesta);
        AfirmarCuerpoDeError(doc.RootElement, "UNAUTHORIZED", "Sesión no válida");
    }

    [Fact]
    public async Task El_rol_efectivo_sale_de_la_base_de_datos_y_no_del_token()
    {
        var usuario = await Fabrica.SembrarUsuarioAsync("inspector@ejemplo.com", "x", clsRol.INSPECTOR);
        var tokenInflado = CrearToken(usuario.Id, usuario.Email, clsRol.SUPER_ADMIN);

        var respuesta = await Cliente(tokenInflado).PostAsync("/api/usuarios", Json("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Hash_scrypt_heredado_permite_ingresar_y_se_regenera()
    {
        const string HASH_NODE = "scrypt:0102030405060708090a0b0c0d0e0f10:936c45fb5f22fb4ecda5400f1bbfa07b387f01b202839ac365449e69b750093f640ad4b15bf730c4e021efb6c3649f22a95482b9353ff6096229d8aaa3b552f5";
        var usuario = await Fabrica.SembrarUsuarioAsync("viejo@ejemplo.com", "x", hashExistente: HASH_NODE);

        await LoginAsync("viejo@ejemplo.com", "x");

        var hash = await LeerEscalarAsync($"SELECT PasswordHash FROM dbo.tblUsuarios WHERE Id = '{usuario.Id}'");
        Assert.StartsWith("pbkdf2-sha256$", hash);
        await LoginAsync("viejo@ejemplo.com", "x");
    }

    [Fact]
    public async Task Excede_el_limite_de_intentos_de_login_con_429()
    {
        var limitada = CrearFabrica(new Dictionary<string, string?> { ["Seguridad:LoginMaxIntentosPorMinuto"] = "3" });
        var cliente = limitada.CreateClient();
        HttpResponseMessage? ultima = null;

        for (var i = 0; i < 4; i++)
        {
            ultima = await cliente.PostAsJsonAsync("/api/auth/login", new { email = "a@b.co", password = "mal" });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, ultima!.StatusCode);
        using var doc = await LeerAsync(ultima);
        AfirmarCuerpoDeError(doc.RootElement, "RATE_LIMITED", "Demasiados intentos. Intenta de nuevo en un minuto.");
        Assert.True(ultima.Headers.Contains("Retry-After"));
    }

    private async Task EjecutarSqlAsync(string sql)
    {
        await using var conexion = new Microsoft.Data.SqlClient.SqlConnection(BaseDatos.CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new Microsoft.Data.SqlClient.SqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    private async Task<string> LeerEscalarAsync(string sql)
    {
        await using var conexion = new Microsoft.Data.SqlClient.SqlConnection(BaseDatos.CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new Microsoft.Data.SqlClient.SqlCommand(sql, conexion);
        return (string)(await comando.ExecuteScalarAsync())!;
    }
}
