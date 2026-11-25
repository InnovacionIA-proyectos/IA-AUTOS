using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// La espera del inspector del punto dentro de la entrega por parada: el conductor la activa en una parada con inspectores donde nadie tomo la entrega, desde su hora corren los plazos que TI ajusta (el aviso al coordinador a
/// los X minutos y el plan B a los X mas Y), un inspector que toma la entrega la termina y, cumplidos los plazos, el conductor cierra la parada sin inspector del punto. Los plazos se evaluan dentro de cada peticion que los
/// necesita, sin esperar a ningun proceso periodico.
/// </summary>
public sealed partial class clsEntregasService
{
    private static readonly TimeSpan COLOMBIA = TimeSpan.FromHours(-5);
    private const int LIMITE_AVISOS_POR_EJECUCION = 200;

    // ----------------------------------------------------------------------------------------------- activar

    /// <summary>
    /// El conductor del viaje activa «No hay inspector» en una parada: guarda el motivo, la hora y el GPS de su celular y empieza a correr la espera. 403 si no es el conductor del viaje; 409 si el viaje no esta activo o con el
    /// motivo <c>parada_sin_inspectores</c> (la parada no tiene inspectores), <c>entrega_tomada</c> (un inspector ya la tomo), <c>entrega_cerrada</c> (ya se entrego) o <c>sin_llegue_en_parada</c> (ningun carro de la parada tiene
    /// «Llegue»); 400 con el motivo o la hora invalidos. Activar de nuevo con la espera abierta devuelve esa misma sin reiniciar el reloj. Devuelve la espera y si se creo ahora. Si al llegar ya pasaron los minutos del aviso, el
    /// coordinador lo recibe en esta misma peticion.
    /// </summary>
    public async Task<(EsperaDelPuntoDto Espera, bool Creada)> ActivarSinInspectorAsync(Guid viajeId, Guid lugarId, ActivarSinInspectorRequest solicitud, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        if (actual.Rol != clsRol.CONDUCTOR || viaje.ConductorId != actual.IdObligatorio())
        {
            throw new clsProhibidoException("Solo el conductor del viaje activa la excepción «No hay inspector» en una parada", clsMotivosRechazo.VIAJE_NO_ASIGNADO);
        }

        viaje.AsegurarActivo();
        if (await esperas.ObtenerAbiertaAsync(viajeId, lugarId, ct) is { } existente)
        {
            return (await EsperaADtoAsync(existente, ct), false);
        }

        var errores = new Dictionary<string, string[]>();
        var motivo = await MotivoDeLaEsperaAsync(solicitud, errores, ct);
        var ahora = reloj.Ahora;
        var momento = clsMomentoDeFirmaOpcional.Leer(solicitud.FechaMomento, solicitud.Lat, solicitud.Lng, solicitud.PrecisionM, ahora, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var unidades = await CargarUnidadesAsync(viaje, ct);
        var delPunto = DelPunto(unidades, lugarId);
        if (!await reclamos.LugarTieneInspectoresAsync(lugarId, ct))
        {
            throw new clsConflictoException("Esta parada no tiene inspectores asignados: la entrega la recibe la persona del lugar", clsMotivosRechazo.PARADA_SIN_INSPECTORES);
        }

        if (await reclamos.ObtenerActivoAsync(viajeId, clsProcesoReclamo.ENTREGA, lugarId, ct) is not null)
        {
            throw new clsConflictoException("Un inspector del punto ya tomó la entrega de esta parada: firma con él", clsMotivosRechazo.ENTREGA_TOMADA);
        }

        if (delPunto.All(u => !u.Pendiente))
        {
            throw new clsConflictoException("La entrega de esta parada ya se cerró", clsMotivosRechazo.ENTREGA_CERRADA);
        }

        if (!delPunto.Any(u => u.ConductorLlego))
        {
            throw new clsConflictoException("Marca «Llegué» en esta parada antes de activar «No hay inspector»", clsMotivosRechazo.SIN_LLEGUE_EN_PARADA);
        }

        var espera = clsEsperaInspectorPunto.Activar(
            viajeId, lugarId, viaje.ConductorId!.Value, motivo!, solicitud.MotivoSinInspectorTexto, momento?.Momento ?? ahora, momento?.Lat, momento?.Lng, momento?.PrecisionM, ahora);
        esperas.Agregar(espera);
        var clienteId = delPunto[0].Vehiculo.ClienteId;
        await auditor.RegistrarDeViajeAsync(
            "espera_inspector_punto.activar", "esperas_inspector_punto", espera.Id, viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(espera)), clienteId, ct);

        // Dos activaciones a la vez: el indice unico de la espera abierta deja pasar una y la otra recibe la misma espera.
        try
        {
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }
        catch (clsConflictoException)
        {
            unidadDeTrabajo.DescartarCambios();
            if (await esperas.ObtenerAbiertaAsync(viajeId, lugarId, ct) is { } ganadora)
            {
                return (await EsperaADtoAsync(ganadora, ct), false);
            }

            throw;
        }

        if (ahora >= (await plazos.PlazosAsync(espera.ActivadaAt, ct)).AvisaAt)
        {
            await AvisarEsperaAsync(espera, viaje, delPunto.Count(u => u.Pendiente), clienteId, ct);
        }

        return (await EsperaADtoAsync(espera, ct), true);
    }

