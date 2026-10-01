using IAAutos.API.Configuracion;
using IAAutos.Aplicacion;
using IAAutos.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.Services
    .AddAplicacion(builder.Configuration)
    .AddInfraestructura(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

// Comandos de operacion (corren con la misma imagen y terminan sin levantar el servidor).
if (args.Length > 0 && args[0] == clsComandosCli.CREAR_SUPERADMIN)
{
    using var alcance = app.Services.CreateScope();
    return await clsComandosCli.CrearSuperAdminAsync(alcance.ServiceProvider, args, Console.Out, Environment.GetEnvironmentVariable);
}

app.UsarPipelineApi();
app.Run();
return 0;

// Necesario para WebApplicationFactory en las pruebas de integracion.
public partial class Program;
