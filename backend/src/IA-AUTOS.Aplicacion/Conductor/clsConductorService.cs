using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Conductor.DTOConductor;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Conductor;

/// <summary>
/// Lo que hace el conductor en ruta con cada unidad de su viaje: registrar las novedades del recorrido y confirmar la llegada. Las dos viven en la etapa de Transporte del VIN: las
/// novedades van en su inspeccion de salida (que se crea al registrar la primera) y la llegada cierra esa salida y crea y cierra la inspeccion de llegada.
/// </summary>
public sealed class clsConductorService(
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IInspeccionRepository inspecciones,
    IUsuarioRepository usuarios,
    clsFabricaEvidencias fabricaEvidencias,
    clsVerificadorEtapaPrevia etapaPrevia,
    IDescriptorHallazgos descriptorHallazgos,
    ICatalogosLectura catalogos,
    clsEmisorNotificaciones emisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<RegistrarNovedadRequest> validadorNovedad,
    IValidator<RegistrarLlegadaRequest> validadorLlegada)
{
    /// <summary>La unidad de un viaje del conductor con su etapa de Transporte.</summary>
    private sealed record Unidad(clsViaje Viaje, clsVehiculo Vehiculo, clsEtapa Transporte);

    /// <summary>
    /// Guarda la novedad del conductor con su foto: la crea sobre la inspeccion de salida de Transporte del VIN (que crea en borrador, con la fecha y el GPS de la novedad, si aun no existe)
    /// y avisa a los coordinadores de calidad y administradores del cliente del vehiculo. 403 si el viaje no es suyo; 409 si el Cargue no esta cerrado (ETAPA_PREVIA_SIN_CIERRE), si la
    /// unidad no sale (UNIDAD_NO_SALE) o si la salida de Transporte ya se cerro.
    /// </summary>
    public async Task<HallazgoCreadoDto> RegistrarNovedadAsync(Guid viajeId, string vin, RegistrarNovedadRequest solicitud, CancellationToken ct = default)
    {
        await validadorNovedad.ValidarOLanzarAsync(solicitud, ct);
        if (!(await catalogos.CodigosAsync(clsNombresCatalogo.TAMANOS_DANO, soloActivos: true, ct)).Contains(solicitud.TamanoDano!.Trim()))
        {
            throw clsErroresCampos.De("tamano_dano", "Tamaño de daño inválido");
        }

        var unidad = await ResolverUnidadAsync(viajeId, vin, ct);
        var ahora = reloj.Ahora;
        var salida = await inspecciones.ObtenerUltimaDeEtapaAsync(unidad.Transporte.Id, clsMomento.SALIDA, ct);
        if (salida is null)
        {
            await etapaPrevia.AsegurarCerradaAsync(unidad.Transporte, ct);
            salida = clsInspeccion.Crear(
                unidad.Transporte.Id, unidad.Viaje.Id, actual.IdObligatorio(), clsMomento.SALIDA, solicitud.Fecha!.Value, solicitud.Lat, solicitud.Lng, null, null, ahora);
            inspecciones.Agregar(salida);
            await auditor.RegistrarDeViajeAsync(
                "inspeccion.crear", "inspecciones", salida.Id, unidad.Viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(salida)), unidad.Vehiculo.ClienteId, ct);
        }
        else
        {
            salida.AsegurarEditable();
        }

        var foto = solicitud.Evidencia!;
        var hallazgo = clsHallazgo.CrearNovedad(salida.Id, solicitud.TipoNovedad!, solicitud.TamanoDano!.Trim(), ahora);
        var evidencia = await fabricaEvidencias.CrearAsync(
            salida.Id, hallazgo.Id, null, esConstancia: false, foto.StoragePath!, foto.SelloFecha!.Value, foto.SelloLat, foto.SelloLng, foto.Precision, foto.DispositivoId, null,
            foto.HashSha256, ct);
        unidad.Viaje.RegistrarActividad(ahora);
        inspecciones.Agregar(hallazgo);
        inspecciones.Agregar(evidencia);
        await auditor.RegistrarDeViajeAsync(
            "novedad.crear", "hallazgos", hallazgo.Id, unidad.Viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(hallazgo)), unidad.Vehiculo.ClienteId, ct);
        await auditor.RegistrarDeViajeAsync(
            "evidencia.crear", "evidencias", evidencia.Id, unidad.Viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(evidencia)), unidad.Vehiculo.ClienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);

        await AvisarNovedadAsync(unidad, hallazgo, ct);
        return clsMapeadorInspecciones.AHallazgoCreado((await descriptorHallazgos.DescribirAsync([hallazgo], ct))[0], evidencia);
    }

    /// <summary>
    /// Confirma la llegada de la unidad: cierra la salida de Transporte si esta abierta y crea y cierra la llegada con la fecha y el GPS recibidos. Responde con la llegada y cuantas
    /// novedades registro el conductor. 409 LLEGADA_YA_CERRADA si ya estaba confirmada (un reintento del celular es un exito) y 409 UNIDAD_NO_SALE si la unidad no sale del viaje.
    /// </summary>
    public async Task<LlegadaDto> RegistrarLlegadaAsync(Guid viajeId, string vin, RegistrarLlegadaRequest solicitud, CancellationToken ct = default)
    {
        await validadorLlegada.ValidarOLanzarAsync(solicitud, ct);
        var unidad = await ResolverUnidadAsync(viajeId, vin, ct);
        var ahora = reloj.Ahora;
        var llegada = await inspecciones.ObtenerUltimaDeEtapaAsync(unidad.Transporte.Id, clsMomento.LLEGADA, ct);
        if (llegada is { EstaCerrada: true })
        {
            throw new clsLlegadaYaCerradaException(unidad.Vehiculo.Vin);
        }

        // La salida abierta (con las novedades) se cierra con la llegada: ya no se agregan novedades a esa unidad.
        var salida = await inspecciones.ObtenerUltimaDeEtapaAsync(unidad.Transporte.Id, clsMomento.SALIDA, ct);
        if (salida is { EstaCerrada: false })
        {
            var salidaAntes = clsInstantaneasAuditoria.De(salida);
            salida.Cerrar(await inspecciones.ObtenerResumenCierreAsync(salida.Id, ct), [], exigeUbicacionPropia: true, ahora);
            await auditor.RegistrarDeViajeAsync(
                "inspeccion.cerrar", "inspecciones", salida.Id, unidad.Viaje.Id, new DetalleAuditoria(salidaAntes, clsInstantaneasAuditoria.De(salida)), unidad.Vehiculo.ClienteId, ct);
        }

        object? llegadaAntes = null;
        if (llegada is null)
        {
            llegada = clsInspeccion.Crear(
                unidad.Transporte.Id, unidad.Viaje.Id, actual.IdObligatorio(), clsMomento.LLEGADA, solicitud.Fecha!.Value, solicitud.Lat, solicitud.Lng, null, null, ahora);
            inspecciones.Agregar(llegada);
            await auditor.RegistrarDeViajeAsync(
                "inspeccion.crear", "inspecciones", llegada.Id, unidad.Viaje.Id, new DetalleAuditoria(null, clsInstantaneasAuditoria.De(llegada)), unidad.Vehiculo.ClienteId, ct);
            llegada.Cerrar(new clsResumenCierre(0, 0, 0, 0, []), [], exigeUbicacionPropia: true, ahora);
        }
        else
        {
            llegadaAntes = clsInstantaneasAuditoria.De(llegada);
            llegada.Cerrar(await inspecciones.ObtenerResumenCierreAsync(llegada.Id, ct), [], exigeUbicacionPropia: true, ahora);
        }

        unidad.Viaje.RegistrarActividad(ahora);
        await auditor.RegistrarDeViajeAsync(
            "llegada.registrar", "inspecciones", llegada.Id, unidad.Viaje.Id, new DetalleAuditoria(llegadaAntes, clsInstantaneasAuditoria.De(llegada)), unidad.Vehiculo.ClienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);

        var novedades = salida is null ? 0 : await inspecciones.ContarNovedadesAsync(salida.Id, ct);
        var conductor = (await usuarios.ObtenerPorIdAsync(actual.IdObligatorio(), ct))?.Nombre;
        return new LlegadaDto(clsMapeadorInspecciones.AInspeccion(llegada, unidad.Viaje.Placa, unidad.Viaje.Trailer, conductor), novedades, novedades == 0);
    }

    /// <summary>
    /// El viaje es del conductor (403 si no), esta activo (409), el VIN esta en el viaje (404) y no se marco «no sale» (409 UNIDAD_NO_SALE), y la unidad tiene la etapa de Transporte (409).
    /// </summary>
    private async Task<Unidad> ResolverUnidadAsync(Guid viajeId, string vin, CancellationToken ct)
    {
        var viaje = await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");
        if (viaje.ConductorId != actual.Id)
        {
            throw new clsProhibidoException("El viaje no está asignado a su usuario");
        }

        viaje.AsegurarActivo();
        var vinNormalizado = clsVin.Normalizar(vin);
        var vehiculo = await vehiculos.ObtenerPorVinAsync(vinNormalizado, ct) ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vinNormalizado} no encontrado");
        var enlace = await viajes.ObtenerVehiculoAsync(viaje.Id, vehiculo.Id, ct) ?? throw new clsEntidadNoEncontradaException($"El VIN {vehiculo.Vin} no está en este viaje");
        if (enlace.NoSale)
        {
            throw new clsUnidadNoSaleException(vehiculo.Vin);
        }

        var transporteId = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Codigo == clsCodigosEtapa.TRANSPORTE)?.Id;
        var transporte = (await viajes.ObtenerEtapasAsync(viaje.Id, vehiculo.Id, ct)).FirstOrDefault(e => e.EtapaCatalogoId == transporteId)
            ?? throw new clsConflictoException($"La unidad {vehiculo.Vin} no tiene la etapa de Transporte en este viaje");
        return new Unidad(viaje, vehiculo, transporte);
    }

    /// <summary>La alerta de urgencia alta a los coordinadores de calidad y administradores activos del cliente del vehiculo (ninguno de otro cliente), por bandeja y push.</summary>
    private async Task AvisarNovedadAsync(Unidad unidad, clsHallazgo hallazgo, CancellationToken ct)
    {
        var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], unidad.Vehiculo.ClienteId, ct);
        var conductor = (await usuarios.ObtenerPorIdAsync(actual.IdObligatorio(), ct))?.Nombre ?? "El conductor";
        var viaje = unidad.Viaje.CodigoMtrack is { } codigo ? $" (viaje {codigo})" : string.Empty;
        await emisor.EmitirAsync(
            new clsAlerta(
                clsTipoNotificacion.NOVEDAD_CONDUCTOR,
                clsUrgenciaNotificacion.ALTA,
                destinatarios.Select(d => d.Id).ToList(),
                unidad.Vehiculo.ClienteId,
                unidad.Viaje.Id,
                unidad.Vehiculo.Id,
                hallazgo.Id,
                hallazgo.InspeccionId,
                $"Novedad en ruta: {unidad.Vehiculo.Vin}",
                $"{conductor} registró una novedad de tipo {TextoDeTipo(hallazgo.TipoNovedad)} (tamaño {hallazgo.TamanoDano}) en la unidad {unidad.Vehiculo.Vin}{viaje}.",
                $"{clsTipoNotificacion.NOVEDAD_CONDUCTOR}:{hallazgo.Id}"),
            ct);
    }

    private static string TextoDeTipo(string? tipo) => tipo?.Replace('_', ' ') ?? string.Empty;
}
