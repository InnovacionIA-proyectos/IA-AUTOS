using System.Text.RegularExpressions;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Viajes;

/// <summary>Estados de un viaje. Un viaje finalizado o cancelado ya no admite cambios ni inspecciones nuevas.</summary>
public static class clsEstadoViaje
{
    public const string PROGRAMADO = "programado";
    public const string EN_CURSO = "en_curso";
    public const string FINALIZADO = "finalizado";
    public const string CANCELADO = "cancelado";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { PROGRAMADO, EN_CURSO, FINALIZADO, CANCELADO };

    /// <summary>Viajes que aun se pueden modificar: programados o en curso.</summary>
    public static readonly string[] ACTIVOS = [PROGRAMADO, EN_CURSO];

    public static bool EsActivo(string estado) => estado is PROGRAMADO or EN_CURSO;

    /// <summary>programado -> en_curso -> finalizado; un viaje programado o en curso se puede cancelar. Finalizado y cancelado son finales.</summary>
    public static bool PuedePasar(string actual, string nuevo) =>
        (actual, nuevo) is (PROGRAMADO, EN_CURSO) or (PROGRAMADO, CANCELADO) or (EN_CURSO, FINALIZADO) or (EN_CURSO, CANCELADO);
}

/// <summary>
/// El viaje es el eje del modelo: una unidad transportadora (tipo, placa, trailer) lleva uno o varios VIN con un conductor y, si hay,
/// un inspector, entre un origen y un destino. Cada etapa e inspeccion cuelga de un viaje, y inspectores y conductores solo ven los
/// VIN de sus viajes.
/// </summary>
public sealed partial class clsViaje : clsEntidadBase
{
    public const int CODIGO_MAX = 50;
    public const int PLACA_MAX = 50;
    public const int TRAILER_MAX = 50;

    private clsViaje()
    {
    }

    /// <summary>Codigo del viaje en MTrack. Se digita o llega en el Excel (aun no hay integracion). Unico. Nulo en los viajes migrados.</summary>
    public string? CodigoMtrack { get; private set; }

    public Guid? TipoUnidadId { get; private set; }

    public string? Placa { get; private set; }

    public string? Trailer { get; private set; }

    public Guid? ConductorId { get; private set; }

    public Guid? InspectorId { get; private set; }

    public Guid? OrigenId { get; private set; }

    public Guid? DestinoId { get; private set; }

    public string Estado { get; private set; } = clsEstadoViaje.PROGRAMADO;

    public Guid? CreadoPor { get; private set; }

    public bool EstaActivo => clsEstadoViaje.EsActivo(Estado);

    public static clsViaje Crear(
        string? codigoMtrack,
        Guid? tipoUnidadId,
        string? placa,
        string? trailer,
        Guid? conductorId,
        Guid? inspectorId,
        Guid? origenId,
        Guid? destinoId,
        Guid? creadoPor,
        DateTimeOffset ahora)
    {
        var viaje = new clsViaje
        {
            CodigoMtrack = CodigoValido(codigoMtrack),
            TipoUnidadId = tipoUnidadId is { } t && t != Guid.Empty ? t : throw clsErroresCampos.De("tipo_unidad_id", "La unidad transportadora es requerida"),
            Placa = PlacaValida(placa),
            Trailer = TrailerValido(trailer),
            ConductorId = conductorId,
            InspectorId = inspectorId,
            OrigenId = origenId,
            DestinoId = destinoId,
            CreadoPor = creadoPor,
        };
        viaje.Inicializar(ahora);
        return viaje;
    }

