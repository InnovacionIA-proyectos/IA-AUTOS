using FluentValidation;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Notificaciones.DTONotificaciones;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Notificaciones;

/// <summary>Consultas de solo lectura de la bandeja de notificaciones.</summary>
public interface IConsultasNotificaciones
{
    /// <summary>
    /// Una pagina de la bandeja (las notificaciones del canal bandeja, las mas recientes primero). Con <paramref name="soloDeUsuario"/>, solo las de ese destinatario; sin el, las de todos. El
    /// ultimo valor es cuantas hay sin leer en ese alcance, sin contar los filtros.
    /// </summary>
    Task<(IReadOnlyList<NotificacionDto> Filas, int Total, int NoLeidas)> ListarAsync(
        FiltrosNotificaciones filtros, Guid? soloDeUsuario, int desplazamiento, int tamano, CancellationToken ct = default);

    Task<NotificacionDto?> ObtenerAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// La bandeja de notificaciones: cada usuario ve las suyas (el super administrador ve las de todos los clientes), las marca leidas y el coordinador de calidad y los administradores responden
/// la novedad del conductor, que llega al conductor del viaje.
/// </summary>
public sealed class clsNotificacionesService(
    INotificacionRepository notificaciones,
    IConsultasNotificaciones consultas,
    IViajeRepository viajes,
    clsEmisorNotificaciones emisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<ResponderNotificacionRequest> validadorRespuesta)
{
    public const int TAMANO_POR_DEFECTO = 50;
    public const int TAMANO_MAXIMO = 200;

    public async Task<ListadoNotificacionesDto> ListarAsync(FiltrosNotificacionesRequest solicitud, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        bool? leida = null;
        if (!string.IsNullOrWhiteSpace(solicitud.Leida))
        {
            if (bool.TryParse(solicitud.Leida, out var valor))
            {
                leida = valor;
            }
            else
            {
                errores["leida"] = ["Debe ser true o false"];
            }
        }

        var tipo = clsParseoFiltros.Catalogo(solicitud.Tipo, clsTipoNotificacion.VALORES, "tipo", errores);
        var urgencia = clsParseoFiltros.Catalogo(solicitud.Urgencia, clsUrgenciaNotificacion.VALORES, "urgencia", errores);
        if (errores.Count > 0)
        {
            throw new clsValidacionException(
                "Datos inválidos",
                new Dictionary<string, object> { ["formErrors"] = Array.Empty<string>(), ["fieldErrors"] = errores });
        }

        var pagina = Math.Max(solicitud.Pagina ?? 1, 1);
        var tamano = Math.Clamp(solicitud.Tamano ?? TAMANO_POR_DEFECTO, 1, TAMANO_MAXIMO);
        var (filas, total, noLeidas) = await consultas.ListarAsync(
            new FiltrosNotificaciones(leida, tipo, urgencia), SoloDeUsuario(), (pagina - 1) * tamano, tamano, ct);
        return new ListadoNotificacionesDto(filas, total, pagina, tamano, noLeidas);
    }

    /// <summary>Marca la notificacion como leida (solo su destinatario). Leerla otra vez no cambia la primera lectura.</summary>
    public async Task<NotificacionDto> MarcarLeidaAsync(Guid id, CancellationToken ct = default)
    {
        var notificacion = await ObtenerDeBandejaAsync(id, ct);
        if (notificacion.UsuarioId != actual.Id)
        {
            throw new clsProhibidoException("La notificación es de otro usuario");
        }

        if (!notificacion.EstaLeida)
        {
            notificacion.MarcarLeida(reloj.Ahora);
            auditor.Registrar(
                "notificacion.leer",
                "notificaciones",
                notificacion.Id,
                detalle: new DetalleAuditoria(new { leida = false }, new { leida = true, notificacion.LeidaAt }),
                viajeId: notificacion.ViajeId,
                clienteId: notificacion.ClienteId);
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }

        return await consultas.ObtenerAsync(id, ct) ?? throw new clsEntidadNoEncontradaException($"Notificación {id} no encontrada");
    }

    /// <summary>
    /// Responde la novedad del conductor: la respuesta queda en la novedad (la ven todos los coordinadores y administradores de su cliente) y le llega al conductor del viaje. Solo la primera
    /// respuesta se guarda: la segunda recibe 409. 403 si la novedad es de otro cliente; 409 si la notificacion no es de una novedad del conductor.
    /// </summary>
    public async Task<NotificacionDto> ResponderAsync(Guid id, ResponderNotificacionRequest solicitud, CancellationToken ct = default)
    {
        await validadorRespuesta.ValidarOLanzarAsync(solicitud, ct);
        var notificacion = await ObtenerDeBandejaAsync(id, ct);
        if (actual.ClienteRestringido() is { } cliente && notificacion.ClienteId != cliente)
        {
            throw new clsProhibidoException("La novedad es de otro cliente");
        }

        if (notificacion.Tipo != clsTipoNotificacion.NOVEDAD_CONDUCTOR || notificacion.HallazgoId is not { } hallazgoId)
        {
            throw new clsConflictoException("Solo se responde una notificación de novedad del conductor");
        }

        if (await notificaciones.NovedadRespondidaAsync(hallazgoId, ct))
        {
            throw new clsConflictoException("La novedad ya fue respondida");
        }

        var texto = clsNotificacion.TextoDeRespuesta(solicitud.Texto);
        var ahora = reloj.Ahora;
        // La primera respuesta gana aunque dos coordinadores respondan a la vez: el cambio solo toca las filas aun sin respuesta.
        if (await notificaciones.ResponderNovedadAsync(hallazgoId, texto, solicitud.Medio!, actual.IdObligatorio(), ahora, ct) == 0)
        {
            throw new clsConflictoException("La novedad ya fue respondida");
        }

        auditor.Registrar(
            "notificacion.responder",
            "notificaciones",
            notificacion.Id,
            detalle: new DetalleAuditoria(new { respondida = false }, new { respondida = true, texto, medio = solicitud.Medio, hallazgo_id = hallazgoId }),
            viajeId: notificacion.ViajeId,
            clienteId: notificacion.ClienteId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);

        var viaje = notificacion.ViajeId is { } viajeId ? await viajes.ObtenerPorIdAsync(viajeId, ct) : null;
        if (viaje?.ConductorId is { } conductor)
        {
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.RESPUESTA_CONDUCTOR,
                    clsUrgenciaNotificacion.ALTA,
                    [conductor],
                    notificacion.ClienteId,
                    notificacion.ViajeId,
                    notificacion.VehiculoId,
                    hallazgoId,
                    notificacion.InspeccionId,
                    "Respuesta a tu novedad",
                    texto,
                    $"{clsTipoNotificacion.RESPUESTA_CONDUCTOR}:{hallazgoId}"),
                ct);
        }

        return await consultas.ObtenerAsync(id, ct) ?? throw new clsEntidadNoEncontradaException($"Notificación {id} no encontrada");
    }

    /// <summary>El super administrador ve las notificaciones de todos los clientes; el resto, solo las suyas.</summary>
    private Guid? SoloDeUsuario() => actual.Rol == clsRol.SUPER_ADMIN ? null : actual.IdObligatorio();

    /// <summary>La notificacion de la bandeja (404 si no existe o es de otro canal: las de push solo registran el envio).</summary>
    private async Task<clsNotificacion> ObtenerDeBandejaAsync(Guid id, CancellationToken ct)
    {
        var notificacion = await notificaciones.ObtenerPorIdAsync(id, ct);
        return notificacion is { Canal: clsCanalNotificacion.BANDEJA }
            ? notificacion
            : throw new clsEntidadNoEncontradaException($"Notificación {id} no encontrada");
    }
}
