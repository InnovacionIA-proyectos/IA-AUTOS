using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// Un hallazgo del listado: el <c>HallazgoDto</c> mas el contexto de donde salio (VIN, viaje y su codigo, proceso, momento, estado y cierre de su inspeccion) y su foto propia
/// (nula si no tiene).
/// </summary>
public record HallazgoListadoDto : HallazgoDto
{
    public HallazgoListadoDto(
        HallazgoDto hallazgo,
        string vin,
        Guid viajeId,
        string? codigoMtrack,
        string proceso,
        string momento,
        string inspeccionEstado,
        DateTimeOffset? cerradaAt,
        EvidenciaDto? evidencia)
        : base(hallazgo)
    {
        Vin = vin;
        ViajeId = viajeId;
        CodigoMtrack = codigoMtrack;
        Proceso = proceso;
        Momento = momento;
        InspeccionEstado = inspeccionEstado;
        CerradaAt = cerradaAt;
        Evidencia = evidencia;
    }

    public string Vin { get; init; }

    public Guid ViajeId { get; init; }

    public string? CodigoMtrack { get; init; }

    public string Proceso { get; init; }

    public string Momento { get; init; }

    public string InspeccionEstado { get; init; }

    public DateTimeOffset? CerradaAt { get; init; }

    public EvidenciaDto? Evidencia { get; init; }
}

/// <summary>El hallazgo con su historial de seguimiento y sus notas aclaratorias (vacio si no tiene).</summary>
public sealed record HallazgoDetalleDto : HallazgoListadoDto
{
    public HallazgoDetalleDto(HallazgoListadoDto fila, IReadOnlyList<SeguimientoDto> seguimiento, IReadOnlyList<NotaAclaratoriaDto> notasAclaratorias)
        : base(fila)
    {
        Seguimiento = seguimiento;
        NotasAclaratorias = notasAclaratorias;
    }

    public IReadOnlyList<SeguimientoDto> Seguimiento { get; init; }

    public IReadOnlyList<NotaAclaratoriaDto> NotasAclaratorias { get; init; }
}

public sealed record ListadoHallazgosDto(IReadOnlyList<HallazgoListadoDto> Items, int Total, int Pagina, int Tamano);

/// <summary>Filtros del listado tal como llegan en la consulta (texto); el servicio los valida y responde todos los errores juntos.</summary>
public sealed record FiltrosHallazgosRequest(
    string? EstadoSeguimiento,
    string? Origen,
    string? Revisado,
    string? ClienteId,
    string? ViajeId,
    string? Vin,
    string? InspeccionId,
    string? Nivel,
    string? CuadranteNumero,
    string? Lado,
    string? Proceso,
    string? Desde,
    string? Hasta,
    string? Orden,
    int? Pagina,
    int? Tamano);

/// <summary>Los filtros ya validados (el alcance de quien consulta, por viaje o por cliente, va aparte).</summary>
public sealed record FiltrosHallazgos(
    string? EstadoSeguimiento,
    string? Origen,
    bool? Revisado,
    Guid? ViajeId,
    string? Vin,
    Guid? InspeccionId,
    string? Nivel,
    int? CuadranteNumero,
    string? Lado,
    string? Proceso,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta,
    string Orden);

/// <summary>Como se ordenan los hallazgos del listado.</summary>
public static class clsOrdenHallazgos
{
    /// <summary>Del cierre de la inspeccion mas reciente al mas antiguo (por defecto).</summary>
    public const string RECIENTES = "recientes";

    public const string ANTIGUOS = "antiguos";

    /// <summary>De la severidad mas alta (V1+) a la mas baja (V3); las novedades sin nivel al final.</summary>
    public const string SEVERIDAD = "severidad";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { RECIENTES, ANTIGUOS, SEVERIDAD };
}

/// <summary>Lectura de los hallazgos (listado y detalle). La implementa la infraestructura.</summary>
public interface IConsultasHallazgos
{
    /// <summary>
    /// Una pagina del listado. Con <paramref name="soloDeUsuario"/>, solo los de los viajes de ese usuario; con <paramref name="soloDeCliente"/> (o el cliente elegido por el super
    /// administrador), solo los de VIN de ese cliente.
    /// </summary>
    Task<(IReadOnlyList<HallazgoListadoDto> Filas, int Total)> ListarAsync(
        FiltrosHallazgos filtros, Guid? soloDeUsuario, Guid? clienteId, int desplazamiento, int tamano, CancellationToken ct = default);

    /// <summary>El hallazgo como una fila del listado (y el viaje de su inspeccion); nulo si no existe.</summary>
    Task<(HallazgoListadoDto Fila, Guid ViajeId)?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>True si el hallazgo es de un viaje en el que participa ese usuario.</summary>
    Task<bool> UsuarioVeElViajeAsync(Guid viajeId, Guid usuarioId, CancellationToken ct = default);
}

