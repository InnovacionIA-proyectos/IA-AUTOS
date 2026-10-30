using IAAutos.Aplicacion.Alertas;
using IAAutos.Aplicacion.Comun;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAAutos.Infraestructura.Alertas;

/// <summary>
/// El proceso periodico de alertas: cada minuto revisa si toca correr los avisos operativos (cada 10 minutos) o los patrones (cada hora) y los ejecuta en su propio alcance. Un fallo se registra y el
/// proceso sigue: la proxima ejecucion lo reintenta. Si dos instancias corren a la vez no se repite nada, porque cada alerta tiene su clave unica.
/// </summary>
public sealed class clsProcesoAlertas(IServiceScopeFactory alcances, IOptions<clsOpcionesAlertas> opciones, IReloj reloj, ILogger<clsProcesoAlertas> registro) : BackgroundService
{
    private static readonly TimeSpan LATIDO = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken parada)
    {
        if (!opciones.Value.ProcesosActivos)
        {
            registro.LogInformation("Los procesos periodicos de alertas estan apagados (Alertas:ProcesosActivos)");
            return;
        }

        var ultimoAviso = DateTimeOffset.MinValue;
        var ultimoUmbral = DateTimeOffset.MinValue;
        using var temporizador = new PeriodicTimer(LATIDO);
        do
        {
            var ahora = reloj.Ahora;
            if (ahora - ultimoAviso >= TimeSpan.FromMinutes(Math.Max(opciones.Value.AvisosCadaMinutos, 1)))
            {
                ultimoAviso = ahora;
                await EjecutarAsync("los avisos operativos", p => p.EjecutarAvisosAsync(parada), parada);
            }

            if (ahora - ultimoUmbral >= TimeSpan.FromMinutes(Math.Max(opciones.Value.UmbralesCadaMinutos, 1)))
            {
                ultimoUmbral = ahora;
                await EjecutarAsync("los patrones por umbral", p => p.EjecutarUmbralesAsync(parada), parada);
                await EjecutarAsync("la autocalibración de ubicaciones", p => p.EjecutarAutocalibracionAsync(parada), parada);
            }
        }
        while (await SiguienteLatidoAsync(temporizador, parada));
    }

    private static async Task<bool> SiguienteLatidoAsync(PeriodicTimer temporizador, CancellationToken parada)
    {
        try
        {
            return await temporizador.WaitForNextTickAsync(parada);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task EjecutarAsync(string que, Func<clsAlertasPeriodicas, Task<int>> proceso, CancellationToken parada)
    {
        try
        {
            using var alcance = alcances.CreateScope();
            var creadas = await proceso(alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>());
            registro.LogInformation("Proceso de alertas: {Que} ({Creadas} notificaciones intentadas)", que, creadas);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogError(ex, "Fallo el proceso de alertas: {Que}", que);
        }
    }
}
