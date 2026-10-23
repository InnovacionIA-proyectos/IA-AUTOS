using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.ReporteDiario;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAAutos.Infraestructura.Alertas;

/// <summary>
/// El proceso programado del reporte diario: cada minuto le pregunta al servicio si toca (la hora y los destinatarios se leen de los parametros en cada vuelta, asi que un cambio se aplica sin
/// reiniciar). Una vez terminado el dia no vuelve a consultar hasta el dia siguiente; si el envio fallo, espera los minutos configurados antes de reintentar. Si dos instancias corren a la vez,
/// la clave unica de la notificacion impide registrar el mismo reporte dos veces.
/// </summary>
public sealed class clsProcesoReporteDiario(IServiceScopeFactory alcances, IOptions<clsOpcionesReporteDiario> opciones, IReloj reloj, ILogger<clsProcesoReporteDiario> registro) : BackgroundService
{
    private static readonly TimeSpan LATIDO = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken parada)
    {
        if (!opciones.Value.ProcesoActivo)
        {
            registro.LogInformation("El proceso del reporte diario esta apagado (ReporteDiario:ProcesoActivo)");
            return;
        }

        DateOnly? diaTerminado = null;
        var proximoIntento = DateTimeOffset.MinValue;
        using var temporizador = new PeriodicTimer(LATIDO);
        do
        {
            var ahora = reloj.Ahora;
            var dia = clsReporteDiarioService.DiaReportado(ahora);
            if (diaTerminado == dia || ahora < proximoIntento)
            {
                continue;
            }

            try
            {
                using var alcance = alcances.CreateScope();
                var resultado = await alcance.ServiceProvider.GetRequiredService<clsReporteDiarioService>().EjecutarAsync(parada);
                if (resultado.Terminado)
                {
                    diaTerminado = resultado.Dia;
                }

                if (resultado.Estado == EstadoReporteDiario.Fallo)
                {
                    proximoIntento = ahora.AddMinutes(Math.Max(opciones.Value.ReintentoCadaMinutos, 1));
                }
                else if (resultado.Estado != EstadoReporteDiario.AunNoEsLaHora)
                {
                    registro.LogInformation("Reporte diario del {Dia}: {Estado}", resultado.Dia, resultado.Estado);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registro.LogError(ex, "Fallo el proceso del reporte diario");
                proximoIntento = ahora.AddMinutes(Math.Max(opciones.Value.ReintentoCadaMinutos, 1));
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
}
