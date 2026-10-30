using FluentValidation;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes.Validators;

internal static class clsReglasViajes
{
    public const int MAXIMO_VINES = 500;
    public const string MENSAJE_UUID = "Debe ser un UUID válido";

    public static bool UuidOpcionalValido(Guid? valor) => valor is null || valor != Guid.Empty;

    public const string MENSAJE_SIN_DESTINO = "La unidad requiere un destino";

    public static bool UnidadesValidas(IReadOnlyList<UnidadDelViajeRequest>? unidades) =>
        unidades is null || (unidades.Count <= MAXIMO_VINES && unidades.All(u => u is not null && !string.IsNullOrWhiteSpace(u.Vin)));

    /// <summary>Cada unidad trae su destino: un lugar (no nulo ni vacio).</summary>
    public static bool UnidadesConDestino(IReadOnlyList<UnidadDelViajeRequest>? unidades) =>
        unidades is null || unidades.All(u => u?.LugarDestinoId is { } destino && destino != Guid.Empty);
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
        RuleFor(x => x.Unidades).Must(clsReglasViajes.UnidadesValidas)
            .WithMessage($"Máximo {clsReglasViajes.MAXIMO_VINES} unidades por solicitud y ninguna sin VIN")
            .Must(clsReglasViajes.UnidadesConDestino).WithMessage(clsReglasViajes.MENSAJE_SIN_DESTINO);
    }
}

public sealed class AgregarUnidadesRequestValidator : AbstractValidator<AgregarUnidadesRequest>
{
    public AgregarUnidadesRequestValidator()
    {
        RuleFor(x => x.Unidades).Must(v => v is { Count: > 0 }).WithMessage("Se requiere al menos una unidad")
            .Must(clsReglasViajes.UnidadesValidas).WithMessage($"Máximo {clsReglasViajes.MAXIMO_VINES} unidades por solicitud y ninguna sin VIN")
            .Must(clsReglasViajes.UnidadesConDestino).WithMessage(clsReglasViajes.MENSAJE_SIN_DESTINO);
    }
}

public sealed class CambiarDestinoUnidadRequestValidator : AbstractValidator<CambiarDestinoUnidadRequest>
{
    public CambiarDestinoUnidadRequestValidator()
    {
        RuleFor(x => x.LugarDestinoId).Must(d => d is not null && d != Guid.Empty).WithMessage(clsReglasViajes.MENSAJE_SIN_DESTINO);
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
