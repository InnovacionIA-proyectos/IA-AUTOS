using System.Text.RegularExpressions;
using FluentValidation;
using IAAutos.Dominio.Catalogos;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Custodia;
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
    private const int MOTIVO_MAX = 30;
    private const int MOTIVO_TEXTO_MAX = 300;

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
        // El motivo y su texto solo los admite el servicio (conductor, salida del Cargue con apoyo); aqui solo la forma: sin espacios vacios y de 1 a 300 caracteres.
        RuleFor(x => x.MotivoSinInspector).Must(m => m is null || m.Trim().Length is > 0 and <= MOTIVO_MAX)
            .WithMessage("El motivo no puede estar vacío");
        RuleFor(x => x.MotivoSinInspectorTexto).Must(t => t is null || t.Trim().Length is > 0 and <= MOTIVO_TEXTO_MAX)
            .WithMessage($"El texto del motivo debe tener de 1 a {MOTIVO_TEXTO_MAX} caracteres");
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

public sealed class FotoEvidenciaRequestValidator : AbstractValidator<FotoEvidenciaRequest>
{
    public FotoEvidenciaRequestValidator() =>
        clsReglasFoto.Aplicar(this, x => x.StoragePath, x => x.SelloFecha, x => x.SelloLat, x => x.SelloLng, x => x.Precision, x => x.DispositivoId, x => x.Nota, x => x.HashSha256);
}

/// <summary>La zona de un hallazgo: lado + numero de un cuadrante, o una zona provisional de la lista (exactamente una de las dos formas).</summary>
internal static class clsReglasZona
{
    public static void Aplicar<T>(AbstractValidator<T> validador, Func<T, int?> cuadranteNumero, Func<T, string?> lado, Func<T, string?> zonaProvisional)
    {
        validador.RuleFor(x => cuadranteNumero(x)).Must((x, numero) => numero is not null ^ !string.IsNullOrWhiteSpace(zonaProvisional(x)))
            .WithMessage("Indique el cuadrante (cuadrante_numero y lado) o una zona provisional (zona_provisional), no ambos ni ninguno").OverridePropertyName("CuadranteNumero");
        validador.RuleFor(x => cuadranteNumero(x)).GreaterThanOrEqualTo(1).When(x => cuadranteNumero(x) is not null)
            .WithMessage("El número del cuadrante debe ser un entero >= 1").OverridePropertyName("CuadranteNumero");
        validador.RuleFor(x => lado(x)).Must(l => l is not null && clsLado.VALORES.Contains(l))
            .When(x => cuadranteNumero(x) is { } n && n >= 1 && clsLado.LlevaLado(n))
            .WithMessage("El lado es requerido del cuadrante 1 al 25 (izquierdo o derecho)").OverridePropertyName("Lado");
        validador.RuleFor(x => lado(x)).Must(l => l is null)
            .When(x => cuadranteNumero(x) is { } n && n > clsLado.ULTIMO_NUMERO_CON_LADO)
            .WithMessage("Del cuadrante 26 en adelante no se envía el lado").OverridePropertyName("Lado");
        validador.RuleFor(x => lado(x)).Must(l => l is null)
            .When(x => cuadranteNumero(x) is null && !string.IsNullOrWhiteSpace(zonaProvisional(x)))
            .WithMessage("Una zona provisional no lleva lado").OverridePropertyName("Lado");
        validador.RuleFor(x => zonaProvisional(x)).Must(z => z is not null && clsZonaProvisional.VALORES.Contains(z))
            .When(x => !string.IsNullOrWhiteSpace(zonaProvisional(x)))
            .WithMessage("Zona provisional inválida").OverridePropertyName("ZonaProvisional");
    }
}

public sealed class CrearHallazgoRequestValidator : AbstractValidator<CrearHallazgoRequest>
{
    public CrearHallazgoRequestValidator()
    {
        clsReglasZona.Aplicar(this, x => x.CuadranteNumero, x => x.Lado, x => x.ZonaProvisional);
        RuleFor(x => x.ClaseDano).Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Clase de daño inválida");
        RuleFor(x => x.TamanoDano).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Tamaño de daño inválido");
        RuleFor(x => x.Nota).Must(n => n is null || n.Trim().Length <= clsHallazgo.NOTA_MAX)
            .WithMessage($"La nota admite máximo {clsHallazgo.NOTA_MAX} caracteres");
        // Un hallazgo preexistente siempre apunta al hallazgo previo del vehiculo que ya lo tenia (y solo ese lleva origen).
        RuleFor(x => x.HallazgoOrigenId).NotNull().When(x => x.EsPreexistente == true)
            .WithMessage("Un hallazgo preexistente exige hallazgo_origen_id: el hallazgo previo del vehículo que ya tenía el daño");
        RuleFor(x => x.HallazgoOrigenId).Must(id => id is null).When(x => x.EsPreexistente != true)
            .WithMessage("hallazgo_origen_id solo se envía con es_preexistente verdadero");
        RuleFor(x => x.HallazgoOrigenId).Must(id => id is null || clsReglasComunes.UuidValido(id))
            .WithMessage(clsReglasComunes.MENSAJE_UUID);
        // Un hallazgo sin foto propia no se guarda.
        RuleFor(x => x.Evidencia).NotNull().WithMessage("Un hallazgo exige su foto: envíe la evidencia junto con el hallazgo");
        RuleFor(x => x.Evidencia!).SetValidator(new FotoEvidenciaRequestValidator()).When(x => x.Evidencia is not null);
    }
}