    /// <summary>El motivo de la espera: un codigo activo del catalogo <c>motivos-sin-inspector</c>, con su texto solo si el motivo lo pide. Los errores se acumulan por campo.</summary>
    private async Task<string?> MotivoDeLaEsperaAsync(ActivarSinInspectorRequest solicitud, Dictionary<string, string[]> errores, CancellationToken ct)
    {
        var codigo = clsValidador.Opcional(solicitud.MotivoSinInspector);
        if (codigo is null)
        {
            errores["motivo_sin_inspector"] = ["El motivo es obligatorio: use el código de un motivo activo de motivos-sin-inspector"];
            return null;
        }

        var motivo = (await catalogos.ValoresAsync(clsNombresCatalogo.MOTIVOS_SIN_INSPECTOR, ct)).FirstOrDefault(m => m.Codigo == codigo && m.Activo);
        if (motivo is null)
        {
            errores["motivo_sin_inspector"] = ["Motivo inválido: use el código de un motivo activo de motivos-sin-inspector"];
            return null;
        }

        var texto = clsValidador.Opcional(solicitud.MotivoSinInspectorTexto);
        var requiereTexto = motivo.TryGetValue("requiere_texto", out var requiere) && requiere is true;
        if (requiereTexto && texto is null)
        {
            errores["motivo_sin_inspector_texto"] = ["El motivo elegido exige escribir cuál es"];
        }
        else if (!requiereTexto && texto is not null)
        {
            errores["motivo_sin_inspector_texto"] = ["El motivo elegido no admite texto"];
        }
        else if (texto is { Length: > clsEsperaInspectorPunto.TEXTO_MAX })
        {
            errores["motivo_sin_inspector_texto"] = [$"El texto admite máximo {clsEsperaInspectorPunto.TEXTO_MAX} caracteres"];
        }

        return codigo;
    }

    // ---------------------------------------------------------------------------------------------- el plan B

    /// <summary>
    /// Verifica, con los parametros vigentes y la hora del servidor de esta misma peticion, que ya se cumplieron X mas Y minutos desde la hora en que el conductor activo la excepcion: devuelve la espera (el plan B esta
    /// habilitado) o responde 409 <c>plan_b_no_habilitado</c> con los minutos que faltan. Nunca responde «espere» despues del plazo.
    /// </summary>
    private async Task<clsEsperaInspectorPunto> AsegurarPlanBHabilitadoAsync(clsEsperaInspectorPunto espera, CancellationToken ct)
    {
        var habilita = (await plazos.PlazosAsync(espera.ActivadaAt, ct)).HabilitaPlanBAt;
        var ahora = reloj.Ahora;
        if (ahora >= habilita)
        {
            return espera;
        }

        var faltan = Math.Max((int)Math.Ceiling((habilita - ahora).TotalMinutes), 1);
        throw new clsConflictoException(
            $"Todavía no puedes cerrar sin inspector del punto: faltan {faltan} minutos",
            clsMotivosRechazo.PLAN_B_NO_HABILITADO,
            new Dictionary<string, object> { ["faltan_minutos"] = faltan, ["plan_b_desde"] = habilita });
    }

    /// <summary>La espera termina como cerrada sin inspector del punto, con su auditoria (hora de activacion, de habilitacion y del cierre, y quien recibio). No hace nada si la entrega no se cerro con el plan B.</summary>
    private async Task CerrarEsperaAsync(clsEsperaInspectorPunto? espera, clsViaje viaje, Guid lugarId, Guid actorId, string? recibioNombre, DateTimeOffset ahora, Guid clienteId, CancellationToken ct)
    {
        if (espera is null)
        {
            return;
        }

        var antes = clsInstantaneasAuditoria.De(espera);
        var habilita = (await plazos.PlazosAsync(espera.ActivadaAt, ct)).HabilitaPlanBAt;
        espera.CerrarSinInspectorDelPunto(actorId, ahora);
        await auditor.RegistrarDeViajeAsync(
            "espera_inspector_punto.cerrar", "esperas_inspector_punto", espera.Id, viaje.Id,
            new DetalleAuditoria(antes, new { espera = clsInstantaneasAuditoria.De(espera), lugar_id = lugarId, activada_at = espera.ActivadaAt, habilitada_at = habilita, cerrada_at = ahora, recibio = recibioNombre }), clienteId, ct);
    }

