using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Parametros;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Consultas de solo lectura de inspecciones (listado e informe detallado).</summary>
public interface IConsultasInspecciones
{
    /// <summary>
    /// Una pagina del listado y el total que cumple los filtros; con <paramref name="soloDeUsuario"/>, solo informes de los viajes de ese usuario y, con <paramref name="soloDeCliente"/>, solo los de VIN
    /// de ese cliente. Sin <paramref name="orden"/>, las mas recientes primero.
    /// </summary>
    Task<(IReadOnlyList<InspeccionListadoDto> Filas, int Total)> ListarAsync(
        FiltrosInspecciones filtros, Guid? soloDeUsuario, Guid? soloDeCliente, PaginaSolicitada pagina, OrdenSolicitado? orden, CancellationToken ct = default);

    /// <summary>El hallazgo tal como esta guardado (con su zona, nivel y evidencia), o nulo si no existe.</summary>
    Task<HallazgoDto?> ObtenerHallazgoAsync(Guid hallazgoId, CancellationToken ct = default);

    /// <summary>El historial de seguimiento del hallazgo, del mas antiguo al mas reciente.</summary>
    Task<IReadOnlyList<SeguimientoDto>> ObtenerHistorialAsync(Guid hallazgoId, CancellationToken ct = default);

    /// <summary>True si la inspeccion es de un VIN de ese cliente.</summary>
    Task<bool> EsDeClienteAsync(Guid inspeccionId, Guid clienteId, CancellationToken ct = default);

    Task<InformeDetalleDto?> ObtenerInformeAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Los hallazgos de las inspecciones cerradas anteriores del mismo VIN (de cualquier viaje, incluido uno del que la unidad no salio), del cierre mas reciente
    /// al mas antiguo. Una inspeccion corregida cuenta con su ultima version cerrada y no con la que reemplazo.
    /// </summary>
    Task<IReadOnlyList<HallazgoPrevioDto>> ObtenerHallazgosPreviosAsync(Guid inspeccionId, CancellationToken ct = default);
}

/// <summary>Arma el DTO de los hallazgos con lo que solo referencian: su zona, su nivel de severidad y si ya tienen su foto.</summary>
public interface IDescriptorHallazgos
{
    Task<IReadOnlyList<HallazgoDto>> DescribirAsync(IReadOnlyCollection<clsHallazgo> hallazgos, CancellationToken ct = default);
}

