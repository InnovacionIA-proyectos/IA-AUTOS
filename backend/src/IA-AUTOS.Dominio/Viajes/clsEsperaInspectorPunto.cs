using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Viajes;

/// <summary>Como va la espera del inspector de una parada.</summary>
public static class clsEstadoEspera
{
    /// <summary>El conductor activo la excepcion y nadie ha tomado la entrega de la parada ni se cerro sin inspector.</summary>
    public const string ESPERANDO = "esperando";

    /// <summary>Un inspector tomo la entrega de la parada mientras se esperaba.</summary>
    public const string INSPECTOR_LLEGO = "inspector_llego";

    /// <summary>El conductor cerro la entrega de la parada con el plan B, sin inspector del punto.</summary>
    public const string CERRADA_SIN_INSPECTOR_DEL_PUNTO = "cerrada_sin_inspector_del_punto";

    /// <summary>El viaje se cancelo o se finalizo a mano con la espera abierta.</summary>
    public const string ANULADA = "anulada";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { ESPERANDO, INSPECTOR_LLEGO, CERRADA_SIN_INSPECTOR_DEL_PUNTO, ANULADA };
}

/// <summary>
/// La espera del inspector del punto en una parada: nace cuando el conductor activa la excepcion «No hay inspector» en una parada con inspectores asignados donde nadie tomo la entrega. Guarda el motivo, la hora y el GPS
/// del celular en el momento en que la activo (<see cref="ActivadaAt"/>, nunca posterior a la hora del servidor) y la hora en que llego al servidor (<see cref="clsEntidadBase.CreatedAt"/>). Desde <see cref="ActivadaAt"/> corren
/// los dos plazos que TI ajusta (el aviso al coordinador y el plan B), que se calculan con los parametros vigentes y no se guardan. Termina cuando un inspector toma la entrega (<see cref="clsEstadoEspera.INSPECTOR_LLEGO"/>), cuando
/// el conductor cierra la parada sin inspector del punto (<see cref="clsEstadoEspera.CERRADA_SIN_INSPECTOR_DEL_PUNTO"/>) o cuando el viaje se cancela o se finaliza a mano (<see cref="clsEstadoEspera.ANULADA"/>). Una parada
/// tiene a lo sumo una espera abierta; las que terminaron quedan como historial y el conductor puede activar una nueva.
/// </summary>
public sealed class clsEsperaInspectorPunto : clsEntidadBase
{
    public const int TEXTO_MAX = 300;

    private clsEsperaInspectorPunto()
    {
    }

    public Guid ViajeId { get; private set; }

    public Guid LugarId { get; private set; }

    /// <summary>El conductor que activo la excepcion.</summary>
    public Guid ConductorId { get; private set; }

    /// <summary>El codigo del motivo (catalogo <c>motivos-sin-inspector</c>).</summary>
    public string MotivoCodigo { get; private set; } = string.Empty;

    public string? MotivoTexto { get; private set; }

    /// <summary>La hora del celular del conductor cuando activo la excepcion: desde ella corren los plazos.</summary>
    public DateTimeOffset ActivadaAt { get; private set; }

    public double? Lat { get; private set; }

    public double? Lng { get; private set; }

    public double? PrecisionM { get; private set; }

    public string Estado { get; private set; } = clsEstadoEspera.ESPERANDO;

    /// <summary>Cuando se avisó al coordinador de que nadie tomo la entrega; nulo mientras no se ha avisado.</summary>
    public DateTimeOffset? AvisoAt { get; private set; }

    /// <summary>El inspector que tomo la entrega durante la espera.</summary>
    public Guid? InspectorId { get; private set; }

    public DateTimeOffset? InspectorLlegoAt { get; private set; }

    /// <summary>Cuando el conductor cerro la parada sin inspector del punto, y quien lo hizo.</summary>
    public DateTimeOffset? CerradaAt { get; private set; }

    public Guid? CerradaPor { get; private set; }

    public bool EstaEsperando => Estado == clsEstadoEspera.ESPERANDO;

