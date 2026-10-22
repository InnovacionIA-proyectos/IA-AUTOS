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
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<CrearViajeRequest> validadorCrear,
    IValidator<AgregarVinesRequest> validadorVines,
    IValidator<CambiarEstadoViajeRequest> validadorEstado)
{
    public const int LIMITE_LISTADO = 200;

    private static readonly string[] CAMPOS_EDITABLES =
        ["codigo_mtrack", "tipo_unidad_id", "placa", "trailer", "conductor_id", "inspector_id", "origen_id", "destino_id", "con_personal_apoyo"];

    private static readonly string[] CAMPOS_SIN_NULO = ["codigo_mtrack", "tipo_unidad_id", "placa", "con_personal_apoyo"];

    // ------------------------------------------------------------------------------------------------ alta

    public async Task<ViajeDto> CrearAsync(CrearViajeRequest solicitud, CancellationToken ct = default)
    {
        await validadorCrear.ValidarOLanzarAsync(solicitud, ct);
        var codigo = clsViaje.CodigoValido(solicitud.CodigoMtrack);
        await AsegurarCodigoLibreAsync(codigo, null, ct);
        await ValidarReferenciasAsync(solicitud.TipoUnidadId, solicitud.ConductorId, solicitud.InspectorId, solicitud.OrigenId, solicitud.DestinoId, ct);

        var viaje = clsViaje.Crear(
            codigo, solicitud.TipoUnidadId, solicitud.Placa, solicitud.Trailer, solicitud.ConductorId, solicitud.InspectorId,
            solicitud.OrigenId, solicitud.DestinoId, actual.IdObligatorio(), reloj.Ahora);

        var vehiculosAAgregar = await ResolverVehiculosAsync(viaje, solicitud.Vines ?? [], new HashSet<Guid>(), ct);
        viajes.Agregar(viaje);
        await ensamblaje.AgregarVehiculosAsync(viaje, vehiculosAAgregar, solicitud.ConPersonalApoyo ?? false, await catalogos.RutaEstandarAsync(ct), ct);
        await auditor.RegistrarDeViajeAsync(
            "viaje.crear",
            "viajes",
            viaje.Id,
            viaje.Id,
            new DetalleAuditoria(null, new { viaje = clsInstantaneasAuditoria.De(viaje), vines = vehiculosAAgregar.Select(v => v.Vin).ToList() }),
            vehiculosAAgregar.Select(v => (Guid?)v.ClienteId).FirstOrDefault(),
            ct);

        // Un codigo MTrack repetido que se coló entre la consulta y el guardado se traduce a 409 al confirmar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerDtoAsync(viaje.Id, ct);
    }

    // ---------------------------------------------------------------------------------------------- consulta

    public async Task<IReadOnlyList<ViajeResumenDto>> ListarAsync(FiltrosViajesRequest solicitud, CancellationToken ct = default) =>
        await consultas.ListarAsync(ParsearFiltros(solicitud, ClienteRestringido), actual.SoloSusViajes(), ClienteRestringido, LIMITE_LISTADO, ct);

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
    /// Cambia los campos enviados de un viaje programado o en curso. Al cambiar el origen o el destino se actualiza el lugar del
    /// Cargue o del Descargue de sus VIN, y al cambiar el personal de apoyo se marca el Cargue; las etapas con informe cerrado no se tocan.
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
        var inspectorId = lector.Identificador("inspector_id");
        var origenId = lector.Identificador("origen_id");
        var destinoId = lector.Identificador("destino_id");
        await ValidarReferenciasAsync(tipoUnidadId, conductorId, inspectorId, origenId, destinoId, ct);

        var antes = clsInstantaneasAuditoria.De(viaje);
        viaje.Actualizar(
            codigo,
            tipoUnidadId,
            lector.Texto("placa"),
            lector.Tiene("trailer"), lector.Texto("trailer"),
            lector.Tiene("conductor_id"), conductorId,
            lector.Tiene("inspector_id"), inspectorId,
            lector.Tiene("origen_id"), origenId,
            lector.Tiene("destino_id"), destinoId,
            reloj.Ahora);

        var personalApoyo = lector.Booleano("con_personal_apoyo");
        if (lector.Tiene("origen_id") || lector.Tiene("destino_id") || personalApoyo is not null)
        {
            await PropagarAEtapasAsync(viaje, lector.Tiene("origen_id"), lector.Tiene("destino_id"), personalApoyo, ct);
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

    /// <summary>Agrega VIN a un viaje activo: se crean sus etapas. Todo o nada: si alguno no existe o ya esta en otro viaje activo, no se agrega ninguno.</summary>
    public async Task<ViajeDto> AgregarVinesAsync(Guid id, AgregarVinesRequest solicitud, CancellationToken ct = default)
    {
        await validadorVines.ValidarOLanzarAsync(solicitud, ct);
        var viaje = await ObtenerViajeAsync(id, ct);
        viaje.AsegurarActivo();

        var yaEnElViaje = (await viajes.ObtenerVehiculosAsync(id, ct)).Select(v => v.VehiculoId).ToHashSet();
        var conPersonalApoyo = (await viajes.ObtenerEtapasAsync(id, null, ct)).Any(e => e.ConPersonalApoyo);
        var vehiculosAAgregar = await ResolverVehiculosAsync(viaje, solicitud.Vines!, yaEnElViaje, ct);
        await ensamblaje.AgregarVehiculosAsync(viaje, vehiculosAAgregar, conPersonalApoyo, await catalogos.RutaEstandarAsync(ct), ct);
        await auditor.RegistrarDeViajeAsync(
            "viaje.agregar_vin", "viajes", viaje.Id, viaje.Id, new DetalleAuditoria(null, new { vines = vehiculosAAgregar.Select(v => v.Vin).ToList() }), ct: ct);
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
        if (actual.SoloSusViajes() is { } usuarioId && !viaje.Participa(usuarioId))
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
            throw clsErroresCampos.De("vines", $"No existen en el inventario: {string.Join(", ", faltan)}. Cárguelos antes de asignarlos a un viaje");
        }

        var encontrados = normalizados.Select(v => existentes[v]).ToList();
        var inactivos = encontrados.Where(v => !v.Activo).Select(v => v.Vin).ToList();
        if (inactivos.Count > 0)
        {
            throw clsErroresCampos.De("vines", $"Están dados de baja y no se asignan a un viaje: {string.Join(", ", inactivos)}");
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

    /// <summary>Lleva a las etapas del viaje el cambio de origen, de destino o de personal de apoyo; las que ya tienen informe cerrado no cambian.</summary>
    private async Task PropagarAEtapasAsync(clsViaje viaje, bool cambiaOrigen, bool cambiaDestino, bool? personalApoyo, CancellationToken ct)
    {
        var codigosPorEtapa = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).ToDictionary(v => v.Id, v => v.Codigo);
        var cerradas = await viajes.EtapasConInformeCerradoAsync(viaje.Id, ct);
        foreach (var etapa in await viajes.ObtenerEtapasAsync(viaje.Id, null, ct))
        {
            if (cerradas.Contains(etapa.Id) || !codigosPorEtapa.TryGetValue(etapa.EtapaCatalogoId, out var codigo))
            {
                continue;
            }

            if (codigo == clsCodigosEtapa.CARGUE && cambiaOrigen)
            {
                etapa.CambiarLugar(viaje.OrigenId, reloj.Ahora);
            }
            else if (codigo == clsCodigosEtapa.DESCARGUE && cambiaDestino)
            {
                etapa.CambiarLugar(viaje.DestinoId, reloj.Ahora);
            }

            if (codigo == clsCodigosEtapa.CARGUE && personalApoyo is { } marca)
            {
                etapa.CambiarPersonalApoyo(marca, reloj.Ahora);
            }
        }
    }

    /// <summary>
    /// <c>clienteId</c> solo lo elige el super administrador; quien tiene su alcance limitado a un cliente (<paramref name="clienteRestringido"/>) siempre
    /// recibe el suyo y pedir otro cliente responde 403 (igual que en el listado de usuarios).
    /// </summary>
    private static FiltrosViajes ParsearFiltros(FiltrosViajesRequest s, Guid? clienteRestringido)
    {
        var errores = new Dictionary<string, string[]>();
        var estado = clsParseoFiltros.Catalogo(s.Estado, clsEstadoViaje.VALORES, "estado", errores);
        var conductorId = clsParseoFiltros.Uuid(s.ConductorId, "conductorId", errores);
        var desde = clsParseoFiltros.Fecha(s.Desde, "desde", errores);
        var hasta = clsParseoFiltros.Fecha(s.Hasta, "hasta", errores);
        var clienteId = clsParseoFiltros.Uuid(s.ClienteId, "clienteId", errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        if (clienteRestringido is { } propio && clienteId is { } otro && otro != propio)
        {
            throw new clsProhibidoException("Solo puede consultar lo de su propio cliente");
        }

        return new FiltrosViajes(
            estado,
            clsValidador.Opcional(s.CodigoMtrack),
            string.IsNullOrWhiteSpace(s.Vin) ? null : clsVin.Normalizar(s.Vin),
            conductorId,
            desde,
            hasta,
            clienteRestringido is null ? clienteId : null);
    }
}
