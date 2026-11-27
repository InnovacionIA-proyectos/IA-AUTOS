using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// Cuerpo de marcar que una unidad no sale del viaje: el codigo del motivo del catalogo <c>motivos-no-sale</c>, su texto (obligatorio solo con «otro», hasta 300 caracteres), el hallazgo que lo causa (obligatorio solo
/// en los motivos que lo exigen) y, si ya lo hay, el VIN que la reemplaza.
/// </summary>
public sealed record MarcarNoSaleRequest(string? MotivoCodigo, string? MotivoTexto, Guid? HallazgoId, string? VinReemplazo);

/// <summary>Cuerpo de bajar una unidad del viaje: el motivo (obligatorio, hasta 300 caracteres).</summary>
public sealed record BajarDelViajeRequest(string? Motivo);

public sealed class MarcarNoSaleRequestValidator : AbstractValidator<MarcarNoSaleRequest>
{
    public MarcarNoSaleRequestValidator()
    {
        RuleFor(x => x.MotivoCodigo).Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("El motivo es requerido");
        RuleFor(x => x.MotivoTexto).Must(m => m is null || m.Trim().Length <= clsViajeVehiculo.MOTIVO_TEXTO_MAX)
            .WithMessage($"El motivo admite máximo {clsViajeVehiculo.MOTIVO_TEXTO_MAX} caracteres");
        RuleFor(x => x.HallazgoId).Must(h => h is null || h != Guid.Empty).WithMessage("Debe ser un UUID válido");
    }
}

public sealed class BajarDelViajeRequestValidator : AbstractValidator<BajarDelViajeRequest>
{
    public BajarDelViajeRequestValidator()
    {
        RuleFor(x => x.Motivo).Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("El motivo es requerido");
        RuleFor(x => x.Motivo).Must(m => m is null || m.Trim().Length <= clsViajeVehiculo.MOTIVO_TEXTO_MAX)
            .WithMessage($"El motivo admite máximo {clsViajeVehiculo.MOTIVO_TEXTO_MAX} caracteres");
    }
}

