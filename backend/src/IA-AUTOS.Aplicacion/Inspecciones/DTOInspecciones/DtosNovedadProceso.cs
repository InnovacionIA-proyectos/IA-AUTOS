namespace IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

/// <summary>
/// El motivo por el que el conductor hizo la salida del cargue porque el inspector previsto no estaba: el codigo y el nombre del catalogo <c>motivos-sin-inspector</c> y el texto que escribio
/// (obligatorio solo cuando el motivo lo requiere).
/// </summary>
public sealed record MotivoSinInspectorDto(string Codigo, string Nombre, string? Texto);

/// <summary>
/// Algo que paso en el proceso y no es un dano: <c>Tipo</c> es <c>cargue_sin_inspector</c> o <c>recibo_sin_entrega</c>. <c>MotivoCodigo</c> y <c>Motivo</c> (el nombre) salen del catalogo
/// <c>motivos-sin-inspector</c> y <c>Texto</c> es lo que escribio quien la registro; <c>Fecha</c> es el momento en que ocurrio.
/// </summary>
public sealed record NovedadProcesoDto(string Tipo, string? MotivoCodigo, string? Motivo, string? Texto, string? UsuarioNombre, DateTimeOffset Fecha);

/// <summary>El motivo de una inspeccion (si la hizo el conductor sin el inspector previsto) y sus novedades de proceso, de la mas antigua a la mas reciente.</summary>
public sealed record NovedadesDeInspeccionDto(MotivoSinInspectorDto? Motivo, IReadOnlyList<NovedadProcesoDto> Novedades)
{
    /// <summary>Una inspeccion sin motivo ni novedades de proceso (la gran mayoria).</summary>
    public static readonly NovedadesDeInspeccionDto VACIAS = new(null, []);
}

/// <summary>Lectura del motivo y de las novedades de proceso de las inspecciones, para armar sus DTO.</summary>
public interface IConsultasNovedadesProceso
{
    /// <summary>Por cada inspeccion que tiene motivo o novedades, lo suyo; las que no tienen nada no aparecen.</summary>
    Task<IReadOnlyDictionary<Guid, NovedadesDeInspeccionDto>> DeInspeccionesAsync(IReadOnlyCollection<Guid> inspeccionIds, CancellationToken ct = default);
}

public static class clsConsultasNovedadesProcesoExtensiones
{
    /// <summary>Lo de una sola inspeccion; sin motivo ni novedades, <see cref="NovedadesDeInspeccionDto.VACIAS"/>.</summary>
    public static async Task<NovedadesDeInspeccionDto> DeInspeccionAsync(this IConsultasNovedadesProceso consultas, Guid inspeccionId, CancellationToken ct = default) =>
        (await consultas.DeInspeccionesAsync([inspeccionId], ct)).GetValueOrDefault(inspeccionId) ?? NovedadesDeInspeccionDto.VACIAS;
}
