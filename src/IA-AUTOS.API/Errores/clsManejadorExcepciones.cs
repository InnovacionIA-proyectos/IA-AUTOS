using IAAutos.Dominio.Comun;
using Microsoft.AspNetCore.Diagnostics;

namespace IAAutos.API.Errores;

/// <summary>
/// Manejo global de errores: convierte excepciones de dominio en respuestas JSON consistentes y oculta
/// los detalles de cualquier error inesperado (se registran solo en el servidor).
/// </summary>
internal sealed class clsManejadorExcepciones(ILogger<clsManejadorExcepciones> registro) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception excepcion, CancellationToken ct)
    {
        switch (excepcion)
        {
            case clsExcepcionDominio dominio:
                await clsCuerpoError.EscribirAsync(contexto, dominio.Codigo, dominio.Message, dominio.Detalles);
                return true;

            case BadHttpRequestException solicitudInvalida:
                await clsCuerpoError.EscribirAsync(
                    contexto,
                    clsCodigosError.VALIDATION_ERROR,
                    "Solicitud inválida",
                    estado: solicitudInvalida.StatusCode);
                return true;

            case OperationCanceledException when contexto.RequestAborted.IsCancellationRequested:
                // El cliente cerro la conexion: no hay a quien responder.
                return true;

            default:
                registro.LogError(excepcion, "Error no controlado en {Metodo} {Ruta}", contexto.Request.Method, contexto.Request.Path);
                await clsCuerpoError.EscribirAsync(contexto, clsCodigosError.INTERNAL_ERROR, "Error interno del servidor");
                return true;
        }
    }
}
