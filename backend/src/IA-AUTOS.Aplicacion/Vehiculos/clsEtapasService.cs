using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Vehiculos;

/// <summary>Etapa nueva de un vehiculo: la etapa del catalogo es obligatoria; el lugar (punto de custodia) es opcional.</summary>
public sealed record CrearEtapaRequest(Guid? VehiculoId, Guid? EtapaCatalogoId, Guid? PuntoCustodiaId, int? Orden, bool? RequiereInspeccion);

public sealed record EtapaDto(
    Guid Id,
    Guid VehiculoId,
    Guid EtapaCatalogoId,
    Guid? PuntoCustodiaId,
    int Orden,
    bool RequiereInspeccion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CrearEtapaRequestValidator : AbstractValidator<CrearEtapaRequest>
{
    public CrearEtapaRequestValidator()
    {
        RuleFor(x => x.VehiculoId).Must(v => v is not null && v != Guid.Empty).WithMessage("Debe ser un UUID válido");
        RuleFor(x => x.EtapaCatalogoId).Must(v => v is not null && v != Guid.Empty).WithMessage("Debe ser un UUID válido");
        RuleFor(x => x.PuntoCustodiaId).Must(v => v != Guid.Empty).When(x => x.PuntoCustodiaId is not null).WithMessage("Debe ser un UUID válido");
        RuleFor(x => x.Orden).Must(o => o is >= 1).WithMessage("El orden debe ser un entero >= 1");
    }
}

/// <summary>El administrador define la ruta de custodia de un VIN: etapas ordenadas y si exigen informe nuevo o solo firma.</summary>
public sealed class clsEtapasService(
    IEtapaRepository etapas,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IReloj reloj,
    ICatalogosLectura catalogos,
    IValidator<CrearEtapaRequest> validador)
{
    public async Task<EtapaDto> CrearAsync(CrearEtapaRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        await ValidarCatalogosAsync(solicitud, ct);
        var etapa = clsEtapa.Crear(
            solicitud.VehiculoId!.Value,
            solicitud.EtapaCatalogoId!.Value,
            solicitud.PuntoCustodiaId,
            solicitud.Orden!.Value,
            solicitud.RequiereInspeccion ?? true,
            reloj.Ahora);
        etapas.Agregar(etapa);
        auditor.Registrar("etapa.crear", "etapas", etapa.Id);

        // Un vehiculo o punto inexistente y un orden repetido se traducen a 404 y 409 al confirmar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new EtapaDto(etapa.Id, etapa.VehiculoId, etapa.EtapaCatalogoId, etapa.PuntoCustodiaId, etapa.Orden, etapa.RequiereInspeccion, etapa.CreatedAt, etapa.UpdatedAt);
    }

    /// <summary>La etapa y el lugar deben existir en sus catalogos y estar activos (un valor inactivo ya no se ofrece para etapas nuevas).</summary>
    private async Task ValidarCatalogosAsync(CrearEtapaRequest solicitud, CancellationToken ct)
    {
        if (!await catalogos.ExisteActivoAsync(clsNombresCatalogo.ETAPAS, solicitud.EtapaCatalogoId!.Value, ct))
        {
            throw clsErroresCampos.De("etapa_catalogo_id", "La etapa no existe en el catálogo o está inactiva");
        }

        if (solicitud.PuntoCustodiaId is { } lugar && !await catalogos.ExisteActivoAsync(clsNombresCatalogo.PUNTOS_CUSTODIA, lugar, ct))
        {
            throw clsErroresCampos.De("punto_custodia_id", "El punto de custodia no existe en el catálogo o está inactivo");
        }
    }
}
