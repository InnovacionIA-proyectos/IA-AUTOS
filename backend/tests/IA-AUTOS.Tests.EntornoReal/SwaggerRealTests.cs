using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace IAAutos.Tests.EntornoReal;

/// <summary>
/// La interfaz /swagger solo existe con Documentacion__Habilitada=true (o en Development). Solo se piden recursos estaticos:
/// no se modifican datos.
/// </summary>
public sealed class SwaggerRealTests
{
    private static WebApplicationFactory<Program> CrearFabrica(bool documentacion) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(constructor =>
        {
            constructor.UseEnvironment("Testing");
            // Va al final de la configuracion: gana sobre la variable de entorno Documentacion__Habilitada.
            constructor.ConfigureAppConfiguration((_, configuracion) => configuracion.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Documentacion:Habilitada"] = documentacion ? "true" : "false" }));
        });

    [FactEntornoReal]
    public async Task Con_la_documentacion_habilitada_la_interfaz_y_el_contrato_responden()
    {
        await using var fabrica = CrearFabrica(documentacion: true);
        var cliente = fabrica.CreateClient();

        var interfaz = await cliente.GetAsync("/swagger/index.html");
        var cuerpo = await interfaz.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, interfaz.StatusCode);
        Assert.Contains("swagger-ui", cuerpo);
        // La interfaz necesita scripts y estilos propios: la politica de /swagger es mas amplia que la del resto de la API.
        Assert.Contains("script-src 'self'", interfaz.Headers.GetValues("Content-Security-Policy").Single());

        var contrato = await cliente.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, contrato.StatusCode);
    }

    [FactEntornoReal]
    public async Task El_resto_de_la_api_conserva_la_politica_estricta_aunque_la_documentacion_este_habilitada()
    {
        await using var fabrica = CrearFabrica(documentacion: true);

        var respuesta = await fabrica.CreateClient().GetAsync("/api/usuarios");

        Assert.Equal("default-src 'none'; frame-ancestors 'none'", respuesta.Headers.GetValues("Content-Security-Policy").Single());
    }

    [FactEntornoReal]
    public async Task Sin_la_documentacion_habilitada_no_se_publica_ni_la_interfaz_ni_el_contrato()
    {
        await using var fabrica = CrearFabrica(documentacion: false);
        var cliente = fabrica.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/openapi/v1.json")).StatusCode);
    }
}
