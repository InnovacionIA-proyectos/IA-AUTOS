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

/// <summary>
/// Como va una unidad en su recorrido de Transporte segun lo que registra el conductor: sin nada registrado, con novedades en ruta, llegada confirmada sin novedades o llegada
/// confirmada con novedades.
/// </summary>
public static class clsEstadoTransporte
{
    public const string SIN_REGISTRO = "sin_registro";
    public const string CON_NOVEDAD = "con_novedad";
    public const string SIN_NOVEDAD = "sin_novedad";
    public const string LLEGADA_CON_NOVEDAD = "llegada_con_novedad";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { SIN_REGISTRO, CON_NOVEDAD, SIN_NOVEDAD, LLEGADA_CON_NOVEDAD };

    public static string De(bool llegadaCerrada, int novedades) => (llegadaCerrada, novedades > 0) switch
    {
        (true, true) => LLEGADA_CON_NOVEDAD,
        (true, false) => SIN_NOVEDAD,
        (false, true) => CON_NOVEDAD,
        _ => SIN_REGISTRO,
    };
}

/// <summary>Estado de una unidad dentro de un viaje: activa, o que no sale (un hallazgo grave la retiro del viaje).</summary>
public static class clsEstadoUnidad
{
    public const string ACTIVA = "activa";
    public const string NO_SALE = "no_sale";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { ACTIVA, NO_SALE };
}

/// <summary>
/// Un VIN dentro de un viaje. Una unidad que no sale conserva este vinculo (con su motivo, el hallazgo que la causo, quien la marco y cuando) para no
/// perder su trazabilidad cuando mas adelante vuelva a otro viaje.
/// </summary>
public sealed class clsViajeVehiculo : clsEntidadBase
{
    public const int NO_SALE_MOTIVO_MAX = 500;

    private clsViajeVehiculo()
    {
    }

    public Guid ViajeId { get; private set; }

    public Guid VehiculoId { get; private set; }

    /// <summary><see cref="clsEstadoUnidad"/>.</summary>
    public string EstadoUnidad { get; private set; } = clsEstadoUnidad.ACTIVA;

    public string? NoSaleMotivo { get; private set; }

    public Guid? NoSaleHallazgoId { get; private set; }

    public Guid? NoSaleMarcadoPor { get; private set; }

    public DateTimeOffset? NoSaleAt { get; private set; }

    /// <summary>El vehiculo que reemplaza a la unidad que no sale, si ya se asigno.</summary>
    public Guid? ReemplazoVehiculoId { get; private set; }

    public bool NoSale => EstadoUnidad == clsEstadoUnidad.NO_SALE;

    /// <summary>
    /// El VIN ya completo su ruta en este viaje (todas sus etapas con informe cerrado y la unidad no esta marcada <c>no_sale</c>): la misma regla de <c>vwViajesCompletos</c>. La mantienen los triggers
    /// de la base al cambiar lo que la determina; la aplicacion solo la lee.
    /// </summary>
    public bool Completa { get; private set; }

    /// <summary>
    /// La unidad no sale de este viaje por el hallazgo indicado: queda con su motivo, el hallazgo, quien la marco y cuando. Conserva sus etapas e inspecciones y el
    /// viaje sigue. 409 si ya estaba marcada.
    /// </summary>
    public void MarcarNoSale(string? motivo, Guid hallazgoId, Guid marcadoPor, Guid? reemplazoVehiculoId, DateTimeOffset ahora)
    {
        var texto = clsValidador.Requerido(motivo, "El motivo es requerido");
        if (texto.Length > NO_SALE_MOTIVO_MAX)
        {
            throw clsErroresCampos.De("motivo", $"El motivo admite máximo {NO_SALE_MOTIVO_MAX} caracteres");
        }

        if (NoSale)
        {
            throw new clsConflictoException("La unidad ya está marcada como que no sale de este viaje");
        }

        EstadoUnidad = clsEstadoUnidad.NO_SALE;
        NoSaleMotivo = texto;
        NoSaleHallazgoId = hallazgoId;
        NoSaleMarcadoPor = marcadoPor;
        NoSaleAt = ahora;
        ReemplazoVehiculoId = reemplazoVehiculoId;
        Tocar(ahora);
    }

    public static clsViajeVehiculo Crear(Guid viajeId, Guid vehiculoId, DateTimeOffset ahora)
    {
        var vinEnViaje = new clsViajeVehiculo { ViajeId = viajeId, VehiculoId = vehiculoId };
        vinEnViaje.Inicializar(ahora);
        return vinEnViaje;
    }
}
