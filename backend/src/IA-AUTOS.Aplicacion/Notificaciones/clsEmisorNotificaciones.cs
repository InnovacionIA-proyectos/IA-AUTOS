using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Notificaciones;

/// <summary>
/// Una alerta para varios destinatarios. <c>ClaveBase</c> identifica el hecho (por ejemplo «novedad_conductor:{hallazgo}»): con el destinatario y el canal forma la clave que impide
/// repetir la misma alerta.
/// </summary>
public sealed record clsAlerta(
    string Tipo,
    string Urgencia,
    IReadOnlyCollection<Guid> Destinatarios,
    Guid? ClienteId,
    Guid? ViajeId,
    Guid? VehiculoId,
    Guid? HallazgoId,
    Guid? InspeccionId,
    string Titulo,
    string Mensaje,
    string ClaveBase);

/// <summary>
/// Crea las notificaciones de una alerta: una por destinatario en la bandeja y una por destinatario por push (FCM a todos sus dispositivos activos). El envio nunca rompe la peticion que
/// lo origino: si FCM falla, la notificacion de push queda guardada con su <c>ErrorEnvio</c>. Una alerta con la misma clave no se repite. Quien lo llama ya confirmo lo suyo: aqui se
/// guardan solo las notificaciones.
/// </summary>
public sealed class clsEmisorNotificaciones(
    INotificacionRepository notificaciones,
    IDispositivoRepository dispositivos,
    IEnviadorPush enviador,
    IUnidadDeTrabajo unidadDeTrabajo,
    IReloj reloj,
    ILogger<clsEmisorNotificaciones> registro)
{
    public const string SIN_DISPOSITIVOS = "El usuario no tiene dispositivos activos para recibir el push";

    public async Task EmitirAsync(clsAlerta alerta, CancellationToken ct = default)
    {
        try
        {
            await CrearAsync(alerta, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una falla al avisar no deshace el hecho que se avisa (la novedad ya quedo guardada): se registra y la peticion sigue.
            unidadDeTrabajo.DescartarCambios();
            registro.LogError(ex, "No se pudieron crear las notificaciones de {Tipo} ({Clave})", alerta.Tipo, alerta.ClaveBase);
        }
    }

    private async Task CrearAsync(clsAlerta alerta, CancellationToken ct)
    {
        var destinatarios = alerta.Destinatarios.Distinct().ToList();
        if (destinatarios.Count == 0)
        {
            return;
        }

        var existentes = await notificaciones.ClavesExistentesAsync(
            destinatarios.SelectMany(d => new[] { Clave(alerta, d, clsCanalNotificacion.BANDEJA), Clave(alerta, d, clsCanalNotificacion.PUSH) }).ToList(), ct);
        var porUsuario = (await dispositivos.ObtenerActivosDeUsuariosAsync(destinatarios, ct)).ToLookup(d => d.UsuarioId);
        var ahora = reloj.Ahora;
        var creadas = false;

        foreach (var destinatario in destinatarios)
        {
            var claveBandeja = Clave(alerta, destinatario, clsCanalNotificacion.BANDEJA);
            var bandeja = existentes.Contains(claveBandeja) ? null : Crear(alerta, destinatario, clsCanalNotificacion.BANDEJA, ahora, null, claveBandeja, ahora);
            if (bandeja is not null)
            {
                notificaciones.Agregar(bandeja);
                creadas = true;
            }

            var clavePush = Clave(alerta, destinatario, clsCanalNotificacion.PUSH);
            if (existentes.Contains(clavePush))
            {
                continue;
            }

            var datos = new Dictionary<string, string> { ["tipo"] = alerta.Tipo };
            if (bandeja is not null)
            {
                datos["notificacion_id"] = bandeja.Id.ToString();
            }

            if (alerta.HallazgoId is { } hallazgo)
            {
                datos["hallazgo_id"] = hallazgo.ToString();
            }

            if (alerta.ViajeId is { } viaje)
            {
                datos["viaje_id"] = viaje.ToString();
            }

            var error = await EnviarPushAsync(porUsuario[destinatario].ToList(), new clsMensajePush(alerta.Titulo, alerta.Mensaje, datos), ahora, ct);
            notificaciones.Agregar(Crear(alerta, destinatario, clsCanalNotificacion.PUSH, error is null ? ahora : null, error, clavePush, ahora));
            creadas = true;
        }

        if (creadas)
        {
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }
    }

    /// <summary>Envia el push a cada dispositivo activo del usuario. Devuelve nulo si llego a alguno y, si a ninguno, el motivo; un token que FCM ya no reconoce se desactiva.</summary>
    private async Task<string?> EnviarPushAsync(IReadOnlyList<clsDispositivo> dispositivosDelUsuario, clsMensajePush mensaje, DateTimeOffset ahora, CancellationToken ct)
    {
        if (dispositivosDelUsuario.Count == 0)
        {
            return SIN_DISPOSITIVOS;
        }

        var errores = new List<string>();
        var llego = false;
        foreach (var dispositivo in dispositivosDelUsuario)
        {
            clsResultadoPush resultado;
            try
            {
                resultado = await enviador.EnviarAsync(dispositivo.TokenFcm, mensaje, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registro.LogWarning(ex, "Fallo el envio del push al dispositivo {Dispositivo}", dispositivo.Id);
                resultado = clsResultadoPush.Fallo("No se pudo enviar el push");
            }

            if (resultado.Enviado)
            {
                llego = true;
                continue;
            }

            errores.Add(resultado.Error ?? "No se pudo enviar el push");
            if (resultado.TokenInvalido)
            {
                dispositivo.Desactivar(ahora);
            }
        }

        return llego ? null : string.Join("; ", errores.Distinct());
    }

    private static clsNotificacion Crear(clsAlerta a, Guid destinatario, string canal, DateTimeOffset? enviadaAt, string? error, string clave, DateTimeOffset ahora) =>
        clsNotificacion.Crear(
            a.Tipo, a.Urgencia, destinatario, a.ClienteId, a.ViajeId, a.VehiculoId, a.HallazgoId, a.InspeccionId, a.Titulo, a.Mensaje, canal, enviadaAt, error, clave, ahora);

    private static string Clave(clsAlerta alerta, Guid destinatario, string canal) => $"{alerta.ClaveBase}:{destinatario}:{canal}";
}
