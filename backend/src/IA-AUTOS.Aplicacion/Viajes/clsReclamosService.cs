using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// El inspector reclama el camion que va a revisar («revisar este»): el cargue de HOY de uno de sus puntos o la entrega de un viaje que llega a uno de sus puntos. Reclamar llena el inspector del viaje (en el
/// cargue) y deja el reclamo activo; si dos inspectores reclaman a la vez gana uno y el otro recibe 409 «Ya fue tomado por otro inspector» (lo decide un indice unico de la base). Liberar lo hace una persona:
/// el inspector duenyo, el coordinador o un administrador; el viaje queda sin inspector y lo capturado conserva su autor. No hay caducidad automatica. Reclamar y liberar quedan en la auditoria.
/// </summary>
public sealed class clsReclamosService(
    IReclamoRepository reclamos,
    IViajeRepository viajes,
    IUsuarioRepository usuarios,
    IConsultasViajes consultas,
    IConsultasPendientes pendientes,
    clsAccesoViajes acceso,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj)
{
    private static readonly TimeSpan COLOMBIA = TimeSpan.FromHours(-5);

    /// <summary>
    /// La lista de pendientes del inspector: los cargues programados para HOY en sus puntos y las entregas de viajes en curso que llegan a sus puntos, cada uno con su estado: <c>disponible</c>, <c>en_revision</c> (con el nombre del
    /// otro inspector) o <c>mio</c>. Los cargues de otros dias y de otros puntos no salen. Un usuario sin puntos recibe la lista vacia.
    /// </summary>
    public async Task<IReadOnlyList<PendienteDto>> ListarPendientesAsync(CancellationToken ct = default)
    {
        var usuarioId = actual.IdObligatorio();
        var puntos = await reclamos.LugaresDelInspectorAsync(usuarioId, ct);
        if (puntos.Count == 0)
        {
            return [];
        }

        var inicioDelDia = new DateTimeOffset(acceso.Hoy().ToDateTime(TimeOnly.MinValue), COLOMBIA);
        return await pendientes.ListarAsync(usuarioId, puntos, inicioDelDia, inicioDelDia.AddDays(1), ct);
    }

    /// <summary>
    /// Reclama un cargue pendiente de hoy en un punto del inspector o la entrega de un viaje que llega a un punto suyo. Reclamar de nuevo lo que ya es suyo no cambia nada. Devuelve el reclamo y si se creo ahora.
    /// 403 si el viaje no es de ninguno de sus puntos; 409 si el viaje no esta activo, el cargue no es de hoy o ya lo tomo otro inspector.
    /// </summary>
    public async Task<(ReclamoDto Reclamo, bool Creado)> ReclamarAsync(Guid viajeId, ReclamarViajeRequest solicitud, CancellationToken ct = default)
    {
        var proceso = clsValidador.Opcional(solicitud.Proceso);
        if (proceso is null || !clsProcesoReclamo.VALORES.Contains(proceso))
        {
            throw clsErroresCampos.De("proceso", $"Proceso inválido. Permitidos: {string.Join(", ", clsProcesoReclamo.VALORES.Order())}");
        }

        var viaje = await ObtenerViajeAsync(viajeId, ct);
        viaje.AsegurarActivo();
        var inspectorId = actual.IdObligatorio();
        var puntos = await reclamos.LugaresDelInspectorAsync(inspectorId, ct);
        if (proceso == clsProcesoReclamo.CARGUE)
        {
            if (viaje.LugarCargueId is not { } lugarCargue || !puntos.Contains(lugarCargue))
            {
                throw new clsProhibidoException("El cargue no es de ninguno de sus puntos");
            }

            if (viaje.FechaProgramada is not { } fecha || DateOnly.FromDateTime(fecha.ToOffset(COLOMBIA).DateTime) != Hoy())
            {
                throw new clsConflictoException("El cargue no está programado para hoy");
            }
        }
        else
        {
            var destinos = (await viajes.ObtenerVehiculosAsync(viajeId, ct)).Where(v => !v.NoSale && v.LugarDestinoId is not null).Select(v => v.LugarDestinoId!.Value);
            if (!destinos.Any(puntos.Contains))
            {
                throw new clsProhibidoException("La entrega del viaje no llega a ninguno de sus puntos");
            }
        }

        var activo = await reclamos.ObtenerActivoAsync(viajeId, proceso, ct);
        if (activo is not null)
        {
            return activo.InspectorId == inspectorId ? (await ADtoAsync(activo, ct), false) : throw new clsConflictoException("Ya fue tomado por otro inspector");
        }

        var ahora = reloj.Ahora;
        var reclamo = clsReclamo.Crear(viajeId, inspectorId, proceso, ahora);
        reclamos.Agregar(reclamo);
        var antesViaje = viaje.InspectorId;
        if (proceso == clsProcesoReclamo.CARGUE)
        {
            viaje.AsignarInspector(inspectorId, ahora);
        }

        await auditor.RegistrarDeViajeAsync(
            "viaje.reclamar", "reclamos", reclamo.Id, viaje.Id, new DetalleAuditoria(new { inspector_id = antesViaje }, new { reclamo_id = reclamo.Id, proceso, inspector_id = inspectorId }), ct: ct);

        // Dos reclamos simultaneos: el indice unico de reclamos activos deja pasar uno y el otro se traduce a 409 al guardar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return (await ADtoAsync(reclamo, ct), true);
    }

    /// <summary>
    /// Libera el reclamo activo del viaje y proceso. Lo libera el inspector duenyo, el coordinador de calidad o un administrador (403 cualquier otro). El viaje queda sin inspector (si el reclamo era del
    /// cargue y el inspector del viaje era ese), el cargue vuelve a estar pendiente y lo que el inspector capturo conserva su autor. 404 si no hay un reclamo activo.
    /// </summary>
    public async Task<ReclamoDto> LiberarAsync(Guid viajeId, string proceso, CancellationToken ct = default)
    {
        if (!clsProcesoReclamo.VALORES.Contains(proceso))
        {
            throw clsErroresCampos.De("proceso", $"Proceso inválido. Permitidos: {string.Join(", ", clsProcesoReclamo.VALORES.Order())}");
        }

        var viaje = await ObtenerViajeAsync(viajeId, ct);
        await AsegurarAlcanceAsync(viaje, ct);
        var reclamo = await reclamos.ObtenerActivoAsync(viajeId, proceso, ct)
            ?? throw new clsEntidadNoEncontradaException("El viaje no tiene un reclamo activo de ese proceso");
        var actorId = actual.IdObligatorio();
        if (actual.Rol == clsRol.INSPECTOR && reclamo.InspectorId != actorId)
        {
            throw new clsProhibidoException("Solo el inspector que lo reclamó, el coordinador o un administrador liberan el reclamo");
        }

        var ahora = reloj.Ahora;
        reclamo.Liberar(actorId, ahora);
        if (proceso == clsProcesoReclamo.CARGUE && viaje.InspectorId == reclamo.InspectorId)
        {
            viaje.AsignarInspector(null, ahora);
        }

        await auditor.RegistrarDeViajeAsync(
            "viaje.liberar_reclamo", "reclamos", reclamo.Id, viaje.Id,
            new DetalleAuditoria(new { reclamo_id = reclamo.Id, proceso, inspector_id = reclamo.InspectorId }, new { liberado_por = actorId, liberado_at = ahora }), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ADtoAsync(reclamo, ct);
    }

    /// <summary>Los reclamos del viaje (activos y liberados), del mas reciente al mas antiguo: queda registrado quien reclamo y quien libero. Inspector y conductor solo ven los de sus viajes o los que reclamaron.</summary>
    public async Task<IReadOnlyList<ReclamoDto>> ListarAsync(Guid viajeId, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        var delViaje = await reclamos.ObtenerDeViajeAsync(viajeId, ct);
        if (actual.SoloSusViajes() is { } usuarioId)
        {
            if (!viaje.Participa(usuarioId) && !delViaje.Any(r => r.InspectorId == usuarioId) && !await acceso.PuedeVerAsync(viaje, usuarioId, ct))
            {
                throw new clsProhibidoException("El viaje no está asignado a su usuario");
            }
        }
        else
        {
            await AsegurarAlcanceAsync(viaje, ct);
        }

        var dtos = new List<ReclamoDto>(delViaje.Count);
        foreach (var reclamo in delViaje)
        {
            dtos.Add(await ADtoAsync(reclamo, ct));
        }

        return dtos;
    }

    // ----------------------------------------------------------------------------------------------- internos

    private DateOnly Hoy() => acceso.Hoy();

    private async Task<clsViaje> ObtenerViajeAsync(Guid viajeId, CancellationToken ct) =>
        await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");

    /// <summary>Quien administra viajes sin ser super administrador solo toca los viajes con unidades de su cliente (403).</summary>
    private async Task AsegurarAlcanceAsync(clsViaje viaje, CancellationToken ct)
    {
        if (actual.Rol != clsRol.INSPECTOR && actual.ClienteRestringido() is { } cliente && !await consultas.ViajeEsDeClienteAsync(viaje.Id, cliente, ct))
        {
            throw new clsProhibidoException("El viaje no tiene unidades de su cliente");
        }
    }

    private async Task<ReclamoDto> ADtoAsync(clsReclamo r, CancellationToken ct)
    {
        var inspector = await usuarios.ObtenerPorIdAsync(r.InspectorId, ct);
        var liberador = r.LiberadoPor is { } id ? await usuarios.ObtenerPorIdAsync(id, ct) : null;
        return new ReclamoDto(r.Id, r.ViajeId, r.Proceso, r.InspectorId, inspector?.Nombre, r.ReclamadoAt, r.EstaActivo, r.LiberadoAt, r.LiberadoPor, liberador?.Nombre);
    }
}
