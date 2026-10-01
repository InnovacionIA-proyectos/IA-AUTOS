using System.Text.RegularExpressions;
using FluentValidation;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Inspecciones.Validators;

internal static partial class clsReglasComunes
{
    public const string MENSAJE_UUID = "Debe ser un UUID válido";
    public const string MENSAJE_FECHA = "Debe ser una fecha/hora ISO 8601 válida";

    public static bool UuidValido(Guid? valor) => valor is not null && valor != Guid.Empty;

    /// <summary>Nombre plano de archivo (sin carpetas ni recorridos de ruta), como los que devuelve la subida de imagenes.</summary>
    public static bool NombreDeArchivoSeguro(string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && !valor.Contains("..", StringComparison.Ordinal) && NombrePlano().IsMatch(valor);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,200}$")]
    private static partial Regex NombrePlano();
}

public sealed class CrearInspeccionRequestValidator : AbstractValidator<CrearInspeccionRequest>
{
    private const int NOTAS_MAX = 1000;

    public CrearInspeccionRequestValidator()
    {
        RuleFor(x => x.EtapaId).Must(clsReglasComunes.UuidValido).WithMessage(clsReglasComunes.MENSAJE_UUID);
        RuleFor(x => x.Momento).Must(m => m is not null && clsMomento.VALORES.Contains(m)).WithMessage("Momento inválido");
        RuleFor(x => x.Fecha).Must(f => f is not null).WithMessage(clsReglasComunes.MENSAJE_FECHA);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).When(x => x.Lat is not null).WithMessage("lat fuera de rango (-90 a 90)");
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180).When(x => x.Lng is not null).WithMessage("lng fuera de rango (-180 a 180)");
        // Una inspeccion nueva siempre nace en borrador: el cierre pasa por /cerrar, que valida evidencias y geolocalizacion.
        RuleFor(x => x.Estado)
            .Must(e => e is null or clsEstadoInspeccion.BORRADOR)
            .WithMessage("Una inspección nueva debe iniciar en borrador; se cierra con /cerrar");
        RuleFor(x => x.Notas).Must(n => n is null || n.Trim().Length <= NOTAS_MAX)
            .WithMessage($"Las notas admiten máximo {NOTAS_MAX} caracteres");
    }
}

public sealed class CrearHallazgoRequestValidator : AbstractValidator<CrearHallazgoRequest>
{
    public CrearHallazgoRequestValidator()
    {
        RuleFor(x => x.ParteVehiculo).Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage("La parte del vehículo es requerida")
            .Must(p => p is null || p.Trim().Length <= 80).WithMessage("La parte del vehículo admite máximo 80 caracteres");
        RuleFor(x => x.ClaseDano).Must(c => c is not null && clsClaseDano.VALORES.Contains(c)).WithMessage("Clase de daño inválida");
        RuleFor(x => x.TamanoDano).Must(t => t is not null && clsTamanoDano.VALORES.Contains(t)).WithMessage("Tamaño de daño inválido");
        RuleFor(x => x.CoordsDiagrama)
            .Must(c => c is { X: >= 0 and <= 1, Y: >= 0 and <= 1 })
            .When(x => x.CoordsDiagrama is not null)
            .WithMessage("Las coordenadas del diagrama deben estar entre 0 y 1");
    }
}

public sealed class CrearEvidenciaRequestValidator : AbstractValidator<CrearEvidenciaRequest>
{
    public CrearEvidenciaRequestValidator()
    {
        RuleFor(x => x.StoragePath).Must(clsReglasComunes.NombreDeArchivoSeguro)
            .WithMessage("storage_path es requerido y debe ser el nombre del archivo subido");
        RuleFor(x => x.SelloFecha).Must(f => f is not null).WithMessage(clsReglasComunes.MENSAJE_FECHA);
        RuleFor(x => x.HallazgoId).Must(clsReglasComunes.UuidValido).When(x => x.HallazgoId is not null)
            .WithMessage(clsReglasComunes.MENSAJE_UUID);
        RuleFor(x => x.SelloLat).InclusiveBetween(-90, 90).When(x => x.SelloLat is not null).WithMessage("sello_lat fuera de rango (-90 a 90)");
        RuleFor(x => x.SelloLng).InclusiveBetween(-180, 180).When(x => x.SelloLng is not null).WithMessage("sello_lng fuera de rango (-180 a 180)");
    }
}

public sealed class FirmarTransferenciaRequestValidator : AbstractValidator<FirmarTransferenciaRequest>
{
    /// <summary>Limite de una firma dibujada (trazo SVG o ruta de imagen): 200 KB de texto.</summary>
    public const int FIRMA_MAX = 200_000;

    public FirmarTransferenciaRequestValidator()
    {
        RuleFor(x => x.EntregaUsuarioId).Must(clsReglasComunes.UuidValido).WithMessage(clsReglasComunes.MENSAJE_UUID);
        RuleFor(x => x.RecibeUsuarioId).Must(clsReglasComunes.UuidValido).WithMessage(clsReglasComunes.MENSAJE_UUID);
        RuleFor(x => x.RecibeUsuarioId)
            .Must((x, recibe) => recibe != x.EntregaUsuarioId)
            .When(x => clsReglasComunes.UuidValido(x.EntregaUsuarioId) && clsReglasComunes.UuidValido(x.RecibeUsuarioId))
            .WithMessage("Quien entrega y quien recibe deben ser usuarios distintos");
        RuleFor(x => x.FirmaEntrega).Must(f => f is null || f.Length <= FIRMA_MAX).WithMessage("La firma excede el tamaño máximo permitido");
        RuleFor(x => x.FirmaRecibe).Must(f => f is null || f.Length <= FIRMA_MAX).WithMessage("La firma excede el tamaño máximo permitido");
    }
}