/// <summary>
/// Flujo de campo: crear la inspeccion de salida o de llegada de una etapa, agregar fotos y hallazgos (cada uno con su foto), registrar la
/// transferencia de custodia (firma parcial acumulada), cerrarla como evidencia inmutable y, si hay un error, corregirla con una version nueva.
/// </summary>
public sealed class clsInspeccionesService(
    IInspeccionRepository inspecciones,
    IEtapaRepository etapas,
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    clsFabricaEvidencias fabricaEvidencias,
    clsVerificadorEtapaPrevia etapaPrevia,
    clsResolutorZona resolutorZona,
    clsCalculadoraVistas calculadoraVistas,
    clsAlertaHallazgoCritico alertaCritica,
    IUsuarioRepository usuarios,
    IConsultasInspecciones consultas,
    IConsultasNovedadesProceso consultasNovedades,
    INovedadProcesoRepository novedades,
    clsEmisorNotificaciones emisor,
    IDescriptorHallazgos descriptorHallazgos,
    clsAccesoViajes acceso,
    IParametros parametros,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    ICatalogosLectura catalogos,
    IOptions<clsOpcionesSeguridad> seguridad,
    ILogger<clsInspeccionesService> registro,
    IValidator<CrearInspeccionRequest> validadorInspeccion,
    IValidator<CorregirInspeccionRequest> validadorCorreccion,
    IValidator<CrearHallazgoRequest> validadorHallazgo,
    IValidator<CrearEvidenciaRequest> validadorEvidencia)
{
    /// <summary>Los campos por los que se puede ordenar el listado (<c>orden=campo:asc|desc</c>).</summary>
    public static readonly IReadOnlySet<string> CAMPOS_ORDEN = new HashSet<string> { "fecha", "vin", "momento", "estado", "proceso", "version", "created_at", "cerrada_at" };

    public async Task<InspeccionDto> CrearAsync(CrearInspeccionRequest solicitud, CancellationToken ct = default)
    {
        await validadorInspeccion.ValidarOLanzarAsync(solicitud, ct);
        var etapa = await etapas.ObtenerPorIdAsync(solicitud.EtapaId!.Value, ct)
            ?? throw new clsEntidadNoEncontradaException("La etapa indicada no existe");
        var viaje = await viajes.ObtenerPorIdAsync(etapa.ViajeId, ct)
            ?? throw new clsEntidadNoEncontradaException("El viaje de la etapa no existe");

        // Inspectores y conductores solo inspeccionan los viajes en los que participan, y solo mientras el viaje siga activo.
        if (actual.SoloSusViajes() is { } usuarioViaje && !await acceso.ParticipaOReclamaAsync(viaje, usuarioViaje, ct))
        {
            throw new clsProhibidoException("El viaje de esta etapa no está asignado a su usuario");
        }

        if (!viaje.EstaActivo)
        {
            throw new clsConflictoException($"El viaje está {viaje.Estado} y no admite inspecciones nuevas");
        }

        // La salida de un Cargue la hace siempre el inspector: el conductor solo firma el recibo. Si no hay inspector, el conductor del viaje la hace activando en el momento la excepcion
        // «No hay inspector» (con su motivo); sin motivo es 403 y en cualquier otro caso el motivo no se admite (400).
        var porExcepcion = actual.Rol == clsRol.CONDUCTOR && solicitud.Momento == clsMomento.SALIDA && await EsCargueAsync(etapa, ct);
        var motivo = await ResolverMotivoAsync(solicitud, porExcepcion, ct);

        // La salida de una etapa solo se crea cuando la etapa anterior del VIN ya termino (su llegada esta cerrada).
        if (solicitud.Momento == clsMomento.SALIDA)
        {
            await etapaPrevia.AsegurarCerradaAsync(etapa, ct);
        }

        // El usuario siempre sale del token: el cliente no puede crear informes a nombre de otro.
        var inspeccion = clsInspeccion.Crear(
            etapa.Id,
            viaje.Id,
            actual.IdObligatorio(),
            solicitud.Momento!,
            solicitud.Fecha!.Value,
            solicitud.Lat,
            solicitud.Lng,
            null,
            solicitud.Notas,
            reloj.Ahora,
            motivo?.Codigo,
            motivo is null ? null : clsValidador.Opcional(solicitud.MotivoSinInspectorTexto));
        viaje.RegistrarActividad(reloj.Ahora);
        inspecciones.Agregar(inspeccion);
        await auditor.RegistrarDeViajeAsync(
            "inspeccion.crear", "inspecciones", inspeccion.Id, viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(inspeccion)), ct: ct);

        // La excepcion deja una novedad de proceso (con la fecha, lat y lng del momento en que el conductor eligio el motivo) y su registro en la auditoria, en la misma confirmacion.
        clsNovedadProceso? novedad = null;
        clsVehiculo? vehiculo = null;
        if (motivo is not null)
        {
            vehiculo = await vehiculos.ObtenerPorIdAsync(etapa.VehiculoId, ct) ?? throw new clsEntidadNoEncontradaException("El vehículo de la etapa no existe");
            novedad = clsNovedadProceso.Crear(
                clsTipoNovedadProceso.CARGUE_SIN_INSPECTOR,
                viaje.Id,
                vehiculo.Id,
                etapa.Id,
                inspeccion.Id,
                inspeccion.MotivoSinInspector,
                inspeccion.MotivoSinInspectorTexto,
                actual.IdObligatorio(),
                solicitud.Fecha!.Value,
                solicitud.Lat,
                solicitud.Lng,
                reloj.Ahora);
            novedades.Agregar(novedad);
            await auditor.RegistrarDeViajeAsync(
                "novedad_proceso.crear", "novedades_proceso", novedad.Id, viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(novedad)), vehiculo.ClienteId, ct);
        }

        // Un usuario inexistente (404) y una segunda inspeccion original del mismo momento en la etapa (409) se traducen al confirmar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (novedad is not null && vehiculo is not null)
        {
            await AvisarCargueSinInspectorAsync(novedad, vehiculo, viaje, motivo!, ct);
        }

        var conductor = viaje.ConductorId is { } conductorId ? (await usuarios.ObtenerPorIdAsync(conductorId, ct))?.Nombre : null;
        return clsMapeadorInspecciones.AInspeccion(inspeccion, viaje.Placa, viaje.Trailer, conductor, await consultasNovedades.DeInspeccionAsync(inspeccion.Id, ct));
    }

    /// <summary>
    /// El motivo del conductor que hace la salida de un Cargue con personal de apoyo porque el inspector previsto no esta. Sin motivo, 403 (el conductor solo firma el recibo); un motivo que no
    /// existe o esta inactivo, o un texto que falta o que el motivo no admite, 400 con <c>fieldErrors</c>. Fuera de esa salida el motivo y su texto no se admiten (400 «Campo no admitido»).
    /// </summary>
    private async Task<ValorCatalogo?> ResolverMotivoAsync(CrearInspeccionRequest solicitud, bool porExcepcion, CancellationToken ct)
    {
        if (!porExcepcion)
        {
            var errores = new Dictionary<string, string[]>();
            if (solicitud.MotivoSinInspector is not null)
            {
                errores["motivo_sin_inspector"] = ["Campo no admitido"];
            }

            if (solicitud.MotivoSinInspectorTexto is not null)
            {
                errores["motivo_sin_inspector_texto"] = ["Campo no admitido"];
            }

            return errores.Count > 0 ? throw clsErroresCampos.Varios(errores) : null;
        }

        var codigo = clsValidador.Opcional(solicitud.MotivoSinInspector);
        if (codigo is null)
        {
            throw new clsProhibidoException("La inspección de salida del cargue la hace un inspector; si no hay inspector, active la excepción");
        }

        var motivo = (await catalogos.ValoresAsync(clsNombresCatalogo.MOTIVOS_SIN_INSPECTOR, ct)).FirstOrDefault(m => m.Codigo == codigo && m.Activo)
            ?? throw clsErroresCampos.De("motivo_sin_inspector", "Motivo inválido: use el código de un motivo activo de motivos-sin-inspector");
        var texto = clsValidador.Opcional(solicitud.MotivoSinInspectorTexto);
        var requiereTexto = motivo.TryGetValue("requiere_texto", out var requiere) && requiere is true;
        if (requiereTexto && texto is null)
        {
            throw clsErroresCampos.De("motivo_sin_inspector_texto", "El motivo elegido exige escribir cuál es");
        }

        return !requiereTexto && texto is not null
            ? throw clsErroresCampos.De("motivo_sin_inspector_texto", "El motivo elegido no admite texto")
            : motivo;
    }

    /// <summary>
    /// El aviso de urgencia alta (bandeja y push) a los coordinadores de calidad y administradores activos del cliente del vehiculo, nunca de otro cliente. Un fallo al avisar no deshace la
    /// inspeccion ni la novedad, que ya quedaron guardadas.
    /// </summary>
    private async Task AvisarCargueSinInspectorAsync(clsNovedadProceso novedad, clsVehiculo vehiculo, clsViaje viaje, ValorCatalogo motivo, CancellationToken ct)
    {
        var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], vehiculo.ClienteId, ct);
        var autor = (await usuarios.ObtenerPorIdAsync(novedad.UsuarioId, ct))?.Nombre ?? "El conductor";
        var detalle = novedad.MotivoTexto is null ? string.Empty : $": {novedad.MotivoTexto}";
        var codigoViaje = viaje.CodigoMtrack is { } codigo ? $" (viaje {codigo})" : string.Empty;
        await emisor.EmitirAsync(
            new clsAlerta(
                clsTipoNotificacion.NOVEDAD_PROCESO,
                clsUrgenciaNotificacion.ALTA,
                destinatarios.Select(d => d.Id).ToList(),
                vehiculo.ClienteId,
                viaje.Id,
                vehiculo.Id,
                null,
                novedad.InspeccionId,
                $"Cargue sin inspector: {vehiculo.Vin}",
                $"{autor} hizo el cargue de la unidad {vehiculo.Vin}{codigoViaje} sin inspector. Motivo: {motivo["nombre"]}{detalle}.",
                $"{clsTipoNotificacion.NOVEDAD_PROCESO}:{novedad.Id}"),
            ct);
    }

    /// <summary>
    /// Corrige una inspeccion CERRADA: crea la version siguiente (en borrador y vacia) que referencia a la anterior; la anterior no cambia. Solo
    /// se corrige la ultima version y mientras el viaje siga activo. Quien corrige es quien participa en el viaje (o un administrador).
    /// </summary>
    public async Task<InspeccionDto> CorregirAsync(Guid inspeccionId, CorregirInspeccionRequest solicitud, CancellationToken ct = default)
    {
        await validadorCorreccion.ValidarOLanzarAsync(solicitud, ct);
        var original = await inspecciones.ObtenerPorIdAsync(inspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {inspeccionId} no encontrada");
        var viaje = await viajes.ObtenerPorIdAsync(original.ViajeId, ct)
            ?? throw new clsEntidadNoEncontradaException("El viaje de la inspección no existe");

        if (actual.SoloSusViajes() is { } usuarioViaje && !await acceso.ParticipaOReclamaAsync(viaje, usuarioViaje, ct))
        {
            throw new clsProhibidoException("El viaje de esta inspección no está asignado a su usuario");
        }

        if (!viaje.EstaActivo)
        {
            throw new clsConflictoException($"El viaje está {viaje.Estado} y no admite correcciones");
        }

        if (await inspecciones.ObtenerCorreccionIdAsync(inspeccionId, ct) is { } corregidaPor)
        {
            throw new clsConflictoException($"Esta inspección ya fue corregida (versión {corregidaPor}): corrija la última versión");
        }

        var ahora = reloj.Ahora;
        var nueva = original.Corregir(actual.IdObligatorio(), solicitud.Fecha ?? ahora, solicitud.Lat, solicitud.Lng, solicitud.Notas, ahora);
        viaje.RegistrarActividad(ahora);
        inspecciones.Agregar(nueva);
        await auditor.RegistrarDeViajeAsync(
            "inspeccion.corregir", "inspecciones", nueva.Id, viaje.Id, new DetalleAuditoria(clsInstantaneasAuditoria.De(original), clsInstantaneasAuditoria.De(nueva)), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        var conductor = viaje.ConductorId is { } conductorId ? (await usuarios.ObtenerPorIdAsync(conductorId, ct))?.Nombre : null;
        return clsMapeadorInspecciones.AInspeccion(nueva, viaje.Placa, viaje.Trailer, conductor, await consultasNovedades.DeInspeccionAsync(nueva.Id, ct));
    }

    /// <summary>Una pagina de informes: <c>{items, total, pagina, tamano}</c>. Quien se limita a su cliente solo ve el suyo (pedir otro responde 403); el resto puede elegir uno con <c>cliente_id</c>.</summary>
    public async Task<PaginaDto<InspeccionListadoDto>> ListarAsync(FiltrosInspeccionesRequest solicitud, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        var filtros = await LeerFiltrosAsync(solicitud, catalogos, errores, ct);
        var pagina = clsPaginacion.Pagina(solicitud.Pagina, solicitud.Tamano, errores);
        var orden = clsPaginacion.Orden(solicitud.Orden, CAMPOS_ORDEN, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var (filas, total) = await consultas.ListarAsync(filtros, actual.SoloSusViajes(), actual.ClienteDelFiltro(filtros.ClienteId), pagina, orden, ct);
        return new PaginaDto<InspeccionListadoDto>(filas, total, pagina.Pagina, pagina.Tamano);
    }

    /// <summary>
    /// Los hallazgos previos del VIN de la inspeccion, para que el cliente proponga un hallazgo nuevo como preexistente. Inspector, coordinador de calidad y administrador; el inspector
    /// solo los pide de inspecciones de sus viajes y quien se limita a su cliente, de VIN de su cliente (403 si es de otro).
    /// </summary>
    public async Task<IReadOnlyList<HallazgoPrevioDto>> ObtenerHallazgosPreviosAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        var inspeccion = await inspecciones.ObtenerPorIdAsync(inspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {inspeccionId} no encontrada");
        if (actual.SoloSusViajes() is { } usuarioViaje)
        {
            var viaje = await viajes.ObtenerPorIdAsync(inspeccion.ViajeId, ct);
            if (viaje is null || !await acceso.ParticipaOReclamaAsync(viaje, usuarioViaje, ct))
            {
                throw new clsProhibidoException("La inspección pertenece a un viaje que no está asignado a su usuario");
            }
        }

        await AsegurarClienteAsync(inspeccionId, ct);
        return await consultas.ObtenerHallazgosPreviosAsync(inspeccionId, ct);
    }

    /// <summary>
    /// Informe completo. Inspectores y conductores solo abren los informes de sus viajes y quien se limita a su cliente, los de VIN de su cliente (403 si es de otro). El conductor
    /// recibe sin hallazgos ni fotos de hallazgos los informes que no creo.
    /// </summary>
    public async Task<InformeDetalleDto> ObtenerInformeAsync(Guid id, CancellationToken ct = default)
    {
        var informe = await consultas.ObtenerInformeAsync(id, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {id} no encontrada");
        await AsegurarClienteAsync(id, ct);
        if (actual.SoloSusViajes() is { } usuarioViaje)
        {
            var viaje = await viajes.ObtenerPorIdAsync(informe.ViajeId, ct);
            if (viaje is null || !await acceso.ParticipaOReclamaAsync(viaje, usuarioViaje, ct))
            {
                throw new clsProhibidoException("La inspección pertenece a un viaje que no está asignado a su usuario");
            }
        }

        return informe.ParaConductor(actual);
    }

    /// <summary>
    /// Guarda el hallazgo con su zona, su clase, su tamano y su nivel de severidad, junto con su foto propia (un hallazgo sin foto no se guarda: 400). El
    /// origen sale del rol: el conductor registra novedades sin nivel; el inspector y el administrador siempre asignan el nivel.
    /// </summary>
    public async Task<HallazgoCreadoDto> AgregarHallazgoAsync(Guid inspeccionId, CrearHallazgoRequest solicitud, CancellationToken ct = default)
    {
        await ValidarHallazgoAsync(solicitud, ct);
        var inspeccion = await ObtenerParaEscrituraAsync(inspeccionId, ct);
        inspeccion.AsegurarEditable();
        var foto = solicitud.Evidencia!;

        var zona = await resolutorZona.ResolverAsync(inspeccionId, solicitud.CuadranteNumero, solicitud.Lado, solicitud.ZonaProvisional, ct);
        var clase = solicitud.ClaseDano!.Trim();
        if (solicitud.HallazgoOrigenId is { } origenId
            && !await inspecciones.EsHallazgoPrevioDelVehiculoAsync(origenId, inspeccionId, zona.Id, clase, ct))
        {
            throw clsErroresCampos.De(
                "hallazgo_origen_id", "El hallazgo de origen debe ser de una inspección cerrada anterior del mismo vehículo, con la misma zona y la misma clase de daño");
        }

        var hallazgo = clsHallazgo.Crear(
            inspeccionId,
            zona.Id,
            clase,
            solicitud.TamanoDano!,
            solicitud.NivelSeveridad,
            actual.Rol == clsRol.CONDUCTOR ? clsOrigenHallazgo.CONDUCTOR : clsOrigenHallazgo.INSPECTOR,
            solicitud.Nota,
            solicitud.HallazgoOrigenId,
            actual.IdObligatorio(),
            reloj.Ahora);
        var evidencia = await fabricaEvidencias.CrearAsync(
            inspeccionId, hallazgo.Id, null, esConstancia: false, foto.StoragePath!, foto.SelloFecha!.Value, foto.SelloLat, foto.SelloLng, foto.Precision,
            foto.DispositivoId, foto.Nota, foto.HashSha256, ct);
        inspecciones.Agregar(hallazgo);
        inspecciones.Agregar(evidencia);
        await auditor.RegistrarDeViajeAsync(
            "hallazgo.crear", "hallazgos", hallazgo.Id, inspeccion.ViajeId, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(hallazgo)), ct: ct);
        await auditor.RegistrarDeViajeAsync(
            "evidencia.crear", "evidencias", evidencia.Id, inspeccion.ViajeId, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(evidencia)), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.AHallazgoCreado((await descriptorHallazgos.DescribirAsync([hallazgo], ct))[0], evidencia);
    }

    public async Task<EvidenciaDto> AgregarEvidenciaAsync(Guid inspeccionId, CrearEvidenciaRequest solicitud, CancellationToken ct = default)
    {
        await validadorEvidencia.ValidarOLanzarAsync(solicitud, ct);
        var inspeccion = await ObtenerParaEscrituraAsync(inspeccionId, ct);
        inspeccion.AsegurarEditable();

        // Una foto es de una vista o de un hallazgo, no de las dos cosas.
        if (solicitud.HallazgoId is not null && solicitud.VistaId is not null)
        {
            throw clsErroresCampos.De("vista_id", "Una foto es de una vista o de un hallazgo, no de ambos");
        }

        if (solicitud.HallazgoId is { } hallazgoId
            && !await inspecciones.ExisteHallazgoEnInspeccionAsync(hallazgoId, inspeccionId, ct))
        {
            throw new clsEntidadNoEncontradaException("El hallazgo indicado no pertenece a esta inspección");
        }

        if (solicitud.VistaId is { } vistaId)
        {
            await ValidarVistaAsync(vistaId, solicitud.Nota, ct);
        }

        // Toda foto es de una vista o de un hallazgo; la unica sin ninguno de los dos es la de la acomodacion de la segunda inspeccion del cargue.
        var deAcomodacion = solicitud.HallazgoId is null && solicitud.VistaId is null;
        if (deAcomodacion && !await EsAcomodacionAsync(inspeccion, ct))
        {
            throw clsErroresCampos.De("vista_id", "Indique la vista de la foto (vista_id) o el hallazgo al que pertenece (hallazgo_id)");
        }

        var evidencia = deAcomodacion
            ? await fabricaEvidencias.CrearDeAcomodacionAsync(
                inspeccionId, solicitud.StoragePath!, solicitud.SelloFecha!.Value, solicitud.SelloLat, solicitud.SelloLng, solicitud.Precision, solicitud.DispositivoId, solicitud.Nota, solicitud.HashSha256, ct)
            : await fabricaEvidencias.CrearAsync(
                inspeccionId, solicitud.HallazgoId, solicitud.VistaId, esConstancia: false, solicitud.StoragePath!, solicitud.SelloFecha!.Value, solicitud.SelloLat,
                solicitud.SelloLng, solicitud.Precision, solicitud.DispositivoId, solicitud.Nota, solicitud.HashSha256, ct);
        inspecciones.Agregar(evidencia);
        await auditor.RegistrarDeViajeAsync(
            "evidencia.crear", "evidencias", evidencia.Id, inspeccion.ViajeId, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(evidencia)), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.AEvidencia(evidencia);
    }

    /// <summary>
    /// Cierra la inspeccion como evidencia inmutable. En Cargue y Descargue exige la foto de cada vista obligatoria (segun el tipo del
    /// vehiculo; 400 con el nombre de la que falta), una foto propia por hallazgo y que toda foto tenga ubicacion; las fotos adicionales
    /// (baul y cojineria) no bloquean. En Transporte (registro del conductor) no exige las vistas, pero si el GPS del registro.
    /// </summary>
    public async Task<CerrarInspeccionResponse> CerrarAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        var inspeccion = await ObtenerParaEscrituraAsync(inspeccionId, ct);
        var resumen = await inspecciones.ObtenerResumenCierreAsync(inspeccionId, ct);
        var etapa = await etapas.ObtenerPorIdAsync(inspeccion.EtapaId, ct)
            ?? throw new clsEntidadNoEncontradaException("La etapa de la inspección no existe");
        var codigoEtapa = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == etapa.EtapaCatalogoId)?.Codigo;
        // La segunda inspeccion del cargue (la salida del Cargue) documenta la acomodacion en la ninera: no exige las fotos de las vistas de estado (quedan solo en la primera) sino la posicion de cada
        // unidad activa del viaje y las fotos de la acomodacion.
        var esAcomodacion = EsSegundaInspeccionDelCargue(codigoEtapa, inspeccion);
        var exigeVistas = clsEtapaCatalogo.ExigeVistas(codigoEtapa) && !esAcomodacion;
        var faltantes = exigeVistas && !inspeccion.EstaCerrada ? await calculadoraVistas.FaltantesAsync(etapa, resumen, ct) : [];
        var faltantesDeAcomodacion = esAcomodacion && !inspeccion.EstaCerrada ? await FaltantesDeAcomodacionAsync(inspeccion, ct) : null;
        var antes = clsInstantaneasAuditoria.De(inspeccion);
        inspeccion.Cerrar(resumen, faltantes, exigeUbicacionPropia: !exigeVistas, reloj.Ahora, faltantesDeAcomodacion);
        await auditor.RegistrarDeViajeAsync(
            "inspeccion.cerrar", "inspecciones", inspeccion.Id, inspeccion.ViajeId, new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(inspeccion)), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        await AvisarHallazgosCriticosAsync(inspeccion, etapa, codigoEtapa, ct);
        return new CerrarInspeccionResponse(
            clsMapeadorInspecciones.AInspeccion(inspeccion, await consultasNovedades.DeInspeccionAsync(inspeccion.Id, ct)),
            new ResumenCierreDto(resumen.TotalEvidencias, resumen.TotalHallazgos));
    }

    /// <summary>
    /// Lo que le falta a la acomodacion para cerrar la segunda inspeccion del cargue: «Falta la posicion de la unidad {VIN}» por cada unidad activa del viaje sin posicion (las que no salen no la exigen) y las fotos de
    /// la acomodacion que faltan para llegar al minimo del parametro <c>acomodacion_fotos_minimas</c>.
    /// </summary>
    private async Task<IReadOnlyList<string>> FaltantesDeAcomodacionAsync(clsInspeccion inspeccion, CancellationToken ct)
    {
        var errores = new List<string>();
        var sinPosicion = new List<string>();
        foreach (var enlace in (await viajes.ObtenerVehiculosAsync(inspeccion.ViajeId, ct)).Where(v => !v.NoSale && v.Posicion is null))
        {
            var vehiculo = await vehiculos.ObtenerPorIdAsync(enlace.VehiculoId, ct);
            sinPosicion.Add(vehiculo?.Vin ?? enlace.VehiculoId.ToString());
        }

        errores.AddRange(sinPosicion.Order(StringComparer.Ordinal).Select(vin => $"Falta la posición de la unidad {vin}"));
        var minimas = Math.Max(await parametros.EnteroAsync(clsClavesParametros.ACOMODACION_FOTOS_MINIMAS, 1, ct), 0);
        var fotos = await inspecciones.ContarFotosLibresAsync(inspeccion.Id, ct);
        if (fotos < minimas)
        {
            errores.Add($"Faltan fotos de la acomodación: se exige al menos {minimas} y hay {fotos}");
        }

        return errores;
    }

    private static bool EsSegundaInspeccionDelCargue(string? codigoEtapa, clsInspeccion inspeccion) => codigoEtapa == clsCodigosEtapa.CARGUE && inspeccion.Momento == clsMomento.SALIDA;

    private async Task<bool> EsAcomodacionAsync(clsInspeccion inspeccion, CancellationToken ct)
    {
        var etapa = await etapas.ObtenerPorIdAsync(inspeccion.EtapaId, ct);
        var codigo = etapa is null ? null : (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == etapa.EtapaCatalogoId)?.Codigo;
        return EsSegundaInspeccionDelCargue(codigo, inspeccion);
    }

    /// <summary>True si la etapa es el Cargue (la etapa del catalogo con ese codigo).</summary>
    private async Task<bool> EsCargueAsync(clsEtapa etapa, CancellationToken ct) =>
        (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == etapa.EtapaCatalogoId)?.Codigo == clsCodigosEtapa.CARGUE;

    /// <summary>
    /// Al cerrarse la inspeccion, si tiene un hallazgo de nivel critico alerta al coordinador de calidad y a los administradores del cliente (una alerta por inspeccion). Un fallo al avisar no deshace el cierre.
    /// </summary>
    private async Task AvisarHallazgosCriticosAsync(clsInspeccion inspeccion, clsEtapa etapa, string? proceso, CancellationToken ct)
    {
        try
        {
            var vehiculo = await vehiculos.ObtenerPorIdAsync(etapa.VehiculoId, ct);
            if (vehiculo is not null)
            {
                await alertaCritica.EmitirSiHayCriticosAsync(
                    await inspecciones.ObtenerHallazgosDeInspeccionAsync(inspeccion.Id, ct), vehiculo, inspeccion.ViajeId, proceso ?? "la etapa", ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La inspeccion ya quedo cerrada: el aviso es un efecto secundario y no puede responder error por ella.
            registro.LogError(ex, "No se pudo avisar del hallazgo critico de la inspeccion {Inspeccion}", inspeccion.Id);
        }
    }

    /// <summary>La vista debe existir y estar activa; la foto de una vista adicional (baul, cojineria) exige la nota del faltante o dano.</summary>
    private async Task ValidarVistaAsync(Guid vistaId, string? nota, CancellationToken ct)
    {
        var vista = (await catalogos.ValoresAsync(clsNombresCatalogo.VISTAS_FOTO, ct)).FirstOrDefault(v => v.Id == vistaId)
            ?? throw clsErroresCampos.De("vista_id", "La vista no existe en el catálogo");
        if (!vista.Activo)
        {
            throw clsErroresCampos.De("vista_id", "La vista está inactiva");
        }

        if (vista["obligatoria"] is false && string.IsNullOrWhiteSpace(nota))
        {
            throw clsErroresCampos.De("nota", "La foto de una vista adicional exige una nota con el faltante o daño");
        }
    }

    /// <summary>Obtiene la inspeccion (404 si no existe) y verifica que el usuario pueda modificarla.</summary>
    private async Task<clsInspeccion> ObtenerParaEscrituraAsync(Guid inspeccionId, CancellationToken ct)
    {
        var inspeccion = await inspecciones.ObtenerPorIdAsync(inspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {inspeccionId} no encontrada");

        // Solo quien creo el informe (o un administrador) le agrega datos o lo cierra.
        if (seguridad.Value.ValidarPropiedadInspeccion && !actual.EsAdministrador() && inspeccion.UsuarioId != actual.Id)
        {
            throw new clsProhibidoException("No tiene permisos sobre esta inspección");
        }

        return inspeccion;
    }

    /// <summary>
    /// Valida el cuerpo y ademas que la clase y el tamano existan en el catalogo y esten activos (un valor inactivo ya no se ofrece para
    /// hallazgos nuevos). Los errores de ambas validaciones se devuelven juntos.
    /// </summary>
    private async Task ValidarHallazgoAsync(CrearHallazgoRequest solicitud, CancellationToken ct)
    {
        var deCatalogo = new Dictionary<string, string[]>();
        if (!string.IsNullOrWhiteSpace(solicitud.ClaseDano)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.CLASES_DANO, soloActivos: true, ct)).Contains(solicitud.ClaseDano))
        {
            deCatalogo["clase_dano"] = ["Clase de daño inválida"];
        }

        if (!string.IsNullOrWhiteSpace(solicitud.TamanoDano)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.TAMANOS_DANO, soloActivos: true, ct)).Contains(solicitud.TamanoDano))
        {
            deCatalogo["tamano_dano"] = ["Tamaño de daño inválido"];
        }

        if (!string.IsNullOrWhiteSpace(solicitud.NivelSeveridad)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.NIVELES_SEVERIDAD, soloActivos: true, ct)).Contains(solicitud.NivelSeveridad))
        {
            deCatalogo["nivel_severidad"] = ["Nivel de severidad inválido"];
        }

        try
        {
            await validadorHallazgo.ValidarOLanzarAsync(solicitud, ct);
        }
        catch (clsValidacionException ex) when (deCatalogo.Count > 0 && ex.Detalles is Dictionary<string, object> detalles
            && detalles.TryGetValue("fieldErrors", out var campos) && campos is Dictionary<string, string[]> porCampo)
        {
            foreach (var (campo, mensajes) in deCatalogo)
            {
                porCampo.TryAdd(campo, mensajes);
            }

            throw;
        }

        if (deCatalogo.Count > 0)
        {
            throw new clsValidacionException(
                "Datos inválidos",
                new Dictionary<string, object> { ["formErrors"] = Array.Empty<string>(), ["fieldErrors"] = deCatalogo });
        }
    }

    /// <summary>Quien se limita a su cliente solo abre inspecciones de VIN de su cliente (403 si es de otro).</summary>
    private async Task AsegurarClienteAsync(Guid inspeccionId, CancellationToken ct)
    {
        if (actual.ClienteRestringido() is { } cliente && !await consultas.EsDeClienteAsync(inspeccionId, cliente, ct))
        {
            throw new clsProhibidoException("La inspección es de un VIN de otro cliente");
        }
    }

    /// <summary>Los filtros del listado validados (la exportacion a Excel los usa tambien): todos los errores juntos, con 400.</summary>
    internal static async Task<FiltrosInspecciones> ParsearFiltrosAsync(FiltrosInspeccionesRequest s, ICatalogosLectura catalogos, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();
        var filtros = await LeerFiltrosAsync(s, catalogos, errores, ct);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        return filtros;
    }

    /// <summary>Lee los filtros acumulando los errores por campo (la validacion unica del VIN incluida) para responder todos juntos.</summary>
    private static async Task<FiltrosInspecciones> LeerFiltrosAsync(FiltrosInspeccionesRequest s, ICatalogosLectura catalogos, Dictionary<string, string[]> errores, CancellationToken ct)
    {
        var vin = clsParseoFiltros.Vin(s.Vin, "vin", errores);
        var usuarioId = clsParseoFiltros.Uuid(s.UsuarioId, "usuario_id", errores);
        var viajeId = clsParseoFiltros.Uuid(s.ViajeId, "viaje_id", errores);
        var momento = clsParseoFiltros.Catalogo(s.Momento, clsMomento.VALORES, "momento", errores);
        var estado = clsParseoFiltros.Catalogo(s.Estado, clsEstadoInspeccion.VALORES, "estado", errores);
        // El proceso es una etapa del catalogo; se aceptan tambien las inactivas porque hay informes historicos de ellas.
        var proceso = clsParseoFiltros.Catalogo(s.Proceso, await catalogos.CodigosAsync(clsNombresCatalogo.ETAPAS, soloActivos: false, ct), "proceso", errores);
        var operacion = clsParseoFiltros.Catalogo(s.Operacion, clsOperacion.VALORES, "operacion", errores);
        var clienteId = clsParseoFiltros.Uuid(s.ClienteId, "cliente_id", errores);
        var origenId = clsParseoFiltros.Uuid(s.OrigenId, "origen_id", errores);
        var destinoId = clsParseoFiltros.Uuid(s.DestinoId, "destino_id", errores);
        var desde = clsParseoFiltros.Fecha(s.Desde, "desde", errores);
        var hasta = clsParseoFiltros.Fecha(s.Hasta, "hasta", errores);
        return new FiltrosInspecciones(vin, usuarioId, viajeId, momento, estado, proceso, operacion, clsValidador.Opcional(s.Marca), clienteId, origenId, destinoId, desde, hasta);
    }
}
