using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>Arranca la API real en memoria contra la base de pruebas con la configuracion indicada.</summary>
public sealed class clsFabricaApi(
    string cadenaConexion,
    IReadOnlyDictionary<string, string?>? extra = null,
    Action<IServiceCollection>? servicios = null) : WebApplicationFactory<Program>
{
    public const string SECRETO_JWT = "secreto-de-pruebas-con-mas-de-32-caracteres-aleatorios";
    public const string TOKEN_ETL = "token-etl-de-pruebas";
    public const string ORIGEN_PERMITIDO = "https://web.ejemplo.com";

    protected override void ConfigureWebHost(IWebHostBuilder constructor)
    {
        constructor.UseEnvironment("Testing");
        var valores = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = cadenaConexion,
            ["Jwt:Secret"] = SECRETO_JWT,
            ["Etl:IngestToken"] = TOKEN_ETL,
            ["Cors:Origins"] = ORIGEN_PERMITIDO,
            ["Contrasenas:Iteraciones"] = "1000",
            ["Seguridad:LoginMaxIntentosPorMinuto"] = "1000",
            ["Storage:LocalPath"] = Path.Combine(Path.GetTempPath(), "iaautos-pruebas-" + Guid.NewGuid().ToString("N")),
        };
        foreach (var (clave, valor) in extra ?? new Dictionary<string, string?>())
        {
            valores[clave] = valor;
        }

        foreach (var (clave, valor) in valores)
        {
            constructor.UseSetting(clave, valor);
        }

        if (servicios is not null)
        {
            constructor.ConfigureTestServices(servicios);
        }
    }

    /// <summary>Crea un usuario directamente en la base (con la clave indicada hasheada con el servicio real).</summary>
    public async Task<clsUsuario> SembrarUsuarioAsync(
        string email,
        string password,
        string rol = clsRol.INSPECTOR,
        bool activo = true,
        string? hashExistente = null,
        string nombre = "Usuario de prueba",
        string? cedula = null)
    {
        using var alcance = Services.CreateScope();
        var s = alcance.ServiceProvider;
        var hasher = s.GetRequiredService<IServicioContrasenas>();
        var usuario = clsUsuario.Crear(nombre, email, rol, null, hashExistente ?? hasher.Hashear(password), DateTimeOffset.UtcNow, cedula);
        if (!activo)
        {
            usuario.Retirar(DateTimeOffset.UtcNow);
        }

        s.GetRequiredService<IUsuarioRepository>().Agregar(usuario);
        await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        return usuario;
    }

    /// <summary>Crea un usuario con un identificador conocido (por ejemplo, para tokens generados fuera de la API).</summary>
    public async Task SembrarUsuarioConIdAsync(Guid id, string email, string password, string rol)
    {
        using var alcance = Services.CreateScope();
        var hash = alcance.ServiceProvider.GetRequiredService<IServicioContrasenas>().Hashear(password);
        await using var conexion = new Microsoft.Data.SqlClient.SqlConnection(cadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new Microsoft.Data.SqlClient.SqlCommand(
            "INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, Activo, PasswordHash) VALUES (@id, N'Usuario con id fijo', @email, @rol, 1, @hash)",
            conexion);
        comando.Parameters.AddWithValue("@id", id);
        comando.Parameters.AddWithValue("@email", email);
        comando.Parameters.AddWithValue("@rol", rol);
        comando.Parameters.AddWithValue("@hash", hash);
        await comando.ExecuteNonQueryAsync();
    }
}