    /// <summary>
    /// Guarda la entrega. Si se cerro con el plan B y un inspector tomo la entrega a la vez, la base deja pasar a uno solo: aqui se responde 409 <c>entrega_tomada</c> y nada de la firma se guarda (el conductor la conserva en su
    /// cola para firmar con el inspector).
    /// </summary>
    private async Task GuardarCerrandoEsperaAsync(clsEsperaInspectorPunto? espera, Guid viajeId, Guid lugarId, CancellationToken ct)
    {
        try
        {
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }
        catch (clsConflictoException) when (espera is not null)
        {
            unidadDeTrabajo.DescartarCambios();
            if (await esperas.ObtenerUltimaAsync(viajeId, lugarId, ct) is { Estado: clsEstadoEspera.INSPECTOR_LLEGO })
            {
                throw new clsConflictoException("Mientras se enviaba, un inspector del punto tomó la entrega: firma con él", clsMotivosRechazo.ENTREGA_TOMADA);
            }

            throw;
        }
    }

    // ------------------------------------------------------------------------------------ danos sin calificar

    /// <summary>Los danos sin nivel que el conductor marco en las revisiones de llegada de los carros de la parada, con su foto, por VIN.</summary>
    private async Task<List<DanoPorCalificarDto>> DanosPorCalificarAsync(List<Unidad> delPunto, CancellationToken ct)
    {
        var pendientesDeNivel = new List<(string Vin, clsHallazgo Hallazgo)>();
        foreach (var unidad in delPunto.Where(u => u.Llegada is not null).OrderBy(u => u.Vehiculo.Vin, StringComparer.Ordinal))
        {
            foreach (var hallazgo in await inspecciones.ObtenerHallazgosDeInspeccionAsync(unidad.Llegada!.Id, ct))
            {
                if (hallazgo.Origen == clsOrigenHallazgo.CONDUCTOR && hallazgo.NivelSeveridad is null)
                {
                    pendientesDeNivel.Add((unidad.Vehiculo.Vin, hallazgo));
                }
            }
        }

        if (pendientesDeNivel.Count == 0)
        {
            return [];
        }

        var fotos = await inspecciones.PrimeraFotoDeHallazgosAsync([.. pendientesDeNivel.Select(p => p.Hallazgo.Id)], ct);
        return [.. pendientesDeNivel.Select(p => new DanoPorCalificarDto(p.Hallazgo.Id, p.Vin, p.Hallazgo.ParteVehiculo, p.Hallazgo.ClaseDano, fotos.TryGetValue(p.Hallazgo.Id, out var foto) ? foto : null))];
    }

    /// <summary>
    /// Con la parada tomada por un inspector, la firma por parada y el cierre con una sola firma exigen que ya esten calificados los danos que el conductor marco en las revisiones de llegada de la parada (los del plan B):
    /// 409 <c>danos_sin_calificar</c> con cuantos faltan. Una parada sin revisiones del conductor firma como siempre.
    /// </summary>
    private async Task AsegurarDanosCalificadosAsync(List<Unidad> delPunto, Firmantes firmantes, CancellationToken ct)
    {
        if (!firmantes.ConInspector)
        {
            return;
        }

        var faltan = (await DanosPorCalificarAsync(delPunto, ct)).Count;
        if (faltan > 0)
        {
            throw new clsConflictoException(
                $"Antes de firmar, califica los daños que marcó el conductor en esta parada: falta{(faltan == 1 ? string.Empty : "n")} {faltan}",
                clsMotivosRechazo.DANOS_SIN_CALIFICAR,
                new Dictionary<string, object> { ["faltan"] = faltan });
        }
    }

    // ------------------------------------------------------------------------------------------------ consulta

    /// <summary>La ultima espera de la parada con sus plazos vigentes y el inspector que llego, o nulo si nunca se activo.</summary>
    private async Task<EsperaDelPuntoDto?> EsperaDeLaParadaAsync(Guid viajeId, Guid lugarId, CancellationToken ct) =>
        await esperas.ObtenerUltimaAsync(viajeId, lugarId, ct) is { } espera ? await EsperaADtoAsync(espera, ct) : null;

