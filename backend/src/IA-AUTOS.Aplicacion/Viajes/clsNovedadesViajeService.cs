using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// La novedad del viaje: lo que el conductor del viaje o un inspector que participa reporta sobre el viaje y no sobre un carro (una falla del camion, un bloqueo de via, un carro que no llego al patio). No es un hallazgo ni
/// cuelga de una unidad: no crea ni toca la salida de transporte, no suma a las novedades ni a los danos de ningun carro, no cambia el estado del viaje ni marca que el camion salio. Guarda quien la reporto, su rol, el tipo
/// del catalogo <c>tipos-novedad-viaje</c>, el texto, la fecha y el GPS del celular (sin GPS se acepta y queda «sin ubicacion») y la foto sellada si la trae, y avisa a los coordinadores de calidad y administradores de cada
/// cliente con carros en el viaje con la urgencia del tipo. El <c>id_envio</c> del celular evita duplicarla al reenviarla.
/// </summary>
public sealed class clsNovedadesViajeService(
    IViajeRepository viajes,
    IReclamoRepository reclamos,
    IVehiculoRepository vehiculos,
    IUsuarioRepository usuarios,
    INovedadViajeRepository novedades,
    IConsultasNovedadesViaje consultas,
    ICatalogosLectura catalogos,
    clsFabricaEvidencias fabricaEvidencias,
    clsEmisorNotificaciones emisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<RegistrarNovedadViajeRequest> validador)
{
    /// <summary>
    /// Guarda la novedad del viaje y avisa. Responde la novedad y si se creo ahora: con un <c>id_envio</c> ya guardado devuelve la misma novedad (sin otra foto ni otro aviso) y, si el contenido es otro, 409 con el motivo
    /// <c>envio_repetido_distinto</c>. 400 con un tipo desconocido o inactivo, un tipo que no es para el rol de quien reporta, un texto vacio o de mas de 300 caracteres; 403 si no es el conductor del viaje ni un inspector que
    /// participa en ese proceso (motivo <c>inspector_sin_ese_proceso</c>); 409 si el viaje no esta activo.
    /// </summary>
    public async Task<(NovedadViajeDto Novedad, bool Creada)> RegistrarAsync(Guid viajeId, RegistrarNovedadViajeRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var viaje = await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");
        var usuarioId = actual.IdObligatorio();
        var rol = actual.Rol ?? string.Empty;
        var (proceso, lugarId) = await AutorizarAsync(viaje, usuarioId, rol, solicitud, ct);

        // Un reenvio de algo que ya se guardo responde lo mismo aunque el viaje ya no este activo: la cola del celular puede llegar tarde.
        if (solicitud.IdEnvio is { } idEnvio && await novedades.ObtenerPorEnvioAsync(viaje.Id, idEnvio, ct) is { } existente)
        {
            return (await RepetidaAsync(existente, usuarioId, solicitud, ct), false);
        }

        viaje.AsegurarActivo();
        var tipo = await TipoAsync(solicitud.Tipo!, rol, ct);
        clsFotoNovedadViaje? foto = null;
        if (solicitud.Evidencia is { } evidencia)
        {
            var hash = await fabricaEvidencias.HashDelArchivoAsync(evidencia.StoragePath!, evidencia.HashSha256, ct);
            foto = clsFotoNovedadViaje.Crear(evidencia.StoragePath!, evidencia.SelloFecha!.Value, evidencia.SelloLat, evidencia.SelloLng, evidencia.Precision, evidencia.DispositivoId, hash);
        }

        var novedad = clsNovedadViaje.Crear(
            viaje.Id, tipo.Codigo!, usuarioId, rol, proceso, lugarId, solicitud.Texto, solicitud.Fecha, solicitud.Lat, solicitud.Lng, solicitud.IdEnvio, foto, reloj.Ahora);
        novedades.Agregar(novedad);
        await auditor.RegistrarDeViajeAsync(
            "novedad_viaje.crear", "novedades_viaje", novedad.Id, viaje.Id, new DetalleAuditoria(
                null,
                new
                {
                    novedad_id = novedad.Id,
                    tipo = novedad.TipoCodigo,
                    rol = novedad.Rol,
                    proceso = novedad.Proceso,
                    lugar_id = novedad.LugarId,
                    fecha = novedad.Fecha,
                    sin_ubicacion = novedad.SinUbicacion,
                    con_foto = novedad.ConFoto,
                    id_envio = novedad.IdEnvio,
                }),
            ct: ct);
        try
        {
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }
        catch (clsConflictoException) when (solicitud.IdEnvio is not null)
        {
            // Dos envios del mismo identificador a la vez: gana el primero y el otro recibe la misma novedad.
            unidadDeTrabajo.DescartarCambios();
            if (await novedades.ObtenerPorEnvioAsync(viaje.Id, solicitud.IdEnvio.Value, ct) is { } ganadora)
            {
                return (await RepetidaAsync(ganadora, usuarioId, solicitud, ct), false);
            }

            throw;
        }

        var dto = await consultas.ObtenerAsync(novedad.Id, ct) ?? throw new clsErrorInternoException("La novedad del viaje no se pudo leer después de guardarla");
        await AvisarAsync(viaje, dto, ct);
        return (dto, true);
    }

    /// <summary>
    /// Quien puede reportar: el conductor del viaje (que no indica proceso ni parada) o un inspector que participa: en el cargue, el inspector del viaje o quien reclamo el cargue; en la entrega, quien reclamo la entrega de esa
    /// parada. Cualquier otro rol, o un conductor de otro viaje, recibe 403. Devuelve el proceso y la parada que se guardan.
    /// </summary>
    private async Task<(string? Proceso, Guid? LugarId)> AutorizarAsync(clsViaje viaje, Guid usuarioId, string rol, RegistrarNovedadViajeRequest solicitud, CancellationToken ct)
    {
        if (rol == clsRol.CONDUCTOR)
        {
            if (viaje.ConductorId != usuarioId)
            {
                throw new clsProhibidoException("El viaje no está asignado a su usuario", clsMotivosRechazo.VIAJE_NO_ASIGNADO);
            }

            var errores = new Dictionary<string, string[]>();
            if (solicitud.Proceso is not null)
            {
                errores["proceso"] = ["Solo el inspector indica el proceso de la novedad"];
            }

            if (solicitud.LugarId is not null)
            {
                errores["lugar_id"] = ["Solo el inspector indica la parada de la novedad"];
            }

            return errores.Count > 0 ? throw clsErroresCampos.Varios(errores) : (null, null);
        }

        if (rol != clsRol.INSPECTOR)
        {
            throw new clsProhibidoException("Solo el conductor del viaje y los inspectores reportan novedades del viaje");
        }

        var proceso = solicitud.Proceso ?? throw clsErroresCampos.De("proceso", "Indique el proceso de la novedad: cargue o entrega");
        if (proceso == clsProcesoDeNovedadViaje.CARGUE)
        {
            if (solicitud.LugarId is not null)
            {
                throw clsErroresCampos.De("lugar_id", "La parada solo se indica en la novedad de la entrega");
            }

            var reclamoDelCargue = await reclamos.ObtenerActivoAsync(viaje.Id, clsProcesoReclamo.CARGUE, null, ct);
            return viaje.InspectorId == usuarioId || reclamoDelCargue?.InspectorId == usuarioId
                ? (proceso, null)
                : throw SinEseProceso(proceso);
        }

        var parada = solicitud.LugarId ?? throw clsErroresCampos.De("lugar_id", "La novedad de la entrega se reporta por parada: indique el lugar de destino (lugar_id)");
        var reclamoDeLaEntrega = await reclamos.ObtenerActivoAsync(viaje.Id, clsProcesoReclamo.ENTREGA, parada, ct);
        return reclamoDeLaEntrega?.InspectorId == usuarioId ? (proceso, parada) : throw SinEseProceso(proceso);
    }

    private static clsProhibidoException SinEseProceso(string proceso) =>
        new($"No participa en el {proceso} de este viaje: solo reporta quien tiene ese proceso", clsMotivosRechazo.INSPECTOR_SIN_ESE_PROCESO);

    /// <summary>El tipo activo del catalogo para quien reporta: 400 «Tipo de novedad desconocido» si no existe o esta inactivo y 400 «Ese tipo de novedad no es para tu rol» si es del otro rol.</summary>
    private async Task<ValorCatalogo> TipoAsync(string codigo, string rol, CancellationToken ct)
    {
        var limpio = codigo.Trim();
        var tipo = (await catalogos.ValoresAsync(clsNombresCatalogo.TIPOS_NOVEDAD_VIAJE, ct)).FirstOrDefault(t => t.Codigo == limpio && t.Activo)
            ?? throw clsErroresCampos.De("tipo", "Tipo de novedad desconocido");
        return clsRolDeNovedadViaje.Admite((string)tipo["rol"]!, rol) ? tipo : throw clsErroresCampos.De("tipo", "Ese tipo de novedad no es para tu rol");
    }

    /// <summary>La novedad que ya estaba guardada con ese identificador de envio: la misma si el contenido coincide y 409 <c>envio_repetido_distinto</c> si no.</summary>
    private async Task<NovedadViajeDto> RepetidaAsync(clsNovedadViaje existente, Guid usuarioId, RegistrarNovedadViajeRequest solicitud, CancellationToken ct)
    {
        var mismo = existente.UsuarioId == usuarioId
            && existente.TipoCodigo == solicitud.Tipo?.Trim()
            && existente.Texto == solicitud.Texto?.Trim()
            && existente.FotoStoragePath == solicitud.Evidencia?.StoragePath?.Trim();
        return mismo
            ? await consultas.ObtenerAsync(existente.Id, ct) ?? throw new clsErrorInternoException("La novedad del viaje no se pudo leer")
            : throw new clsConflictoException("Ese identificador de envío ya se usó con otra novedad", clsMotivosRechazo.ENVIO_REPETIDO_DISTINTO);
    }

    /// <summary>
    /// El aviso a los coordinadores de calidad y administradores activos de cada cliente con carros en el viaje (uno por cliente, nunca de otro cliente), con la urgencia del tipo. El mensaje lleva el tipo y el texto, sin el
    /// nombre de nadie; el nombre de quien la reporto va en los <c>datos</c>. La clave es la de la novedad: un reenvio no avisa dos veces.
    /// </summary>
    private async Task AvisarAsync(clsViaje viaje, NovedadViajeDto novedad, CancellationToken ct)
    {
        var clientes = new List<Guid>();
        foreach (var enlace in await viajes.ObtenerVehiculosAsync(viaje.Id, ct))
        {
            if (await vehiculos.ObtenerPorIdAsync(enlace.VehiculoId, ct) is { } vehiculo && !clientes.Contains(vehiculo.ClienteId))
            {
                clientes.Add(vehiculo.ClienteId);
            }
        }

        var codigo = viaje.CodigoMtrack ?? viaje.Placa;
        var datos = new Dictionary<string, object?>
        {
            ["tipo_novedad"] = novedad.Tipo,
            ["tipo_novedad_nombre"] = novedad.TipoNombre,
            ["rol"] = novedad.Rol,
            ["proceso"] = novedad.Proceso,
            ["lugar"] = novedad.Lugar,
            ["usuario_nombre"] = novedad.UsuarioNombre,
            ["con_foto"] = novedad.Foto is not null,
        };
        foreach (var cliente in clientes)
        {
            var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], cliente, ct);
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.NOVEDAD_VIAJE,
                    novedad.Urgencia,
                    destinatarios.Select(d => d.Id).ToList(),
                    cliente,
                    viaje.Id,
                    null,
                    null,
                    null,
                    $"Novedad del viaje: {novedad.TipoNombre} ({codigo})",
                    $"{novedad.TipoNombre}: {novedad.Texto}",
                    $"{clsTipoNotificacion.NOVEDAD_VIAJE}:{novedad.Id}",
                    datos),
                ct);
        }
    }
}
