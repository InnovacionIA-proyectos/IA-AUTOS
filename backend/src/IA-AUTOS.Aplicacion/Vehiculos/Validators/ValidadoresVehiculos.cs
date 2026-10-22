using FluentValidation;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Vehiculos.Validators;

public sealed class VinCargaItemValidator : AbstractValidator<VinCargaItem>
{
    public VinCargaItemValidator()
    {
        RuleFor(x => x.Vin).Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("VIN requerido");
        // Un VIN presente debe cumplir la regla unica: 17 caracteres ISO (sin I, O ni Q) o un chasis de 6 a 20 alfanumericos; el motivo es el del propio VIN.
        RuleFor(x => x.Vin)
            .Custom((vin, contexto) =>
            {
                if (!string.IsNullOrWhiteSpace(vin) && clsVin.Validar(vin) is { } motivo)
                {
                    contexto.AddFailure(motivo);
                }
            });
        RuleFor(x => x.Marca).Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("Marca requerida");
        RuleFor(x => x.Operacion).Must(o => o is not null && clsOperacion.VALORES.Contains(o)).WithMessage("Operación inválida");
    }
}

public sealed class CargarVinsRequestValidator : AbstractValidator<CargarVinsRequest>
{
    public const int MAXIMO_FILAS = 5000;

    public CargarVinsRequestValidator()
    {
        RuleFor(x => x.Items)
            .Must(i => i is { Count: > 0 }).WithMessage("No hay filas para cargar")
            .Must(i => i is null || i.Count <= MAXIMO_FILAS).WithMessage($"Máximo {MAXIMO_FILAS} filas por carga");
        RuleForEach(x => x.Items).SetValidator(new VinCargaItemValidator());
    }
}
