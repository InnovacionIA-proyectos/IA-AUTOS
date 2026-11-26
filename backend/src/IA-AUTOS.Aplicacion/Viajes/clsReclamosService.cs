using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// El inspector reclama el camion que va a revisar («revisar este»): el cargue de HOY de uno de sus puntos o la entrega de UNA PARADA de un viaje que llega a uno de sus puntos (cada parada se toma por separado: otra parada del
/// mismo viaje la toma su inspector). Reclamar llena el inspector del viaje (en el cargue) y deja el reclamo activo; si dos inspectores reclaman a la vez gana uno y el otro recibe 409 «Ya fue tomado por otro inspector» (lo decide
/// un indice unico de la base). Liberar lo hace una persona: el inspector duenyo, el coordinador o un administrador; el viaje queda sin inspector y lo capturado conserva su autor. No hay caducidad automatica. Reclamar y
/// liberar quedan en la auditoria.
/// </summary>
public sealed class clsReclamosService(
    IReclamoRepository reclamos,
    IEsperaInspectorPuntoRepository esperas,
    IViajeRepository viajes,
    IUsuarioRepository usuarios,
    IConsultasViajes consultas,
    IConsultasPendientes pendientes,
    ILugarRepository lugares,
    clsAccesoViajes acceso,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    clsConfirmacionEnSitioService confirmacionEnSitio)
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
    /// Reclama un cargue pendiente de hoy en un punto del inspector o la entrega de UNA PARADA (<c>lugar_id</c>, obligatorio: 400 sin el) de un viaje que llega a un punto suyo. Reclamar de nuevo lo que ya es suyo no cambia nada.
    /// Devuelve el reclamo y si se creo ahora. 403 si el viaje o la parada no es de ninguno de sus puntos; 404 si la parada no tiene carros activos por entregar de ese viaje; 409 si el viaje no esta activo, el cargue no es de hoy o
    /// ya lo tomo otro inspector (en la entrega, con el motivo <c>entrega_tomada</c>). Con la posicion del inspector (<c>lat</c>, <c>lng</c>, <c>precision</c>) la entrega se compara con el lugar de la parada con la regla de
    /// las fotos: fuera de rango avisa al coordinador (urgencia alta) pero el reclamo queda creado en todos los casos; el resultado queda en la auditoria. 400 con una posicion invalida.
    /// </summary>
    public async Task<(ReclamoDto Reclamo, bool Creado)> ReclamarAsync(Guid viajeId, ReclamarViajeRequest solicitud, CancellationToken ct = default)
    {
        var proceso = clsValidador.Opcional(solicitud.Proceso);
        if (proceso is null || !clsProcesoReclamo.VALORES.Contains(proceso))
        {
            throw clsErroresCampos.De("proceso", $"Proceso inválido. Permitidos: {string.Join(", ", clsProcesoReclamo.VALORES.Order())}");
        }

        ValidarPosicion(solicitud);
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        viaje.AsegurarActivo();
        var inspectorId = actual.IdObligatorio();
        var puntos = await reclamos.LugaresDelInspectorAsync(inspectorId, ct);
        Guid? lugarEntrega = null;
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
            if (solicitud.LugarId is not { } parada || parada == Guid.Empty)
            {
                throw clsErroresCampos.De("lugar_id", "La entrega se reclama por parada: indique el lugar de destino (lugar_id)");
            }

            lugarEntrega = parada;
            if (!puntos.Contains(parada))
            {
                throw new clsProhibidoException("La parada no es de ninguno de sus puntos", clsMotivosRechazo.NO_ES_SU_PUNTO);
            }

            // Una entrega que el conductor cerro con el plan B, sin inspector del punto, es definitiva: el inspector que llega despues recibe el rechazo que se lo dice y nada se reabre.
            if (await esperas.ObtenerUltimaAsync(viajeId, parada, ct) is { Estado: clsEstadoEspera.CERRADA_SIN_INSPECTOR_DEL_PUNTO } cerrada)
            {
                throw CerradaSinInspector(cerrada);
            }

            if (!(await viajes.ObtenerVehiculosAsync(viajeId, ct)).Any(v => v.EstaActiva && v.LugarDestinoId == parada) || await pendientes.UnidadesPorEntregarAsync(viajeId, parada, ct) == 0)
            {
                throw new clsEntidadNoEncontradaException("La parada no tiene carros activos por entregar en este viaje");
            }
        }

        var activo = await reclamos.ObtenerActivoAsync(viajeId, proceso, lugarEntrega, ct);
        if (activo is not null)
        {
            return activo.InspectorId == inspectorId
                ? (await ADtoAsync(activo, ct), false)
                : throw new clsConflictoException("Ya fue tomado por otro inspector", proceso == clsProcesoReclamo.ENTREGA ? clsMotivosRechazo.ENTREGA_TOMADA : null);
        }

        var ahora = reloj.Ahora;
        var reclamo = clsReclamo.Crear(viajeId, inspectorId, proceso, ahora, lugarEntrega);
        reclamos.Agregar(reclamo);
        var antesViaje = viaje.InspectorId;

        // Si el conductor esperaba al inspector de esta parada, la espera termina como «el inspector llego»: se guarda junto con el reclamo, y si el conductor cierra con el plan B a la vez gana uno solo.
        clsEsperaInspectorPunto? espera = null;
        if (proceso == clsProcesoReclamo.ENTREGA && lugarEntrega is { } paradaEnEspera && await esperas.ObtenerAbiertaAsync(viajeId, paradaEnEspera, ct) is { } abierta)
        {
            espera = abierta;
            var antesEspera = clsInstantaneasAuditoria.De(espera);
            espera.RegistrarLlegadaDelInspector(inspectorId, ahora);
            await auditor.RegistrarDeViajeAsync(
                "espera_inspector_punto.inspector_llego", "esperas_inspector_punto", espera.Id, viaje.Id, new DetalleAuditoria(antesEspera, clsInstantaneasAuditoria.De(espera)), ct: ct);
        }

        // La posicion del inspector solo cuenta en la entrega (el cargue no cambia) y nunca bloquea: solo se compara con el lugar de la parada.
        EvaluacionDeEntrega? evaluacion = null;
        if (proceso == clsProcesoReclamo.ENTREGA && lugarEntrega is { } paradaTomada && solicitud.Lat is { } lat && solicitud.Lng is { } lng)
        {
            evaluacion = await confirmacionEnSitio.EvaluarEntregaAsync(paradaTomada, lat, lng, solicitud.Precision, ct);
        }

        if (proceso == clsProcesoReclamo.CARGUE)
        {
            viaje.AsignarInspector(inspectorId, ahora);
        }

        await auditor.RegistrarDeViajeAsync(
            "viaje.reclamar", "reclamos", reclamo.Id, viaje.Id, new DetalleAuditoria(
                new { inspector_id = antesViaje },
                new
                {
                    reclamo_id = reclamo.Id,
                    proceso,
                    lugar_id = lugarEntrega,
                    inspector_id = inspectorId,
                    confirmacion_sitio = evaluacion is null ? null : new { resultado = evaluacion.Resultado, distancia_m = evaluacion.DistanciaM, radio_m = evaluacion.RadioM, precision_m = evaluacion.PrecisionM },
                }),
            ct: ct);

        // Dos reclamos simultaneos: el indice unico de reclamos activos deja pasar uno y el otro se traduce a 409 al guardar.
        try
        {
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }
        catch (clsConflictoException) when (proceso == clsProcesoReclamo.ENTREGA)
        {
            // Si perdio contra el cierre con el plan B del conductor, el motivo es ese; si perdio contra otro inspector, que la tomo primero.
            unidadDeTrabajo.DescartarCambios();
            if (espera is not null && lugarEntrega is { } paradaPerdida && await esperas.ObtenerUltimaAsync(viajeId, paradaPerdida, ct) is { Estado: clsEstadoEspera.CERRADA_SIN_INSPECTOR_DEL_PUNTO } cerradaAntes)
            {
                throw CerradaSinInspector(cerradaAntes);
            }

            throw new clsConflictoException("Ya fue tomado por otro inspector", clsMotivosRechazo.ENTREGA_TOMADA);
        }

        if (evaluacion is { Resultado: clsResultadoConfirmacionSitio.FUERA_DE_RANGO } && lugarEntrega is { } paradaFuera)
        {
            await confirmacionEnSitio.AvisarEntregaFueraDeRangoAsync(reclamo.Id, viaje, paradaFuera, inspectorId, evaluacion, ct);
        }

        return (await ADtoAsync(reclamo, ct), true);
    }

    /// <summary>409 <c>entrega_cerrada_sin_inspector</c> con la hora del cierre (en la hora de Colombia, y completa en <c>details.cerrada_at</c>).</summary>
    private static clsConflictoException CerradaSinInspector(clsEsperaInspectorPunto espera)
    {
        var cierre = espera.CerradaAt ?? espera.UpdatedAt;
        return new clsConflictoException(
            $"Esta entrega se cerró sin inspector del punto a las {cierre.ToOffset(COLOMBIA).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)}: el cierre es definitivo",
            clsMotivosRechazo.ENTREGA_CERRADA_SIN_INSPECTOR,
            new Dictionary<string, object> { ["cerrada_at"] = cierre });
    }

    /// <summary>La posicion del inspector es opcional, pero va completa: la latitud y la longitud juntas, dentro de su rango, y una precision en metros no negativa. 400 en el campo que no cumple.</summary>
    private static void ValidarPosicion(ReclamarViajeRequest solicitud)
    {
        var errores = new Dictionary<string, string[]>();
        if ((solicitud.Lat is null) != (solicitud.Lng is null))
        {
            errores[solicitud.Lat is null ? "lat" : "lng"] = ["La latitud y la longitud van juntas: envíe las dos o ninguna"];
        }

        if (solicitud.Lat is { } lat && (!double.IsFinite(lat) || lat is < -90 or > 90))
        {
            errores["lat"] = ["La latitud debe estar entre -90 y 90"];
        }

        if (solicitud.Lng is { } lng && (!double.IsFinite(lng) || lng is < -180 or > 180))
        {
            errores["lng"] = ["La longitud debe estar entre -180 y 180"];
        }

        if (solicitud.Precision is { } precision && (!double.IsFinite(precision) || precision < 0))
        {
            errores["precision"] = ["La precisión debe ser un número en metros mayor o igual a 0"];
        }

        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }
    }

    /// <summary>
    /// Libera el reclamo activo del viaje y proceso (en la entrega, el de la parada <paramref name="lugarId"/>, obligatoria: 400 sin ella). Lo libera el inspector duenyo, el coordinador de calidad o un administrador (403 cualquier
    /// otro). El viaje queda sin inspector (si el reclamo era del cargue y el inspector del viaje era ese), el cargue vuelve a estar pendiente y lo que el inspector capturo conserva su autor. 404 si no hay un reclamo activo.
    /// </summary>
    public async Task<ReclamoDto> LiberarAsync(Guid viajeId, string proceso, Guid? lugarId = null, CancellationToken ct = default)
    {
        if (!clsProcesoReclamo.VALORES.Contains(proceso))
        {
            throw clsErroresCampos.De("proceso", $"Proceso inválido. Permitidos: {string.Join(", ", clsProcesoReclamo.VALORES.Order())}");
        }

        if (proceso == clsProcesoReclamo.ENTREGA && lugarId is null)
        {
            throw clsErroresCampos.De("lugar_id", "La entrega se libera por parada: indique el lugar de destino (lugar_id)");
        }

        var viaje = await ObtenerViajeAsync(viajeId, ct);
        await AsegurarAlcanceAsync(viaje, ct);
        var reclamo = await reclamos.ObtenerActivoAsync(viajeId, proceso, proceso == clsProcesoReclamo.ENTREGA ? lugarId : null, ct)
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
            new DetalleAuditoria(new { reclamo_id = reclamo.Id, proceso, lugar_id = reclamo.LugarId, inspector_id = reclamo.InspectorId }, new { liberado_por = actorId, liberado_at = ahora }), ct: ct);
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
        var lugar = r.LugarId is { } lugarId ? (await lugares.ObtenerAsync(lugarId, ct))?.Nombre : null;
        return new ReclamoDto(r.Id, r.ViajeId, r.Proceso, r.InspectorId, inspector?.Nombre, r.ReclamadoAt, r.EstaActivo, r.LiberadoAt, r.LiberadoPor, liberador?.Nombre, r.LugarId, lugar);
    }
}
