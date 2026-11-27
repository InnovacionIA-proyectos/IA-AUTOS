using FluentValidation;
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
/// La entrega por parada: cada unidad del viaje se entrega en SU lugar de destino y, en cada parada, se firma una vez entre quien entrega (el conductor del viaje) y quien recibe: el inspector que tomo la entrega de ESA
/// parada (el reclamo es por parada) o, donde el punto no tiene inspectores, la persona del lugar con su nombre, su cedula y su firma. El inspector del viaje (quien cargo) no recibe por defecto ni tiene un trato aparte: solo
/// firma una parada si es de sus puntos y la tomo. La firma se guarda en la transferencia de cada unidad pendiente de la parada, con la misma firma, fecha e IP y la hora y el GPS del momento en que se firmo; las reservas de
/// quien recibe van por carro. Cada carro se entrega o se rechaza por una novedad de su revision de llegada (con el dano que lo motiva): el carro rechazado queda en el camion, a cargo del conductor, y avisa a los coordinadores.
/// Una sola firma no bloquea: se puede cerrar la entrega con una firma y su motivo codificado (con su evidencia), que deja la novedad de proceso y avisa a los coordinadores de calidad. Todo el orden del destino se hace cumplir:
/// el conductor marca «Llegue», se cierra la revision de llegada de cada carro y despues se firma la parada. El viaje pasa a finalizado cuando todas sus unidades activas tienen el Descargue cerrado y entregado.
/// </summary>
public sealed partial class clsEntregasService(
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IInspeccionRepository inspecciones,
    ITransferenciaCustodiaRepository transferencias,
    INovedadProcesoRepository novedades,
    ICatalogosLectura catalogos,
    IUsuarioRepository usuarios,
    clsEvidenciaDeHallazgos evidenciaDeHallazgos,
    clsFabricaEvidencias fabricaEvidencias,
    IValidator<FotoEvidenciaRequest> validadorFoto,
    clsEmisorNotificaciones emisor,
    IReclamoRepository reclamos,
    IEsperaInspectorPuntoRepository esperas,
    clsEsperaDelDestino plazos,
    clsAccesoViajes acceso,
    clsFinalizadorDelViaje finalizador,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj)
{
    private const string MENSAJE_SIN_RECEPTOR = "Este punto tiene inspector: pídele a un inspector del punto que tome la entrega en su app";
    private const string MENSAJE_RECHAZO_SIN_DANO = "Para no entregar un carro elige el daño de su revisión de llegada que lo motiva";
    private const string MOTIVO_DEL_RECHAZO = "rechazado_por_novedad";

    private sealed class Unidad(clsViajeVehiculo enlace, clsVehiculo vehiculo, clsEtapa? descargue, clsInspeccion? llegada, clsTransferenciaCustodia? transferencia, bool conductorLlego)
    {
        public clsViajeVehiculo Enlace { get; } = enlace;

        public clsVehiculo Vehiculo { get; } = vehiculo;

        public clsEtapa? Descargue { get; } = descargue;

        public clsInspeccion? Llegada { get; } = llegada;

        public clsTransferenciaCustodia? Transferencia { get; set; } = transferencia;

        /// <summary>La llegada de Transporte de la unidad esta confirmada: el conductor marco «Llegue».</summary>
        public bool ConductorLlego { get; } = conductorLlego;

        public bool Rechazada => Transferencia is { EstaRechazada: true };

        /// <summary>Entregada: su transferencia tiene cierre y no es un rechazo.</summary>
        public bool Cerrada => Transferencia is { EstaCerrada: true, EstaRechazada: false };

        /// <summary>Falta entregarla en la parada: ni esta entregada ni esta rechazada en ella.</summary>
        public bool Pendiente => !Cerrada && !Rechazada;
    }

    /// <summary>Quien recibe: un usuario, una persona externa con su nombre, su cedula y su firma (el punto no tiene inspector) o nadie (la entrega se cierra con la sola firma de quien entrega).</summary>
    private sealed record Receptor(Guid? UsuarioId, string? Nombre, string? Cedula, string? Firma)
    {
        public static readonly Receptor NADIE = new(null, null, null, null);

        public bool EsExterno => Nombre is not null;

        public static Receptor DeUsuario(Guid id) => new(id, null, null, null);
    }

    /// <summary>Con quien se firma una parada: quien entrega, quien recibe y el papel del usuario que actua.</summary>
    private sealed record Firmantes(Guid Entrega, Receptor Recibe, bool ConInspector, bool ActorRecibe, bool ActorEntrega, clsEsperaInspectorPunto? EsperaPlanB = null);

    // ---------------------------------------------------------------------------------------------- consulta

    /// <summary>Las entregas del viaje, una por lugar de destino. Conductor e inspector solo ven las de sus viajes (403).</summary>
    public async Task<EntregasDelViajeDto> ListarAsync(Guid viajeId, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        await AsegurarVisibleAsync(viaje, ct);
        var unidades = await CargarUnidadesAsync(viaje, ct);
        var lugares = await LugaresAsync(ct);
        var destinos = unidades.Where(u => u.Enlace.LugarDestinoId is not null).Select(u => u.Enlace.LugarDestinoId!.Value).ToList();
        var conInspector = await reclamos.LugaresConInspectoresAsync(destinos, ct);
        var tomadas = await reclamos.ObtenerActivosDeEntregaAsync(viaje.Id, ct);
        var puntos = new List<EntregaPuntoDto>();
        foreach (var grupo in unidades.Where(u => u.Enlace.LugarDestinoId is not null).GroupBy(u => u.Enlace.LugarDestinoId!.Value))
        {
            puntos.Add(await ArmarPuntoAsync(viaje, grupo.Key, [.. grupo], lugares, conInspector.Contains(grupo.Key), tomadas.FirstOrDefault(r => r.LugarId == grupo.Key), ct));
        }

        return new EntregasDelViajeDto(viaje.Id, viaje.Estado, unidades.Count(u => u.Enlace.LugarDestinoId is null), [.. puntos.OrderBy(p => p.Lugar, StringComparer.CurrentCultureIgnoreCase)]);
    }

    /// <summary>Las unidades del viaje que se entregan en un lugar, con lo que falta de cada una; 404 si ninguna unidad activa del viaje va a ese lugar.</summary>
    public async Task<EntregaPuntoDto> ObtenerPuntoAsync(Guid viajeId, Guid lugarId, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(viajeId, ct);
        await AsegurarVisibleAsync(viaje, ct);
        var unidades = await CargarUnidadesAsync(viaje, ct);
        return await ArmarPuntoDeAsync(viaje, lugarId, unidades, ct);
    }

    // ------------------------------------------------------------------------------------------------ firmas

    /// <summary>
    /// Firma la entrega de la parada: la firma del usuario autenticado (quien entrega o quien recibe) se guarda en la transferencia de cada unidad pendiente de la parada, con la misma firma, fecha e IP y la hora y el GPS del
    /// momento. Quien recibe es el inspector que tomo la entrega de ESA parada (409 <c>sin_receptor</c> si el punto tiene inspectores y nadie la tomo; 403 si el usuario no es el conductor ni el inspector de la parada) o,
    /// donde el punto no tiene inspectores, la persona del lugar: su nombre, su cedula y su firma son obligatorios (400 nombrando lo que falta). Con <c>rechazos</c> los carros se rechazan por un dano de su revision de llegada y
    /// con <c>reservas</c> quien recibe deja sus observaciones por carro. 409 si no hay unidades pendientes, si la revision de llegada de algun carro sigue abierta (<c>llegada_abierta</c>) o si un inspector firma antes de que el
    /// conductor marque «Llegue» (<c>conductor_sin_llegada</c>).
    /// </summary>
    public async Task<EntregaPuntoDto> FirmarAsync(Guid viajeId, Guid lugarId, FirmarEntregaRequest solicitud, ContextoCliente cliente, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        var firma = string.IsNullOrEmpty(solicitud.Firma) ? null : solicitud.Firma;
        if (firma is null)
        {
            errores["firma"] = ["La firma es requerida"];
        }

        var ahora = reloj.Ahora;
        var momento = clsMomentoDeFirmaOpcional.Leer(solicitud.FechaMomento, solicitud.Lat, solicitud.Lng, solicitud.PrecisionM, ahora, errores);
        var reservas = clsCargueNineraService.LimpiarReservas(solicitud.Reservas, errores);
        var rechazos = LimpiarRechazos(solicitud.Rechazos, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var viaje = await ObtenerViajeAsync(viajeId, ct);
        viaje.AsegurarActivo();
        var actorId = actual.IdObligatorio();
        var unidades = await CargarUnidadesAsync(viaje, ct);
        var delPunto = DelPunto(unidades, lugarId);
        var firmantes = await ResolverFirmantesAsync(viaje, lugarId, actorId, solicitud.RecibeNombre, solicitud.RecibeCedula, solicitud.FirmaRecibe, unaFirma: false, sinInspectorDelPunto: false, ct);
        var pendientes = PendientesConRevisionCerrada(delPunto);
        if (actual.Rol == clsRol.INSPECTOR && !delPunto.Any(u => u.ConductorLlego))
        {
            throw new clsConflictoException("El conductor aún no marca «Llegué» en esta parada", clsMotivosRechazo.CONDUCTOR_SIN_LLEGADA);
        }

        await AsegurarDanosCalificadosAsync(delPunto, firmantes, ct);

        VerificarReservas(reservas, pendientes, firmantes);
        var hallazgosDelRechazo = await VerificarRechazosAsync(rechazos, pendientes, firmantes, ct);

        var lugar = (await LugaresAsync(ct)).TryGetValue(lugarId, out var datosLugar) ? datosLugar.Nombre : string.Empty;
        var marcados = new List<(Unidad Unidad, Guid HallazgoId)>();
        foreach (var unidad in pendientes)
        {
            var transferencia = ObtenerOCrear(unidad, firmantes.Entrega, firmantes.Recibe, ahora);
            var antes = clsInstantaneasAuditoria.De(transferencia);
            Guid? rechazo = hallazgosDelRechazo.TryGetValue(unidad.Vehiculo.Vin, out var hallazgoId) ? hallazgoId : null;
            reservas.TryGetValue(unidad.Vehiculo.Vin, out var observaciones);
            if (firmantes.EsperaPlanB is not null && rechazo is null)
            {
                transferencia.MarcarSinInspectorDelPunto();
            }

            if (firmantes.Recibe.EsExterno)
            {
                // Quien entrega firma por si mismo y registra la firma de la persona del lugar, con sus reservas, que no se borran.
                transferencia.FirmarConMomento(actorId, firma, null, momento, cliente.Ip, ahora, porNinera: false, rechazo);
                transferencia.FirmarComoExterno(firmantes.Recibe.Firma, cliente.Ip, ahora, momento, observaciones);
            }
            else
            {
                transferencia.FirmarConMomento(actorId, firma, firmantes.ActorRecibe ? observaciones : null, momento, cliente.Ip, ahora, porNinera: false, rechazo);
            }

            await evidenciaDeHallazgos.RecalcularAsync(unidad.Llegada!.Id, transferencia.TipoCierre, ct);
            if (firmantes.Recibe.EsExterno)
            {
                // Quien recibio (nombre y cedula), cuando firmo y en que punto, en la misma entrada de auditoria.
                await auditor.RegistrarDeViajeAsync(
                    "transferencia.firmar_externo", "transferencias_custodia", transferencia.Id, viaje.Id,
                    new DetalleAuditoria(antes, new { transferencia = clsInstantaneasAuditoria.De(transferencia), punto = new { lugar_id = lugarId, lugar, vin = unidad.Vehiculo.Vin }, momento }), unidad.Vehiculo.ClienteId, ct);
            }
            else
            {
                await auditor.RegistrarDeViajeAsync(
                    "transferencia.firmar", "transferencias_custodia", transferencia.Id, viaje.Id,
                    new DetalleAuditoria(antes, new { transferencia = clsInstantaneasAuditoria.De(transferencia), momento }), unidad.Vehiculo.ClienteId, ct);
            }

            if (rechazo is { } hallazgo)
            {
                marcados.Add((unidad, hallazgo));
                await auditor.RegistrarDeViajeAsync(
                    "entrega.rechazar", "transferencias_custodia", transferencia.Id, viaje.Id,
                    new DetalleAuditoria(null, new { vin = unidad.Vehiculo.Vin, motivo = MOTIVO_DEL_RECHAZO, hallazgo_id = hallazgo, lugar_id = lugarId, lugar, marcado_por = actorId, momento = momento?.Momento ?? ahora, lat = momento?.Lat, lng = momento?.Lng }),
                    unidad.Vehiculo.ClienteId, ct);
            }
        }

        await CerrarEsperaAsync(firmantes.EsperaPlanB, viaje, lugarId, actorId, firmantes.Recibe.Nombre, ahora, delPunto[0].Vehiculo.ClienteId, ct);
        await FinalizarSiCompletoAsync(viaje, unidades, ct);
        await GuardarCerrandoEsperaAsync(firmantes.EsperaPlanB, viaje.Id, lugarId, ct);
        foreach (var (unidad, hallazgoId) in marcados)
        {
            await AvisarRechazoAsync(viaje, lugarId, unidad, hallazgoId, actorId, firmantes.ActorEntrega ? clsParteFirmante.ENTREGA : clsParteFirmante.RECIBE, ct);
        }

        return await ArmarPuntoDeAsync(viaje, lugarId, unidades, ct);
    }

    /// <summary>
    /// Cierra la entrega de la parada con UNA sola firma (la del usuario autenticado, que es quien entrega o quien recibe) y su motivo codificado: <c>se_nego_a_firmar</c> (exige al menos una foto), <c>no_habia_nadie</c> (la
    /// evidencia son las fotos de la revision de llegada de cada carro, que ya debe estar cerrada) o <c>sin_inspector_del_punto</c> (el cierre del plan B: solo el conductor, con la espera del inspector del punto de la parada activada y sus plazos cumplidos). Cada unidad pendiente queda cerrada como «recibo_sin_entrega» con la parte que
    /// no firmo, deja su novedad de proceso con el codigo del motivo y los coordinadores de calidad reciben un solo aviso por entrega y parada. 403 si no es el conductor ni el inspector de la parada; 409 si la otra parte ya firmo, si el
    /// punto tiene inspectores y nadie tomo la entrega (<c>sin_receptor</c>, tambien con <c>no_habia_nadie</c>) o si la revision de llegada de algun carro sigue abierta.
    /// </summary>
    public async Task<EntregaPuntoDto> CerrarConUnaFirmaAsync(Guid viajeId, Guid lugarId, CerrarEntregaUnaFirmaRequest solicitud, ContextoCliente cliente, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        var codigo = clsValidador.Opcional(solicitud.MotivoCodigo)?.ToLowerInvariant();
        if (codigo is null || !clsMotivoUnaFirma.VALORES.Contains(codigo))
        {
            errores["motivo_codigo"] = [$"El motivo es obligatorio: {string.Join(", ", clsMotivoUnaFirma.VALORES.Order())}"];
        }

        var texto = clsValidador.Opcional(solicitud.MotivoTexto);
        if (texto is { Length: > clsNovedadProceso.TEXTO_MAX })
        {
            errores["motivo_texto"] = [$"El texto del motivo admite máximo {clsNovedadProceso.TEXTO_MAX} caracteres"];
        }

        var testigoNombre = clsValidador.Opcional(solicitud.TestigoNombre);
        var testigoCedula = clsValidador.Opcional(solicitud.TestigoCedula);
        if (testigoNombre is not null || testigoCedula is not null)
        {
            foreach (var error in clsTransferenciaCustodia.ErroresDelExterno(testigoNombre, testigoCedula))
            {
                errores[error.Key.Replace("recibe_", "testigo_", StringComparison.Ordinal)] = [.. error.Value.Select(m => m.Replace("de quien recibe", "del testigo", StringComparison.Ordinal))];
            }
        }

        var fotos = solicitud.Fotos ?? [];
        if (codigo == clsMotivoUnaFirma.SE_NEGO_A_FIRMAR && fotos.Count == 0)
        {
            errores["fotos"] = ["Con este motivo la foto es obligatoria"];
        }

        var ahora = reloj.Ahora;
        var momento = clsMomentoDeFirmaOpcional.Leer(solicitud.FechaMomento, solicitud.Lat, solicitud.Lng, solicitud.PrecisionM, ahora, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        foreach (var foto in fotos)
        {
            await validadorFoto.ValidarOLanzarAsync(new FotoEvidenciaRequest(foto.StoragePath, foto.SelloFecha, foto.SelloLat, foto.SelloLng, foto.Precision, foto.DispositivoId, foto.Nota, foto.HashSha256), ct);
        }

        var viaje = await ObtenerViajeAsync(viajeId, ct);
        viaje.AsegurarActivo();
        var actorId = actual.IdObligatorio();
        var unidades = await CargarUnidadesAsync(viaje, ct);
        var delPunto = DelPunto(unidades, lugarId);
        var firmantes = await ResolverFirmantesAsync(viaje, lugarId, actorId, null, null, null, unaFirma: true, sinInspectorDelPunto: codigo == clsMotivoUnaFirma.SIN_INSPECTOR_DEL_PUNTO, ct);
        await AsegurarDanosCalificadosAsync(delPunto, firmantes, ct);
        if (firmantes.ConInspector && codigo == clsMotivoUnaFirma.NO_HABIA_NADIE)
        {
            throw new clsConflictoException(MENSAJE_SIN_RECEPTOR, clsMotivosRechazo.SIN_RECEPTOR);
        }

        var pendientes = PendientesConRevisionCerrada(delPunto);
        if (pendientes.Any(u => u.Transferencia?.RechazoHallazgoId is not null))
        {
            throw new clsConflictoException("Hay carros rechazados esperando la otra firma de la parada: firme la parada para dejar la constancia del rechazo");
        }

        var porVin = pendientes.ToDictionary(u => u.Vehiculo.Vin, StringComparer.OrdinalIgnoreCase);
        var ajenas = fotos.Select(f => clsValidador.Opcional(f.Vin) is { } v ? clsVin.Normalizar(v) : string.Empty).Where(v => v.Length == 0 || !porVin.ContainsKey(v)).Distinct().Order(StringComparer.Ordinal).ToList();
        if (ajenas.Count > 0)
        {
            throw clsErroresCampos.De("fotos", $"Cada foto debe traer el VIN de un carro pendiente de la parada. No lo son: {string.Join(", ", ajenas.Select(v => v.Length == 0 ? "(sin VIN)" : v))}");
        }

        var motivoEscrito = texto ?? clsMotivoUnaFirma.Etiqueta(codigo!);
        var lugar = (await LugaresAsync(ct)).TryGetValue(lugarId, out var datosLugar) ? datosLugar.Nombre : "el punto de entrega";
        var novedadesCreadas = new List<clsNovedadProceso>();
        var fotosCreadas = 0;
        foreach (var unidad in pendientes)
        {
            var transferencia = ObtenerOCrear(unidad, firmantes.Entrega, firmantes.Recibe, ahora);
            var antes = clsInstantaneasAuditoria.De(transferencia);
            if (firmantes.EsperaPlanB is not null)
            {
                transferencia.MarcarSinInspectorDelPunto();
            }

            transferencia.CerrarConUnaFirma(actorId, solicitud.Firma, motivoEscrito, cliente.Ip, ahora, momento, codigo, testigoNombre, testigoCedula);
            foreach (var foto in fotos.Where(f => string.Equals(clsVin.Normalizar(f.Vin!), unidad.Vehiculo.Vin, StringComparison.OrdinalIgnoreCase)))
            {
                var evidencia = await fabricaEvidencias.CrearAsync(
                    unidad.Llegada!.Id, null, null, esConstancia: true, foto.StoragePath!, foto.SelloFecha!.Value, foto.SelloLat, foto.SelloLng, foto.Precision, foto.DispositivoId, foto.Nota, foto.HashSha256, ct);
                inspecciones.Agregar(evidencia);
                fotosCreadas++;
                await auditor.RegistrarDeViajeAsync(
                    "evidencia.crear", "evidencias", evidencia.Id, viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(evidencia)), unidad.Vehiculo.ClienteId, ct);
            }

            await evidenciaDeHallazgos.RecalcularAsync(unidad.Llegada!.Id, transferencia.TipoCierre, ct);
            await auditor.RegistrarDeViajeAsync(
                "transferencia.sin_entrega", "transferencias_custodia", transferencia.Id, viaje.Id,
                new DetalleAuditoria(antes, new { transferencia = clsInstantaneasAuditoria.De(transferencia), motivo_codigo = codigo, momento }), unidad.Vehiculo.ClienteId, ct);

            // La novedad guarda el codigo del motivo; el texto escrito (si lo hay) queda en la transferencia, porque el catalogo solo admite texto con los motivos que lo requieren.
            var novedad = clsNovedadProceso.Crear(
                clsTipoNovedadProceso.RECIBO_SIN_ENTREGA, viaje.Id, unidad.Vehiculo.Id, unidad.Descargue!.Id, unidad.Llegada.Id, codigo, null, actorId, momento?.Momento ?? ahora, momento?.Lat, momento?.Lng, ahora);
            novedades.Agregar(novedad);
            novedadesCreadas.Add(novedad);
            await auditor.RegistrarDeViajeAsync(
                "novedad_proceso.crear", "novedades_proceso", novedad.Id, viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(novedad)), unidad.Vehiculo.ClienteId, ct);
        }

        await CerrarEsperaAsync(firmantes.EsperaPlanB, viaje, lugarId, actorId, null, ahora, delPunto[0].Vehiculo.ClienteId, ct);
        await FinalizarSiCompletoAsync(viaje, unidades, ct);
        await GuardarCerrandoEsperaAsync(firmantes.EsperaPlanB, viaje.Id, lugarId, ct);

        await AvisarUnaFirmaAsync(viaje, lugarId, lugar, pendientes, codigo!, motivoEscrito, actorId, fotosCreadas, testigoNombre, ct);
        return await ArmarPuntoDeAsync(viaje, lugarId, unidades, ct);
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

    private const string MENSAJE_EXTERNO_CON_INSPECTOR = "El punto de entrega tiene inspector asignado: la entrega la firma él, no una persona externa";

    /// <summary>
    /// Quien entrega y quien recibe en la parada. Entrega siempre el conductor del viaje. Si el punto tiene inspectores recibe el inspector que tomo la entrega de ESTA parada: sin reclamo es 409 <c>sin_receptor</c> (nunca se cae
    /// al inspector del viaje) y los campos de la persona externa se rechazan (400). Si no tiene inspectores recibe la persona del lugar: nombre, cedula y firma obligatorios (400 con lo que falta), registrada por el conductor; en
    /// el cierre con una sola firma (<paramref name="unaFirma"/>) nadie recibe. Solo hay una salida cuando el punto tiene inspectores y nadie tomo la entrega: el plan B, que el conductor tiene con la espera del inspector del punto
    /// activada y sus plazos cumplidos (X mas Y minutos desde su hora, evaluados aqui mismo): ahi recibe la persona del lugar o nadie, como en un punto sin inspectores; antes del plazo es 409 <c>plan_b_no_habilitado</c>. 403 si el
    /// usuario no es el conductor del viaje ni el inspector de la parada.
    /// </summary>
    private async Task<Firmantes> ResolverFirmantesAsync(
        clsViaje viaje, Guid lugarId, Guid actorId, string? recibeNombre, string? recibeCedula, string? firmaRecibe, bool unaFirma, bool sinInspectorDelPunto, CancellationToken ct)
    {
        var esConductor = viaje.ConductorId == actorId;
        var conInspector = await reclamos.LugarTieneInspectoresAsync(lugarId, ct);
        var reclamo = conInspector ? await reclamos.ObtenerActivoAsync(viaje.Id, clsProcesoReclamo.ENTREGA, lugarId, ct) : null;
        if (!esConductor)
        {
            if (actual.Rol != clsRol.INSPECTOR)
            {
                throw new clsProhibidoException("Solo el conductor o el inspector que tomó la entrega firman: nadie actúa por otro", clsMotivosRechazo.VIAJE_NO_ASIGNADO);
            }

            if (!conInspector || !(await reclamos.LugaresDelInspectorAsync(actorId, ct)).Contains(lugarId))
            {
                throw new clsProhibidoException("La entrega de esta parada no es de ninguno de sus puntos", clsMotivosRechazo.NO_ES_SU_PUNTO);
            }

            if (reclamo is null)
            {
                throw new clsConflictoException("Primero tome la entrega de esta parada en su app: nadie la ha tomado", clsMotivosRechazo.SIN_RECEPTOR);
            }

            if (reclamo.InspectorId != actorId)
            {
                throw new clsProhibidoException("Esta entrega la tomó otro inspector", clsMotivosRechazo.ENTREGA_TOMADA);
            }
        }

        var nombre = clsValidador.Opcional(recibeNombre);
        var cedula = clsValidador.Opcional(recibeCedula);
        var firma = string.IsNullOrWhiteSpace(firmaRecibe) ? null : firmaRecibe;
        var traeExterno = nombre is not null || cedula is not null || firma is not null;

        // El plan B: el conductor, en un punto con inspectores donde nadie tomo la entrega, con la espera activada y sus plazos cumplidos.
        clsEsperaInspectorPunto? planB = null;
        if (conInspector && reclamo is null && esConductor && await esperas.ObtenerAbiertaAsync(viaje.Id, lugarId, ct) is { } abierta)
        {
            planB = await AsegurarPlanBHabilitadoAsync(abierta, ct);
        }

        if (sinInspectorDelPunto && planB is null)
        {
            throw !esConductor
                ? new clsConflictoException("Solo el conductor cierra la entrega sin inspector del punto", clsMotivosRechazo.MOTIVO_NO_APLICA)
                : !conInspector
                    ? new clsConflictoException("El motivo no aplica: esta parada no tiene inspectores asignados", clsMotivosRechazo.MOTIVO_NO_APLICA)
                    : reclamo is not null
                        ? new clsConflictoException("Un inspector del punto tomó la entrega: firma con él", clsMotivosRechazo.ENTREGA_TOMADA)
                        : new clsConflictoException(MENSAJE_SIN_RECEPTOR, clsMotivosRechazo.SIN_RECEPTOR);
        }

        if (conInspector && planB is null)
        {
            if (traeExterno)
            {
                if (reclamo is not null && esConductor && await esperas.ObtenerUltimaAsync(viaje.Id, lugarId, ct) is { Estado: clsEstadoEspera.INSPECTOR_LLEGO })
                {
                    throw new clsConflictoException("Un inspector del punto tomó la entrega: firma con él", clsMotivosRechazo.ENTREGA_TOMADA);
                }

                var rechazados = new Dictionary<string, string[]>();
                if (nombre is not null)
                {
                    rechazados["recibe_nombre"] = [MENSAJE_EXTERNO_CON_INSPECTOR];
                }

                if (cedula is not null)
                {
                    rechazados["recibe_cedula"] = [MENSAJE_EXTERNO_CON_INSPECTOR];
                }

                if (firma is not null)
                {
                    rechazados["firma_recibe"] = [MENSAJE_EXTERNO_CON_INSPECTOR];
                }

                throw clsErroresCampos.Varios(rechazados);
            }

            if (reclamo is null)
            {
                throw new clsConflictoException(MENSAJE_SIN_RECEPTOR, clsMotivosRechazo.SIN_RECEPTOR);
            }
        }

        var entrega = viaje.ConductorId ?? throw clsErroresCampos.De("entrega_usuario_id", "El viaje no tiene conductor: quien entrega es el conductor del viaje");
        if (conInspector && planB is null)
        {
            return new Firmantes(entrega, Receptor.DeUsuario(reclamo!.InspectorId), true, ActorRecibe: actorId == reclamo.InspectorId, ActorEntrega: esConductor);
        }

        if (unaFirma && !traeExterno)
        {
            return new Firmantes(entrega, Receptor.NADIE, false, ActorRecibe: false, ActorEntrega: true, planB);
        }

        var errores = new Dictionary<string, string[]>();
        foreach (var error in clsTransferenciaCustodia.ErroresDelExterno(nombre, cedula))
        {
            errores[error.Key] = error.Value;
        }

        if (firma is null)
        {
            errores["firma_recibe"] = ["La firma de quien recibe es obligatoria"];
        }

        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        return new Firmantes(entrega, new Receptor(null, nombre, cedula, firma), false, ActorRecibe: false, ActorEntrega: true, planB);
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
                throw new clsConflictoException("Los firmantes de la transferencia no se pueden cambiar", clsMotivosRechazo.FIRMANTES_DE_LA_TRANSFERENCIA);
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
    /// Las unidades activas del viaje con el Descargue, su ULTIMA inspeccion de llegada y su transferencia, y si el conductor ya marco «Llegue» (la llegada de Transporte cerrada). Una unidad que no sale del viaje ya no se
    /// entrega y no aparece. Un carro rechazado al que despues se le cambio el destino vuelve a estar pendiente: su rechazo queda como evidencia y espera su revision nueva.
    /// </summary>
    private async Task<List<Unidad>> CargarUnidadesAsync(clsViaje viaje, CancellationToken ct)
    {
        var codigosPorEtapa = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).ToDictionary(v => v.Id, v => v.Codigo);
        var etapas = await viajes.ObtenerEtapasAsync(viaje.Id, null, ct);
        var resultado = new List<Unidad>();
        foreach (var enlace in (await viajes.ObtenerVehiculosAsync(viaje.Id, ct)).Where(e => e.EstaActiva))
        {
            var vehiculo = await vehiculos.ObtenerPorIdAsync(enlace.VehiculoId, ct) ?? throw new clsEntidadNoEncontradaException("El vehículo del viaje no existe");
            clsEtapa? EtapaDe(string codigoBuscado) => etapas.FirstOrDefault(e => e.VehiculoId == enlace.VehiculoId && codigosPorEtapa.TryGetValue(e.EtapaCatalogoId, out var codigo) && codigo == codigoBuscado);
            var descargue = EtapaDe(clsCodigosEtapa.DESCARGUE);
            var llegada = descargue is null ? null : await inspecciones.ObtenerUltimaDeEtapaAsync(descargue.Id, clsMomento.LLEGADA, ct);
            var transferencia = llegada is null ? null : await transferencias.ObtenerPorInspeccionAsync(llegada.Id, ct);
            if (transferencia is { EstaRechazada: true, FirmadaAt: { } rechazadoAt } && enlace.LugarDestinoCambiadoAt is { } cambiadoAt && cambiadoAt > rechazadoAt)
            {
                llegada = null;
                transferencia = null;
            }

            var transporte = EtapaDe(clsCodigosEtapa.TRANSPORTE);
            var llegueDelConductor = transporte is not null && await inspecciones.ObtenerUltimaDeEtapaAsync(transporte.Id, clsMomento.LLEGADA, ct) is { EstaCerrada: true };
            resultado.Add(new Unidad(enlace, vehiculo, descargue, llegada, transferencia, llegueDelConductor));
        }

        return resultado;
    }

    /// <summary>Las unidades que se entregan en el lugar (404 si ninguna).</summary>
    private static List<Unidad> DelPunto(List<Unidad> unidades, Guid lugarId)
    {
        var delPunto = unidades.Where(u => u.Enlace.LugarDestinoId == lugarId).ToList();
        return delPunto.Count > 0 ? delPunto : throw new clsEntidadNoEncontradaException("Ninguna unidad activa del viaje se entrega en ese lugar");
    }

    /// <summary>
    /// Las pendientes de la parada (409 si no hay), todas con la revision de llegada del Descargue CERRADA, que es donde se firma (409 <c>llegada_abierta</c> nombrando el VIN si alguna esta abierta o aun no existe).
    /// </summary>
    private static List<Unidad> PendientesConRevisionCerrada(List<Unidad> delPunto)
    {
        var pendientes = delPunto.Where(u => u.Pendiente).OrderBy(u => u.Vehiculo.Vin, StringComparer.Ordinal).ToList();
        if (pendientes.Count == 0)
        {
            throw new clsConflictoException("No hay unidades pendientes de entrega en este punto");
        }

        if (pendientes.FirstOrDefault(u => u.Llegada is not { EstaCerrada: true } || u.Descargue is null) is { } abierta)
        {
            throw new clsConflictoException($"Termina la revisión de llegada del VIN {abierta.Vehiculo.Vin} antes de firmar", clsMotivosRechazo.LLEGADA_ABIERTA);
        }

        return pendientes;
    }

    /// <summary>Las reservas deben ser de carros pendientes de la parada (400 nombrando los que no) y solo las deja quien recibe (403); con persona del lugar las deja quien entrega, en su nombre.</summary>
    private static void VerificarReservas(Dictionary<string, string> reservas, List<Unidad> pendientes, Firmantes firmantes)
    {
        if (reservas.Count == 0)
        {
            return;
        }

        var activos = pendientes.Select(u => u.Vehiculo.Vin).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ajenos = reservas.Keys.Where(vin => !activos.Contains(vin)).Order(StringComparer.Ordinal).ToList();
        if (ajenos.Count > 0)
        {
            throw clsErroresCampos.De("reservas", $"No son carros pendientes de esta parada: {string.Join(", ", ajenos)}");
        }

        if (!firmantes.ActorRecibe && !firmantes.Recibe.EsExterno)
        {
            throw new clsProhibidoException("Solo quien recibe deja reservas");
        }
    }

    /// <summary>Los rechazos del cuerpo: un VIN por rechazo, sin repetir (400); el dano es opcional aqui y se exige al verificar contra la revision de llegada.</summary>
    private static Dictionary<string, Guid?> LimpiarRechazos(IReadOnlyList<RechazoDeEntregaRequest>? rechazos, Dictionary<string, string[]> errores)
    {
        var resultado = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        var problemas = new List<string>();
        foreach (var rechazo in rechazos ?? [])
        {
            var vin = clsValidador.Opcional(rechazo.Vin) is { } v ? clsVin.Normalizar(v) : null;
            if (vin is null)
            {
                problemas.Add("Cada rechazo debe traer el VIN del carro");
            }
            else if (!resultado.TryAdd(vin, rechazo.HallazgoId))
            {
                problemas.Add($"El VIN {vin} tiene más de un rechazo");
            }
        }

        if (problemas.Count > 0)
        {
            errores["rechazos"] = [.. problemas];
        }

        return resultado;
    }

    /// <summary>
    /// Verifica los rechazos y devuelve VIN -> dano que lo motiva. Solo los manda el inspector que tomo la entrega o, donde no hay inspectores, el conductor (403). Cada VIN debe ser de la parada y estar pendiente (400 nombrandolo);
    /// el carro sin ningun dano con foto en su revision de llegada no se puede rechazar (409 <c>sin_novedad_para_rechazar</c>); el dano debe ser de esa revision y tener foto (400).
    /// </summary>
    private async Task<Dictionary<string, Guid>> VerificarRechazosAsync(Dictionary<string, Guid?> rechazos, List<Unidad> pendientes, Firmantes firmantes, CancellationToken ct)
    {
        var resultado = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (rechazos.Count == 0)
        {
            return resultado;
        }

        if (firmantes.ConInspector ? !firmantes.ActorRecibe : !firmantes.ActorEntrega)
        {
            throw new clsProhibidoException(firmantes.ConInspector ? "Solo el inspector que tomó la entrega de esta parada marca los rechazos" : "Solo el conductor marca los rechazos donde el punto no tiene inspectores");
        }

        var porVin = pendientes.ToDictionary(u => u.Vehiculo.Vin, StringComparer.OrdinalIgnoreCase);
        var ajenos = rechazos.Keys.Where(vin => !porVin.ContainsKey(vin)).Order(StringComparer.Ordinal).ToList();
        if (ajenos.Count > 0)
        {
            throw clsErroresCampos.De("rechazos", $"No son carros pendientes de esta parada (o ya están entregados): {string.Join(", ", ajenos)}");
        }

        foreach (var (vin, hallazgoId) in rechazos.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            var llegada = porVin[vin].Llegada!;
            var hallazgos = (await inspecciones.ObtenerHallazgosDeInspeccionAsync(llegada.Id, ct)).Select(h => h.Id).ToList();
            var conFoto = await inspecciones.HallazgosConFotoAsync(hallazgos, ct);
            if (conFoto.Count == 0)
            {
                throw new clsConflictoException($"El VIN {porVin[vin].Vehiculo.Vin} no tiene un daño que justifique el rechazo: entrégalo en esta parada", clsMotivosRechazo.SIN_NOVEDAD_PARA_RECHAZAR);
            }

            if (hallazgoId is not { } elegido || !conFoto.Contains(elegido))
            {
                throw clsErroresCampos.De("rechazos", $"{MENSAJE_RECHAZO_SIN_DANO} (VIN {porVin[vin].Vehiculo.Vin})");
            }

            resultado[porVin[vin].Vehiculo.Vin] = elegido;
        }

        return resultado;
    }

    /// <summary>El viaje en curso pasa a finalizado cuando todas sus unidades activas tienen el Descargue cerrado y entregado (inspeccion de llegada cerrada y transferencia con cierre, no rechazado); con entregas pendientes o carros rechazados sigue en curso.</summary>
    private async Task FinalizarSiCompletoAsync(clsViaje viaje, List<Unidad> unidades, CancellationToken ct)
    {
        if (viaje.Estado != clsEstadoViaje.EN_CURSO || unidades.Count == 0 || !unidades.All(u => u.Llegada is { EstaCerrada: true } && u.Cerrada))
        {
            return;
        }

        await finalizador.FinalizarAsync(viaje, ct);
    }

    private async Task<IReadOnlyDictionary<Guid, (string Nombre, string? Ciudad)>> LugaresAsync(CancellationToken ct) =>
        (await catalogos.ValoresAsync(clsNombresCatalogo.LUGARES, ct)).ToDictionary(
            l => l.Id, l => (l.TryGetValue("nombre", out var nombre) ? nombre as string ?? string.Empty : string.Empty, l.TryGetValue("ciudad", out var ciudad) ? ciudad as string : null));

    private async Task<EntregaPuntoDto> ArmarPuntoDeAsync(clsViaje viaje, Guid lugarId, List<Unidad> unidades, CancellationToken ct)
    {
        var delPunto = DelPunto(unidades, lugarId);
        var conInspector = await reclamos.LugarTieneInspectoresAsync(lugarId, ct);
        var reclamo = conInspector ? await reclamos.ObtenerActivoAsync(viaje.Id, clsProcesoReclamo.ENTREGA, lugarId, ct) : null;
        return await ArmarPuntoAsync(viaje, lugarId, delPunto, await LugaresAsync(ct), conInspector, reclamo, ct);
    }

    private async Task<EntregaPuntoDto> ArmarPuntoAsync(
        clsViaje viaje, Guid lugarId, List<Unidad> delPunto, IReadOnlyDictionary<Guid, (string Nombre, string? Ciudad)> lugares, bool lugarTieneInspector, clsReclamo? reclamo, CancellationToken ct)
    {
        var lugar = lugares.TryGetValue(lugarId, out var datos) ? datos : (Nombre: string.Empty, Ciudad: null);
        var llegadas = delPunto.Where(u => u.Llegada is not null).Select(u => u.Llegada!.Id).ToList();
        var fotosDeCierre = await inspecciones.FotosDeConstanciaAsync(llegadas, ct);
        var hallazgosDeRechazo = delPunto.Where(u => u.Transferencia?.RechazoHallazgoId is not null).Select(u => u.Transferencia!.RechazoHallazgoId!.Value).ToList();
        var fotosDeRechazo = await inspecciones.PrimeraFotoDeHallazgosAsync(hallazgosDeRechazo, ct);
        var evidenciasDeRechazo = await inspecciones.EvidenciasPorIdAsync([.. fotosDeRechazo.Values], ct);
        var filas = new List<EntregaUnidadDto>();
        foreach (var u in delPunto.OrderBy(u => u.Vehiculo.Vin, StringComparer.Ordinal))
        {
            var t = u.Transferencia;
            var danosNuevos = u.Llegada is null ? 0 : (await inspecciones.ObtenerHallazgosDeInspeccionAsync(u.Llegada.Id, ct)).Count(h => !h.EsPreexistente);
            RechazoDeEntregaDto? rechazo = null;
            if (u.Rechazada && t!.RechazoHallazgoId is { } hallazgoId && t.RechazoMarcadoPor is { } marcadoPor)
            {
                var fotoId = fotosDeRechazo.TryGetValue(hallazgoId, out var foto) ? foto : (Guid?)null;
                rechazo = new RechazoDeEntregaDto(
                    hallazgoId, fotoId, marcadoPor, (await usuarios.ObtenerPorIdAsync(marcadoPor, ct))?.Nombre, MOTIVO_DEL_RECHAZO,
                    fotoId is { } id && evidenciasDeRechazo.TryGetValue(id, out var evidencia) ? clsFotosDelViaje.DeEvidencia(evidencia) : null);
            }

            var cerradaConUnaFirma = t is { TipoCierre: clsTipoCierre.RECIBO_SIN_ENTREGA };
            IReadOnlyList<clsEvidencia> fotosDelCierre = u.Llegada is { } llegada && fotosDeCierre.TryGetValue(llegada.Id, out var delCierre) ? delCierre : [];
            filas.Add(new EntregaUnidadDto(
                u.Vehiculo.Id,
                u.Vehiculo.Vin,
                u.Rechazada ? "rechazado" : u.Cerrada ? "cerrada" : u.Llegada is null ? "sin_inspeccion" : "pendiente",
                u.Llegada?.Id,
                t?.TipoCierre,
                t?.FirmaEntregaAt,
                t?.FirmaRecibeAt,
                t?.NoFirmaParte,
                cerradaConUnaFirma ? t!.MotivoNoFirma : null,
                t?.RecibeExternoNombre,
                t?.RecibeExternoCedula,
                cerradaConUnaFirma ? t!.MotivoCodigo ?? clsMotivoUnaFirma.OTRO : null,
                t?.ReservasTexto,
                fotosDelCierre.Count,
                t?.TestigoNombre,
                DeMomento(t?.FirmaEntregaMomentoAt, t?.FirmaEntregaLat, t?.FirmaEntregaLng, t?.FirmaEntregaPrecisionM),
                DeMomento(t?.FirmaRecibeMomentoAt, t?.FirmaRecibeLat, t?.FirmaRecibeLng, t?.FirmaRecibePrecisionM),
                danosNuevos,
                rechazo,
                t is { SinInspectorDelPunto: true } && u.Cerrada,
                [.. fotosDelCierre.Select(f => clsFotosDelViaje.DeEvidencia(f))]));
        }

        var entregadas = delPunto.Count(u => u.Cerrada);
        var rechazadas = delPunto.Count(u => u.Rechazada);
        var pendientes = delPunto.Count(u => u.Pendiente);
        var estado = pendientes > 0 ? "pendiente" : rechazadas == 0 ? "entregada" : entregadas > 0 ? "entregada_con_rechazos" : "firmada_todos_rechazados";
        string receptor;
        string? nombre = null;
        if (lugarTieneInspector)
        {
            // Donde el conductor cerro con el plan B recibio la persona del lugar: su nombre queda en la parada aunque tenga inspectores.
            var externo = reclamo is null ? delPunto.Select(u => u.Transferencia?.RecibeExternoNombre).FirstOrDefault(n => n is not null) : null;
            receptor = reclamo is not null ? "inspector_del_punto" : externo is not null ? "persona_del_lugar" : "sin_receptor";
            nombre = reclamo is null ? externo : (await usuarios.ObtenerPorIdAsync(reclamo.InspectorId, ct))?.Nombre;
        }
        else
        {
            receptor = "persona_del_lugar";
            nombre = delPunto.Select(u => u.Transferencia?.RecibeExternoNombre).FirstOrDefault(n => n is not null);
        }

        return new EntregaPuntoDto(
            viaje.Id, viaje.Estado, lugarId, lugar.Nombre, lugar.Ciudad, filas.Count, pendientes, pendientes == 0 && entregadas > 0, filas, lugarTieneInspector, estado, receptor, nombre, delPunto.Any(u => u.ConductorLlego),
            lugarTieneInspector ? await EsperaDeLaParadaAsync(viaje.Id, lugarId, ct) : null, await plazos.AvisoMinutosAsync(ct), await plazos.PlanBMinutosAsync(ct), await DanosPorCalificarAsync(delPunto, ct));
    }

    private static MomentoDeFirmaDto? DeMomento(DateTimeOffset? momento, double? lat, double? lng, double? precisionM) =>
        momento is null ? null : new MomentoDeFirmaDto(momento, lat, lng, precisionM, lat is null || lng is null);

    /// <summary>
    /// Avisa a los coordinadores de calidad del cliente de un carro rechazado en su parada (urgencia alta): «Carro rechazado en destino: {lugar}», con el viaje, el VIN, la parada, el dano que lo motivo, la parte que lo marco y
    /// quien; ninguna cedula viaja. Nunca lanza: un fallo al avisar no deshace el rechazo, que ya quedo guardado.
    /// </summary>
    private async Task AvisarRechazoAsync(clsViaje viaje, Guid lugarId, Unidad unidad, Guid hallazgoId, Guid actorId, string parte, CancellationToken ct)
    {
        try
        {
            var clienteId = unidad.Vehiculo.ClienteId;
            var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD], clienteId, ct);
            var autor = (await usuarios.ObtenerPorIdAsync(actorId, ct))?.Nombre ?? "Quien firmó la entrega";
            var lugar = (await LugaresAsync(ct)).TryGetValue(lugarId, out var datos) ? datos.Nombre : "el punto de entrega";
            var codigoViaje = viaje.CodigoMtrack is { } codigo ? $" (viaje {codigo})" : string.Empty;
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.CARRO_RECHAZADO_EN_DESTINO,
                    clsUrgenciaNotificacion.ALTA,
                    destinatarios.Select(d => d.Id).ToList(),
                    clienteId,
                    viaje.Id,
                    unidad.Vehiculo.Id,
                    hallazgoId,
                    unidad.Llegada?.Id,
                    $"Carro rechazado en destino: {lugar}",
                    $"{autor} rechazó el carro {unidad.Vehiculo.Vin} en {lugar}{codigoViaje} por una novedad de su revisión de llegada. Sigue en el camión a cargo del conductor.",
                    $"{clsTipoNotificacion.CARRO_RECHAZADO_EN_DESTINO}:{viaje.Id}:{unidad.Vehiculo.Id}:{unidad.Llegada?.Id}",
                    new Dictionary<string, object?>
                    {
                        ["viaje_id"] = viaje.Id,
                        ["vin"] = unidad.Vehiculo.Vin,
                        ["lugar_id"] = lugarId,
                        ["lugar"] = lugar,
                        ["hallazgo_id"] = hallazgoId,
                        ["parte"] = parte,
                        ["marcado_por"] = autor,
                    }),
                ct);
        }
        catch (Exception)
        {
            // El aviso es un efecto aparte: el rechazo ya quedo guardado y no se deshace.
        }
    }

    /// <summary>
    /// Avisa a los coordinadores de calidad (y administradores) del cliente de la entrega con una sola firma: el viaje, la parada, quien cerro y el motivo. Una sola alerta por entrega y parada (la clave de dedupe lleva el viaje
    /// y el lugar). Ademas del texto lleva sus campos propios (<c>Datos</c>): el hecho, el lugar, la parte que firmo, el motivo con su codigo, cuantas fotos y el nombre del testigo; ninguna cedula viaja en el aviso. Nunca lanza:
    /// un fallo al avisar no deshace la entrega, que ya quedo guardada.
    /// </summary>
    private async Task AvisarUnaFirmaAsync(clsViaje viaje, Guid lugarId, string lugar, List<Unidad> unidades, string codigo, string motivo, Guid actorId, int fotos, string? testigo, CancellationToken ct)
    {
        try
        {
            var clienteId = unidades[0].Vehiculo.ClienteId;
            var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], clienteId, ct);
            var autor = (await usuarios.ObtenerPorIdAsync(actorId, ct))?.Nombre ?? "El conductor";
            var codigoViaje = viaje.CodigoMtrack is { } c ? $" (viaje {c})" : string.Empty;
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
                    $"{clsMotivoUnaFirma.Etiqueta(codigo)}: {lugar}",
                    $"{autor} cerró la entrega en {lugar}{codigoViaje} con una sola firma ({unidades.Count} unidades: {vines}). Motivo: {motivo}.",
                    $"{clsTipoNotificacion.NOVEDAD_PROCESO}:entrega:{viaje.Id}:{lugarId}",
                    new Dictionary<string, object?>
                    {
                        ["hecho"] = "entrega_con_una_firma",
                        ["lugar_id"] = lugarId,
                        ["lugar"] = lugar,
                        ["parte_que_firmo"] = parteQueFirmo,
                        ["motivo"] = motivo,
                        ["motivo_codigo"] = codigo,
                        ["unidades"] = unidades.Count,
                        ["fotos"] = fotos,
                        ["testigo"] = testigo,
                    }),
                ct);
        }
        catch (Exception)
        {
            // El aviso es un efecto aparte: la entrega ya quedo guardada y no se deshace.
        }
    }
}
