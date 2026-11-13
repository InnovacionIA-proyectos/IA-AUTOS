using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// La entrega por punto: cada unidad del viaje se entrega en SU lugar de destino, y en cada punto se firma una vez entre quien entrega (por defecto el conductor del viaje) y quien recibe (por defecto su
/// inspector): la firma se guarda en la transferencia de cada unidad pendiente del punto, con la misma firma, fecha e IP, y cada unidad conserva su transferencia propia (la del Descargue, sobre la
/// inspeccion de llegada). Una sola firma no bloquea: se puede cerrar la entrega con una firma y su motivo, que deja la novedad de proceso y avisa a los coordinadores de calidad. El viaje pasa a
/// finalizado cuando todas sus unidades activas tienen el Descargue cerrado. El inspector que reclamo la revision de la entrega (<see cref="clsProcesoReclamo.ENTREGA"/>) ve y firma las entregas del viaje y es quien
/// recibe por defecto.
/// </summary>
public sealed class clsEntregasService(
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IInspeccionRepository inspecciones,
    ITransferenciaCustodiaRepository transferencias,
    INovedadProcesoRepository novedades,
    ICatalogosLectura catalogos,
    IUsuarioRepository usuarios,
    clsEvidenciaDeHallazgos evidenciaDeHallazgos,
    clsEmisorNotificaciones emisor,
    IReclamoRepository reclamos,
    clsAccesoViajes acceso,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj)
{
    /// <summary>El codigo del motivo del catalogo <c>motivos-sin-inspector</c> con el que la novedad guarda el motivo escrito de la entrega con una sola firma.</summary>
    private const string MOTIVO_OTRO = "otro";

    private sealed class Unidad(clsViajeVehiculo enlace, clsVehiculo vehiculo, clsEtapa? descargue, clsInspeccion? llegada, clsTransferenciaCustodia? transferencia)
    {
        public clsViajeVehiculo Enlace { get; } = enlace;

        public clsVehiculo Vehiculo { get; } = vehiculo;

        public clsEtapa? Descargue { get; } = descargue;

        public clsInspeccion? Llegada { get; } = llegada;

        public clsTransferenciaCustodia? Transferencia { get; set; } = transferencia;

        public bool Cerrada => Transferencia is { EstaCerrada: true };
    }

    /// <summary>Quien recibe: un usuario, una persona externa con su nombre, su cedula y su firma (el punto no tiene inspector) o nadie (la entrega se cierra con la sola firma de quien entrega).</summary>
    private sealed record Receptor(Guid? UsuarioId, string? Nombre, string? Cedula, string? Firma)
    {
        public static readonly Receptor NADIE = new(null, null, null, null);

        public bool EsExterno => Nombre is not null;

        public static Receptor DeUsuario(Guid id) => new(id, null, null, null);
    }

    // ---------------------------------------------------------------------------------------------- consulta

    /// <summary>Las entregas del viaje, una por lugar de destino. Conductor e inspector solo ven las de sus viajes (403).</summary>
    public async Task<EntregasDelViajeDto> ListarAsync(Guid viajeId, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        await AsegurarVisibleAsync(viaje, ct);
        var unidades = await CargarUnidadesAsync(viaje, ct);
        var lugares = await LugaresAsync(ct);
        var conInspector = await reclamos.LugaresConInspectoresAsync([.. unidades.Where(u => u.Enlace.LugarDestinoId is not null).Select(u => u.Enlace.LugarDestinoId!.Value)], ct);
        var puntos = unidades
            .Where(u => u.Enlace.LugarDestinoId is not null)
            .GroupBy(u => u.Enlace.LugarDestinoId!.Value)
            .Select(g => ArmarPunto(viaje, g.Key, g.ToList(), lugares, conInspector.Contains(g.Key)))
            .OrderBy(p => p.Lugar, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new EntregasDelViajeDto(viaje.Id, viaje.Estado, unidades.Count(u => u.Enlace.LugarDestinoId is null), puntos);
    }

    /// <summary>Las unidades del viaje que se entregan en un lugar, con lo que falta de cada una; 404 si ninguna unidad activa del viaje va a ese lugar.</summary>
    public async Task<EntregaPuntoDto> ObtenerPuntoAsync(Guid viajeId, Guid lugarId, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        await AsegurarVisibleAsync(viaje, ct);
        var unidades = await CargarUnidadesAsync(viaje, ct);
        return ArmarPunto(viaje, lugarId, DelPunto(unidades, lugarId), await LugaresAsync(ct), await reclamos.LugarTieneInspectoresAsync(lugarId, ct));
    }

    // ------------------------------------------------------------------------------------------------ firmas

    /// <summary>
    /// Firma la entrega del punto: la firma del usuario autenticado (quien entrega o quien recibe) se guarda en la transferencia de cada unidad pendiente del punto, con la misma fecha e IP. Las dos firmas cierran
    /// cada transferencia como «firmada». En un punto SIN inspector asignado recibe una persona externa: su nombre, su cedula y su firma son obligatorios (400 nombrando lo que falta) y la entrega queda cerrada como
    /// «firmada», con la auditoria de quien recibio, cuando y en que punto; en un punto CON inspector esos campos se rechazan (400). 403 si no es el conductor ni el inspector del viaje; 409 si no hay unidades pendientes o
    /// alguna aun no tiene la inspeccion de llegada del Descargue.
    /// </summary>
    public async Task<EntregaPuntoDto> FirmarAsync(Guid viajeId, Guid lugarId, FirmarEntregaRequest solicitud, ContextoCliente cliente, CancellationToken ct = default)
    {
        var firma = clsValidador.Opcional(solicitud.Firma) ?? throw clsErroresCampos.De("firma", "La firma es requerida");
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        viaje.AsegurarActivo();
        var reclamante = await ReclamanteDeLaEntregaAsync(viaje, ct);
        var actorId = ActorDelViaje(viaje, reclamante);
        var (entregaId, receptor) = await FirmantesAsync(viaje, lugarId, solicitud, actorId, reclamante, false, ct);
        var unidades = await CargarUnidadesAsync(viaje, ct);
        var pendientes = PendientesConInspeccion(DelPunto(unidades, lugarId));

        var ahora = reloj.Ahora;
        var lugar = (await LugaresAsync(ct)).TryGetValue(lugarId, out var datosLugar) ? datosLugar.Nombre : string.Empty;
        foreach (var unidad in pendientes)
        {
            var transferencia = ObtenerOCrear(unidad, entregaId, receptor, ahora);
            var antes = clsInstantaneasAuditoria.De(transferencia);
            transferencia.Firmar(actorId, firma, cliente.Ip, ahora);
            if (receptor.EsExterno)
            {
                // La persona externa firma con su propio trazo: con las dos firmas la transferencia queda cerrada como «firmada».
                transferencia.FirmarComoExterno(receptor.Firma, cliente.Ip, ahora);
            }

            await evidenciaDeHallazgos.RecalcularAsync(unidad.Llegada!.Id, transferencia.TipoCierre, ct);
            if (receptor.EsExterno)
            {
                // Quien recibio (nombre y cedula), cuando firmo y en que punto, en la misma entrada de auditoria.
                await auditor.RegistrarDeViajeAsync(
                    "transferencia.firmar_externo", "transferencias_custodia", transferencia.Id, viaje.Id,
                    new DetalleAuditoria(antes, new { transferencia = clsInstantaneasAuditoria.De(transferencia), punto = new { lugar_id = lugarId, lugar, vin = unidad.Vehiculo.Vin } }), unidad.Vehiculo.ClienteId, ct);
            }
            else
            {
                await auditor.RegistrarDeViajeAsync(
                    "transferencia.firmar", "transferencias_custodia", transferencia.Id, viaje.Id, new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(transferencia)), unidad.Vehiculo.ClienteId, ct);
            }
        }

        await FinalizarSiCompletoAsync(viaje, unidades, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return ArmarPunto(viaje, lugarId, DelPunto(unidades, lugarId), await LugaresAsync(ct), await reclamos.LugarTieneInspectoresAsync(lugarId, ct));
    }

    /// <summary>
    /// Cierra la entrega del punto con UNA sola firma (la del usuario autenticado, que es quien entrega o quien recibe) y su motivo: cada unidad pendiente queda cerrada como «recibo_sin_entrega» con la parte que no
    /// firmo, deja su novedad de proceso y los coordinadores de calidad reciben un solo aviso por entrega y punto. 403 si no es el conductor ni el inspector del viaje; 409 si la otra parte ya firmo.
    /// </summary>
    public async Task<EntregaPuntoDto> CerrarConUnaFirmaAsync(Guid viajeId, Guid lugarId, CerrarEntregaUnaFirmaRequest solicitud, ContextoCliente cliente, CancellationToken ct = default)
    {
        var motivo = clsValidador.Opcional(solicitud.Motivo) ?? throw clsErroresCampos.De("motivo", "El motivo es obligatorio");
        if (motivo.Length > clsNovedadProceso.TEXTO_MAX)
        {
            throw clsErroresCampos.De("motivo", $"El motivo admite máximo {clsNovedadProceso.TEXTO_MAX} caracteres");
        }

        var viaje = await ObtenerViajeAsync(viajeId, ct);
        viaje.AsegurarActivo();
        var reclamante = await ReclamanteDeLaEntregaAsync(viaje, ct);
        var actorId = ActorDelViaje(viaje, reclamante);
        var unidades = await CargarUnidadesAsync(viaje, ct);
        var pendientes = PendientesConInspeccion(DelPunto(unidades, lugarId));

        // Quien cierra firma su parte; la otra queda sin firmar. Por defecto quien entrega es el conductor y quien recibe, el inspector que reclamo la entrega o, sin reclamo, el del viaje; en un punto sin
        // inspector ni quien reciba, solo queda la firma de quien entrega.
        var (entregaId, receptor) = await FirmantesAsync(viaje, lugarId, new FirmarEntregaRequest(null, null, null), actorId, reclamante, true, ct);
        var ahora = reloj.Ahora;
        var novedadesCreadas = new List<clsNovedadProceso>();
        foreach (var unidad in pendientes)
        {
            var transferencia = ObtenerOCrear(unidad, entregaId, receptor, ahora);
            var antes = clsInstantaneasAuditoria.De(transferencia);
            transferencia.CerrarConUnaFirma(actorId, solicitud.Firma, motivo, cliente.Ip, ahora);
            await evidenciaDeHallazgos.RecalcularAsync(unidad.Llegada!.Id, transferencia.TipoCierre, ct);
            await auditor.RegistrarDeViajeAsync(
                "transferencia.sin_entrega", "transferencias_custodia", transferencia.Id, viaje.Id, new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(transferencia)), unidad.Vehiculo.ClienteId, ct);

            var novedad = clsNovedadProceso.Crear(
                clsTipoNovedadProceso.RECIBO_SIN_ENTREGA, viaje.Id, unidad.Vehiculo.Id, unidad.Descargue!.Id, unidad.Llegada.Id, MOTIVO_OTRO, motivo, actorId, ahora, null, null, ahora);
            novedades.Agregar(novedad);
            novedadesCreadas.Add(novedad);
            await auditor.RegistrarDeViajeAsync(
                "novedad_proceso.crear", "novedades_proceso", novedad.Id, viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(novedad)), unidad.Vehiculo.ClienteId, ct);
        }

        await FinalizarSiCompletoAsync(viaje, unidades, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);

        await AvisarAsync(viaje, lugarId, pendientes, motivo, actorId, ct);
        return ArmarPunto(viaje, lugarId, DelPunto(unidades, lugarId), await LugaresAsync(ct), await reclamos.LugarTieneInspectoresAsync(lugarId, ct));
    }

    // ----------------------------------------------------------------------------------------------- internos

    private async Task<clsViaje> ObtenerViajeAsync(Guid viajeId, CancellationToken ct) =>
        await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");

    /// <summary>Conductor e inspector solo ven lo de sus viajes y, el inspector, las entregas que reclamo (403 si es de otro).</summary>
    private async Task AsegurarVisibleAsync(clsViaje viaje, CancellationToken ct)
    {
        if (actual.SoloSusViajes() is { } usuarioId && !await acceso.PuedeVerAsync(viaje, usuarioId, ct))
        {
            throw new clsProhibidoException("El viaje no está asignado a su usuario");
        }
    }

    /// <summary>El inspector que reclamo la revision de la entrega del viaje (reclamo activo), o nulo.</summary>
    private async Task<Guid?> ReclamanteDeLaEntregaAsync(clsViaje viaje, CancellationToken ct) =>
        (await reclamos.ObtenerActivoAsync(viaje.Id, clsProcesoReclamo.ENTREGA, ct))?.InspectorId;

    /// <summary>Solo el conductor, el inspector del viaje o el inspector que reclamo la entrega firman: nadie actua por otro (403).</summary>
    private Guid ActorDelViaje(clsViaje viaje, Guid? reclamante)
    {
        var actorId = actual.IdObligatorio();
        if (!viaje.Participa(actorId) && reclamante != actorId)
        {
            throw new clsProhibidoException("Solo el conductor o el inspector del viaje firman la entrega: nadie actúa por otro");
        }

        return actorId;
    }

    private const string MENSAJE_EXTERNO_CON_INSPECTOR = "El punto de entrega tiene inspector asignado: la entrega la firma él, no una persona externa";

    /// <summary>
    /// Quien entrega y quien recibe: los indicados o, por defecto, el conductor y el inspector del viaje (el que reclamo la entrega o el del viaje). Deben ser usuarios del viaje, distintos entre si, y quien envia debe
    /// ser uno de los dos. Si el punto NO tiene inspector asignado y no hay quien reciba por defecto (o el cuerpo trae a la persona externa), recibe esa persona externa: nombre, cedula y firma obligatorios (400 con
    /// lo que falta) y quien entrega es quien firma (403 si es otro). En un punto con inspector asignado esos campos se rechazan (400). Con <paramref name="admiteSinReceptor"/> (el cierre con una sola firma), un punto sin
    /// inspector ni quien reciba deja a nadie como receptor.
    /// </summary>
    private async Task<(Guid Entrega, Receptor Recibe)> FirmantesAsync(
        clsViaje viaje, Guid lugarId, FirmarEntregaRequest solicitud, Guid actorId, Guid? reclamante, bool admiteSinReceptor, CancellationToken ct)
    {
        bool EsDelViaje(Guid id) => id == viaje.ConductorId || id == viaje.InspectorId || id == reclamante;
        var nombre = clsValidador.Opcional(solicitud.RecibeNombre);
        var cedula = clsValidador.Opcional(solicitud.RecibeCedula);
        var firmaRecibe = string.IsNullOrWhiteSpace(solicitud.FirmaRecibe) ? null : solicitud.FirmaRecibe;
        var traeExterno = nombre is not null || cedula is not null || firmaRecibe is not null;
        var conInspector = await reclamos.LugarTieneInspectoresAsync(lugarId, ct);
        if (traeExterno && conInspector)
        {
            var rechazados = new Dictionary<string, string[]>();
            if (nombre is not null)
            {
                rechazados["recibe_nombre"] = [MENSAJE_EXTERNO_CON_INSPECTOR];
            }

            if (cedula is not null)
            {
                rechazados["recibe_cedula"] = [MENSAJE_EXTERNO_CON_INSPECTOR];
            }

            if (firmaRecibe is not null)
            {
                rechazados["firma_recibe"] = [MENSAJE_EXTERNO_CON_INSPECTOR];
            }

            throw clsErroresCampos.Varios(rechazados);
        }

        if (traeExterno && solicitud.RecibeUsuarioId is not null)
        {
            throw clsErroresCampos.De("recibe_usuario_id", "Indique un usuario que recibe o una persona externa, no las dos");
        }

        var entrega = solicitud.EntregaUsuarioId ?? viaje.ConductorId;
        var recibe = solicitud.RecibeUsuarioId ?? reclamante ?? viaje.InspectorId;
        var esExterno = traeExterno || (recibe is null && !conInspector);
        var errores = new Dictionary<string, string[]>();
        if (entrega is null)
        {
            errores["entrega_usuario_id"] = ["Indique quién entrega: el viaje no tiene conductor"];
        }
        else if (!EsDelViaje(entrega.Value))
        {
            errores["entrega_usuario_id"] = ["Quien entrega debe ser un usuario del viaje"];
        }

        if (!esExterno)
        {
            if (recibe is null)
            {
                errores["recibe_usuario_id"] = ["Indique quién recibe: el viaje no tiene inspector"];
            }
            else if (!EsDelViaje(recibe.Value))
            {
                errores["recibe_usuario_id"] = ["Quien recibe debe ser un usuario del viaje"];
            }

            if (errores.Count == 0 && entrega == recibe)
            {
                errores["recibe_usuario_id"] = ["Quien recibe debe ser distinto de quien entrega"];
            }
        }
        else if (!admiteSinReceptor)
        {
            foreach (var error in clsTransferenciaCustodia.ErroresDelExterno(nombre, cedula))
            {
                errores[error.Key] = error.Value;
            }

            if (firmaRecibe is null)
            {
                errores["firma_recibe"] = ["La firma de quien recibe es obligatoria"];
            }
        }

        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        if (esExterno)
        {
            if (actorId != entrega)
            {
                throw new clsProhibidoException("Solo quien entrega puede registrar a la persona que recibe: nadie actúa por otro");
            }

            return (entrega!.Value, admiteSinReceptor && !traeExterno ? Receptor.NADIE : new Receptor(null, nombre, cedula, firmaRecibe));
        }

        if (actorId != entrega && actorId != recibe)
        {
            throw new clsProhibidoException("Solo quien entrega o quien recibe puede firmar: nadie actúa por otro");
        }

        return (entrega!.Value, Receptor.DeUsuario(recibe!.Value));
    }

    private static bool Igual(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>La transferencia de la unidad, o una nueva sobre su inspeccion de llegada si es la primera vez. Si ya existe, quienes firman deben ser los mismos (409).</summary>
    private clsTransferenciaCustodia ObtenerOCrear(Unidad unidad, Guid entregaId, Receptor recibe, DateTimeOffset ahora)
    {
        if (unidad.Transferencia is { } existente)
        {
            var mismoReceptor = recibe.EsExterno
                ? existente.RecibeEsExterno && Igual(existente.RecibeExternoNombre, recibe.Nombre) && Igual(existente.RecibeExternoCedula, recibe.Cedula)
                : !existente.RecibeEsExterno && existente.RecibeUsuarioId == recibe.UsuarioId;
            if ((existente.EntregaUsuarioId is not null && existente.EntregaUsuarioId != entregaId) || !mismoReceptor)
            {
                throw new clsConflictoException("Los firmantes de la transferencia no se pueden cambiar");
            }

            return existente;
        }

        var nueva = recibe.EsExterno
            ? clsTransferenciaCustodia.CrearConExterno(unidad.Llegada!.Id, entregaId, recibe.Nombre, recibe.Cedula, ahora)
            : recibe.UsuarioId is { } usuarioId
                ? clsTransferenciaCustodia.Crear(unidad.Llegada!.Id, entregaId, usuarioId, ahora)
                : clsTransferenciaCustodia.CrearSinReceptor(unidad.Llegada!.Id, entregaId, ahora);
        transferencias.Agregar(nueva);
        unidad.Transferencia = nueva;
        return nueva;
    }

    /// <summary>
    /// Las unidades activas del viaje con el Descargue, su inspeccion de llegada y su transferencia. Una unidad que no sale del viaje ya no se entrega y no aparece.
    /// </summary>
    private async Task<List<Unidad>> CargarUnidadesAsync(clsViaje viaje, CancellationToken ct)
    {
        var codigosPorEtapa = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).ToDictionary(v => v.Id, v => v.Codigo);
        var etapas = await viajes.ObtenerEtapasAsync(viaje.Id, null, ct);
        var resultado = new List<Unidad>();
        foreach (var enlace in (await viajes.ObtenerVehiculosAsync(viaje.Id, ct)).Where(e => !e.NoSale))
        {
            var vehiculo = await vehiculos.ObtenerPorIdAsync(enlace.VehiculoId, ct) ?? throw new clsEntidadNoEncontradaException("El vehículo del viaje no existe");
            var descargue = etapas.FirstOrDefault(e => e.VehiculoId == enlace.VehiculoId && codigosPorEtapa.TryGetValue(e.EtapaCatalogoId, out var codigo) && codigo == clsCodigosEtapa.DESCARGUE);
            var llegada = descargue is null ? null : await inspecciones.ObtenerUltimaDeEtapaAsync(descargue.Id, clsMomento.LLEGADA, ct);
            var transferencia = llegada is null ? null : await transferencias.ObtenerPorInspeccionAsync(llegada.Id, ct);
            resultado.Add(new Unidad(enlace, vehiculo, descargue, llegada, transferencia));
        }

        return resultado;
    }

    /// <summary>Las unidades que se entregan en el lugar (404 si ninguna).</summary>
    private static List<Unidad> DelPunto(List<Unidad> unidades, Guid lugarId)
    {
        var delPunto = unidades.Where(u => u.Enlace.LugarDestinoId == lugarId).ToList();
        return delPunto.Count > 0 ? delPunto : throw new clsEntidadNoEncontradaException("Ninguna unidad activa del viaje se entrega en ese lugar");
    }

    /// <summary>Las pendientes del punto (409 si no hay) y todas con la inspeccion de llegada del Descargue, que es donde se firma (409 si alguna aun no la tiene).</summary>
    private static List<Unidad> PendientesConInspeccion(List<Unidad> delPunto)
    {
        var pendientes = delPunto.Where(u => !u.Cerrada).ToList();
        if (pendientes.Count == 0)
        {
            throw new clsConflictoException("No hay unidades pendientes de entrega en este punto");
        }

        var sinInspeccion = pendientes.Where(u => u.Llegada is null || u.Descargue is null).Select(u => u.Vehiculo.Vin).ToList();
        if (sinInspeccion.Count > 0)
        {
            throw new clsConflictoException($"Aún no tienen la inspección de llegada del descargue: {string.Join(", ", sinInspeccion)}");
        }

        return pendientes;
    }

    /// <summary>El viaje en curso pasa a finalizado cuando todas sus unidades activas tienen el Descargue cerrado (inspeccion de llegada cerrada y transferencia con cierre); con entregas pendientes sigue en curso.</summary>
    private async Task FinalizarSiCompletoAsync(clsViaje viaje, List<Unidad> unidades, CancellationToken ct)
    {
        if (viaje.Estado != clsEstadoViaje.EN_CURSO || unidades.Count == 0 || !unidades.All(u => u.Llegada is { EstaCerrada: true } && u.Cerrada))
        {
            return;
        }

        viaje.CambiarEstado(clsEstadoViaje.FINALIZADO, reloj.Ahora);
        await auditor.RegistrarDeViajeAsync(
            $"viaje.estado.{viaje.Estado}", "viajes", viaje.Id, viaje.Id, new DetalleAuditoria(new { estado = clsEstadoViaje.EN_CURSO }, new { estado = viaje.Estado }), ct: ct);
    }

    private async Task<IReadOnlyDictionary<Guid, (string Nombre, string? Ciudad)>> LugaresAsync(CancellationToken ct) =>
        (await catalogos.ValoresAsync(clsNombresCatalogo.LUGARES, ct)).ToDictionary(
            l => l.Id, l => (l.TryGetValue("nombre", out var nombre) ? nombre as string ?? string.Empty : string.Empty, l.TryGetValue("ciudad", out var ciudad) ? ciudad as string : null));

    private static EntregaPuntoDto ArmarPunto(clsViaje viaje, Guid lugarId, List<Unidad> delPunto, IReadOnlyDictionary<Guid, (string Nombre, string? Ciudad)> lugares, bool lugarTieneInspector)
    {
        var lugar = lugares.TryGetValue(lugarId, out var datos) ? datos : (Nombre: string.Empty, Ciudad: null);
        var filas = delPunto
            .OrderBy(u => u.Vehiculo.Vin, StringComparer.Ordinal)
            .Select(u => new EntregaUnidadDto(
                u.Vehiculo.Id,
                u.Vehiculo.Vin,
                u.Cerrada ? "cerrada" : u.Llegada is null ? "sin_inspeccion" : "pendiente",
                u.Llegada?.Id,
                u.Transferencia?.TipoCierre,
                u.Transferencia?.FirmaEntregaAt,
                u.Transferencia?.FirmaRecibeAt,
                u.Transferencia?.NoFirmaParte,
                u.Transferencia is { TipoCierre: clsTipoCierre.RECIBO_SIN_ENTREGA } cerrada ? cerrada.MotivoNoFirma : null,
                u.Transferencia?.RecibeExternoNombre,
                u.Transferencia?.RecibeExternoCedula))
            .ToList();
        var pendientes = filas.Count(f => f.Estado != "cerrada");
        return new EntregaPuntoDto(viaje.Id, viaje.Estado, lugarId, lugar.Nombre, lugar.Ciudad, filas.Count, pendientes, pendientes == 0, filas, lugarTieneInspector);
    }

    /// <summary>
    /// Avisa a los coordinadores de calidad (y administradores) del cliente de la entrega con una sola firma: el viaje, el punto, quien cerro y el motivo. Una sola alerta por entrega y punto (la clave de dedupe lleva
    /// el viaje y el lugar). Ademas del texto lleva sus campos propios (<c>Datos</c>): el lugar, la parte que firmo y el motivo; la cedula de quien recibe nunca viaja en el aviso. Nunca lanza: un fallo al avisar no
    /// deshace la entrega, que ya quedo guardada.
    /// </summary>
    private async Task AvisarAsync(clsViaje viaje, Guid lugarId, List<Unidad> unidades, string motivo, Guid actorId, CancellationToken ct)
    {
        try
        {
            var clienteId = unidades[0].Vehiculo.ClienteId;
            var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], clienteId, ct);
            var autor = (await usuarios.ObtenerPorIdAsync(actorId, ct))?.Nombre ?? "El conductor";
            var lugar = (await LugaresAsync(ct)).TryGetValue(lugarId, out var datos) ? datos.Nombre : "el punto de entrega";
            var codigoViaje = viaje.CodigoMtrack is { } codigo ? $" (viaje {codigo})" : string.Empty;
            var vines = string.Join(", ", unidades.Select(u => u.Vehiculo.Vin));
            // La parte que firmo es la otra de la que no firmo (la transferencia ya quedo cerrada con una sola firma).
            var parteQueFirmo = unidades[0].Transferencia?.NoFirmaParte == clsParteFirmante.RECIBE ? clsParteFirmante.ENTREGA : clsParteFirmante.RECIBE;
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.NOVEDAD_PROCESO,
                    clsUrgenciaNotificacion.ALTA,
                    destinatarios.Select(d => d.Id).ToList(),
                    clienteId,
                    viaje.Id,
                    null,
                    null,
                    null,
                    $"Entrega con una sola firma: {lugar}",
                    $"{autor} cerró la entrega en {lugar}{codigoViaje} con una sola firma ({unidades.Count} unidades: {vines}). Motivo: {motivo}.",
                    $"{clsTipoNotificacion.NOVEDAD_PROCESO}:entrega:{viaje.Id}:{lugarId}",
                    new Dictionary<string, object?>
                    {
                        ["hecho"] = "entrega_con_una_firma",
                        ["lugar_id"] = lugarId,
                        ["lugar"] = lugar,
                        ["parte_que_firmo"] = parteQueFirmo,
                        ["motivo"] = motivo,
                        ["unidades"] = unidades.Count,
                    }),
                ct);
        }
        catch (Exception)
        {
            // El aviso es un efecto aparte: la entrega ya quedo guardada y no se deshace.
        }
    }
}
