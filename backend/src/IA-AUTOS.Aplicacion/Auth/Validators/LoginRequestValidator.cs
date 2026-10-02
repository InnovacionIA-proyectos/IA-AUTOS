using FluentValidation;
using IAAutos.Aplicacion.Auth.DTOAuth;

namespace IAAutos.Aplicacion.Auth.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Email) && !string.IsNullOrEmpty(x.Password))
            .WithMessage("Correo y contraseña son obligatorios");
    }
}
