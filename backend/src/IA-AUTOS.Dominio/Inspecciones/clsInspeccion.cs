using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>
/// Acto de revision de una etapa (un informe por etapa). Al cerrarse es evidencia inmutable.
/// </summary>
public sealed class clsInspeccion : clsEntidadBase
{
    private const int NOTAS_MAX = 1000;

    private clsInspeccion()
    {
    }

    public Guid EtapaId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public string Momento { get; private set; } = string.Empty;

    public DateTimeOffset Fecha { get; private set; }

    public double? Lat { get; private set; }

    public double? Lng { get; private set; }

    public string Estado { get; private set; } = clsEstadoInspeccion.BORRADOR;

    /// <summary>Datos del transporte capturados al iniciar la inspeccion en la app.</summary>
    public string? ConductorNombre { get; private set; }

    public string? Placa { get; private set; }

    public string? Trailer { get; private set; }

    public string? Notas { get; private set; }

    public bool EstaCerrada => Estado == clsEstadoInspeccion.CERRADA;

    public static clsInspeccion Crear(
        Guid etapaId,
        Guid usuarioId,
        string momento,
        DateTimeOffset fecha,
        double? lat,
        double? lng,
        string? estado,
        string? conductorNombre,
        string? placa,
        string? trailer,
        string? notas,
        DateTimeOffset ahora)
    {
        clsValidador.EnCatalogo(momento, clsMomento.VALORES, "momento");
        var estadoFinal = estado ?? clsEstadoInspeccion.BORRADOR;
        clsValidador.EnCatalogo(estadoFinal, clsEstadoInspeccion.VALORES, "estado");
        clsValidador.Latitud(lat);
        clsValidador.Longitud(lng);
        var notasLimpias = clsValidador.Opcional(notas);
        if (notasLimpias is { Length: > NOTAS_MAX })
        {
            throw new clsValidacionException($"Las notas admiten máximo {NOTAS_MAX} caracteres");
        }

        var inspeccion = new clsInspeccion
        {
            EtapaId = etapaId,
            UsuarioId = usuarioId,
            Momento = momento,
            Fecha = fecha,
            Lat = lat,
            Lng = lng,
            Estado = estadoFinal,
            ConductorNombre = clsValidador.Opcional(conductorNombre),
            Placa = clsValidador.Opcional(placa),
            Trailer = clsValidador.Opcional(trailer),
            Notas = notasLimpias,
        };
        inspeccion.Inicializar(ahora);
        return inspeccion;
    }

    /// <summary>Una inspeccion cerrada es inmutable: no admite hallazgos ni evidencias nuevos.</summary>
    public void AsegurarEditable()
    {
        if (EstaCerrada)
        {
            throw new clsConflictoException("La inspección está cerrada y es inmutable; no admite cambios");
        }
    }

    /// <summary>
    /// Cierra la inspeccion como evidencia inmutable. Las fotos solo son obligatorias cuando hay una novedad
    /// (al menos una por inspeccion con hallazgos) y toda foto presente debe estar geolocalizada.
    /// </summary>
    public void Cerrar(clsResumenCierre resumen, DateTimeOffset ahora)
    {
        if (EstaCerrada)
        {
            throw new clsConflictoException("La inspección ya está cerrada");
        }

        var errores = new List<string>();
        if (resumen.TotalHallazgos > 0 && resumen.TotalEvidencias < 1)
        {
            errores.Add("Hay una novedad registrada: adjunta al menos una foto de la novedad");
        }

        if (resumen.EvidenciasSinGeo > 0)
        {
            errores.Add($"{resumen.EvidenciasSinGeo} foto(s) sin geolocalización del sello");
        }

        if (errores.Count > 0)
        {
            throw new clsValidacionException("La inspección no cumple los requisitos para cerrarse", errores);
        }

        Estado = clsEstadoInspeccion.CERRADA;
        Tocar(ahora);
    }
}
