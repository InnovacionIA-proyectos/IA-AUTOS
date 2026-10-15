using FluentValidation;
using IAAutos.Aplicacion.Auth.DTOAuth;

namespace IAAutos.Aplicacion.Auth.Validators;

public sealed class LoginConductorRequestValidator : AbstractValidator<LoginConductorRequest>
{
    public LoginConductorRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Cedula) && !string.IsNullOrEmpty(x.Pin))
            .WithMessage("Cédula y PIN son obligatorios");
    }
}

public sealed class CambiarPinRequestValidator : AbstractValidator<CambiarPinRequest>
{
    public CambiarPinRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.PinActual) && !string.IsNullOrEmpty(x.PinNuevo))
            .WithMessage("El PIN actual y el PIN nuevo son obligatorios");
    }
}
