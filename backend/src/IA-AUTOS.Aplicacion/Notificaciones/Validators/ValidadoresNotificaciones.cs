using FluentValidation;
using IAAutos.Aplicacion.Notificaciones.DTONotificaciones;
using IAAutos.Dominio.Notificaciones;

namespace IAAutos.Aplicacion.Notificaciones.Validators;

public sealed class ResponderNotificacionRequestValidator : AbstractValidator<ResponderNotificacionRequest>
{
    public ResponderNotificacionRequestValidator()
    {
        RuleFor(x => x.Texto).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("El texto de la respuesta es requerido");
        RuleFor(x => x.Texto).Must(t => t is null || t.Trim().Length <= clsNotificacion.RESPUESTA_MAX)
            .WithMessage($"El texto admite máximo {clsNotificacion.RESPUESTA_MAX} caracteres");
        RuleFor(x => x.Medio).Must(m => m is not null && clsMedioRespuesta.VALORES.Contains(m))
            .WithMessage($"Medio inválido: use {clsMedioRespuesta.APP} o {clsMedioRespuesta.LLAMADA}");
    }
}

public sealed class RegistrarDispositivoRequestValidator : AbstractValidator<RegistrarDispositivoRequest>
{
    public RegistrarDispositivoRequestValidator()
    {
        RuleFor(x => x.TokenFcm).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("El token de FCM es requerido");
        RuleFor(x => x.TokenFcm).Must(t => t is null || t.Trim().Length <= clsDispositivo.TOKEN_MAX)
            .WithMessage($"El token admite máximo {clsDispositivo.TOKEN_MAX} caracteres");
        RuleFor(x => x.Plataforma).Must(p => p is not null && clsPlataformaDispositivo.VALORES.Contains(p))
            .WithMessage($"Plataforma inválida: use {string.Join(", ", clsPlataformaDispositivo.VALORES.Order())}");
        RuleFor(x => x.Modelo).Must(m => m is null || m.Trim().Length <= clsDispositivo.MODELO_MAX)
            .WithMessage($"El modelo admite máximo {clsDispositivo.MODELO_MAX} caracteres");
    }
}
