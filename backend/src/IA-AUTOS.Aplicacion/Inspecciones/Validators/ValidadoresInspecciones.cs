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

    public static bool HashValido(string? valor) => string.IsNullOrWhiteSpace(valor) || Sha256Hexadecimal().IsMatch(valor.Trim());

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,200}$")]
    private static partial Regex NombrePlano();

    [GeneratedRegex(@"^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Hexadecimal();
}

public sealed class CrearInspeccionRequestValidator : AbstractValidator<CrearInspeccionRequest>
{
    private const int NOTAS_MAX = 1000;

    public CrearInspeccionRequestValidator()
    {
        RuleFor(x => x.EtapaId).Must(clsReglasComunes.UuidValido).WithMessage(clsReglasComunes.MENSAJE_UUID);
        RuleFor(x => x.Momento).Must(m => m is not null && clsMomento.VALORES.Contains(m))
            .WithMessage($"Momento inválido: use {clsMomento.SALIDA} o {clsMomento.LLEGADA}");
        RuleFor(x => x.Fecha).Must(f => f is not null).WithMessage(clsReglasComunes.MENSAJE_FECHA);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).When(x => x.Lat is not null).WithMessage("lat fuera de rango (-90 a 90)");
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180).When(x => x.Lng is not null).WithMessage("lng fuera de rango (-180 a 180)");
        // Una inspeccion nueva siempre nace en borrador: el cierre pasa por /cerrar, que valida las fotos y su geolocalizacion.
        RuleFor(x => x.Estado)
            .Must(e => e is null or clsEstadoInspeccion.BORRADOR)
            .WithMessage("Una inspección nueva debe iniciar en borrador; se cierra con /cerrar");
        RuleFor(x => x.Notas).Must(n => n is null || n.Trim().Length <= NOTAS_MAX)
            .WithMessage($"Las notas admiten máximo {NOTAS_MAX} caracteres");
    }
}

public sealed class CorregirInspeccionRequestValidator : AbstractValidator<CorregirInspeccionRequest>
{
    private const int NOTAS_MAX = 1000;

    public CorregirInspeccionRequestValidator()
    {
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).When(x => x.Lat is not null).WithMessage("lat fuera de rango (-90 a 90)");
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180).When(x => x.Lng is not null).WithMessage("lng fuera de rango (-180 a 180)");
        RuleFor(x => x.Notas).Must(n => n is null || n.Trim().Length <= NOTAS_MAX)
            .WithMessage($"Las notas admiten máximo {NOTAS_MAX} caracteres");
    }
}

/// <summary>Reglas del sello y de los datos de una foto, comunes a las fotos de vistas y a las de hallazgos.</summary>
internal static class clsReglasFoto
{
    public static void Aplicar<T>(
        AbstractValidator<T> validador,
        Func<T, string?> storagePath,
        Func<T, DateTimeOffset?> selloFecha,
        Func<T, double?> lat,
        Func<T, double?> lng,
        Func<T, double?> precision,
        Func<T, string?> dispositivo,
        Func<T, string?> nota,
        Func<T, string?> hash)
    {
        validador.RuleFor(x => storagePath(x)).Must(clsReglasComunes.NombreDeArchivoSeguro)
            .WithMessage("storage_path es requerido y debe ser el nombre del archivo subido").OverridePropertyName("StoragePath");
        validador.RuleFor(x => selloFecha(x)).Must(f => f is not null).WithMessage(clsReglasComunes.MENSAJE_FECHA).OverridePropertyName("SelloFecha");
        validador.RuleFor(x => lat(x)).InclusiveBetween(-90, 90).When(x => lat(x) is not null)
            .WithMessage("sello_lat fuera de rango (-90 a 90)").OverridePropertyName("SelloLat");
        validador.RuleFor(x => lng(x)).InclusiveBetween(-180, 180).When(x => lng(x) is not null)
            .WithMessage("sello_lng fuera de rango (-180 a 180)").OverridePropertyName("SelloLng");
        validador.RuleFor(x => precision(x)).Must(p => p is { } v && double.IsFinite(v) && v >= 0).When(x => precision(x) is not null)
            .WithMessage("La precisión debe ser un número en metros mayor o igual a 0").OverridePropertyName("Precision");
        validador.RuleFor(x => dispositivo(x)).Must(d => d is null || d.Trim().Length <= clsEvidencia.DISPOSITIVO_MAX)
            .WithMessage($"El dispositivo admite máximo {clsEvidencia.DISPOSITIVO_MAX} caracteres").OverridePropertyName("DispositivoId");
        validador.RuleFor(x => nota(x)).Must(n => n is null || n.Trim().Length <= clsEvidencia.NOTA_MAX)
            .WithMessage($"La nota admite máximo {clsEvidencia.NOTA_MAX} caracteres").OverridePropertyName("Nota");
        validador.RuleFor(x => hash(x)).Must(clsReglasComunes.HashValido)
            .WithMessage("El hash SHA-256 debe tener 64 caracteres hexadecimales").OverridePropertyName("HashSha256");
    }
}

