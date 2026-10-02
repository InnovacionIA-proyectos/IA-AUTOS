using FluentValidation;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Vehiculos;

public sealed record CrearEtapaRequest(Guid? VehiculoId, Guid? PuntoCustodiaId, int? Orden, bool? RequiereInspeccion);

public sealed record EtapaDto(
    Guid Id,
    Guid VehiculoId,
    Guid PuntoCustodiaId,
    int Orden,
    bool RequiereInspeccion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CrearEtapaRequestValidator : AbstractValidator<CrearEtapaRequest>
{
    public CrearEtapaRequestValidator()
    {
        RuleFor(x => x.VehiculoId).Must(v => v is not null && v != Guid.Empty).WithMessage("Debe ser un UUID válido");
        RuleFor(x => x.PuntoCustodiaId).Must(v => v is not null && v != Guid.Empty).WithMessage("Debe ser un UUID válido");
        RuleFor(x => x.Orden).Must(o => o is >= 1).WithMessage("El orden debe ser un entero >= 1");
    }
}

/// <summary>El administrador define la ruta de custodia de un VIN: etapas ordenadas y si exigen informe nuevo o solo firma.</summary>
public sealed class clsEtapasService(
    IEtapaRepository etapas,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IReloj reloj,
    IValidator<CrearEtapaRequest> validador)
{
    public async Task<EtapaDto> CrearAsync(CrearEtapaRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var etapa = clsEtapa.Crear(
            solicitud.VehiculoId!.Value,
            solicitud.PuntoCustodiaId!.Value,
            solicitud.Orden!.Value,
            solicitud.RequiereInspeccion ?? true,
            reloj.Ahora);
        etapas.Agregar(etapa);
        auditor.Registrar("etapa.crear", "etapas", etapa.Id);

        // Un vehiculo o punto inexistente y un orden repetido se traducen a 404 y 409 al confirmar.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new EtapaDto(etapa.Id, etapa.VehiculoId, etapa.PuntoCustodiaId, etapa.Orden, etapa.RequiereInspeccion, etapa.CreatedAt, etapa.UpdatedAt);
    }
}
