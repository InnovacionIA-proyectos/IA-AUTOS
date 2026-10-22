using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Inspecciones;
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
    /// Listado; con <paramref name="soloDeUsuario"/>, solo informes de los viajes de ese usuario y, con <paramref name="soloDeCliente"/>, solo los de VIN de ese cliente.
    /// </summary>
    Task<IReadOnlyList<InspeccionListadoDto>> ListarAsync(FiltrosInspecciones filtros, Guid? soloDeUsuario, Guid? soloDeCliente, int limite, CancellationToken ct = default);

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
    IDescriptorHallazgos descriptorHallazgos,
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
    public const int LIMITE_LISTADO = 200;

    public async Task<InspeccionDto> CrearAsync(CrearInspeccionRequest solicitud, CancellationToken ct = default)
    {
        await validadorInspeccion.ValidarOLanzarAsync(solicitud, ct);
        var etapa = await etapas.ObtenerPorIdAsync(solicitud.EtapaId!.Value, ct)
            ?? throw new clsEntidadNoEncontradaException("La etapa indicada no existe");
        var viaje = await viajes.ObtenerPorIdAsync(etapa.ViajeId, ct)
            ?? throw new clsEntidadNoEncontradaException("El viaje de la etapa no existe");

        // Inspectores y conductores solo inspeccionan los viajes en los que participan, y solo mientras el viaje siga activo.
        if (actual.SoloSusViajes() is { } usuarioViaje && !viaje.Participa(usuarioViaje))
        {
            throw new clsProhibidoException("El viaje de esta etapa no está asignado a su usuario");
        }

        if (!viaje.EstaActivo)
        {
            throw new clsConflictoException($"El viaje está {viaje.Estado} y no admite inspecciones nuevas");
        }

        // En un Cargue con personal de apoyo la salida la hace el inspector: el conductor solo firma el recibo.
        if (actual.Rol == clsRol.CONDUCTOR && solicitud.Momento == clsMomento.SALIDA && etapa.ConPersonalApoyo && await EsCargueAsync(etapa, ct))
        {
            throw new clsProhibidoException("Este cargue tiene personal de apoyo: el conductor solo firma el recibo");
        }

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
            reloj.Ahora);
        viaje.RegistrarActividad(reloj.Ahora);
        inspecciones.Agregar(inspeccion);
        await auditor.RegistrarDeViajeAsync(
            "inspeccion.crear", "inspecciones", inspeccion.Id, viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(inspeccion)), ct: ct);

        // Un usuario inexistente (404) y una segunda inspeccion original del mismo momento en la etapa (409) se traducen al confirmar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        var conductor = viaje.ConductorId is { } conductorId ? (await usuarios.ObtenerPorIdAsync(conductorId, ct))?.Nombre : null;
        return clsMapeadorInspecciones.AInspeccion(inspeccion, viaje.Placa, viaje.Trailer, conductor);
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

        if (actual.SoloSusViajes() is { } usuarioViaje && !viaje.Participa(usuarioViaje))
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
        return clsMapeadorInspecciones.AInspeccion(nueva, viaje.Placa, viaje.Trailer, conductor);
    }

    public async Task<IReadOnlyList<InspeccionListadoDto>> ListarAsync(FiltrosInspeccionesRequest solicitud, CancellationToken ct = default) =>
        await consultas.ListarAsync(await ParsearFiltrosAsync(solicitud, ct), actual.SoloSusViajes(), actual.ClienteRestringido(), LIMITE_LISTADO, ct);

    /// <summary>
    /// Los hallazgos previos del VIN de la inspeccion, para que el cliente proponga un hallazgo nuevo como preexistente. Solo inspector y administrador; el inspector
    /// solo los pide de inspecciones de sus viajes (403 si es de otro).
    /// </summary>
    public async Task<IReadOnlyList<HallazgoPrevioDto>> ObtenerHallazgosPreviosAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        var inspeccion = await inspecciones.ObtenerPorIdAsync(inspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {inspeccionId} no encontrada");
        if (actual.SoloSusViajes() is { } usuarioViaje)
        {
            var viaje = await viajes.ObtenerPorIdAsync(inspeccion.ViajeId, ct);
            if (viaje is null || !viaje.Participa(usuarioViaje))
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
            if (viaje is null || !viaje.Participa(usuarioViaje))
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

        var evidencia = await fabricaEvidencias.CrearAsync(
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
        var exigeVistas = clsEtapaCatalogo.ExigeVistas(codigoEtapa);
        var faltantes = exigeVistas && !inspeccion.EstaCerrada ? await calculadoraVistas.FaltantesAsync(etapa, resumen, ct) : [];
        var antes = clsInstantaneasAuditoria.De(inspeccion);
        inspeccion.Cerrar(resumen, faltantes, exigeUbicacionPropia: !exigeVistas, reloj.Ahora);
        await auditor.RegistrarDeViajeAsync(
            "inspeccion.cerrar", "inspecciones", inspeccion.Id, inspeccion.ViajeId, new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(inspeccion)), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        await AvisarHallazgosCriticosAsync(inspeccion, etapa, codigoEtapa, ct);
        return new CerrarInspeccionResponse(
            clsMapeadorInspecciones.AInspeccion(inspeccion),
            new ResumenCierreDto(resumen.TotalEvidencias, resumen.TotalHallazgos));
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

    private async Task<FiltrosInspecciones> ParsearFiltrosAsync(FiltrosInspeccionesRequest s, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();

        string? vin = null;
        if (!string.IsNullOrWhiteSpace(s.Vin))
        {
            vin = clsVin.Normalizar(s.Vin);
            if (!clsVin.EsFormatoIso(vin))
            {
                errores["vin"] = ["VIN inválido: 17 caracteres alfanuméricos, sin las letras I, O ni Q"];
            }
        }

        var usuarioId = clsParseoFiltros.Uuid(s.UsuarioId, "usuarioId", errores);
        var viajeId = clsParseoFiltros.Uuid(s.ViajeId, "viajeId", errores);
        var momento = clsParseoFiltros.Catalogo(s.Momento, clsMomento.VALORES, "momento", errores);
        var estado = clsParseoFiltros.Catalogo(s.Estado, clsEstadoInspeccion.VALORES, "estado", errores);
        // El proceso es una etapa del catalogo; se aceptan tambien las inactivas porque hay informes historicos de ellas.
        var proceso = clsParseoFiltros.Catalogo(s.Proceso, await catalogos.CodigosAsync(clsNombresCatalogo.ETAPAS, soloActivos: false, ct), "proceso", errores);
        var desde = clsParseoFiltros.Fecha(s.Desde, "desde", errores);
        var hasta = clsParseoFiltros.Fecha(s.Hasta, "hasta", errores);

        if (errores.Count > 0)
        {
            throw new clsValidacionException(
                "Datos inválidos",
                new Dictionary<string, object> { ["formErrors"] = Array.Empty<string>(), ["fieldErrors"] = errores });
        }

        return new FiltrosInspecciones(vin, usuarioId, viajeId, momento, estado, proceso, desde, hasta);
    }
}