/// <summary>
/// Listado y detalle de los hallazgos. El inspector solo ve los de sus viajes; el coordinador, comercial y administradores, los de su cliente (el super administrador, todos y puede
/// elegir un cliente); el conductor no los consulta. Lo consumen la bandeja del coordinador y las novedades por revisar.
/// </summary>
public sealed class clsHallazgosService(
    IConsultasHallazgos consultas,
    IConsultasInspecciones consultasInspecciones,
    IConsultasNotas notas,
    IUsuarioActual actual)
{
    public const int TAMANO_POR_DEFECTO = 50;
    public const int TAMANO_MAXIMO = 200;

    public async Task<ListadoHallazgosDto> ListarAsync(FiltrosHallazgosRequest solicitud, CancellationToken ct = default)
    {
        var (filtros, clienteElegido) = Parsear(solicitud);
        var pagina = Math.Max(solicitud.Pagina ?? 1, 1);
        var tamano = Math.Clamp(solicitud.Tamano ?? TAMANO_POR_DEFECTO, 1, TAMANO_MAXIMO);
        var (filas, total) = await consultas.ListarAsync(filtros, actual.SoloSusViajes(), actual.ClienteDelFiltro(clienteElegido), (pagina - 1) * tamano, tamano, ct);
        return new ListadoHallazgosDto(filas, total, pagina, tamano);
    }

    /// <summary>El hallazgo con su historial. 404 si no existe; 403 si es de un viaje ajeno (inspector) o de otro cliente.</summary>
    public async Task<HallazgoDetalleDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var (fila, viajeId) = await consultas.ObtenerAsync(id, ct) ?? throw new clsEntidadNoEncontradaException($"Hallazgo {id} no encontrado");
        if (actual.SoloSusViajes() is { } usuarioViaje && !await consultas.UsuarioVeElViajeAsync(viajeId, usuarioViaje, ct))
        {
            throw new clsProhibidoException("El hallazgo es de un viaje que no está asignado a su usuario");
        }

        if (actual.ClienteRestringido() is { } cliente && !await consultasInspecciones.EsDeClienteAsync(fila.InspeccionId, cliente, ct))
        {
            throw new clsProhibidoException("El hallazgo es de un VIN de otro cliente");
        }

        return new HallazgoDetalleDto(fila, await consultasInspecciones.ObtenerHistorialAsync(id, ct), await notas.ListarAsync(clsEntidadNota.HALLAZGO, id, ct));
    }

    private static (FiltrosHallazgos Filtros, Guid? ClienteElegido) Parsear(FiltrosHallazgosRequest s)
    {
        var errores = new Dictionary<string, string[]>();
        var estado = clsParseoFiltros.Catalogo(s.EstadoSeguimiento, clsEstadoSeguimiento.VALORES, "estado_seguimiento", errores);
        var origen = clsParseoFiltros.Catalogo(s.Origen, clsOrigenHallazgo.VALORES, "origen", errores);
        var orden = clsParseoFiltros.Catalogo(s.Orden, clsOrdenHallazgos.VALORES, "orden", errores) ?? clsOrdenHallazgos.RECIENTES;
        var lado = clsParseoFiltros.Catalogo(s.Lado, clsLado.VALORES, "lado", errores);
        var nivel = clsParseoFiltros.Catalogo(s.Nivel, NIVELES, "nivel", errores);
        var viajeId = clsParseoFiltros.Uuid(s.ViajeId, "viaje_id", errores);
        var inspeccionId = clsParseoFiltros.Uuid(s.InspeccionId, "inspeccion_id", errores);
        var clienteId = clsParseoFiltros.Uuid(s.ClienteId, "cliente_id", errores);
        var desde = clsParseoFiltros.Fecha(s.Desde, "desde", errores);
        var hasta = clsParseoFiltros.Fecha(s.Hasta, "hasta", errores);

        bool? revisado = null;
        if (!string.IsNullOrWhiteSpace(s.Revisado))
        {
            if (bool.TryParse(s.Revisado, out var valor))
            {
                revisado = valor;
            }
            else
            {
                errores["revisado"] = ["revisado debe ser true o false"];
            }
        }

        int? cuadrante = null;
        if (!string.IsNullOrWhiteSpace(s.CuadranteNumero))
        {
            if (int.TryParse(s.CuadranteNumero, out var numero) && numero >= 1)
            {
                cuadrante = numero;
            }
            else
            {
                errores["cuadrante_numero"] = ["cuadrante_numero debe ser un entero >= 1"];
            }
        }

        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var filtros = new FiltrosHallazgos(
            estado, origen, revisado, viajeId, clsValidador.Opcional(s.Vin) is { } vin ? clsVin.Normalizar(vin) : null, inspeccionId, nivel, cuadrante, lado,
            clsValidador.Opcional(s.Proceso), desde, hasta, orden);
        return (filtros, clienteId);
    }

    private static readonly IReadOnlySet<string> NIVELES = new HashSet<string> { clsCodigosNivel.V1_PLUS, clsCodigosNivel.V1, clsCodigosNivel.V2, clsCodigosNivel.V3 };
}
