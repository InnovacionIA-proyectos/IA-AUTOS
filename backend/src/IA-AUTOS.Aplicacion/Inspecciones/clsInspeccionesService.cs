using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Consultas de solo lectura de inspecciones (listado e informe detallado).</summary>
public interface IConsultasInspecciones
{
    /// <summary>Listado; con <paramref name="soloDeUsuario"/>, solo informes de los viajes de ese usuario.</summary>
    Task<IReadOnlyList<InspeccionListadoDto>> ListarAsync(FiltrosInspecciones filtros, Guid? soloDeUsuario, int limite, CancellationToken ct = default);

    Task<InformeDetalleDto?> ObtenerInformeAsync(Guid id, CancellationToken ct = default);
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
    IUsuarioRepository usuarios,
    IConsultasInspecciones consultas,
    IDescriptorHallazgos descriptorHallazgos,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    ICatalogosLectura catalogos,
    IOptions<clsOpcionesSeguridad> seguridad,
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

        // La salida de una etapa solo se crea cuando la etapa anterior del VIN ya termino (su llegada esta cerrada).
        if (solicitud.Momento == clsMomento.SALIDA)
        {
            await AsegurarEtapaPreviaCerradaAsync(etapa, ct);
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
        auditor.Registrar("inspeccion.crear", "inspecciones", inspeccion.Id);

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
        auditor.Registrar("inspeccion.corregir", "inspecciones", nueva.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        var conductor = viaje.ConductorId is { } conductorId ? (await usuarios.ObtenerPorIdAsync(conductorId, ct))?.Nombre : null;
        return clsMapeadorInspecciones.AInspeccion(nueva, viaje.Placa, viaje.Trailer, conductor);
    }

    public async Task<IReadOnlyList<InspeccionListadoDto>> ListarAsync(FiltrosInspeccionesRequest solicitud, CancellationToken ct = default) =>
        await consultas.ListarAsync(await ParsearFiltrosAsync(solicitud, ct), actual.SoloSusViajes(), LIMITE_LISTADO, ct);

    /// <summary>Informe completo. Inspectores y conductores solo abren los informes de sus viajes (403 si es de otro).</summary>
    public async Task<InformeDetalleDto> ObtenerInformeAsync(Guid id, CancellationToken ct = default)
    {
        var informe = await consultas.ObtenerInformeAsync(id, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {id} no encontrada");
        if (actual.SoloSusViajes() is { } usuarioViaje)
        {
            var viaje = await viajes.ObtenerPorIdAsync(informe.ViajeId, ct);
            if (viaje is null || !viaje.Participa(usuarioViaje))
            {
                throw new clsProhibidoException("La inspección pertenece a un viaje que no está asignado a su usuario");
            }
        }

        return informe;
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

        var zona = await ResolverZonaAsync(inspeccionId, solicitud, ct);
        var clase = solicitud.ClaseDano!.Trim();
        if (solicitud.HallazgoOrigenId is { } origenId
            && !await inspecciones.EsHallazgoPrevioDelVehiculoAsync(origenId, inspeccionId, zona.Id, clase, ct))
        {
            throw clsErroresCampos.De(
                "hallazgo_origen_id", "El hallazgo de origen debe ser de otra inspección del mismo vehículo, con la misma zona y la misma clase de daño");
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
        auditor.Registrar("hallazgo.crear", "hallazgos", hallazgo.Id);
        auditor.Registrar("evidencia.crear", "evidencias", evidencia.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.AHallazgoCreado((await descriptorHallazgos.DescribirAsync([hallazgo], ct))[0], evidencia);
    }

    /// <summary>
    /// La zona del hallazgo: la numerada (cuadrante + lado, del cuadrante del tipo del vehiculo o del generico) o la provisional con ese codigo. 400 con
    /// el campo que corresponde si no existe o esta inactiva.
    /// </summary>
    private async Task<clsZonaDano> ResolverZonaAsync(Guid inspeccionId, CrearHallazgoRequest solicitud, CancellationToken ct)
    {
        if (solicitud.CuadranteNumero is { } numero)
        {
            var tipoVehiculo = await inspecciones.ObtenerTipoVehiculoAsync(inspeccionId, ct);
            return await inspecciones.ObtenerZonaNumeradaAsync(numero, solicitud.Lado, tipoVehiculo, ct)
                ?? throw clsErroresCampos.De("cuadrante_numero", "El cuadrante indicado no existe o está inactivo");
        }

        return await inspecciones.ObtenerZonaProvisionalAsync(solicitud.ZonaProvisional!, ct)
            ?? throw clsErroresCampos.De("zona_provisional", "La zona provisional indicada no existe o está inactiva");
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
        auditor.Registrar("evidencia.crear", "evidencias", evidencia.Id);
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
        var faltantes = exigeVistas && !inspeccion.EstaCerrada ? await VistasFaltantesAsync(etapa, resumen, ct) : [];
        inspeccion.Cerrar(resumen, faltantes, exigeUbicacionPropia: !exigeVistas, reloj.Ahora);
        auditor.Registrar("inspeccion.cerrar", "inspecciones", inspeccion.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new CerrarInspeccionResponse(
            clsMapeadorInspecciones.AInspeccion(inspeccion),
            new ResumenCierreDto(resumen.TotalEvidencias, resumen.TotalHallazgos));
    }

    /// <summary>
    /// La salida de una etapa exige que la etapa anterior del mismo VIN en el viaje este terminada: su inspeccion de llegada cerrada y ninguna
    /// transferencia suya sin tipo de cierre (409 ETAPA_PREVIA_SIN_CIERRE con la etapa que falta cerrar y el motivo). La primera etapa no tiene anterior.
    /// </summary>
    private async Task AsegurarEtapaPreviaCerradaAsync(clsEtapa etapa, CancellationToken ct)
    {
        var previa = await etapas.ObtenerPreviaAsync(etapa.ViajeId, etapa.VehiculoId, etapa.Orden, ct);
        if (previa is null)
        {
            return;
        }

        var motivo = !await inspecciones.TieneLlegadaCerradaAsync(previa.Id, ct)
            ? clsEtapaPreviaSinCierreException.MOTIVO_SIN_LLEGADA
            : await inspecciones.TieneTransferenciaSinCierreAsync(previa.Id, ct) ? clsEtapaPreviaSinCierreException.MOTIVO_TRANSFERENCIA_ABIERTA : null;
        if (motivo is null)
        {
            return;
        }

        var nombre = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == previa.EtapaCatalogoId);
        throw new clsEtapaPreviaSinCierreException(previa.Id, nombre?["nombre"] as string ?? nombre?.Codigo ?? "anterior", motivo);
    }

    /// <summary>Nombres de las vistas obligatorias (activas, del tipo del vehiculo o de cualquier tipo) que aun no tienen foto, en el orden del catalogo.</summary>
    private async Task<IReadOnlyList<string>> VistasFaltantesAsync(clsEtapa etapa, clsResumenCierre resumen, CancellationToken ct)
    {
        var vehiculo = await vehiculos.ObtenerPorIdAsync(etapa.VehiculoId, ct)
            ?? throw new clsEntidadNoEncontradaException("El vehículo de la etapa no existe");
        return (await catalogos.ValoresAsync(clsNombresCatalogo.VISTAS_FOTO, ct))
            .Where(v => v.Activo && v["obligatoria"] is true
                && (v["tipo_vehiculo"] is not string tipo || string.Equals(tipo, vehiculo.TipoVehiculo, StringComparison.OrdinalIgnoreCase))
                && !resumen.VistasFotografiadas.Contains(v.Id))
            .OrderBy(v => v["orden"] as int? ?? 0)
            .Select(v => v["nombre"] as string ?? v.Codigo ?? v.Id.ToString())
            .ToList();
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
