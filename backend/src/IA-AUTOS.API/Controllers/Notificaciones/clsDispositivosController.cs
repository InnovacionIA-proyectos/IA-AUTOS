using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Notificaciones.DTONotificaciones;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Notificaciones;

/// <summary>Los celulares del usuario donde recibe las notificaciones push (token de FCM).</summary>
[Route("api/dispositivos")]
public sealed class clsDispositivosController(clsDispositivosService servicio) : clsControladorBase
{
    /// <summary>Registra el dispositivo del usuario (<c>token_fcm</c>, <c>plataforma</c> y <c>modelo</c>) o, si el token ya existe, lo actualiza y lo deja activo a su nombre.</summary>
    [HttpPost]
    [ProducesResponseType<DispositivoDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(RegistrarDispositivoRequest))]
    public async Task<IActionResult> Registrar(CancellationToken ct) =>
        Ok(await servicio.RegistrarAsync(await LeerCuerpoAsync<RegistrarDispositivoRequest>(ct), ct));

    /// <summary>Retira el token del usuario al cerrar sesion: deja de recibir push. Siempre responde 204, tambien si el token no existe o ya estaba retirado.</summary>
    [HttpDelete("{token_fcm}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Retirar([FromRoute(Name = "token_fcm")] string tokenFcm, CancellationToken ct)
    {
        await servicio.RetirarAsync(tokenFcm, ct);
        return NoContent();
    }
}
