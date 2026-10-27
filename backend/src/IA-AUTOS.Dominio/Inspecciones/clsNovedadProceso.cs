using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>Lo que registra una novedad de proceso: algo que paso en el proceso y no es un dano.</summary>
public static class clsTipoNovedadProceso
{
    /// <summary>El conductor hizo la inspeccion de salida del cargue porque el inspector previsto no estaba.</summary>
    public const string CARGUE_SIN_INSPECTOR = "cargue_sin_inspector";

    /// <summary>La transferencia del cargue se cerro sin firma de entrega (el conductor hizo la salida).</summary>
    public const string RECIBO_SIN_ENTREGA = "recibo_sin_entrega";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { CARGUE_SIN_INSPECTOR, RECIBO_SIN_ENTREGA };
}

/// <summary>
/// Una novedad de proceso: un registro nuevo con su autor, su fecha y el lugar donde ocurrio; no se edita ni se borra (la base de datos lo impide). No es un hallazgo: no tiene zona, clase
/// ni nivel y no cuenta en los patrones, el mapa de calor ni la tasa de rechazo. El cargue sin inspector siempre trae su motivo (del catalogo <c>motivos-sin-inspector</c>) y, si el
/// motivo lo requiere, su texto.
/// </summary>
public sealed class clsNovedadProceso
{
    public const int TEXTO_MAX = 300;

    private clsNovedadProceso()
    {
    }

    public Guid Id { get; private set; }

    public string Tipo { get; private set; } = string.Empty;

    public Guid ViajeId { get; private set; }

    public Guid VehiculoId { get; private set; }

    public Guid EtapaId { get; private set; }

    public Guid InspeccionId { get; private set; }

    /// <summary>El codigo del motivo (catalogo <c>motivos-sin-inspector</c>); obligatorio en el cargue sin inspector.</summary>
    public string? MotivoCodigo { get; private set; }

    public string? MotivoTexto { get; private set; }

    public Guid UsuarioId { get; private set; }

    /// <summary>Cuando ocurrio (el momento en que el conductor eligio el motivo, aunque lo envie despues por falta de senal).</summary>
    public DateTimeOffset Fecha { get; private set; }

    public double? Lat { get; private set; }

    public double? Lng { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static clsNovedadProceso Crear(
        string tipo,
        Guid viajeId,
        Guid vehiculoId,
        Guid etapaId,
        Guid inspeccionId,
        string? motivoCodigo,
        string? motivoTexto,
        Guid usuarioId,
        DateTimeOffset fecha,
        double? lat,
        double? lng,
        DateTimeOffset ahora)
    {
        clsValidador.EnCatalogo(tipo, clsTipoNovedadProceso.VALORES, "tipo");
        var codigo = clsValidador.Opcional(motivoCodigo);
        if (tipo == clsTipoNovedadProceso.CARGUE_SIN_INSPECTOR && codigo is null)
        {
            throw clsErroresCampos.De("motivo_sin_inspector", "El cargue sin inspector exige su motivo");
        }

        var texto = clsValidador.Opcional(motivoTexto);
        if (texto is not null && codigo is null)
        {
            throw clsErroresCampos.De("motivo_sin_inspector_texto", "El texto del motivo exige un motivo");
        }

        if (texto is { Length: > TEXTO_MAX })
        {
            throw clsErroresCampos.De("motivo_sin_inspector_texto", $"El texto del motivo admite máximo {TEXTO_MAX} caracteres");
        }

        clsValidador.Latitud(lat);
        clsValidador.Longitud(lng);
        return new clsNovedadProceso
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            ViajeId = viajeId,
            VehiculoId = vehiculoId,
            EtapaId = etapaId,
            InspeccionId = inspeccionId,
            MotivoCodigo = codigo,
            MotivoTexto = texto,
            UsuarioId = usuarioId,
            Fecha = fecha,
            Lat = lat,
            Lng = lng,
            CreatedAt = ahora,
        };
    }
}

public interface INovedadProcesoRepository
{
    void Agregar(clsNovedadProceso novedad);
}
