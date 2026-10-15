using FluentValidation;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes.Validators;

internal static class clsReglasViajes
{
    public const int MAXIMO_VINES = 500;
    public const string MENSAJE_UUID = "Debe ser un UUID válido";

    public static bool UuidOpcionalValido(Guid? valor) => valor is null || valor != Guid.Empty;

    public static bool VinesValidos(IReadOnlyList<string>? vines) =>
        vines is null || (vines.Count <= MAXIMO_VINES && vines.All(v => !string.IsNullOrWhiteSpace(v)));
}

public sealed class CrearViajeRequestValidator : AbstractValidator<CrearViajeRequest>
{
    public CrearViajeRequestValidator()
    {
        RuleFor(x => x.CodigoMtrack).Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("El código MTrack es requerido");
        RuleFor(x => x.TipoUnidadId).Must(t => t is not null && t != Guid.Empty).WithMessage("La unidad transportadora es requerida");
        RuleFor(x => x.Placa).Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage("La placa es requerida");
        RuleFor(x => x.ConductorId).Must(clsReglasViajes.UuidOpcionalValido).WithMessage(clsReglasViajes.MENSAJE_UUID);
        RuleFor(x => x.InspectorId).Must(clsReglasViajes.UuidOpcionalValido).WithMessage(clsReglasViajes.MENSAJE_UUID);
        RuleFor(x => x.OrigenId).Must(clsReglasViajes.UuidOpcionalValido).WithMessage(clsReglasViajes.MENSAJE_UUID);
        RuleFor(x => x.DestinoId).Must(clsReglasViajes.UuidOpcionalValido).WithMessage(clsReglasViajes.MENSAJE_UUID);
        RuleFor(x => x.Vines).Must(clsReglasViajes.VinesValidos)
            .WithMessage($"Máximo {clsReglasViajes.MAXIMO_VINES} VIN por solicitud y ninguno vacío");
    }
}

public sealed class AgregarVinesRequestValidator : AbstractValidator<AgregarVinesRequest>
{
    public AgregarVinesRequestValidator()
    {
        RuleFor(x => x.Vines).Must(v => v is { Count: > 0 }).WithMessage("Se requiere al menos un VIN")
            .Must(clsReglasViajes.VinesValidos).WithMessage($"Máximo {clsReglasViajes.MAXIMO_VINES} VIN por solicitud y ninguno vacío");
    }
}

public sealed class CambiarEstadoViajeRequestValidator : AbstractValidator<CambiarEstadoViajeRequest>
{
    public CambiarEstadoViajeRequestValidator()
    {
        RuleFor(x => x.Estado).Must(e => e is not null && clsEstadoViaje.VALORES.Contains(e))
            .WithMessage($"Estado inválido. Permitidos: {string.Join(", ", clsEstadoViaje.VALORES.Order())}");
    }
}

public sealed class CargarViajesRequestValidator : AbstractValidator<CargarViajesRequest>
{
    public const int MAXIMO_FILAS = 5000;

    public CargarViajesRequestValidator()
    {
        RuleFor(x => x.Filas)
            .Must(f => f is { Count: > 0 }).WithMessage("Se requiere al menos una fila")
            .Must(f => f is null || f.Count <= MAXIMO_FILAS).WithMessage($"Máximo {MAXIMO_FILAS} filas por carga");
        RuleForEach(x => x.Filas).Must(f => f is not null).WithMessage("La fila no puede ser nula");
    }
}
