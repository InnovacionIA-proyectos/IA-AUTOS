using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>Que hecho registra una fila del historial de seguimiento de un hallazgo.</summary>
public static class clsAccionSeguimiento
{
    /// <summary>El coordinador cambia el estado del hallazgo.</summary>
    public const string SEGUIMIENTO = "seguimiento";

    /// <summary>Un inspector revisa una novedad del conductor.</summary>
    public const string REVISION = "revision";

    /// <summary>La unidad no sale del viaje por ese hallazgo.</summary>
    public const string NO_SALE = "no_sale";
}

/// <summary>Codigos de los cuatro niveles de severidad (V1+, V1, V2 y V3) a los que la logica del negocio hace referencia.</summary>
public static class clsCodigosNivel
{
    public const string V1_PLUS = "v1_plus";
    public const string V1 = "v1";
    public const string V2 = "v2";
    public const string V3 = "v3";
}

/// <summary>
/// Cuanta evidencia tiene un hallazgo. Esta completa cuando tiene su foto propia y la transferencia de su inspeccion se cerro firmada o con reservas (en las dos
/// firman las dos partes); una transferencia sin cerrar, o cerrada como «no firma», no cuenta. Marcar un hallazgo como rechazado nunca se bloquea por esto: un V1 se
/// rechaza en preembarque, antes de que exista una transferencia firmada; lo que falta se informa y el hallazgo no cuenta en la tasa de rechazo mientras no este completo.
/// </summary>
public static class clsEvidenciaHallazgo
{
    public const string FOTO_HALLAZGO = "foto_hallazgo";
    public const string TRANSFERENCIA_SIN_CIERRE = "transferencia_sin_cierre";
    public const string TRANSFERENCIA_NO_FIRMA = "transferencia_no_firma";

    /// <summary>
    /// Lo que le falta al hallazgo: su foto propia (que no sea la del sello de quien valida) y el cierre de la transferencia de su inspeccion
    /// (<paramref name="tipoCierre"/> nulo si la transferencia no existe o sigue abierta).
    /// </summary>
    public static IReadOnlyList<string> Faltante(bool tieneFotoPropia, string? tipoCierre)
    {
        var faltante = new List<string>();
        if (!tieneFotoPropia)
        {
            faltante.Add(FOTO_HALLAZGO);
        }

        if (tipoCierre is null)
        {
            faltante.Add(TRANSFERENCIA_SIN_CIERRE);
        }
        else if (tipoCierre == clsTipoCierre.NO_FIRMA)
        {
            faltante.Add(TRANSFERENCIA_NO_FIRMA);
        }

        return faltante;
    }

    public static bool EstaCompleta(bool tieneFotoPropia, string? tipoCierre) => Faltante(tieneFotoPropia, tipoCierre).Count == 0;
}

/// <summary>
/// Una fila del historial de seguimiento de un hallazgo: un hecho que nunca se corrige (la tabla es append-only). Un cambio de seguimiento dice el estado anterior y el nuevo,
/// quien lo hizo, su nota y, si la hay, la foto del sello de quien valida; un «no sale» queda con el motivo como nota.
/// </summary>
public sealed class clsHallazgoSeguimiento : clsEntidadBase
{
    public const int NOTA_MAX = 1000;

    private clsHallazgoSeguimiento()
    {
    }

    public Guid HallazgoId { get; private set; }

    /// <summary><see cref="clsAccionSeguimiento"/>.</summary>
    public string Accion { get; private set; } = string.Empty;

    public string? EstadoAnterior { get; private set; }

    public string? EstadoNuevo { get; private set; }

    public Guid UsuarioId { get; private set; }

    public string? Nota { get; private set; }

    /// <summary>La foto del sello de quien valida (una evidencia de la inspeccion marcada como sello).</summary>
    public Guid? EvidenciaSelloId { get; private set; }

    /// <summary>JSON con lo asignado en una revision.</summary>
    public string? Detalle { get; private set; }

    public static clsHallazgoSeguimiento CrearCambioDeEstado(
        Guid hallazgoId,
        string estadoAnterior,
        string estadoNuevo,
        Guid usuarioId,
        string? nota,
        Guid? evidenciaSelloId,
        DateTimeOffset ahora)
    {
        if (!clsEstadoSeguimiento.ASIGNABLES.Contains(estadoNuevo))
        {
            throw clsErroresCampos.De("estado", "El estado debe ser validado, rechazado, recuperado o derogado");
        }

        return Construir(hallazgoId, clsAccionSeguimiento.SEGUIMIENTO, estadoAnterior, estadoNuevo, usuarioId, nota, evidenciaSelloId, ahora);
    }

    /// <summary>La revision de una novedad del conductor: quien la hizo, su nota y lo asignado (<paramref name="detalle"/>, un JSON).</summary>
    public static clsHallazgoSeguimiento CrearRevision(Guid hallazgoId, Guid usuarioId, string? nota, string detalle, DateTimeOffset ahora) =>
        Construir(hallazgoId, clsAccionSeguimiento.REVISION, null, null, usuarioId, nota, null, ahora, detalle);

    public static clsHallazgoSeguimiento CrearNoSale(Guid hallazgoId, Guid usuarioId, string motivo, DateTimeOffset ahora) =>
        Construir(hallazgoId, clsAccionSeguimiento.NO_SALE, null, null, usuarioId, motivo, null, ahora);

    private static clsHallazgoSeguimiento Construir(
        Guid hallazgoId,
        string accion,
        string? estadoAnterior,
        string? estadoNuevo,
        Guid usuarioId,
        string? nota,
        Guid? evidenciaSelloId,
        DateTimeOffset ahora,
        string? detalle = null)
    {
        var texto = clsValidador.Opcional(nota);
        if (texto is { Length: > NOTA_MAX })
        {
            throw clsErroresCampos.De("nota", $"La nota admite máximo {NOTA_MAX} caracteres");
        }

        var fila = new clsHallazgoSeguimiento
        {
            HallazgoId = hallazgoId,
            Accion = accion,
            EstadoAnterior = estadoAnterior,
            EstadoNuevo = estadoNuevo,
            UsuarioId = usuarioId,
            Nota = texto,
            EvidenciaSelloId = evidenciaSelloId,
            Detalle = detalle,
        };
        fila.Inicializar(ahora);
        return fila;
    }
}
