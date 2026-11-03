using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>Base de las pruebas HTTP: base limpia por prueba, cliente con y sin token, y utilidades de JSON y JWT.</summary>
public abstract class clsPruebaApiBase(clsBaseDatosFixture bd) : IAsyncLifetime
{
    private readonly List<clsFabricaApi> _fabricas = [];

    protected clsBaseDatosFixture BaseDatos { get; } = bd;

    protected clsFabricaApi Fabrica { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        await BaseDatos.LimpiarAsync();
        Fabrica = CrearFabrica();
    }

    public virtual async Task DisposeAsync()
    {
        foreach (var f in _fabricas)
        {
            await f.DisposeAsync();
        }
    }

    protected clsFabricaApi CrearFabrica(IReadOnlyDictionary<string, string?>? extra = null, Action<IServiceCollection>? servicios = null)
    {
        var fabrica = new clsFabricaApi(BaseDatos.CadenaConexion, extra, servicios);
        _fabricas.Add(fabrica);
        return fabrica;
    }

    protected HttpClient Cliente(string? token = null, clsFabricaApi? fabrica = null)
    {
        var cliente = (fabrica ?? Fabrica).CreateClient();
        if (token is not null)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
    }

    /// <summary>
    /// Un lugar real del catalogo <c>lugares</c>, creado por la API (la que renueva el cache de los catalogos): el existente con ese nombre, o uno nuevo del tipo y la ciudad indicados.
    /// Quien llama usa un cliente con permiso de administrar catalogos (coordinador de calidad, admin).
    /// </summary>
    protected static async Task<Guid> LugarRealAsync(HttpClient cliente, string nombre = "Lugar de prueba", string tipo = "destino", string ciudad = "Medellin")
    {
        var lista = await cliente.GetFromJsonAsync<JsonElement>("/api/catalogos/lugares");
        foreach (var lugar in lista.GetProperty("items").EnumerateArray())
        {
            if (lugar.GetProperty("nombre").GetString() == nombre)
            {
                return lugar.GetProperty("id").GetGuid();
            }
        }

        var respuesta = await cliente.PostAsJsonAsync("/api/catalogos/lugares", new { nombre, tipo, ciudad });
        respuesta.EnsureSuccessStatusCode();
        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Inicia sesion por la API y devuelve el token emitido.</summary>
    protected async Task<string> LoginAsync(string email, string password, clsFabricaApi? fabrica = null)
    {
        var respuesta = await Cliente(fabrica: fabrica).PostAsJsonAsync("/api/auth/login", new { email, password });
        respuesta.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    /// <summary>Cambia un parametro en la base y descarta lo que la aplicacion tiene guardado (la lectura se guarda un minuto), para que la prueba vea el valor nuevo de inmediato.</summary>
    protected async Task CambiarParametroAsync(string clave, string? valor, clsFabricaApi? fabrica = null)
    {
        await new clsSembrador(BaseDatos.CadenaConexion).EjecutarAsync(
            "UPDATE dbo.tblParametros SET Valor = @v WHERE Clave = @c", ("@v", (object?)valor ?? DBNull.Value), ("@c", clave));
        using var alcance = (fabrica ?? Fabrica).Services.CreateScope();
        alcance.ServiceProvider.GetRequiredService<IParametros>().Invalidar();
    }

    /// <summary>La URL firmada vigente que daria el servidor para un archivo (por ejemplo, uno sembrado directamente en la base).</summary>
    protected string UrlFirmada(string archivo, clsFabricaApi? fabrica = null) =>
        (fabrica ?? Fabrica).Services.GetRequiredService<IFirmadorUrls>().Firmar(archivo, "http://localhost", 15)!.Url;

    /// <summary>Las filas de una pagina de un listado (<c>{items, total, pagina, tamano}</c>).</summary>
    protected static JsonElement Items(JsonElement pagina) => pagina.GetProperty("items");

    protected static StringContent Json(string texto) => new(texto, Encoding.UTF8, "application/json");

    protected static async Task<JsonDocument> LeerAsync(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

    /// <summary>Genera un token HS256 con los mismos claims que emitia el sistema anterior (sub, email, user_role).</summary>
    protected static string CrearToken(Guid sub, string? email, string rol, string? secreto = null, DateTime? expira = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                ["sub"] = sub.ToString(),
                ["email"] = email ?? string.Empty,
                ["user_role"] = rol,
            },
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expira ?? DateTime.UtcNow.AddDays(30),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secreto ?? clsFabricaApi.SECRETO_JWT)),
                SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