/// <summary>
/// El estado de una unidad dentro del viaje, con sus tres cambios. «No sale»: la marcan el inspector del viaje o el coordinador de calidad (nadie mas) con un motivo del catalogo <c>motivos-no-sale</c>; los motivos de
/// daño se apoyan en un hallazgo; la unidad conserva sus etapas e inspecciones y su vinculo con el viaje, deja de contar como completa y puede quedar reemplazada por otro VIN. «Vuelve a salir»: deshace el «no sale»
/// mientras la entrega del cargue no se haya cerrado y haya puesto libre. «Bajar del viaje»: solo el coordinador de calidad, antes de que se cierre la entrega del cargue; saca al carro del viaje (estado <c>bajado</c>)
/// conservando su historia.
/// </summary>
public sealed class clsNoSaleService(
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IInspeccionRepository inspecciones,
    IEtapaRepository etapas,
    IConsultasViajes consultas,
    clsEnsamblajeViajes ensamblaje,
    ICatalogosLectura catalogos,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<MarcarNoSaleRequest> validador,
    IValidator<BajarDelViajeRequest> validadorBajar)
{
    /// <summary>
    /// Marca la unidad como «no sale» y responde con el viaje. 400 si el motivo no esta en el catalogo («Motivo desconocido») o «otro» no trae su texto («Escribe el motivo»); 409 si el motivo no aplica en el lugar de cargue,
    /// si el hallazgo no corresponde al motivo, no es de ese VIN en ese viaje, o la unidad ya no sale; el VIN de reemplazo sigue las reglas de agregar un VIN a un viaje (400 si no existe, 409 si esta en un viaje activo).
    /// </summary>
    public async Task<ViajeDto> MarcarAsync(Guid viajeId, string vin, MarcarNoSaleRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var viaje = await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");
        await AsegurarPermisoAsync(viaje, ct);
        viaje.AsegurarActivo();

        var vehiculo = await vehiculos.ObtenerPorVinAsync(vin, ct) ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");
        var enlace = await viajes.ObtenerVehiculoAsync(viaje.Id, vehiculo.Id, ct)
            ?? throw new clsEntidadNoEncontradaException($"El VIN {vehiculo.Vin} no está en este viaje");
        if (enlace.NoSale)
        {
            throw new clsConflictoException($"La unidad {vehiculo.Vin} ya está marcada como que no sale de este viaje");
        }

        if (enlace.Bajado)
        {
            throw new clsConflictoException($"La unidad {vehiculo.Vin} se bajó de este viaje", clsMotivosRechazo.UNIDAD_BAJADA);
        }

        var motivo = await MotivoAsync(solicitud.MotivoCodigo!, ct);
        var texto = clsValidador.Opcional(solicitud.MotivoTexto);
        if (motivo.ExigeTexto && texto is null)
        {
            throw clsErroresCampos.De("motivo_texto", "Escribe el motivo");
        }

        if (motivo.SoloSinInspector && !await LugarDeCargueAdmiteCargueSinInspectorAsync(viaje, ct))
        {
            throw new clsConflictoException("Ese motivo no aplica en este lugar de cargue", clsMotivosRechazo.MOTIVO_NO_APLICA);
        }

        clsHallazgo? hallazgo = null;
        if (motivo.ExigeHallazgo)
        {
            var hallazgoId = solicitud.HallazgoId ?? throw clsErroresCampos.De("hallazgo_id", "El hallazgo es obligatorio para este motivo");
            hallazgo = motivo.Codigo == clsCodigosMotivoNoSale.DANO_REPORTADO_CONDUCTOR
                ? await AsegurarHallazgoDelConductorAsync(viaje, vehiculo, hallazgoId, ct)
                : await AsegurarHallazgoDeLaUnidadAsync(viaje, vehiculo, hallazgoId, ct);
        }

        var reemplazo = await ResolverReemplazoAsync(viaje, vehiculo, solicitud.VinReemplazo, ct);

        var ahora = reloj.Ahora;
        enlace.MarcarNoSale(motivo.Codigo, texto, hallazgo?.Id, actual.IdObligatorio(), reemplazo?.Id, ahora);
        if (reemplazo is not null)
        {
            // El reemplazo hereda el destino de la unidad que sale del viaje.
            await ensamblaje.AgregarVehiculosAsync(
                viaje, [reemplazo], await catalogos.RutaEstandarAsync(ct), ct, new Dictionary<Guid, Guid?> { [reemplazo.Id] = enlace.LugarDestinoId });
        }

        if (hallazgo is not null)
        {
            inspecciones.Agregar(clsHallazgoSeguimiento.CrearNoSale(hallazgo.Id, actual.IdObligatorio(), texto ?? motivo.Nombre, ahora));
        }

        viaje.RegistrarCambio(ahora);
        await auditor.RegistrarDeViajeAsync(
            "viaje.no_sale",
            "viajes",
            viaje.Id,
            viaje.Id,
            new DetalleAuditoria(null, new { vin = vehiculo.Vin, motivo_codigo = motivo.Codigo, motivo_texto = texto, hallazgo_id = hallazgo?.Id, vin_reemplazo = reemplazo?.Vin }),
            vehiculo.ClienteId,
            ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerViajeDtoAsync(viaje.Id, ct);
    }

    /// <summary>
    /// «Vuelve a salir»: la unidad que no sale vuelve a estar activa, sin posicion, y se borra su marca (el historial queda en la auditoria). Solo el inspector del viaje y el coordinador de calidad. 409 si la entrega del
    /// cargue del viaje ya se cerro (<c>cargue_firmado</c>), si el carro ya tiene un reemplazo en el viaje (<c>tiene_reemplazo</c>) o si la niñera no tiene puesto libre (<c>sin_cupo</c>).
    /// </summary>
    public async Task<ViajeDto> VolverASalirAsync(Guid viajeId, string vin, CancellationToken ct = default)
    {
        var viaje = await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");
        await AsegurarPermisoAsync(viaje, ct);
        viaje.AsegurarActivo();
        var (vehiculo, enlace) = await ObtenerUnidadAsync(viaje, vin, ct);
        if (!enlace.NoSale)
        {
            throw new clsConflictoException($"La unidad {vehiculo.Vin} no está marcada como que no sale de este viaje");
        }

        if (await viajes.EntregaDelCargueCerradaAsync(viaje.Id, ct))
        {
            throw new clsConflictoException("La entrega del cargue ya está cerrada: el carro va en otro viaje", clsMotivosRechazo.CARGUE_FIRMADO);
        }

        if (enlace.ReemplazoVehiculoId is { } reemplazoId && await viajes.ObtenerVehiculoAsync(viaje.Id, reemplazoId, ct) is { Bajado: false })
        {
            throw new clsConflictoException("El carro ya tiene reemplazo: quita primero el reemplazo", clsMotivosRechazo.TIENE_REEMPLAZO);
        }

        var activas = (await viajes.ObtenerVehiculosAsync(viaje.Id, ct)).Count(e => e.EstaActiva);
        if (activas + 1 > viaje.CapacidadEfectiva)
        {
            throw new clsConflictoException($"La niñera tiene {viaje.CapacidadEfectiva} puestos y ya hay {activas} unidades", clsMotivosRechazo.SIN_CUPO);
        }

        var antes = new { vin = vehiculo.Vin, estado_unidad = enlace.EstadoUnidad, motivo_codigo = enlace.NoSaleMotivoCodigo, motivo_texto = enlace.NoSaleMotivo, hallazgo_id = enlace.NoSaleHallazgoId, marcado_por = enlace.NoSaleMarcadoPor, marcado_at = enlace.NoSaleAt };
        var ahora = reloj.Ahora;
        enlace.VolverASalir(ahora);
        viaje.RegistrarCambio(ahora);
        await auditor.RegistrarDeViajeAsync(
            "viaje.vuelve_a_salir", "viajes", viaje.Id, viaje.Id, new DetalleAuditoria(antes, new { vin = vehiculo.Vin, estado_unidad = enlace.EstadoUnidad }), vehiculo.ClienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerViajeDtoAsync(viaje.Id, ct);
    }

    /// <summary>
    /// «Bajar del viaje» (un cambio de pedido): solo el coordinador de calidad, con su motivo (hasta 300 caracteres). La unidad queda <c>bajado</c>, sin posicion y conservando su historia; ya no cuenta en el viaje. Una unidad
    /// que no sale tambien se puede bajar. 409 <c>cargue_firmado</c> si la entrega del cargue del viaje o la de esa unidad ya se cerro: el camion ya salio.
    /// </summary>
    public async Task<ViajeDto> BajarAsync(Guid viajeId, string vin, BajarDelViajeRequest solicitud, CancellationToken ct = default)
    {
        await validadorBajar.ValidarOLanzarAsync(solicitud, ct);
        var viaje = await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");
        await AsegurarPermisoDeBajarAsync(viaje, ct);
        viaje.AsegurarActivo();
        var (vehiculo, enlace) = await ObtenerUnidadAsync(viaje, vin, ct);
        if (enlace.Bajado)
        {
            throw new clsConflictoException($"La unidad {vehiculo.Vin} ya se bajó de este viaje", clsMotivosRechazo.UNIDAD_BAJADA);
        }

        if (await viajes.EntregaDelCargueCerradaAsync(viaje.Id, ct) || await viajes.UnidadConEntregaDelCargueCerradaAsync(viaje.Id, vehiculo.Id, ct))
        {
            throw new clsConflictoException("La entrega del cargue ya está cerrada: el camión ya salió y no se puede bajar ningún carro", clsMotivosRechazo.CARGUE_FIRMADO);
        }

        var antes = new { vin = vehiculo.Vin, estado_unidad = enlace.EstadoUnidad, posicion = enlace.Posicion };
        var motivo = solicitud.Motivo!.Trim();
        var ahora = reloj.Ahora;
        enlace.Bajar(motivo, actual.IdObligatorio(), ahora);
        viaje.RegistrarCambio(ahora);
        await auditor.RegistrarDeViajeAsync(
            "viaje.bajar_vin", "viajes", viaje.Id, viaje.Id, new DetalleAuditoria(antes, new { vin = vehiculo.Vin, estado_unidad = enlace.EstadoUnidad, motivo }), vehiculo.ClienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ObtenerViajeDtoAsync(viaje.Id, ct);
    }

    private async Task<ViajeDto> ObtenerViajeDtoAsync(Guid viajeId, CancellationToken ct) =>
        await consultas.ObtenerAsync(viajeId, actual.ClienteRestringido(), ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");

    private async Task<(clsVehiculo Vehiculo, clsViajeVehiculo Enlace)> ObtenerUnidadAsync(clsViaje viaje, string vin, CancellationToken ct)
    {
        var vehiculo = await vehiculos.ObtenerPorVinAsync(vin, ct) ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");
        var enlace = await viajes.ObtenerVehiculoAsync(viaje.Id, vehiculo.Id, ct)
            ?? throw new clsEntidadNoEncontradaException($"El VIN {vehiculo.Vin} no está en este viaje");
        return (vehiculo, enlace);
    }

    /// <summary>El motivo del catalogo <c>motivos-no-sale</c> (activo); un codigo que no esta es 400 «Motivo desconocido».</summary>
    private async Task<clsMotivoNoSaleFila> MotivoAsync(string codigo, CancellationToken ct)
    {
        var limpio = codigo.Trim().ToLowerInvariant();
        var valor = (await catalogos.ValoresAsync(clsNombresCatalogo.MOTIVOS_NO_SALE, ct)).FirstOrDefault(m => m.Codigo == limpio && m.Activo)
            ?? throw clsErroresCampos.De("motivo_codigo", "Motivo desconocido");
        return new clsMotivoNoSaleFila(
            limpio,
            valor.TryGetValue("nombre", out var nombre) && nombre is string n ? n : limpio,
            valor.TryGetValue("exige_hallazgo", out var h) && h is true,
            valor.TryGetValue("exige_texto", out var x) && x is true,
            valor.TryGetValue("solo_sin_inspector", out var s) && s is true);
    }

    private sealed record clsMotivoNoSaleFila(string Codigo, string Nombre, bool ExigeHallazgo, bool ExigeTexto, bool SoloSinInspector);

    /// <summary>
    /// El lugar de cargue del viaje admite el cargue sin inspector: el daño que reporto el conductor solo vale ahi. Hasta que el lugar de cargue lleve ese dato, ningun lugar lo admite.
    /// </summary>
    private static Task<bool> LugarDeCargueAdmiteCargueSinInspectorAsync(clsViaje viaje, CancellationToken ct) => Task.FromResult(false);

    /// <summary>Solo el inspector del viaje y el coordinador de calidad (de un viaje de su cliente); admin, super admin, comercial y el resto reciben 403.</summary>
    private async Task AsegurarPermisoAsync(clsViaje viaje, CancellationToken ct)
    {
        if (actual.Rol == clsRol.INSPECTOR)
        {
            if (viaje.InspectorId != actual.Id)
            {
                throw new clsProhibidoException("Solo el inspector del viaje puede cambiar el estado de una unidad");
            }

            return;
        }

        if (actual.Rol != clsRol.COORDINADOR_CALIDAD)
        {
            throw new clsProhibidoException("Solo el inspector del viaje y el coordinador de calidad pueden cambiar el estado de una unidad");
        }

        await AsegurarClienteAsync(viaje, ct);
    }

    /// <summary>Solo el coordinador de calidad (de un viaje de su cliente) baja carros del viaje: inspector, conductor, admin y super admin reciben 403.</summary>
    private async Task AsegurarPermisoDeBajarAsync(clsViaje viaje, CancellationToken ct)
    {
        if (actual.Rol != clsRol.COORDINADOR_CALIDAD)
        {
            throw new clsProhibidoException("Solo el coordinador de calidad puede bajar un carro del viaje");
        }

        await AsegurarClienteAsync(viaje, ct);
    }

    private async Task AsegurarClienteAsync(clsViaje viaje, CancellationToken ct)
    {
        if (actual.ClienteRestringido() is { } cliente && !await consultas.ViajeEsDeClienteAsync(viaje.Id, cliente, ct))
        {
            throw new clsProhibidoException("El viaje no tiene unidades de su cliente");
        }
    }

    /// <summary>
    /// El hallazgo debe ser V1+, de una inspeccion cerrada de esa unidad en ese viaje y de su version vigente (409 si no): el dano de una version que una correccion cerrada ya reemplazo no justifica que la unidad no
    /// salga (<c>details.motivo: hallazgo_de_version_reemplazada</c>); el de una inspeccion cuya correccion sigue en borrador si, porque esa sigue siendo la vigente.
    /// </summary>
    private async Task<clsHallazgo> AsegurarHallazgoDeLaUnidadAsync(clsViaje viaje, clsVehiculo vehiculo, Guid hallazgoId, CancellationToken ct)
    {
        const string NO_ES_DE_LA_UNIDAD = "El hallazgo indicado no es de ese VIN en ese viaje";
        var hallazgo = await inspecciones.ObtenerHallazgoAsync(hallazgoId, ct) ?? throw new clsConflictoException(NO_ES_DE_LA_UNIDAD);
        var inspeccion = await inspecciones.ObtenerPorIdAsync(hallazgo.InspeccionId, ct) ?? throw new clsConflictoException(NO_ES_DE_LA_UNIDAD);
        var etapa = await etapas.ObtenerPorIdAsync(inspeccion.EtapaId, ct);
        if (inspeccion.ViajeId != viaje.Id || etapa is null || etapa.VehiculoId != vehiculo.Id)
        {
            throw new clsConflictoException(NO_ES_DE_LA_UNIDAD);
        }

        if (inspeccion.Estado != clsEstadoInspeccion.CERRADA)
        {
            throw new clsConflictoException("La inspección del hallazgo sigue en borrador: la unidad solo no sale por un hallazgo de una inspección cerrada");
        }

        if (await inspecciones.TieneCorreccionCerradaAsync(inspeccion.Id, ct))
        {
            throw new clsConflictoException(
                $"El hallazgo es de una versión reemplazada de la inspección (versión {inspeccion.Version}): una corrección cerrada ya la sustituyó. Marque «no sale» con un hallazgo de la versión vigente",
                clsMotivosRechazo.HALLAZGO_DE_VERSION_REEMPLAZADA);
        }

        if (hallazgo.NivelSeveridad != clsCodigosNivel.V1_PLUS)
        {
            throw new clsConflictoException("Solo un hallazgo de nivel V1+ saca una unidad del viaje");
        }

        return hallazgo;
    }

    /// <summary>
    /// El daño que reporto el conductor: un hallazgo de origen conductor, con su foto y sin calificar (sin nivel), de ese carro en ese viaje (409 si no).
    /// </summary>
    private async Task<clsHallazgo> AsegurarHallazgoDelConductorAsync(clsViaje viaje, clsVehiculo vehiculo, Guid hallazgoId, CancellationToken ct)
    {
        const string NO_ES_DE_LA_UNIDAD = "El hallazgo indicado no es de ese VIN en ese viaje";
        var hallazgo = await inspecciones.ObtenerHallazgoAsync(hallazgoId, ct) ?? throw new clsConflictoException(NO_ES_DE_LA_UNIDAD);
        var inspeccion = await inspecciones.ObtenerPorIdAsync(hallazgo.InspeccionId, ct) ?? throw new clsConflictoException(NO_ES_DE_LA_UNIDAD);
        var etapa = await etapas.ObtenerPorIdAsync(inspeccion.EtapaId, ct);
        if (inspeccion.ViajeId != viaje.Id || etapa is null || etapa.VehiculoId != vehiculo.Id)
        {
            throw new clsConflictoException(NO_ES_DE_LA_UNIDAD);
        }

        if (hallazgo.Origen != clsOrigenHallazgo.CONDUCTOR || hallazgo.NivelSeveridad is not null)
        {
            throw new clsConflictoException("El daño debe ser uno que reportó el conductor y que aún no se califica");
        }

        if (!(await inspecciones.HallazgosConFotoAsync([hallazgo.Id], ct)).Contains(hallazgo.Id))
        {
            throw new clsConflictoException("El daño que reportó el conductor debe traer su foto");
        }

        return hallazgo;
    }

    /// <summary>
    /// El vehiculo que reemplaza a la unidad, si se indico: debe existir (400), no estar ya en este viaje ni en otro viaje activo (409) y, si antes no salio de otro viaje, su hallazgo
    /// V1+ debe estar recuperado (409).
    /// </summary>
    private async Task<clsVehiculo?> ResolverReemplazoAsync(clsViaje viaje, clsVehiculo unidad, string? vinReemplazo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(vinReemplazo))
        {
            return null;
        }

        var normalizado = clsVin.Normalizar(vinReemplazo);
        var reemplazo = await vehiculos.ObtenerPorVinAsync(normalizado, ct);
        if (reemplazo is null || (actual.ClienteRestringido() is { } cliente && reemplazo.ClienteId != cliente))
        {
            throw clsErroresCampos.De("vin_reemplazo", $"No existe en el inventario: {normalizado}. Cárguelo antes de asignarlo a un viaje");
        }

        if (reemplazo.Id == unidad.Id)
        {
            throw clsErroresCampos.De("vin_reemplazo", "El VIN de reemplazo debe ser distinto de la unidad que no sale");
        }

        if (await viajes.ObtenerVehiculoAsync(viaje.Id, reemplazo.Id, ct) is not null)
        {
            throw new clsConflictoException($"Ya está en este viaje: {reemplazo.Vin}");
        }

        var enOtros = await viajes.ViajesActivosPorVehiculoAsync([reemplazo.Id], viaje.Id, ct);
        if (enOtros.TryGetValue(reemplazo.Id, out var otro))
        {
            throw new clsConflictoException($"Ya está en otro viaje activo: {reemplazo.Vin} (viaje {otro.CodigoMtrack ?? otro.ViajeId.ToString()})");
        }

        if ((await viajes.HallazgosNoSaleSinRecuperarAsync(reemplazo.Id, ct)).Count > 0)
        {
            throw new clsConflictoException($"El VIN {reemplazo.Vin} antes no salió de un viaje y su hallazgo V1+ aún no está recuperado");
        }

        return reemplazo;
    }
}