    private async Task<EsperaDelPuntoDto> EsperaADtoAsync(clsEsperaInspectorPunto espera, CancellationToken ct)
    {
        var plazo = await plazos.PlazosAsync(espera.ActivadaAt, ct);
        var inspector = espera.InspectorId is { } id ? (await usuarios.ObtenerPorIdAsync(id, ct))?.Nombre : null;
        return new EsperaDelPuntoDto(
            espera.Id, espera.ViajeId, espera.LugarId, espera.Estado, espera.ConductorId, espera.MotivoCodigo, espera.MotivoTexto, espera.ActivadaAt, espera.CreatedAt, espera.Lat, espera.Lng, espera.PrecisionM,
            plazo.AvisaAt, espera.AvisoAt, plazo.HabilitaPlanBAt, espera.EstaEsperando && reloj.Ahora >= plazo.HabilitaPlanBAt, espera.InspectorId, inspector, espera.InspectorLlegoAt, espera.CerradaAt);
    }

    // ------------------------------------------------------------------------------------------------ avisos

    /// <summary>
    /// Avisa a los coordinadores de calidad del cliente de que nadie ha tomado la entrega de una parada pasados los minutos del parametro desde que el conductor activo «No hay inspector» (urgencia alta, bandeja y push). Una vez por
    /// espera: la marca queda en la espera y la clave de dedupe lleva su id. Nunca lanza: la espera ya quedo guardada.
    /// </summary>
    private async Task AvisarEsperaAsync(clsEsperaInspectorPunto espera, clsViaje viaje, int unidades, Guid clienteId, CancellationToken ct)
    {
        try
        {
            var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD], clienteId, ct);
            var lugar = (await LugaresAsync(ct)).TryGetValue(espera.LugarId, out var datos) ? datos.Nombre : "el punto de entrega";
            var conductor = (await usuarios.ObtenerPorIdAsync(espera.ConductorId, ct))?.Nombre ?? "El conductor";
            var ahora = reloj.Ahora;
            var planBDesde = (await plazos.PlazosAsync(espera.ActivadaAt, ct)).HabilitaPlanBAt;
            var codigoViaje = viaje.CodigoMtrack is { } codigo ? $" (viaje {codigo})" : string.Empty;
            var desde = espera.ActivadaAt.ToOffset(COLOMBIA).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var habilita = planBDesde.ToOffset(COLOMBIA).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.ESPERA_INSPECTOR_PUNTO,
                    clsUrgenciaNotificacion.ALTA,
                    destinatarios.Select(d => d.Id).ToList(),
                    clienteId,
                    viaje.Id,
                    null,
                    null,
                    null,
                    $"Nadie ha tomado la entrega: {lugar}",
                    $"{conductor} espera al inspector del punto desde las {desde} en {lugar}{codigoViaje} y nadie ha tomado la entrega. El conductor podrá cerrar sin inspector del punto desde las {habilita}.",
                    $"{clsTipoNotificacion.ESPERA_INSPECTOR_PUNTO}:{espera.Id}",
                    new Dictionary<string, object?>
                    {
                        ["hecho"] = "espera_inspector_punto",
                        ["viaje_id"] = viaje.Id,
                        ["lugar_id"] = espera.LugarId,
                        ["lugar"] = lugar,
                        ["activada_at"] = espera.ActivadaAt,
                        ["minutos_esperando"] = Math.Max((int)(ahora - espera.ActivadaAt).TotalMinutes, 0),
                        ["plan_b_desde"] = planBDesde,
                        ["unidades"] = unidades,
                    }),
                ct);
            espera.MarcarAvisada(ahora);
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El aviso es un efecto aparte: la espera ya quedo guardada y no se deshace; si falla, el proceso periodico lo vuelve a intentar.
            unidadDeTrabajo.DescartarCambios();
        }
    }

    /// <summary>
    /// El proceso de cada minuto: avisa de las esperas abiertas que cumplieron los minutos del aviso sin que nadie tomara la entrega y todavia no avisaron. Devuelve cuantas avisos intento.
    /// </summary>
    public async Task<int> AvisarEsperasVencidasAsync(CancellationToken ct = default)
    {
        var avisadas = 0;
        var minutos = await plazos.AvisoMinutosAsync(ct);
        foreach (var espera in await esperas.ObtenerPorAvisarAsync(reloj.Ahora.AddMinutes(-minutos), LIMITE_AVISOS_POR_EJECUCION, ct))
        {
            var viaje = await viajes.ObtenerPorIdAsync(espera.ViajeId, ct);
            if (viaje is null)
            {
                continue;
            }

            var enlaces = (await viajes.ObtenerVehiculosAsync(viaje.Id, ct)).Where(e => e.EstaActiva && e.LugarDestinoId == espera.LugarId).ToList();
            if (enlaces.Count == 0 || await vehiculos.ObtenerPorIdAsync(enlaces[0].VehiculoId, ct) is not { } vehiculo)
            {
                continue;
            }

            await AvisarEsperaAsync(espera, viaje, enlaces.Count, vehiculo.ClienteId, ct);
            avisadas++;
        }

        return avisadas;
    }
}
