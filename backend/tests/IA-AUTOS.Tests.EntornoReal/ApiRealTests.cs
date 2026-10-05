using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IAAutos.Tests.EntornoReal;

/// <summary>
/// Arranca la API en memoria con la configuracion real (variables de entorno, igual que backend/.env) y solo hace
/// solicitudes de lectura que no modifican datos.
/// </summary>
public sealed class ApiRealTests : IClassFixture<ApiRealTests.FabricaApiReal>
{
    private readonly FabricaApiReal _fabrica;

    public ApiRealTests(FabricaApiReal fabrica) => _fabrica = fabrica;

    public sealed class FabricaApiReal : WebApplicationFactory<Program>
    {
        // Sin sobrescrituras: la API toma ConnectionStrings__*, Jwt__*, Storage__S3__*, etc. del entorno del proceso.
        protected override void ConfigureWebHost(IWebHostBuilder constructor) => constructor.UseEnvironment("Testing");
    }

    [FactEntornoReal]
    public async Task La_salud_confirma_la_conexion_con_la_base_real()
    {
        var respuesta = await _fabrica.CreateClient().GetAsync("/health");
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("\"connected\":true", cuerpo);
    }

    [FactEntornoReal]
    public async Task Un_endpoint_protegido_exige_token()
    {
        var respuesta = await _fabrica.CreateClient().GetAsync("/api/usuarios");
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [FactEntornoReal]
    public async Task Una_ruta_inexistente_responde_404_con_el_formato_de_error_de_la_api()
    {
        var respuesta = await _fabrica.CreateClient().GetAsync("/ruta-que-no-existe");
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Contains("NOT_FOUND", cuerpo);
    }
}
