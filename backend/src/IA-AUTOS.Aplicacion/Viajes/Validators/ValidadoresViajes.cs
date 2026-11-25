using FluentValidation;
using IAAutos.Aplicacion.Inspecciones.Validators;
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
        RuleFor(x => x.FechaProgramada).Must(f => !string.IsNullOrWhiteSpace(f)).WithMessage("La fecha programada es obligatoria");
        RuleFor(x => x.LugarCargueId).Must(l => l is not null && l != Guid.Empty).WithMessage("El lugar de cargue es obligatorio");
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
        RuleFor(x => x.Motivo).Must(m => m is null || m.Trim().Length <= MOTIVO_MAX)
            .WithMessage($"El motivo admite máximo {MOTIVO_MAX} caracteres");
    }

    public const int MOTIVO_MAX = 300;
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

/// <summary>
/// La novedad del viaje: tipo, texto de 1 a 300 caracteres y la fecha del celular son obligatorios; el GPS es opcional (sin el queda «sin ubicacion») pero latitud y longitud van juntas. La foto, si viene, es la misma foto sellada
/// de la novedad de un carro, sin texto libre; su GPS tampoco es obligatorio.
/// </summary>
public sealed class RegistrarNovedadViajeRequestValidator : AbstractValidator<RegistrarNovedadViajeRequest>
{
    public RegistrarNovedadViajeRequestValidator()
    {
        RuleFor(x => x.Tipo).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Tipo de novedad desconocido");
        RuleFor(x => x.Texto).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Escribe qué pasó");
        RuleFor(x => x.Texto).Must(t => t is null || t.Trim().Length <= clsNovedadViaje.TEXTO_MAX).WithMessage($"Escribe máximo {clsNovedadViaje.TEXTO_MAX} caracteres");
        RuleFor(x => x.Fecha).Must(f => f is not null).WithMessage(clsReglasComunes.MENSAJE_FECHA);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).When(x => x.Lat is not null).WithMessage("lat fuera de rango (-90 a 90)");
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180).When(x => x.Lng is not null).WithMessage("lng fuera de rango (-180 a 180)");
        RuleFor(x => x.Lat).Must(v => v is not null).When(x => x.Lng is not null).WithMessage("Envíe la latitud y la longitud juntas, o ninguna si no hay GPS");
        RuleFor(x => x.Lng).Must(v => v is not null).When(x => x.Lat is not null).WithMessage("Envíe la latitud y la longitud juntas, o ninguna si no hay GPS");
        RuleFor(x => x.IdEnvio).Must(v => v is null || v != Guid.Empty).WithMessage(clsReglasViajes.MENSAJE_UUID);
        RuleFor(x => x.LugarId).Must(clsReglasViajes.UuidOpcionalValido).WithMessage(clsReglasViajes.MENSAJE_UUID);
        RuleFor(x => x.Proceso).Must(p => p is null || clsProcesoDeNovedadViaje.VALORES.Contains(p)).WithMessage("El proceso debe ser cargue o entrega");
        When(x => x.Evidencia is not null, () =>
        {
            RuleFor(x => x.Evidencia!).SetValidator(new FotoEvidenciaRequestValidator());
            RuleFor(x => x.Evidencia!.Nota).Must(n => string.IsNullOrWhiteSpace(n)).WithMessage("La novedad no admite texto libre en la foto");
        });
    }
}
