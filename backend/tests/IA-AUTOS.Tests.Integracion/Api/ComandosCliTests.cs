using System.Net;
using System.Net.Http.Json;
using IAAutos.API.Configuracion;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

[Collection(clsColecciones.BASE_DE_DATOS_1)]
public sealed class ComandosCliTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private async Task<(int Codigo, string Salida)> EjecutarAsync(string[] argumentos, string? password)
    {
        using var alcance = Fabrica.Services.CreateScope();
        var salida = new StringWriter();
        var codigo = await clsComandosCli.CrearSuperAdminAsync(
            alcance.ServiceProvider,
            argumentos,
            salida,
            nombre => nombre == clsComandosCli.VARIABLE_PASSWORD ? password : null);
        return (codigo, salida.ToString());
    }

    [Fact]
    public async Task Crea_el_primer_super_admin_que_ya_puede_iniciar_sesion_y_gestionar_usuarios()
    {
        var (codigo, salida) = await EjecutarAsync(["crear-superadmin", "Root@Ejemplo.com", "Ana", "Perez"], "Clave-Inicial-1");

        Assert.Equal(clsComandosCli.OK, codigo);
        Assert.Contains("Super administrador creado: root@ejemplo.com", salida);
        Assert.DoesNotContain("Clave-Inicial-1", salida);
        var token = await LoginAsync("root@ejemplo.com", "Clave-Inicial-1");
        var respuesta = await Cliente(token).GetAsync("/api/usuarios");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var yo = await (await Cliente(token).GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("super_admin", yo.GetProperty("rol").GetString());
        Assert.Equal("Ana Perez", yo.GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task Sobre_un_usuario_existente_restablece_la_clave_reactiva_y_lo_hace_super_admin()
    {
        await Fabrica.SembrarUsuarioAsync("retirado@ejemplo.com", "Clave-Vieja-1", "inspector", activo: false);

        var (codigo, salida) = await EjecutarAsync(["crear-superadmin", "retirado@ejemplo.com", "Recuperado"], "Clave-Nueva-22");

        Assert.Equal(clsComandosCli.OK, codigo);
        Assert.Contains("actualizado", salida);
        var token = await LoginAsync("retirado@ejemplo.com", "Clave-Nueva-22");
        var yo = await (await Cliente(token).GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("super_admin", yo.GetProperty("rol").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsJsonAsync("/api/auth/login", new { email = "retirado@ejemplo.com", password = "Clave-Vieja-1" })).StatusCode);
    }

    [Fact]
    public async Task Sin_la_variable_de_clave_o_sin_argumentos_termina_con_uso_incorrecto_y_no_crea_nada()
    {
        var sinClave = await EjecutarAsync(["crear-superadmin", "a@ejemplo.com", "Ana"], null);
        var sinArgumentos = await EjecutarAsync(["crear-superadmin", "a@ejemplo.com"], "Clave-Inicial-1");

        Assert.Equal(clsComandosCli.USO_INCORRECTO, sinClave.Codigo);
        Assert.Contains("SUPERADMIN_PASSWORD", sinClave.Salida);
        Assert.Equal(clsComandosCli.USO_INCORRECTO, sinArgumentos.Codigo);
        Assert.Equal(0, await new clsSembrador(BaseDatos.CadenaConexion).ContarAsync("SELECT COUNT(*) FROM dbo.tblUsuarios"));
    }

    [Theory]
    [InlineData("corta", "Datos inválidos")]
    [InlineData("Clave-Inicial-1", "Email inválido")]
    public async Task Los_datos_invalidos_terminan_con_error_y_un_mensaje_claro(string password, string mensajeEsperado)
    {
        var correo = password == "corta" ? "valido@ejemplo.com" : "no-es-correo";

        var (codigo, salida) = await EjecutarAsync(["crear-superadmin", correo, "Ana"], password);

        Assert.Equal(clsComandosCli.ERROR, codigo);
        Assert.Contains(mensajeEsperado, salida);
    }
}
