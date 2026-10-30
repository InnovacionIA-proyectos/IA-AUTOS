using System.Text.Json;
using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// Viajes: alta, consulta, cambios, asignacion de VIN, cambio de estado y baja. Inspectores y conductores solo ven (y abren) los
/// viajes en los que participan; quien administra viajes (coordinador, administrador) ve todos.
/// </summary>
public sealed class clsViajesService(
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IUsuarioRepository usuarios,
    IConsultasViajes consultas,
    ICatalogosLectura catalogos,
    clsEnsamblajeViajes ensamblaje,
    clsAccesoViajes acceso,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<CrearViajeRequest> validadorCrear,
    IValidator<AgregarUnidadesRequest> validadorUnidades,
    IValidator<CambiarDestinoUnidadRequest> validadorDestinoUnidad,
    IValidator<CambiarEstadoViajeRequest> validadorEstado)
{
    /// <summary>Los campos por los que se puede ordenar el listado (<c>orden=campo:asc|desc</c>).</summary>
    public static readonly IReadOnlySet<string> CAMPOS_ORDEN = new HashSet<string> { "created_at", "updated_at", "codigo_mtrack", "estado", "placa", "trailer" };

    private static readonly string[] CAMPOS_EDITABLES =
        ["codigo_mtrack", "tipo_unidad_id", "placa", "trailer", "conductor_id", "origen_id", "fecha_programada", "lugar_cargue_id"];

    private static readonly string[] CAMPOS_SIN_NULO = ["codigo_mtrack", "tipo_unidad_id", "placa"];

    // ------------------------------------------------------------------------------------------------ alta

    public async Task<ViajeDto> CrearAsync(CrearViajeRequest solicitud, CancellationToken ct = default)
    {
        await validadorCrear.ValidarOLanzarAsync(solicitud, ct);
        var codigo = clsViaje.CodigoValido(solicitud.CodigoMtrack);
        await AsegurarCodigoLibreAsync(codigo, null, ct);
        await ValidarReferenciasAsync(solicitud.TipoUnidadId, solicitud.ConductorId, solicitud.InspectorId, solicitud.OrigenId, null, ct);
        var unidades = solicitud.Unidades ?? [];
        await ValidarDestinosAsync(unidades, ct);

        // El viaje ya no tiene un destino unico: cada unidad lleva el suyo.
        var viaje = clsViaje.Crear(
            codigo, solicitud.TipoUnidadId, solicitud.Placa, solicitud.Trailer, solicitud.ConductorId, solicitud.InspectorId,
            solicitud.OrigenId, null, actual.IdObligatorio(), reloj.Ahora);

        var vehiculosAAgregar = await ResolverVehiculosAsync(viaje, unidades.Select(u => u.Vin!).ToList(), new HashSet<Guid>(), ct);
        var destinos = DestinosPorVehiculo(vehiculosAAgregar, unidades);
        viajes.Agregar(viaje);
        await ensamblaje.AgregarVehiculosAsync(viaje, vehiculosAAgregar, await catalogos.RutaEstandarAsync(ct), ct, destinos);
        await auditor.RegistrarDeViajeAsync(
            "viaje.crear",
            "viajes",
            viaje.Id,
            viaje.Id,
            new DetalleAuditoria(null, new { viaje = clsInstantaneasAuditoria.De(viaje), vines = vehiculosAAgregar.Select(v => v.Vin).ToList(), destinos = destinos.Select(d => new { vehiculo_id = d.Key, lugar_destino_id = d.Value }).ToList() }),
            vehiculosAAgregar.Select(v => (Guid?)v.ClienteId).FirstOrDefault(),
            ct);

        // Un codigo MTrack repetido que se coló entre la consulta y el guardado se traduce a 409 al confirmar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerDtoAsync(viaje.Id, ct);
    }

    // ---------------------------------------------------------------------------------------------- consulta

    /// <summary>Una pagina de viajes: <c>{items, total, pagina, tamano}</c>, con los filtros, la pagina y el orden validados juntos (400 con todos los errores).</summary>
    public async Task<PaginaDto<ViajeResumenDto>> ListarAsync(FiltrosViajesRequest solicitud, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        var filtros = LeerFiltros(solicitud, errores);
        var pagina = clsPaginacion.Pagina(solicitud.Pagina, solicitud.Tamano, errores);
        var orden = clsPaginacion.Orden(solicitud.Orden, CAMPOS_ORDEN, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var (filas, total) = await consultas.ListarAsync(AlcanzarCliente(filtros, ClienteRestringido), actual.SoloSusViajes(), ClienteRestringido, pagina, orden, ct);
        return new PaginaDto<ViajeResumenDto>(filas, total, pagina.Pagina, pagina.Tamano);
    }

    /// <summary>Los viajes del usuario (como conductor o inspector). Sin estado: los programados y en curso.</summary>
    public async Task<IReadOnlyList<ViajeDto>> MiosAsync(string? estado, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        var filtro = clsParseoFiltros.Catalogo(estado, clsEstadoViaje.VALORES, "estado", errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        string[] estados = filtro is null ? clsEstadoViaje.ACTIVOS : [filtro];
        return await consultas.MiosAsync(actual.IdObligatorio(), estados, ct);
    }

    public async Task<ViajeDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(id, ct);
        await AsegurarAccesoAsync(viaje, ct);
        return await ObtenerDtoAsync(id, ct);
    }

    // ---------------------------------------------------------------------------------------------- cambios

    /// <summary>
    /// Cambia los campos enviados de un viaje programado o en curso. Al cambiar el origen se actualiza el lugar del Cargue de sus VIN, y al cambiar el
    /// personal de apoyo se marca el Cargue; las etapas con informe cerrado no se tocan. El destino es de cada unidad (<see cref="CambiarDestinoDeUnidadAsync"/>) y el inspector lo llena el reclamo del cargue.
    /// </summary>
    public async Task<ViajeDto> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var lector = new clsCamposCatalogo(campos);
        lector.SoloPermitidos(CAMPOS_EDITABLES);
        foreach (var campo in CAMPOS_SIN_NULO.Where(c => lector.Tiene(c) && campos[c].ValueKind == JsonValueKind.Null))
        {
            throw clsErroresCampos.De(campo, "No admite valor nulo");
        }

        var viaje = await ObtenerViajeAsync(id, ct);
        viaje.AsegurarActivo();

        var codigo = lector.Tiene("codigo_mtrack") ? clsViaje.CodigoValido(lector.Texto("codigo_mtrack")) : null;
        if (codigo is not null && !string.Equals(codigo, viaje.CodigoMtrack, StringComparison.OrdinalIgnoreCase))
        {
            await AsegurarCodigoLibreAsync(codigo, viaje.Id, ct);
        }

        var tipoUnidadId = lector.Identificador("tipo_unidad_id");
        var conductorId = lector.Identificador("conductor_id");
        var origenId = lector.Identificador("origen_id");
        await ValidarReferenciasAsync(tipoUnidadId, conductorId, null, origenId, null, ct);

        // La programacion (fecha y lugar real de cargue) solo se corrige mientras el viaje esta programado.
        var cambiaFecha = lector.Tiene("fecha_programada");
        var fechaProgramada = cambiaFecha && campos["fecha_programada"].ValueKind != JsonValueKind.Null
            ? clsFechaProgramada.DeIso(lector.Texto("fecha_programada")) ?? throw clsErroresCampos.De("fecha_programada", "Debe ser una fecha y hora ISO 8601 con zona horaria (por ejemplo 2026-11-03T06:00:00-05:00)")
            : (DateTimeOffset?)null;
        var cambiaLugar = lector.Tiene("lugar_cargue_id");
        var lugarCargueId = lector.Identificador("lugar_cargue_id");
        if (lugarCargueId is { } lugar && !await catalogos.ExisteActivoAsync(clsNombresCatalogo.LUGARES, lugar, ct))
        {
            throw clsErroresCampos.De("lugar_cargue_id", "El lugar de cargue no existe en el catálogo de lugares o está inactivo");
        }

        var antes = clsInstantaneasAuditoria.De(viaje);
        if (cambiaFecha || cambiaLugar)
        {
            viaje.Reprogramar(cambiaFecha, fechaProgramada, cambiaLugar, lugarCargueId, reloj.Ahora);
        }

        viaje.Actualizar(
            codigo,
            tipoUnidadId,
            lector.Texto("placa"),
            lector.Tiene("trailer"), lector.Texto("trailer"),
            lector.Tiene("conductor_id"), conductorId,
            false, null,
            lector.Tiene("origen_id"), origenId,
            false, null,
            reloj.Ahora);

        if (lector.Tiene("origen_id"))
        {
            await PropagarAEtapasAsync(viaje, ct);
        }

        await auditor.RegistrarDeViajeAsync(
            "viaje.actualizar", "viajes", viaje.Id, viaje.Id, new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(viaje)), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerDtoAsync(id, ct);
    }

    public async Task<ViajeDto> CambiarEstadoAsync(Guid id, CambiarEstadoViajeRequest solicitud, CancellationToken ct = default)
    {
        await validadorEstado.ValidarOLanzarAsync(solicitud, ct);
        var viaje = await ObtenerViajeAsync(id, ct);
        var estadoAnterior = viaje.Estado;
        viaje.CambiarEstado(solicitud.Estado!, reloj.Ahora);
        await auditor.RegistrarDeViajeAsync(
            $"viaje.estado.{viaje.Estado}", "viajes", viaje.Id, viaje.Id, new DetalleAuditoria(new { estado = estadoAnterior }, new { estado = viaje.Estado }), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerDtoAsync(id, ct);
    }

    /// <summary>
    /// Agrega unidades a un viaje activo, cada una con su lugar de destino (400 si falta): se crean sus etapas, con el Descargue en el destino de la unidad. Todo o nada: si alguna no existe o ya esta en otro
    /// viaje activo, no se agrega ninguna.
    /// </summary>
    public async Task<ViajeDto> AgregarUnidadesAsync(Guid id, AgregarUnidadesRequest solicitud, CancellationToken ct = default)
    {
        await validadorUnidades.ValidarOLanzarAsync(solicitud, ct);
        var viaje = await ObtenerViajeAsync(id, ct);
        viaje.AsegurarActivo();

        var unidades = solicitud.Unidades!;
        await ValidarDestinosAsync(unidades, ct);
        var yaEnElViaje = (await viajes.ObtenerVehiculosAsync(id, ct)).Select(v => v.VehiculoId).ToHashSet();
        var vehiculosAAgregar = await ResolverVehiculosAsync(viaje, unidades.Select(u => u.Vin!).ToList(), yaEnElViaje, ct);
        var destinos = DestinosPorVehiculo(vehiculosAAgregar, unidades);
        await ensamblaje.AgregarVehiculosAsync(viaje, vehiculosAAgregar, await catalogos.RutaEstandarAsync(ct), ct, destinos);
        await auditor.RegistrarDeViajeAsync(
            "viaje.agregar_vin", "viajes", viaje.Id, viaje.Id,
            new DetalleAuditoria(null, new { vines = vehiculosAAgregar.Select(v => v.Vin).ToList(), destinos = destinos.Select(d => new { vehiculo_id = d.Key, lugar_destino_id = d.Value }).ToList() }), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerDtoAsync(id, ct);
    }

    /// <summary>
    /// Cambia el lugar de destino de UNA unidad de un viaje activo: solo se mueve su etapa de Descargue (las demas unidades no se tocan). 409 si la unidad no sale del viaje o si su Descargue ya tiene un
    /// informe cerrado.
    /// </summary>
    public async Task<ViajeDto> CambiarDestinoDeUnidadAsync(Guid id, string vin, CambiarDestinoUnidadRequest solicitud, CancellationToken ct = default)
    {
        await validadorDestinoUnidad.ValidarOLanzarAsync(solicitud, ct);
        var viaje = await ObtenerViajeAsync(id, ct);
        viaje.AsegurarActivo();
        var vehiculo = await vehiculos.ObtenerPorVinAsync(vin, ct)
            ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");
        var enlace = await viajes.ObtenerVehiculoAsync(id, vehiculo.Id, ct)
            ?? throw new clsEntidadNoEncontradaException($"El VIN {vehiculo.Vin} no está en este viaje");
        await ValidarDestinosAsync([new UnidadDelViajeRequest(vehiculo.Vin, solicitud.LugarDestinoId)], ct);

        var codigosPorEtapa = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).ToDictionary(v => v.Id, v => v.Codigo);
        var descargue = (await viajes.ObtenerEtapasAsync(id, vehiculo.Id, ct)).FirstOrDefault(e => codigosPorEtapa.TryGetValue(e.EtapaCatalogoId, out var codigo) && codigo == clsCodigosEtapa.DESCARGUE);
        if (descargue is not null && (await viajes.EtapasConInformeCerradoAsync(id, ct)).Contains(descargue.Id))
        {
            throw new clsConflictoException("El descargue de la unidad ya tiene un informe cerrado: su destino no se cambia");
        }

        var antes = enlace.LugarDestinoId;
        enlace.CambiarLugarDestino(solicitud.LugarDestinoId!.Value, reloj.Ahora);
        if (descargue is not null && (await ensamblaje.PuntoDeLosLugaresAsync(ct)).TryGetValue(solicitud.LugarDestinoId.Value, out var punto))
        {
            descargue.CambiarLugar(punto, reloj.Ahora);
        }

        await auditor.RegistrarDeViajeAsync(
            "viaje.cambiar_destino_unidad", "viajes", viaje.Id, viaje.Id,
            new DetalleAuditoria(new { vin = vehiculo.Vin, lugar_destino_id = antes }, new { vin = vehiculo.Vin, lugar_destino_id = enlace.LugarDestinoId }), vehiculo.ClienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerDtoAsync(id, ct);
    }

    /// <summary>
    /// Registra la posicion de una unidad en la ninera (<c>posicion</c> de 1 a 7; nulo la libera) durante la segunda inspeccion del cargue. 400 «La posicion debe estar entre 1 y 7»; 409 «La posicion N ya esta ocupada
    /// por otra unidad de este viaje» (si dos solicitudes piden la misma a la vez, gana exactamente una), 409 si la unidad no sale del viaje o si el viaje no tiene su segunda inspeccion del cargue en curso.
    /// El inspector del viaje (o quien reclamo el cargue), el coordinador y los administradores la registran.
    /// </summary>
    public async Task<ViajeDto> AsignarPosicionAsync(Guid id, string vin, AsignarPosicionRequest solicitud, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(id, ct);
        viaje.AsegurarActivo();
        if (actual.SoloSusViajes() is { } usuarioId && !await acceso.ParticipaOReclamaAsync(viaje, usuarioId, ct))
        {
            throw new clsProhibidoException("El viaje no está asignado a su usuario");
        }

        var vehiculo = await vehiculos.ObtenerPorVinAsync(vin, ct)
            ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");
        var enlace = await viajes.ObtenerVehiculoAsync(id, vehiculo.Id, ct)
            ?? throw new clsEntidadNoEncontradaException($"El VIN {vehiculo.Vin} no está en este viaje");
        if (solicitud.Posicion is { } posicion)
        {
            // Primero el rango (400) y despues lo demas.
            if (posicion < clsViajeVehiculo.POSICION_MINIMA || posicion > clsViajeVehiculo.POSICION_MAXIMA)
            {
                throw clsErroresCampos.De("posicion", $"La posición debe estar entre {clsViajeVehiculo.POSICION_MINIMA} y {clsViajeVehiculo.POSICION_MAXIMA}");
            }

            if (!await viajes.TieneSegundaInspeccionDelCargueEnCursoAsync(id, ct))
            {
                throw new clsConflictoException("El viaje no tiene su segunda inspección del cargue en curso: la posición se registra durante ella");
            }

            if ((await viajes.ObtenerVehiculosAsync(id, ct)).Any(v => v.VehiculoId != vehiculo.Id && v.Posicion == posicion))
            {
                throw new clsConflictoException($"La posición {posicion} ya está ocupada por otra unidad de este viaje");
            }
        }

        var antes = enlace.Posicion;
        enlace.AsignarPosicion(solicitud.Posicion, reloj.Ahora);
        await auditor.RegistrarDeViajeAsync(
            "viaje.posicion_unidad", "viajes", viaje.Id, viaje.Id, new DetalleAuditoria(new { vin = vehiculo.Vin, posicion = antes }, new { vin = vehiculo.Vin, posicion = enlace.Posicion }), vehiculo.ClienteId, ct);

        // Dos solicitudes simultaneas por la misma posicion: el indice unico de la base deja pasar una y la otra se traduce a 409 al guardar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerDtoAsync(id, ct);
    }

    /// <summary>Quita un VIN de un viaje activo (se borran sus etapas). No se puede si ya tiene inspecciones en el viaje.</summary>
    public async Task<ViajeDto> QuitarVinAsync(Guid id, string vin, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(id, ct);
        viaje.AsegurarActivo();
        var vehiculo = await vehiculos.ObtenerPorVinAsync(vin, ct)
            ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");
        var enlace = await viajes.ObtenerVehiculoAsync(id, vehiculo.Id, ct)
            ?? throw new clsEntidadNoEncontradaException($"El VIN {vehiculo.Vin} no está en este viaje");
        if (await viajes.TieneInspeccionesAsync(id, vehiculo.Id, ct))
        {
            throw new clsConflictoException("El VIN ya tiene inspecciones en este viaje y no puede quitarse");
        }

        viajes.Eliminar(await viajes.ObtenerEtapasAsync(id, vehiculo.Id, ct));
        viajes.Eliminar(enlace);
        await auditor.RegistrarDeViajeAsync(
            "viaje.quitar_vin", "viajes", viaje.Id, viaje.Id, new DetalleAuditoria(new { vines = new[] { vehiculo.Vin } }, null), vehiculo.ClienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerDtoAsync(id, ct);
    }

    /// <summary>Borra un viaje (con sus VIN y etapas) solo si no tiene inspecciones; si las tiene, se cancela.</summary>
    public async Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var viaje = await ObtenerViajeAsync(id, ct);
        if (await viajes.TieneInspeccionesAsync(id, null, ct))
        {
            throw new clsConflictoException("El viaje tiene inspecciones y no puede eliminarse: cancélelo");
        }

        viajes.Eliminar(await viajes.ObtenerEtapasAsync(id, null, ct));
        foreach (var enlace in await viajes.ObtenerVehiculosAsync(id, ct))
        {
            viajes.Eliminar(enlace);
        }

        viajes.Eliminar(viaje);
        await auditor.RegistrarDeViajeAsync("viaje.eliminar", "viajes", id, id, new DetalleAuditoria(clsInstantaneasAuditoria.De(viaje), null), ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
    }

    // ----------------------------------------------------------------------------------------------- internos

    private async Task<clsViaje> ObtenerViajeAsync(Guid id, CancellationToken ct) =>
        await viajes.ObtenerPorIdAsync(id, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {id} no encontrado");

    private async Task<ViajeDto> ObtenerDtoAsync(Guid id, CancellationToken ct) =>
        await consultas.ObtenerAsync(id, ClienteRestringido, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {id} no encontrado");

    /// <summary>
    /// El viaje no tiene un cliente propio: se toma de sus VIN. Quien administra viajes sin ser super administrador (coordinador, administrador,
    /// comercial...) solo ve los VIN de su cliente y los viajes que los tienen; el conductor y el inspector, que ven lo asignado a ellos, y el
    /// super administrador ven todo. Nulo si no hay restriccion; sin cliente asignado, <see cref="Guid.Empty"/> (no ve nada).
    /// </summary>
    private Guid? ClienteRestringido => actual.ClienteRestringido();

    /// <summary>Inspectores y conductores solo abren los viajes en los que participan; el resto, los de su cliente (el super administrador, todos).</summary>
    private async Task AsegurarAccesoAsync(clsViaje viaje, CancellationToken ct)
    {
        if (actual.SoloSusViajes() is { } usuarioId && !await acceso.PuedeVerAsync(viaje, usuarioId, ct))
        {
            throw new clsProhibidoException("El viaje no está asignado a su usuario");
        }

        if (ClienteRestringido is { } cliente && !await consultas.ViajeEsDeClienteAsync(viaje.Id, cliente, ct))
        {
            throw new clsProhibidoException("El viaje no tiene unidades de su cliente");
        }
    }

    private async Task AsegurarCodigoLibreAsync(string codigo, Guid? exceptoViajeId, CancellationToken ct)
    {
        if (await viajes.ExisteCodigoMtrackAsync(codigo, exceptoViajeId, ct))
        {
            throw new clsConflictoException("Ya existe un viaje con ese código MTrack");
        }
    }

    /// <summary>
    /// La unidad, el conductor, el inspector, el origen y el destino deben existir y estar activos (el conductor y el inspector, con su
    /// rol). Se devuelven todos los errores juntos.
    /// </summary>
    private async Task ValidarReferenciasAsync(Guid? tipoUnidadId, Guid? conductorId, Guid? inspectorId, Guid? origenId, Guid? destinoId, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>();
        if (tipoUnidadId is { } tipo && !await catalogos.ExisteActivoAsync(clsNombresCatalogo.TIPOS_UNIDAD, tipo, ct))
        {
            errores["tipo_unidad_id"] = ["La unidad transportadora no existe en el catálogo o está inactiva"];
        }

        if (origenId is { } origen && !await catalogos.ExisteActivoAsync(clsNombresCatalogo.PUNTOS_CUSTODIA, origen, ct))
        {
            errores["origen_id"] = ["El origen no existe en el catálogo de lugares o está inactivo"];
        }

        if (destinoId is { } destino && !await catalogos.ExisteActivoAsync(clsNombresCatalogo.PUNTOS_CUSTODIA, destino, ct))
        {
            errores["destino_id"] = ["El destino no existe en el catálogo de lugares o está inactivo"];
        }

        if (conductorId is { } conductor && await ErrorDeUsuarioAsync(conductor, clsRol.CONDUCTOR, "conductor", ct) is { } errorConductor)
        {
            errores["conductor_id"] = [errorConductor];
        }

        if (inspectorId is { } inspector && await ErrorDeUsuarioAsync(inspector, clsRol.INSPECTOR, "inspector", ct) is { } errorInspector)
        {
            errores["inspector_id"] = [errorInspector];
        }

        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }
    }

    private async Task<string?> ErrorDeUsuarioAsync(Guid id, string rolEsperado, string nombreRol, CancellationToken ct)
    {
        var usuario = await usuarios.ObtenerPorIdAsync(id, ct);
        if (usuario is null || !usuario.Activo)
        {
            return $"El {nombreRol} indicado no existe o está retirado";
        }

        return usuario.Rol == rolEsperado ? null : $"El usuario indicado no es un {nombreRol}";
    }

    /// <summary>
    /// Vehiculos de los VIN recibidos (sin repetidos). Falla si alguno no existe (400: cargarlo primero), si ya esta en este viaje o si
    /// ya esta en otro viaje activo (409).
    /// </summary>
    private async Task<IReadOnlyList<clsVehiculo>> ResolverVehiculosAsync(
        clsViaje viaje,
        IReadOnlyList<string> vines,
        IReadOnlySet<Guid> yaEnElViaje,
        CancellationToken ct)
    {
        var normalizados = vines.Select(clsVin.Normalizar).Distinct().ToList();
        if (normalizados.Count == 0)
        {
            return [];
        }

        var existentes = await vehiculos.ObtenerPorVinesAsync(normalizados, ct);
        var faltan = normalizados.Where(v => !existentes.ContainsKey(v)).ToList();
        if (faltan.Count > 0)
        {
            throw clsErroresCampos.De("unidades", $"No existen en el inventario: {string.Join(", ", faltan)}. Cárguelos antes de asignarlos a un viaje");
        }

        var encontrados = normalizados.Select(v => existentes[v]).ToList();
        var inactivos = encontrados.Where(v => !v.Activo).Select(v => v.Vin).ToList();
        if (inactivos.Count > 0)
        {
            throw clsErroresCampos.De("unidades", $"Están dados de baja y no se asignan a un viaje: {string.Join(", ", inactivos)}");
        }

        var repetidos = encontrados.Where(v => yaEnElViaje.Contains(v.Id)).Select(v => v.Vin).ToList();
        if (repetidos.Count > 0)
        {
            throw new clsConflictoException($"Ya están en este viaje: {string.Join(", ", repetidos)}");
        }

        var enOtros = await viajes.ViajesActivosPorVehiculoAsync(encontrados.Select(v => v.Id).ToList(), viaje.Id, ct);
        if (enOtros.Count > 0)
        {
            var detalle = encontrados.Where(v => enOtros.ContainsKey(v.Id))
                .Select(v => $"{v.Vin} (viaje {enOtros[v.Id].CodigoMtrack ?? enOtros[v.Id].ViajeId.ToString()})");
            throw new clsConflictoException($"Ya están en otro viaje activo: {string.Join(", ", detalle)}");
        }

        return encontrados;
    }

    /// <summary>
    /// Cada unidad debe traer un destino (400 «La unidad requiere un destino») que sea un lugar activo del catalogo <c>lugares</c>; un mismo VIN repetido no puede ir a dos destinos distintos.
    /// Se devuelven todos los errores juntos bajo <c>unidades</c>.
    /// </summary>
    private async Task ValidarDestinosAsync(IReadOnlyList<UnidadDelViajeRequest> unidades, CancellationToken ct)
    {
        var errores = new List<string>();
        foreach (var unidad in unidades)
        {
            if (unidad.LugarDestinoId is not { } lugar || lugar == Guid.Empty)
            {
                errores.Add("La unidad requiere un destino");
            }
            else if (!await catalogos.ExisteActivoAsync(clsNombresCatalogo.LUGARES, lugar, ct))
            {
                errores.Add($"El lugar de destino de la unidad {unidad.Vin} no existe en el catálogo de lugares o está inactivo");
            }
        }

        errores.AddRange(unidades
            .Where(u => u.Vin is not null && u.LugarDestinoId is not null)
            .GroupBy(u => clsVin.Normalizar(u.Vin!))
            .Where(g => g.Select(u => u.LugarDestinoId).Distinct().Count() > 1)
            .Select(g => $"La unidad {g.Key} aparece con destinos distintos"));
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(new Dictionary<string, string[]> { ["unidades"] = errores.Distinct().ToArray() });
        }
    }

    /// <summary>El destino de cada vehiculo resuelto, tomado de la unidad que lo trajo (por VIN normalizado).</summary>
    private static IReadOnlyDictionary<Guid, Guid?> DestinosPorVehiculo(IReadOnlyList<clsVehiculo> vehiculosResueltos, IReadOnlyList<UnidadDelViajeRequest> unidades)
    {
        var porVin = unidades.GroupBy(u => clsVin.Normalizar(u.Vin!)).ToDictionary(g => g.Key, g => g.First().LugarDestinoId);
        return vehiculosResueltos.ToDictionary(v => v.Id, v => porVin[clsVin.Normalizar(v.Vin)]);
    }

    /// <summary>Lleva al Cargue de las etapas del viaje el cambio de origen; las que ya tienen informe cerrado no cambian. El Descargue es de cada unidad y no se toca aqui.</summary>
    private async Task PropagarAEtapasAsync(clsViaje viaje, CancellationToken ct)
    {
        var codigosPorEtapa = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).ToDictionary(v => v.Id, v => v.Codigo);
        var cerradas = await viajes.EtapasConInformeCerradoAsync(viaje.Id, ct);
        foreach (var etapa in await viajes.ObtenerEtapasAsync(viaje.Id, null, ct))
        {
            if (cerradas.Contains(etapa.Id) || !codigosPorEtapa.TryGetValue(etapa.EtapaCatalogoId, out var codigo))
            {
                continue;
            }

            if (codigo == clsCodigosEtapa.CARGUE)
            {
                etapa.CambiarLugar(viaje.OrigenId, reloj.Ahora);
            }
        }
    }

    /// <summary>
    /// <c>cliente_id</c> solo lo elige el super administrador; quien tiene su alcance limitado a un cliente (<paramref name="clienteRestringido"/>) siempre
    /// recibe el suyo y pedir otro cliente responde 403 (igual que en el listado de usuarios). El VIN sigue la regla unica del VIN.
    /// </summary>
    internal static FiltrosViajes ParsearFiltros(FiltrosViajesRequest s, Guid? clienteRestringido)
    {
        var errores = new Dictionary<string, string[]>();
        var filtros = LeerFiltros(s, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        return AlcanzarCliente(filtros, clienteRestringido);
    }

    /// <summary>Lee los filtros acumulando los errores por campo para responder todos juntos.</summary>
    private static FiltrosViajes LeerFiltros(FiltrosViajesRequest s, Dictionary<string, string[]> errores) => new(
        clsParseoFiltros.Catalogo(s.Estado, clsEstadoViaje.VALORES, "estado", errores),
        clsValidador.Opcional(s.CodigoMtrack),
        clsParseoFiltros.Vin(s.Vin, "vin", errores),
        clsParseoFiltros.Uuid(s.ConductorId, "conductor_id", errores),
        clsParseoFiltros.Uuid(s.InspectorId, "inspector_id", errores),
        clsParseoFiltros.Uuid(s.ClienteId, "cliente_id", errores),
        clsParseoFiltros.Uuid(s.OrigenId, "origen_id", errores),
        clsParseoFiltros.Uuid(s.DestinoId, "destino_id", errores),
        clsParseoFiltros.Fecha(s.Desde, "desde", errores),
        clsParseoFiltros.Fecha(s.Hasta, "hasta", errores));

    /// <summary>Quien se limita a su cliente no elige otro (403) y su cliente ya se aplica como alcance: el filtro solo lo usa quien ve todos los clientes.</summary>
    private static FiltrosViajes AlcanzarCliente(FiltrosViajes filtros, Guid? clienteRestringido)
    {
        if (clienteRestringido is { } propio && filtros.ClienteId is { } otro && otro != propio)
        {
            throw new clsProhibidoException("Solo puede consultar lo de su propio cliente");
        }

        return clienteRestringido is null ? filtros : filtros with { ClienteId = null };
    }
}
