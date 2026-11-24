using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Parametros;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>Los dos plazos de la espera de un inspector en un destino, contados desde que el conductor activo la excepcion «No hay inspector»: el del aviso al coordinador y el del plan B.</summary>
public sealed record PlazosDelDestino(DateTimeOffset AvisaAt, DateTimeOffset HabilitaPlanBAt);

/// <summary>
/// La espera de un inspector en un destino. Desde que el conductor activa la excepcion «No hay inspector» corren dos plazos que TI ajusta sin desplegar: <c>espera_inspector_aviso_minutos</c> (X, 15 por defecto) a cuyo
/// cumplimiento se avisa al coordinador y <c>espera_inspector_plan_b_minutos</c> (Y, 30 por defecto) que se suma a X para habilitar el plan B (la revision de llegada del conductor y el cierre sin inspector del punto).
/// Cada peticion que necesita saber si el plan B ya esta habilitado lo calcula aqui, con los valores vigentes y sin esperar a ningun proceso periodico: nunca se responde «espere» despues del plazo.
/// </summary>
public sealed class clsEsperaDelDestino(IParametros parametros)
{
    public const int AVISO_POR_DEFECTO_MINUTOS = 15;
    public const int PLAN_B_POR_DEFECTO_MINUTOS = 30;

    /// <summary>X: los minutos de espera antes de avisar al coordinador (al menos 1).</summary>
    public async Task<int> AvisoMinutosAsync(CancellationToken ct = default) =>
        Math.Max(await parametros.EnteroAsync(clsClavesParametros.ESPERA_INSPECTOR_AVISO_MINUTOS, AVISO_POR_DEFECTO_MINUTOS, ct), 1);

    /// <summary>Y: los minutos adicionales, despues del aviso, para habilitar el plan B (al menos 1).</summary>
    public async Task<int> PlanBMinutosAsync(CancellationToken ct = default) =>
        Math.Max(await parametros.EnteroAsync(clsClavesParametros.ESPERA_INSPECTOR_PLAN_B_MINUTOS, PLAN_B_POR_DEFECTO_MINUTOS, ct), 1);

    /// <summary>Cuando se avisa al coordinador (X minutos) y cuando se habilita el plan B (X mas Y minutos) para una excepcion activada en <paramref name="activadaAt"/>, con los valores vigentes.</summary>
    public async Task<PlazosDelDestino> PlazosAsync(DateTimeOffset activadaAt, CancellationToken ct = default)
    {
        var aviso = await AvisoMinutosAsync(ct);
        var planB = await PlanBMinutosAsync(ct);
        return new PlazosDelDestino(activadaAt.AddMinutes(aviso), activadaAt.AddMinutes(aviso + planB));
    }

    /// <summary>True si ya se cumplieron X mas Y minutos desde que el conductor activo la excepcion: el plan B esta habilitado (justo en el limite tambien).</summary>
    public async Task<bool> PlanBHabilitadoAsync(DateTimeOffset activadaAt, DateTimeOffset ahora, CancellationToken ct = default) =>
        ahora >= (await PlazosAsync(activadaAt, ct)).HabilitaPlanBAt;
}
