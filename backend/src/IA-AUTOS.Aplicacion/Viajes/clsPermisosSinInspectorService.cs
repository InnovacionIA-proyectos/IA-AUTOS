using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// El permiso de la excepcion «No hay inspector». Donde el punto de cargue NO tiene inspectores asignados el conductor la activa en el momento (como siempre); donde los TIENE la excepcion se pide: el conductor registra
/// su solicitud (motivo, fecha y GPS), se avisa a los inspectores del punto y a los coordinadores y administradores del cliente, y un coordinador o administrador la concede (el conductor queda habilitado de inmediato).
/// Si nadie responde en <see cref="clsPermisoSinInspector.MINUTOS_DE_ESPERA"/> minutos la concede el reloj, marcada como autorizada por tiempo: lo hace un proceso periodico (no depende de que alguien abra una pantalla)
/// y nunca antes del plazo. Todo queda en la auditoria y en la novedad de proceso de la excepcion.
/// </summary>
public sealed class clsPermisosSinInspectorService(
    IPermisoSinInspectorRepository permisos,
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IReclamoRepository reclamos,
    IUsuarioRepository usuarios,
    ICatalogosLectura catalogos,
    clsAccesoViajes acceso,
    clsEmisorNotificaciones emisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj)
{
    private const int LIMITE_POR_EJECUCION = 200;

    // ----------------------------------------------------------------------------------------------- solicitar

    /// <summary>
    /// El conductor del viaje pide el permiso. 403 si no es el conductor del viaje; 409 si el viaje no esta activo o si el punto de cargue no tiene inspectores (ahi la excepcion se activa directamente); 400 con el
    /// motivo o la fecha invalidos. Si el viaje ya tiene su solicitud, devuelve esa (no se duplica).
    /// </summary>
    public async Task<PermisoSinInspectorDto> SolicitarAsync(Guid viajeId, SolicitarPermisoSinInspectorRequest solicitud, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        if (actual.Rol != clsRol.CONDUCTOR || viaje.ConductorId != actual.IdObligatorio())
        {
            throw new clsProhibidoException("Solo el conductor del viaje pide el permiso de la excepción «No hay inspector»");
        }

        viaje.AsegurarActivo();
        if (await permisos.ObtenerDeViajeAsync(viajeId, ct) is { } existente)
        {
            return await ADtoAsync(existente, ct);
        }

        if (viaje.LugarCargueId is not { } lugarId || !await reclamos.LugarTieneInspectoresAsync(lugarId, ct))
        {
            throw new clsConflictoException("El punto de cargue no tiene inspectores asignados: la excepción «No hay inspector» se activa directamente, sin permiso");
        }

        var errores = new Dictionary<string, string[]>();
        var motivo = await MotivoAsync(solicitud, errores, ct);
        var fecha = clsFechaProgramada.DeIso(solicitud.Fecha);
        if (fecha is null)
        {
            errores["fecha"] = ["La fecha es obligatoria: use una fecha y hora ISO 8601 con zona horaria (por ejemplo 2026-11-03T06:00:00-05:00)"];
        }

        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var permiso = clsPermisoSinInspector.Solicitar(viajeId, viaje.ConductorId!.Value, lugarId, motivo, solicitud.MotivoSinInspectorTexto, fecha!.Value, solicitud.Lat, solicitud.Lng, reloj.Ahora);
        permisos.Agregar(permiso);
        var clienteId = await ClienteDelViajeAsync(viaje, ct);
        await auditor.RegistrarDeViajeAsync(
            "permiso_sin_inspector.solicitar", "permisos_sin_inspector", permiso.Id, viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(permiso)), clienteId, ct);

        // Un segundo intento a la vez choca con el indice unico del viaje: la base deja pasar una y la otra se traduce a 409 al guardar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        await AvisarSolicitudAsync(permiso, viaje, clienteId, ct);
        return await ADtoAsync(permiso, ct);
    }

    // ------------------------------------------------------------------------------------------------ conceder

    /// <summary>
    /// Un coordinador de calidad o administrador concede el permiso: el conductor queda habilitado de inmediato, sin esperar al inspector. 404 si el viaje no tiene solicitud (o es de otro cliente); 409 si ya esta
    /// concedido.
    /// </summary>
    public async Task<PermisoSinInspectorDto> ConcederAsync(Guid viajeId, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        await AsegurarVisibleAsync(viaje, ct);
        var permiso = await permisos.ObtenerDeViajeAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException("El viaje no tiene una solicitud de permiso");
        var antes = clsInstantaneasAuditoria.De(permiso);
        permiso.Conceder(actual.IdObligatorio(), reloj.Ahora);
        var clienteId = await ClienteDelViajeAsync(viaje, ct);
        await auditor.RegistrarDeViajeAsync(
            "permiso_sin_inspector.conceder", "permisos_sin_inspector", permiso.Id, viaje.Id, new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(permiso)), clienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        await AvisarConcesionAsync(permiso, viaje, clienteId, ct);
        return await ADtoAsync(permiso, ct);
    }

    // ------------------------------------------------------------------------------------------------ consulta

    /// <summary>
    /// La solicitud del viaje y su estado (pendiente o concedida, con quien la concedio o si fue el reloj). 404 si no hay. La ven el conductor y el inspector del viaje, los coordinadores y administradores del cliente y el
    /// super administrador. Si ya cumplio la espera la concede el reloj aqui mismo, igual que el proceso periodico (no depende de que alguien abra una pantalla: esto solo cubre la espera del proceso).
    /// </summary>
    public async Task<PermisoSinInspectorDto> ObtenerAsync(Guid viajeId, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        if (actual.SoloSusViajes() is { } usuarioId)
        {
            if (!await acceso.ParticipaOReclamaAsync(viaje, usuarioId, ct))
            {
                throw new clsProhibidoException("El viaje no está asignado a su usuario");
            }
        }
        else
        {
            await AsegurarVisibleAsync(viaje, ct);
        }

        var permiso = await permisos.ObtenerDeViajeAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException("El viaje no tiene una solicitud de permiso");
        if (!permiso.EstaConcedido && reloj.Ahora >= permiso.VenceAt)
        {
            await ConcederPorTiempoAsync(permiso, viaje, ct);
        }

        return await ADtoAsync(permiso, ct);
    }

    /// <summary>True si el conductor del viaje puede hacer el cargue sin inspector: el permiso esta concedido o la excepcion ya se activo en el viaje (donde no hay inspectores). Lo usa quien lo habilita a registrar posiciones.</summary>
    public async Task<bool> PermisoConcedidoAsync(Guid viajeId, CancellationToken ct = default) =>
        await permisos.ObtenerDeViajeAsync(viajeId, ct) is { EstaConcedido: true };

    // --------------------------------------------------------------------------------------------- el reloj

    /// <summary>
    /// Concede por tiempo las solicitudes que cumplieron <see cref="clsPermisoSinInspector.MINUTOS_DE_ESPERA"/> minutos sin respuesta, nunca antes. Lo corre el proceso periodico (cada minuto). Devuelve cuantas concedio.
    /// </summary>
    public async Task<int> ConcederVencidosAsync(CancellationToken ct = default)
    {
        var concedidos = 0;
        foreach (var permiso in await permisos.ObtenerPendientesAsync(reloj.Ahora.AddMinutes(-clsPermisoSinInspector.MINUTOS_DE_ESPERA), LIMITE_POR_EJECUCION, ct))
        {
            var viaje = await viajes.ObtenerPorIdAsync(permiso.ViajeId, ct);
            if (viaje is not null)
            {
                await ConcederPorTiempoAsync(permiso, viaje, ct);
                concedidos++;
            }
        }

        return concedidos;
    }

    private async Task ConcederPorTiempoAsync(clsPermisoSinInspector permiso, clsViaje viaje, CancellationToken ct)
    {
        var antes = clsInstantaneasAuditoria.De(permiso);
        permiso.ConcederPorTiempo(reloj.Ahora);
        var clienteId = await ClienteDelViajeAsync(viaje, ct);
        await auditor.RegistrarDeViajeAsync(
            "permiso_sin_inspector.conceder_por_tiempo", "permisos_sin_inspector", permiso.Id, viaje.Id, new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(permiso)), clienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        await AvisarConcesionAsync(permiso, viaje, clienteId, ct);
    }

    // --------------------------------------------------------------------------------------------- internos

    private async Task<clsViaje> ObtenerViajeAsync(Guid viajeId, CancellationToken ct) =>
        await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");

    /// <summary>El cliente de los VIN del viaje (el del primero): a el pertenecen los coordinadores y administradores a quienes se avisa.</summary>
    private async Task<Guid?> ClienteDelViajeAsync(clsViaje viaje, CancellationToken ct)
    {
        foreach (var enlace in await viajes.ObtenerVehiculosAsync(viaje.Id, ct))
        {
            if (await vehiculos.ObtenerPorIdAsync(enlace.VehiculoId, ct) is { } vehiculo)
            {
                return vehiculo.ClienteId;
            }
        }

        return null;
    }

    /// <summary>Quien se limita a su cliente solo ve los viajes de ese cliente (404 si no es suyo).</summary>
    private async Task AsegurarVisibleAsync(clsViaje viaje, CancellationToken ct)
    {
        if (actual.ClienteRestringido() is { } cliente && await ClienteDelViajeAsync(viaje, ct) != cliente)
        {
            throw new clsEntidadNoEncontradaException($"Viaje {viaje.Id} no encontrado");
        }
    }

    private async Task<string?> MotivoAsync(SolicitarPermisoSinInspectorRequest solicitud, Dictionary<string, string[]> errores, CancellationToken ct)
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

        return codigo;
    }

    private async Task<PermisoSinInspectorDto> ADtoAsync(clsPermisoSinInspector p, CancellationToken ct)
    {
        var conductor = (await usuarios.ObtenerPorIdAsync(p.ConductorId, ct))?.Nombre;
        var concedidoPor = p.ConcedidoPor is { } quien ? (await usuarios.ObtenerPorIdAsync(quien, ct))?.Nombre : null;
        var lugar = (await catalogos.ValoresAsync(clsNombresCatalogo.LUGARES, ct)).FirstOrDefault(l => l.Id == p.LugarId);
        var motivo = (await catalogos.ValoresAsync(clsNombresCatalogo.MOTIVOS_SIN_INSPECTOR, ct)).FirstOrDefault(m => m.Codigo == p.MotivoCodigo);
        return new PermisoSinInspectorDto(
            p.Id, p.ViajeId, p.Estado, p.CreatedAt, p.VenceAt, p.ConductorId, conductor, p.LugarId, lugar is not null && lugar.TryGetValue("nombre", out var nombre) ? nombre as string : null,
            p.MotivoCodigo, motivo is not null && motivo.TryGetValue("nombre", out var nombreMotivo) ? nombreMotivo as string : null, p.MotivoTexto, p.Fecha, p.Lat, p.Lng,
            p.ConcedidoAt, p.ConcedidoPor, concedidoPor, p.PorTiempo);
    }

    // ------------------------------------------------------------------------------------------------ avisos

    /// <summary>Avisa de la solicitud (urgencia alta) a los inspectores del punto y a los coordinadores y administradores del cliente. Nunca lanza: el permiso ya quedo guardado.</summary>
    private async Task AvisarSolicitudAsync(clsPermisoSinInspector permiso, clsViaje viaje, Guid? clienteId, CancellationToken ct)
    {
        try
        {
            var destinatarios = (await reclamos.InspectoresDelLugarAsync(permiso.LugarId, ct)).ToList();
            if (clienteId is not null)
            {
                destinatarios.AddRange((await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], clienteId.Value, ct)).Select(u => u.Id));
            }

            var lugar = await NombreDelLugarAsync(permiso.LugarId, ct);
            var codigo = viaje.CodigoMtrack is { } c ? $" (viaje {c})" : string.Empty;
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.PERMISO_SIN_INSPECTOR,
                    clsUrgenciaNotificacion.ALTA,
                    destinatarios.Distinct().ToList(),
                    clienteId,
                    viaje.Id,
                    null,
                    null,
                    null,
                    $"Solicitud de permiso: no hay inspector en {lugar}",
                    $"El conductor pide permiso para hacer el cargue sin inspector en {lugar}{codigo}. Si nadie responde en {clsPermisoSinInspector.MINUTOS_DE_ESPERA} minutos, el permiso se concede por tiempo.",
                    $"{clsTipoNotificacion.PERMISO_SIN_INSPECTOR}:solicitud:{viaje.Id}",
                    new Dictionary<string, object?>
                    {
                        ["hecho"] = "solicitud",
                        ["permiso_id"] = permiso.Id,
                        ["viaje_id"] = viaje.Id,
                        ["lugar_id"] = permiso.LugarId,
                        ["lugar"] = lugar,
                        ["motivo"] = permiso.MotivoCodigo,
                        ["vence_at"] = permiso.VenceAt,
                    }),
                ct);
        }
        catch (Exception)
        {
            // El aviso es un efecto aparte: la solicitud ya quedo guardada y no se deshace.
        }
    }

    /// <summary>Avisa al conductor que el permiso se concedio (o que se concedio por tiempo) y al coordinador del cliente cuando fue el reloj. Nunca lanza.</summary>
    private async Task AvisarConcesionAsync(clsPermisoSinInspector permiso, clsViaje viaje, Guid? clienteId, CancellationToken ct)
    {
        try
        {
            var destinatarios = new List<Guid> { permiso.ConductorId };
            if (permiso.PorTiempo && clienteId is not null)
            {
                destinatarios.AddRange((await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], clienteId.Value, ct)).Select(u => u.Id));
            }

            var lugar = await NombreDelLugarAsync(permiso.LugarId, ct);
            var quien = permiso.PorTiempo ? $"se concedió por tiempo (pasaron {clsPermisoSinInspector.MINUTOS_DE_ESPERA} minutos sin respuesta)" : "fue concedido";
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.PERMISO_SIN_INSPECTOR,
                    clsUrgenciaNotificacion.ALTA,
                    destinatarios.Distinct().ToList(),
                    clienteId,
                    viaje.Id,
                    null,
                    null,
                    null,
                    $"Permiso concedido: cargue sin inspector en {lugar}",
                    $"El permiso para hacer el cargue sin inspector en {lugar} {quien}.",
                    $"{clsTipoNotificacion.PERMISO_SIN_INSPECTOR}:concesion:{viaje.Id}",
                    new Dictionary<string, object?>
                    {
                        ["hecho"] = "concesion",
                        ["permiso_id"] = permiso.Id,
                        ["viaje_id"] = viaje.Id,
                        ["lugar_id"] = permiso.LugarId,
                        ["lugar"] = lugar,
                        ["autorizado_por_tiempo"] = permiso.PorTiempo,
                    }),
                ct);
        }
        catch (Exception)
        {
            // El aviso es un efecto aparte: la concesion ya quedo guardada y no se deshace.
        }
    }

    private async Task<string> NombreDelLugarAsync(Guid lugarId, CancellationToken ct) =>
        (await catalogos.ValoresAsync(clsNombresCatalogo.LUGARES, ct)).FirstOrDefault(l => l.Id == lugarId) is { } lugar && lugar.TryGetValue("nombre", out var nombre) && nombre is string texto
            ? texto
            : "el punto de cargue";
}
