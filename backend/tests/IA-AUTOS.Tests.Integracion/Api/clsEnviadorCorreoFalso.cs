using IAAutos.Aplicacion.ReporteDiario;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>El envio de correo de las pruebas: guarda lo que se «envia» y falla con lo que cada prueba indique (por defecto, todo sale).</summary>
public sealed class clsEnviadorCorreoFalso : IEnviadorCorreo
{
    private readonly List<clsCorreo> _enviados = [];

    /// <summary>Si no es nulo, cada envio devuelve este resultado en lugar de salir.</summary>
    public clsResultadoCorreo? Respuesta { get; set; }

    /// <summary>Si es verdadero, el envio lanza una excepcion (un fallo no previsto del cliente de correo).</summary>
    public bool Lanza { get; set; }

    public IReadOnlyList<clsCorreo> Enviados => _enviados;

    public Task<clsResultadoCorreo> EnviarAsync(clsCorreo correo, CancellationToken ct = default)
    {
        if (Lanza)
        {
            throw new InvalidOperationException("El servidor de correo no responde");
        }

        _enviados.Add(correo);
        return Task.FromResult(Respuesta ?? clsResultadoCorreo.Exito);
    }
}