    public static clsEsperaInspectorPunto Activar(
        Guid viajeId, Guid lugarId, Guid conductorId, string motivoCodigo, string? motivoTexto, DateTimeOffset activadaAt, double? lat, double? lng, double? precisionM, DateTimeOffset ahora)
    {
        var texto = clsValidador.Opcional(motivoTexto);
        if (texto is { Length: > TEXTO_MAX })
        {
            throw clsErroresCampos.De("motivo_sin_inspector_texto", $"El texto admite máximo {TEXTO_MAX} caracteres");
        }

        var espera = new clsEsperaInspectorPunto
        {
            ViajeId = viajeId,
            LugarId = lugarId,
            ConductorId = conductorId,
            MotivoCodigo = motivoCodigo,
            MotivoTexto = texto,
            ActivadaAt = activadaAt > ahora ? ahora : activadaAt,
            Lat = lat,
            Lng = lng,
            PrecisionM = lat is null ? null : precisionM,
        };
        espera.Inicializar(ahora);
        return espera;
    }

    /// <summary>Un inspector tomo la entrega de la parada mientras se esperaba: la espera termina como «el inspector llego».</summary>
    public void RegistrarLlegadaDelInspector(Guid inspectorId, DateTimeOffset ahora)
    {
        AsegurarEsperando();
        Estado = clsEstadoEspera.INSPECTOR_LLEGO;
        InspectorId = inspectorId;
        InspectorLlegoAt = ahora;
        Tocar(ahora);
    }

    /// <summary>El conductor cerro la entrega de la parada con el plan B, sin inspector del punto.</summary>
    public void CerrarSinInspectorDelPunto(Guid conductorId, DateTimeOffset ahora)
    {
        AsegurarEsperando();
        Estado = clsEstadoEspera.CERRADA_SIN_INSPECTOR_DEL_PUNTO;
        CerradaAt = ahora;
        CerradaPor = conductorId;
        Tocar(ahora);
    }

    /// <summary>El viaje se cancelo o se finalizo a mano: la espera abierta ya no tiene sentido.</summary>
    public void Anular(DateTimeOffset ahora)
    {
        AsegurarEsperando();
        Estado = clsEstadoEspera.ANULADA;
        Tocar(ahora);
    }

    /// <summary>Marca que ya se aviso al coordinador de que nadie tomo la entrega.</summary>
    public void MarcarAvisada(DateTimeOffset ahora)
    {
        AvisoAt ??= ahora;
        Tocar(ahora);
    }

    private void AsegurarEsperando()
    {
        if (!EstaEsperando)
        {
            throw new clsConflictoException("La espera del inspector del punto ya terminó");
        }
    }
}

public interface IEsperaInspectorPuntoRepository
{
    /// <summary>La espera abierta de la parada (<see cref="clsEstadoEspera.ESPERANDO"/>), o nulo.</summary>
    Task<clsEsperaInspectorPunto?> ObtenerAbiertaAsync(Guid viajeId, Guid lugarId, CancellationToken ct = default);

    /// <summary>La ultima espera de la parada (la mas reciente por hora de activacion), abierta o terminada, o nulo.</summary>
    Task<clsEsperaInspectorPunto?> ObtenerUltimaAsync(Guid viajeId, Guid lugarId, CancellationToken ct = default);

    /// <summary>Las esperas abiertas del viaje.</summary>
    Task<IReadOnlyList<clsEsperaInspectorPunto>> ObtenerAbiertasDeViajeAsync(Guid viajeId, CancellationToken ct = default);

    /// <summary>Las esperas abiertas que aun no avisaron y que se activaron a mas tardar en <paramref name="activadasHasta"/> (las que ya cumplieron el plazo del aviso), de la mas antigua a la mas reciente.</summary>
    Task<IReadOnlyList<clsEsperaInspectorPunto>> ObtenerPorAvisarAsync(DateTimeOffset activadasHasta, int limite, CancellationToken ct = default);

    void Agregar(clsEsperaInspectorPunto espera);
}