/// <summary>Reglas de forma comunes a las solicitudes de la transferencia: los dos ids (opcionales aqui; el servicio los exige al crearla) y la firma.</summary>
internal static class clsReglasTransferencia
{
    /// <summary>Limite de una firma dibujada (trazo SVG o ruta de imagen): 200 KB de texto.</summary>
    public const int FIRMA_MAX = 200_000;

    public static void Ids<T>(AbstractValidator<T> validador, Func<T, Guid?> entrega, Func<T, Guid?> recibe)
    {
        validador.RuleFor(x => entrega(x)).Must(clsReglasComunes.UuidValido).When(x => entrega(x) is not null)
            .WithMessage(clsReglasComunes.MENSAJE_UUID).OverridePropertyName("EntregaUsuarioId");
        validador.RuleFor(x => recibe(x)).Must(clsReglasComunes.UuidValido).When(x => recibe(x) is not null)
            .WithMessage(clsReglasComunes.MENSAJE_UUID).OverridePropertyName("RecibeUsuarioId");
        validador.RuleFor(x => recibe(x))
            .Must((x, r) => r != entrega(x))
            .When(x => clsReglasComunes.UuidValido(entrega(x)) && clsReglasComunes.UuidValido(recibe(x)))
            .WithMessage("Quien entrega y quien recibe deben ser usuarios distintos").OverridePropertyName("RecibeUsuarioId");
    }

    public static void Firma<T>(AbstractValidator<T> validador, Func<T, string?> firma) =>
        validador.RuleFor(x => firma(x))
            .Must(f => !string.IsNullOrEmpty(f)).WithMessage("La firma es obligatoria")
            .Must(f => f is null || f.Length <= FIRMA_MAX).WithMessage("La firma excede el tamaño máximo permitido")
            .OverridePropertyName("Firma");
}

public sealed class FirmarTransferenciaRequestValidator : AbstractValidator<FirmarTransferenciaRequest>
{
    public FirmarTransferenciaRequestValidator()
    {
        clsReglasTransferencia.Ids(this, x => x.EntregaUsuarioId, x => x.RecibeUsuarioId);
        clsReglasTransferencia.Firma(this, x => x.Firma);
    }
}

public sealed class RecibirConReservasRequestValidator : AbstractValidator<RecibirConReservasRequest>
{
    public RecibirConReservasRequestValidator()
    {
        clsReglasTransferencia.Ids(this, x => x.EntregaUsuarioId, x => x.RecibeUsuarioId);
        clsReglasTransferencia.Firma(this, x => x.Firma);
        // Recibir con reservas sin escribir las observaciones no se permite.
        RuleFor(x => x.Reservas).Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage("Escriba las observaciones: no se puede recibir con reservas sin ellas")
            .Must(r => r is null || r.Trim().Length <= clsTransferenciaCustodia.RESERVAS_MAX)
            .WithMessage($"Las observaciones admiten máximo {clsTransferenciaCustodia.RESERVAS_MAX} caracteres");
    }
}

public sealed class RegistrarNoFirmaRequestValidator : AbstractValidator<RegistrarNoFirmaRequest>
{
    public RegistrarNoFirmaRequestValidator()
    {
        clsReglasTransferencia.Ids(this, x => x.EntregaUsuarioId, x => x.RecibeUsuarioId);
        RuleFor(x => x.Parte).Must(p => p is not null && clsParteFirmante.VALORES.Contains(p))
            .WithMessage("Indique quien no firmó: entrega o recibe");
        RuleFor(x => x.Motivo).Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("El motivo de la no firma es obligatorio")
            .Must(m => m is null || m.Trim().Length <= clsTransferenciaCustodia.MOTIVO_MAX)
            .WithMessage($"El motivo admite máximo {clsTransferenciaCustodia.MOTIVO_MAX} caracteres");
        RuleFor(x => x.TestigoUsuarioId).Must(clsReglasComunes.UuidValido).WithMessage("La constancia exige un testigo con usuario");
        RuleFor(x => x.Evidencia).NotNull().WithMessage("La constancia exige una foto");
        RuleFor(x => x.Evidencia!).SetValidator(new FotoEvidenciaRequestValidator()).When(x => x.Evidencia is not null);
    }
}
