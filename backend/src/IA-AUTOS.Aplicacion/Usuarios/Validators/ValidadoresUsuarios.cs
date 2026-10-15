using FluentValidation;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Usuarios;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Usuarios.Validators;

/// <summary>
/// Reglas de forma del alta: el nombre y el rol siempre; con contrasena, el correo; sin contrasena (PIN) la cedula, y solo para
/// conductor e inspector. La cedula se normaliza y valida en el dominio.
/// </summary>
public sealed class CrearUsuarioRequestValidator : AbstractValidator<CrearUsuarioRequest>
{
    public CrearUsuarioRequestValidator(IOptions<clsOpcionesSeguridad> opciones)
    {
        var minimo = opciones.Value.PasswordMinimo;
        RuleFor(x => x.Nombre).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre es requerido");
        RuleFor(x => x.Rol).Must(r => r is not null && clsRol.VALORES.Contains(r)).WithMessage("Rol inválido");
        RuleFor(x => x.SubtipoInspector)
            .Must(s => s is null || clsSubtipoInspector.VALORES.Contains(s))
            .WithMessage("Subtipo de inspector inválido");
        RuleFor(x => x.Email).Must(EsEmail).When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("Email inválido");

        // Con contrasena entra por correo; sin ella, por cedula y PIN (solo personal de campo).
        RuleFor(x => x.Password)
            .Must(p => p!.Length >= minimo).When(x => x.Password is not null)
            .WithMessage($"La contraseña debe tener al menos {minimo} caracteres");
        RuleFor(x => x.Email).Must(e => !string.IsNullOrWhiteSpace(e)).When(x => x.Password is not null)
            .WithMessage("El correo es requerido para entrar con contraseña");
        RuleFor(x => x.Password).NotNull().When(x => x.Rol is not null && clsRol.VALORES.Contains(x.Rol) && !clsTipoCredencial.ROLES_CON_PIN.Contains(x.Rol))
            .WithMessage("La contraseña es requerida para este rol");
        RuleFor(x => x.Cedula).Must(c => !string.IsNullOrWhiteSpace(c))
            .When(x => x.Password is null || x.Rol == clsRol.CONDUCTOR)
            .WithMessage("La cédula es requerida para el conductor y para quien entra con PIN");
        RuleFor(x => x.Pin).Must(p => p is null).When(x => x.Password is not null)
            .WithMessage("No se envía PIN a un usuario que entra con contraseña");
    }

    internal static bool EsEmail(string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && System.Text.RegularExpressions.Regex.IsMatch(valor.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
}