    /// <summary>
    /// Cambia lo propio del viaje. Los campos que admiten vacio (trailer, conductor, inspector, origen y destino) se cambian solo
    /// si su indicador es verdadero: asi se distingue "no enviado" de "enviado en nulo".
    /// </summary>
    public void Actualizar(
        string? codigoMtrack,
        Guid? tipoUnidadId,
        string? placa,
        bool cambiaTrailer,
        string? trailer,
        bool cambiaConductor,
        Guid? conductorId,
        bool cambiaInspector,
        Guid? inspectorId,
        bool cambiaOrigen,
        Guid? origenId,
        bool cambiaDestino,
        Guid? destinoId,
        DateTimeOffset ahora)
    {
        AsegurarActivo();
        if (codigoMtrack is not null)
        {
            CodigoMtrack = CodigoValido(codigoMtrack);
        }

        if (tipoUnidadId is not null)
        {
            TipoUnidadId = tipoUnidadId.Value != Guid.Empty ? tipoUnidadId : throw clsErroresCampos.De("tipo_unidad_id", "Debe ser un UUID válido");
        }

        if (placa is not null)
        {
            Placa = PlacaValida(placa);
        }

        if (cambiaTrailer)
        {
            Trailer = TrailerValido(trailer);
        }

        if (cambiaConductor)
        {
            ConductorId = conductorId;
        }

        if (cambiaInspector)
        {
            InspectorId = inspectorId;
        }

        if (cambiaOrigen)
        {
            OrigenId = origenId;
        }

        if (cambiaDestino)
        {
            DestinoId = destinoId;
        }

        Tocar(ahora);
    }

    /// <summary>programado -> en_curso -> finalizado, o cancelado desde programado o en curso. Cualquier otro cambio es un conflicto.</summary>
    public void CambiarEstado(string nuevo, DateTimeOffset ahora)
    {
        if (!clsEstadoViaje.VALORES.Contains(nuevo))
        {
            throw clsErroresCampos.De("estado", $"Valor inválido. Permitidos: {string.Join(", ", clsEstadoViaje.VALORES.Order())}");
        }

        if (!clsEstadoViaje.PuedePasar(Estado, nuevo))
        {
            throw new clsConflictoException($"Un viaje {Estado.Replace('_', ' ')} no puede pasar a {nuevo.Replace('_', ' ')}");
        }

        Estado = nuevo;
        Tocar(ahora);
    }

    /// <summary>La primera inspeccion de un viaje programado lo pone en curso.</summary>
    public void RegistrarActividad(DateTimeOffset ahora)
    {
        if (Estado == clsEstadoViaje.PROGRAMADO)
        {
            Estado = clsEstadoViaje.EN_CURSO;
            Tocar(ahora);
        }
    }

    /// <summary>Un viaje finalizado o cancelado no admite cambios (409).</summary>
    public void AsegurarActivo()
    {
        if (!EstaActivo)
        {
            throw new clsConflictoException($"El viaje está {Estado} y no admite cambios");
        }
    }

    /// <summary>El conductor o el inspector del viaje, mientras el viaje no este cancelado.</summary>
    public bool Participa(Guid usuarioId) =>
        Estado != clsEstadoViaje.CANCELADO && (ConductorId == usuarioId || InspectorId == usuarioId);

    public static string CodigoValido(string? codigo)
    {
        var limpio = clsValidador.Opcional(codigo)
            ?? throw clsErroresCampos.De("codigo_mtrack", "El código MTrack es requerido");
        if (limpio.Length > CODIGO_MAX || !FormatoCodigo().IsMatch(limpio))
        {
            throw clsErroresCampos.De("codigo_mtrack", $"El código MTrack admite letras, números y . _ - / (máximo {CODIGO_MAX} caracteres)");
        }

        return limpio;
    }

    public static string PlacaValida(string? placa)
    {
        var limpia = clsValidador.Opcional(placa) ?? throw clsErroresCampos.De("placa", "La placa es requerida");
        return limpia.Length <= PLACA_MAX ? limpia : throw clsErroresCampos.De("placa", $"La placa admite máximo {PLACA_MAX} caracteres");
    }

    public static string? TrailerValido(string? trailer)
    {
        var limpio = clsValidador.Opcional(trailer);
        return limpio is { Length: > TRAILER_MAX }
            ? throw clsErroresCampos.De("trailer", $"El tráiler admite máximo {TRAILER_MAX} caracteres")
            : limpio;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._\-/]+$")]
    private static partial Regex FormatoCodigo();
}

/// <summary>Un VIN dentro de un viaje.</summary>
public sealed class clsViajeVehiculo : clsEntidadBase
{
    private clsViajeVehiculo()
    {
    }

    public Guid ViajeId { get; private set; }

    public Guid VehiculoId { get; private set; }

    public static clsViajeVehiculo Crear(Guid viajeId, Guid vehiculoId, DateTimeOffset ahora)
    {
        var vinEnViaje = new clsViajeVehiculo { ViajeId = viajeId, VehiculoId = vehiculoId };
        vinEnViaje.Inicializar(ahora);
        return vinEnViaje;
    }
}
