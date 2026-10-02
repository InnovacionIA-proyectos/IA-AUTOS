using FluentValidation;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Usuarios;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Usuarios.Validators;

public sealed class CrearUsuarioRequestValidator : AbstractValidator<CrearUsuarioRequest>
{
    public CrearUsuarioRequestValidator(IOptions<clsOpcionesSeguridad> opciones)
    {
        var minimo = opciones.Value.PasswordMinimo;
        RuleFor(x => x.Nombre).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre es requerido");
        RuleFor(x => x.Email).Must(EsEmail).WithMessage("Email inválido");
        RuleFor(x => x.Rol).Must(r => r is not null && clsRol.VALORES.Contains(r)).WithMessage("Rol inválido");
        RuleFor(x => x.SubtipoInspector)
            .Must(s => s is null || clsSubtipoInspector.VALORES.Contains(s))
            .WithMessage("Subtipo de inspector inválido");
        RuleFor(x => x.Password)
            .Must(p => p is not null && p.Length >= minimo)
            .WithMessage($"La contraseña debe tener al menos {minimo} caracteres");
    }

    internal static bool EsEmail(string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && System.Text.RegularExpressions.Regex.IsMatch(valor.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
}

public sealed class ActualizarUsuarioRequestValidator : AbstractValidator<ActualizarUsuarioRequest>
{
    public ActualizarUsuarioRequestValidator(IOptions<clsOpcionesSeguridad> opciones)
    {
        var minimo = opciones.Value.PasswordMinimo;
        RuleFor(x => x.Nombre).Must(n => !string.IsNullOrWhiteSpace(n)).When(x => x.Nombre is not null)
            .WithMessage("El nombre es requerido");
        RuleFor(x => x.Email).Must(CrearUsuarioRequestValidator.EsEmail).When(x => x.Email is not null)
            .WithMessage("Email inválido");
        RuleFor(x => x.Rol).Must(r => clsRol.VALORES.Contains(r!)).When(x => x.Rol is not null)
            .WithMessage("Rol inválido");
        RuleFor(x => x.SubtipoInspector).Must(s => clsSubtipoInspector.VALORES.Contains(s!)).When(x => x.SubtipoInspector is not null)
            .WithMessage("Subtipo de inspector inválido");
        RuleFor(x => x.Password).Must(p => p!.Length >= minimo).When(x => x.Password is not null)
            .WithMessage($"La contraseña debe tener al menos {minimo} caracteres");
    }
}
