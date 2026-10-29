using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>Cuerpo de marcar que una unidad no sale del viaje: el motivo, el hallazgo V1+ que lo causa y, si ya lo hay, el VIN que la reemplaza.</summary>
public sealed record MarcarNoSaleRequest(string? Motivo, Guid? HallazgoId, string? VinReemplazo);

public sealed class MarcarNoSaleRequestValidator : AbstractValidator<MarcarNoSaleRequest>
{
    public MarcarNoSaleRequestValidator()
    {
        RuleFor(x => x.Motivo).Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("El motivo es requerido");
        RuleFor(x => x.Motivo).Must(m => m is null || m.Trim().Length <= clsViajeVehiculo.NO_SALE_MOTIVO_MAX)
            .WithMessage($"El motivo admite máximo {clsViajeVehiculo.NO_SALE_MOTIVO_MAX} caracteres");
        RuleFor(x => x.HallazgoId).Must(h => h is { } id && id != Guid.Empty).WithMessage("Debe ser un UUID válido");
    }
}

/// <summary>
/// Una unidad con un hallazgo V1+ puede no salir del viaje sin bloquearlo: la marcan el inspector del viaje o el coordinador de calidad (nadie mas), con su motivo y el hallazgo
/// que lo causa. La unidad conserva sus etapas e inspecciones y su vinculo con el viaje, deja de contar como completa y puede quedar reemplazada por otro VIN, que entra al viaje con
/// sus etapas nuevas; un VIN que antes no salio solo vuelve a entrar si su hallazgo V1+ ya esta recuperado.
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
    IValidator<MarcarNoSaleRequest> validador)
{
    /// <summary>
    /// Marca la unidad como «no sale» y responde con el viaje. 409 si el hallazgo no es V1+, no es de ese VIN en ese viaje, su inspeccion no esta cerrada o la unidad ya no sale;
    /// el VIN de reemplazo sigue las reglas de agregar un VIN a un viaje (400 si no existe, 409 si esta en un viaje activo).
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

        var hallazgo = await AsegurarHallazgoDeLaUnidadAsync(viaje, vehiculo, solicitud.HallazgoId!.Value, ct);
        var reemplazo = await ResolverReemplazoAsync(viaje, vehiculo, solicitud.VinReemplazo, ct);

        var ahora = reloj.Ahora;
        var motivo = solicitud.Motivo!.Trim();
        enlace.MarcarNoSale(motivo, hallazgo.Id, actual.IdObligatorio(), reemplazo?.Id, ahora);
        if (reemplazo is not null)
        {
            var conPersonalApoyo = (await viajes.ObtenerEtapasAsync(viaje.Id, null, ct)).Any(e => e.ConPersonalApoyo);
            // El reemplazo hereda el destino de la unidad que sale del viaje.
            await ensamblaje.AgregarVehiculosAsync(
                viaje, [reemplazo], conPersonalApoyo, await catalogos.RutaEstandarAsync(ct), ct, new Dictionary<Guid, Guid?> { [reemplazo.Id] = enlace.LugarDestinoId });
        }

        inspecciones.Agregar(clsHallazgoSeguimiento.CrearNoSale(hallazgo.Id, actual.IdObligatorio(), motivo, ahora));
        viaje.RegistrarActividad(ahora);
        await auditor.RegistrarDeViajeAsync(
            "viaje.no_sale",
            "viajes",
            viaje.Id,
            viaje.Id,
            new DetalleAuditoria(null, new { vin = vehiculo.Vin, motivo, hallazgo_id = hallazgo.Id, vin_reemplazo = reemplazo?.Vin }),
            vehiculo.ClienteId,
            ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await consultas.ObtenerAsync(viaje.Id, actual.ClienteRestringido(), ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");
    }

    /// <summary>Solo el inspector del viaje y el coordinador de calidad (de un viaje de su cliente); admin, comercial y el resto reciben 403.</summary>
    private async Task AsegurarPermisoAsync(clsViaje viaje, CancellationToken ct)
    {
        if (actual.Rol == clsRol.INSPECTOR)
        {
            if (viaje.InspectorId != actual.Id)
            {
                throw new clsProhibidoException("Solo el inspector del viaje puede marcar que una unidad no sale");
            }

            return;
        }

        if (actual.Rol != clsRol.COORDINADOR_CALIDAD)
        {
            throw new clsProhibidoException("Solo el inspector del viaje y el coordinador de calidad pueden marcar que una unidad no sale");
        }

        if (actual.ClienteRestringido() is { } cliente && !await consultas.ViajeEsDeClienteAsync(viaje.Id, cliente, ct))
        {
            throw new clsProhibidoException("El viaje no tiene unidades de su cliente");
        }
    }

    /// <summary>El hallazgo debe ser V1+, de una inspeccion cerrada de esa unidad en ese viaje (409 si no).</summary>
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

        if (hallazgo.NivelSeveridad != clsCodigosNivel.V1_PLUS)
        {
            throw new clsConflictoException("Solo un hallazgo de nivel V1+ saca una unidad del viaje");
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
