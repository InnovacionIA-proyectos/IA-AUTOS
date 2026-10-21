using FluentValidation;
using IAAutos.Aplicacion.Conductor.DTOConductor;
using IAAutos.Aplicacion.Inspecciones.Validators;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Conductor.Validators;

/// <summary>La fecha y el GPS del momento: el conductor siempre los envia (el celular los guarda aunque no haya senal).</summary>
internal static class clsReglasMomento
{
    public static void Aplicar<T>(AbstractValidator<T> validador, Func<T, DateTimeOffset?> fecha, Func<T, double?> lat, Func<T, double?> lng)
    {
        validador.RuleFor(x => fecha(x)).Must(f => f is not null).WithMessage(clsReglasComunes.MENSAJE_FECHA).OverridePropertyName("Fecha");
        validador.RuleFor(x => lat(x)).Must(v => v is not null).WithMessage("lat es requerida: el registro exige la ubicación del dispositivo")
            .OverridePropertyName("Lat");
        validador.RuleFor(x => lat(x)).InclusiveBetween(-90, 90).When(x => lat(x) is not null).WithMessage("lat fuera de rango (-90 a 90)")
            .OverridePropertyName("Lat");
        validador.RuleFor(x => lng(x)).Must(v => v is not null).WithMessage("lng es requerida: el registro exige la ubicación del dispositivo")
            .OverridePropertyName("Lng");
        validador.RuleFor(x => lng(x)).InclusiveBetween(-180, 180).When(x => lng(x) is not null).WithMessage("lng fuera de rango (-180 a 180)")
            .OverridePropertyName("Lng");
    }
}

public sealed class RegistrarNovedadRequestValidator : AbstractValidator<RegistrarNovedadRequest>
{
    public RegistrarNovedadRequestValidator()
    {
        clsReglasMomento.Aplicar(this, x => x.Fecha, x => x.Lat, x => x.Lng);
        RuleFor(x => x.TipoNovedad).Must(t => t is not null && clsTipoNovedad.VALORES.Contains(t))
            .WithMessage($"Tipo de novedad inválido: use {string.Join(", ", clsTipoNovedad.VALORES.Order())}");
        RuleFor(x => x.TamanoDano).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Tamaño de daño inválido");
        // La foto es de la novedad: sin ella no se guarda. Va sellada con la ubicacion (si no, el registro no se podria cerrar al confirmar la llegada) y sin texto libre.
        RuleFor(x => x.Evidencia).NotNull().WithMessage("Una novedad exige su foto: envíe la evidencia junto con la novedad");
        When(x => x.Evidencia is not null, () =>
        {
            RuleFor(x => x.Evidencia!).SetValidator(new FotoEvidenciaRequestValidator());
            RuleFor(x => x.Evidencia!.SelloLat).Must(v => v is not null).WithMessage("sello_lat es requerida: la foto de la novedad exige su ubicación");
            RuleFor(x => x.Evidencia!.SelloLng).Must(v => v is not null).WithMessage("sello_lng es requerida: la foto de la novedad exige su ubicación");
            RuleFor(x => x.Evidencia!.Nota).Must(n => string.IsNullOrWhiteSpace(n)).WithMessage("La novedad no admite texto libre");
        });
    }
}

public sealed class RegistrarLlegadaRequestValidator : AbstractValidator<RegistrarLlegadaRequest>
{
    public RegistrarLlegadaRequestValidator() => clsReglasMomento.Aplicar(this, x => x.Fecha, x => x.Lat, x => x.Lng);
}