public sealed class CrearEvidenciaRequestValidator : AbstractValidator<CrearEvidenciaRequest>
{
    public CrearEvidenciaRequestValidator()
    {
        clsReglasFoto.Aplicar(this, x => x.StoragePath, x => x.SelloFecha, x => x.SelloLat, x => x.SelloLng, x => x.Precision, x => x.DispositivoId, x => x.Nota, x => x.HashSha256);
        RuleFor(x => x.HallazgoId).Must(clsReglasComunes.UuidValido).When(x => x.HallazgoId is not null)
            .WithMessage(clsReglasComunes.MENSAJE_UUID);
        RuleFor(x => x.VistaId).Must(clsReglasComunes.UuidValido).When(x => x.VistaId is not null)
            .WithMessage(clsReglasComunes.MENSAJE_UUID);
        // Toda foto es de una vista o de un hallazgo.
        RuleFor(x => x.VistaId).Must((x, vista) => vista is not null || x.HallazgoId is not null)
            .WithMessage("Indique la vista de la foto (vista_id) o el hallazgo al que pertenece (hallazgo_id)");
    }
}

public sealed class FotoHallazgoRequestValidator : AbstractValidator<FotoHallazgoRequest>
{
    public FotoHallazgoRequestValidator() =>
        clsReglasFoto.Aplicar(this, x => x.StoragePath, x => x.SelloFecha, x => x.SelloLat, x => x.SelloLng, x => x.Precision, x => x.DispositivoId, x => x.Nota, x => x.HashSha256);
}

public sealed class CrearHallazgoRequestValidator : AbstractValidator<CrearHallazgoRequest>
{
    public CrearHallazgoRequestValidator()
    {
        RuleFor(x => x.ParteVehiculo).Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage("La parte del vehículo es requerida")
            .Must(p => p is null || p.Trim().Length <= 80).WithMessage("La parte del vehículo admite máximo 80 caracteres");
        RuleFor(x => x.ClaseDano).Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Clase de daño inválida");
        RuleFor(x => x.TamanoDano).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Tamaño de daño inválido");
        RuleFor(x => x.CoordsDiagrama)
            .Must(c => c is { X: >= 0 and <= 1, Y: >= 0 and <= 1 })
            .When(x => x.CoordsDiagrama is not null)
            .WithMessage("Las coordenadas del diagrama deben estar entre 0 y 1");
        // Un hallazgo sin foto propia no se guarda.
        RuleFor(x => x.Evidencia).NotNull().WithMessage("Un hallazgo exige su foto: envíe la evidencia junto con el hallazgo");
        RuleFor(x => x.Evidencia!).SetValidator(new FotoHallazgoRequestValidator()).When(x => x.Evidencia is not null);
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
