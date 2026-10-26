using IAAutos.Aplicacion.Notificaciones;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>El envio de push de las pruebas: guarda lo que se «envia» y falla con lo que cada prueba indique (por defecto, todo llega).</summary>
public sealed class clsEnviadorPushFalso : IEnviadorPush
{
    private readonly List<(string Token, clsMensajePush Mensaje)> _enviados = [];

    /// <summary>Si no es nulo, cada envio devuelve este resultado en lugar de llegar.</summary>
    public Func<string, clsResultadoPush>? Respuesta { get; set; }

    /// <summary>Si es verdadero, el envio lanza una excepcion (un fallo no previsto del cliente de FCM).</summary>
    public bool Lanza { get; set; }

    public IReadOnlyList<(string Token, clsMensajePush Mensaje)> Enviados
    {
        get
        {
            lock (_enviados)
            {
                return [.. _enviados];
            }
        }
    }

    public Task<clsResultadoPush> EnviarAsync(string tokenFcm, clsMensajePush mensaje, CancellationToken ct = default)
    {
        if (Lanza)
        {
            throw new HttpRequestException("FCM no responde");
        }

        lock (_enviados)
        {
            _enviados.Add((tokenFcm, mensaje));
        }

        return Task.FromResult(Respuesta?.Invoke(tokenFcm) ?? clsResultadoPush.Exito);
    }
}
