using FluentValidation;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Notificaciones.DTONotificaciones;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;

namespace IAAutos.Aplicacion.Notificaciones;

/// <summary>El dispositivo registrado: su plataforma, su modelo y si recibe push.</summary>
public sealed record DispositivoDto(Guid Id, string Plataforma, string? Modelo, bool Activo, DateTimeOffset UltimoUsoAt);

/// <summary>
/// Los celulares donde el usuario recibe las notificaciones push. El token de FCM es unico: registrarlo de nuevo lo actualiza (y, si el celular cambio de usuario, pasa al nuevo) y cerrar la
/// sesion lo retira.
/// </summary>
public sealed class clsDispositivosService(
    IDispositivoRepository dispositivos,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<RegistrarDispositivoRequest> validador)
{
    /// <summary>Registra el token del dispositivo del usuario o, si ya existe, lo actualiza y lo deja activo a su nombre.</summary>
    public async Task<DispositivoDto> RegistrarAsync(RegistrarDispositivoRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var ahora = reloj.Ahora;
        var token = clsDispositivo.TokenValido(solicitud.TokenFcm);
        var dispositivo = await dispositivos.ObtenerPorTokenAsync(token, ct);
        object? antes = dispositivo is null ? null : new { dispositivo.Plataforma, dispositivo.Modelo, dispositivo.Activo, dispositivo.UsuarioId };
        if (dispositivo is null)
        {
            dispositivo = clsDispositivo.Registrar(actual.IdObligatorio(), token, solicitud.Plataforma, solicitud.Modelo, ahora);
            dispositivos.Agregar(dispositivo);
        }
        else
        {
            dispositivo.Actualizar(actual.IdObligatorio(), solicitud.Plataforma, solicitud.Modelo, ahora);
        }

        auditor.Registrar(
            "dispositivo.registrar",
            "dispositivos",
            dispositivo.Id,
            detalle: new DetalleAuditoria(antes, new { dispositivo.Plataforma, dispositivo.Modelo, dispositivo.Activo, dispositivo.UsuarioId }));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new DispositivoDto(dispositivo.Id, dispositivo.Plataforma, dispositivo.Modelo, dispositivo.Activo, dispositivo.UltimoUsoAt);
    }

    /// <summary>
    /// Retira el token del usuario (deja de recibir push). Repetirlo, o retirar un token que no existe o es de otro usuario, no hace nada: cerrar la sesion siempre termina bien.
    /// </summary>
    public async Task RetirarAsync(string tokenFcm, CancellationToken ct = default)
    {
        var dispositivo = await dispositivos.ObtenerPorTokenAsync(tokenFcm.Trim(), ct);
        if (dispositivo is null || dispositivo.UsuarioId != actual.Id || !dispositivo.Activo)
        {
            return;
        }

        dispositivo.Desactivar(reloj.Ahora);
        auditor.Registrar(
            "dispositivo.retirar", "dispositivos", dispositivo.Id, detalle: new DetalleAuditoria(new { activo = true }, new { activo = false }));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
    }
}
