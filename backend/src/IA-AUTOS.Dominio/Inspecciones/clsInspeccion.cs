using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>
/// Acto de revision de una etapa: por etapa hay una inspeccion de salida y una de llegada. Al cerrarse es evidencia inmutable; un error se
/// corrige creando una version nueva que referencia a la anterior (la anterior no cambia).
/// </summary>
public sealed class clsInspeccion : clsEntidadBase
{
    private const int NOTAS_MAX = 1000;
    private const int MOTIVO_TEXTO_MAX = 300;

    private clsInspeccion()
    {
    }

    public Guid EtapaId { get; private set; }

    /// <summary>Viaje de la etapa: de el salen la placa, el trailer y el conductor.</summary>
    public Guid ViajeId { get; private set; }

    public Guid UsuarioId { get; private set; }

    /// <summary><see cref="clsMomento"/>: salida o llegada.</summary>
    public string Momento { get; private set; } = string.Empty;

    public DateTimeOffset Fecha { get; private set; }

    public double? Lat { get; private set; }

    public double? Lng { get; private set; }

    public string Estado { get; private set; } = clsEstadoInspeccion.BORRADOR;

    /// <summary>Datos del transporte capturados en la app antes de que existieran los viajes. Las inspecciones nuevas no los llenan: los toman del viaje.</summary>
    public string? ConductorNombre { get; private set; }

    public string? Placa { get; private set; }

    public string? Trailer { get; private set; }

    public string? Notas { get; private set; }

    /// <summary>Cuando se empezo la inspeccion (hora del servidor).</summary>
    public DateTimeOffset IniciadaAt { get; private set; }

    /// <summary>Cuando se cerro (hora del servidor). Nulo mientras esta en borrador.</summary>
    public DateTimeOffset? CerradaAt { get; private set; }

    /// <summary>1 en la original; 2, 3... en las correcciones.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>La version que esta corrige. Nulo en la original.</summary>
    public Guid? CorrigeInspeccionId { get; private set; }

    /// <summary>
    /// El motivo (codigo del catalogo <c>motivos-sin-inspector</c>) por el que el conductor hizo la salida del cargue con personal de apoyo porque el inspector previsto no estaba. Nulo en
    /// cualquier otra inspeccion.
    /// </summary>
    public string? MotivoSinInspector { get; private set; }

    /// <summary>El texto del motivo, obligatorio solo cuando el motivo lo requiere.</summary>
    public string? MotivoSinInspectorTexto { get; private set; }

    /// <summary>El resultado de confirmar «en sitio» la evidencia sellada (<see cref="clsResultadoConfirmacionSitio"/>). Nulo mientras no se determina y en las inspecciones que no son de Cargue ni de Descargue.</summary>
    public string? ConfirmacionSitio { get; private set; }

    /// <summary>La distancia, en metros, de la foto que determino la confirmacion al centro del lugar; nula cuando no se pudo calcular.</summary>
    public double? ConfirmacionDistanciaM { get; private set; }

    /// <summary>La foto que determino la confirmacion.</summary>
    public Guid? ConfirmacionEvidenciaId { get; private set; }

    public bool EstaCerrada => Estado == clsEstadoInspeccion.CERRADA;

    public static clsInspeccion Crear(
        Guid etapaId,
        Guid viajeId,
        Guid usuarioId,
        string momento,
        DateTimeOffset fecha,
        double? lat,
        double? lng,
        string? estado,
        string? notas,
        DateTimeOffset ahora,
        string? motivoSinInspector = null,
        string? motivoSinInspectorTexto = null) =>
        Nueva(etapaId, viajeId, usuarioId, momento, fecha, lat, lng, estado, notas, version: 1, corrigeInspeccionId: null, ahora, motivoSinInspector, motivoSinInspectorTexto);

    /// <summary>
    /// Crea la version siguiente de una inspeccion CERRADA para corregirla: misma etapa, viaje y momento, en borrador y vacia (se vuelven a
    /// tomar las fotos). Esta inspeccion no cambia. Solo se corrige la ultima version: quien llame debe comprobar que nadie la corrigio ya.
    /// </summary>
    public clsInspeccion Corregir(Guid usuarioId, DateTimeOffset fecha, double? lat, double? lng, string? notas, DateTimeOffset ahora)
    {
        if (!EstaCerrada)
        {
            throw new clsConflictoException("Solo se puede corregir una inspección cerrada: la que está en borrador se sigue editando");
        }

        // La correccion conserva el motivo: sigue siendo una salida hecha por el conductor porque el inspector no estaba.
        return Nueva(EtapaId, ViajeId, usuarioId, Momento, fecha, lat, lng, null, notas, Version + 1, Id, ahora, MotivoSinInspector, MotivoSinInspectorTexto);
    }

    private static clsInspeccion Nueva(
        Guid etapaId,
        Guid viajeId,
        Guid usuarioId,
        string momento,
        DateTimeOffset fecha,
        double? lat,
        double? lng,
        string? estado,
        string? notas,
        int version,
        Guid? corrigeInspeccionId,
        DateTimeOffset ahora,
        string? motivoSinInspector,
        string? motivoSinInspectorTexto)
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

        var motivo = clsValidador.Opcional(motivoSinInspector);
        var motivoTexto = clsValidador.Opcional(motivoSinInspectorTexto);
        if (motivo is not null && momento != clsMomento.SALIDA)
        {
            throw clsErroresCampos.De("motivo_sin_inspector", "Campo no admitido");
        }

        if (motivoTexto is not null && motivo is null)
        {
            throw clsErroresCampos.De("motivo_sin_inspector_texto", "El texto del motivo exige un motivo");
        }

        if (motivoTexto is { Length: > MOTIVO_TEXTO_MAX })
        {
            throw clsErroresCampos.De("motivo_sin_inspector_texto", $"El texto del motivo admite máximo {MOTIVO_TEXTO_MAX} caracteres");
        }

        var inspeccion = new clsInspeccion
        {
            EtapaId = etapaId,
            ViajeId = viajeId,
            UsuarioId = usuarioId,
            Momento = momento,
            Fecha = fecha,
            Lat = lat,
            Lng = lng,
            Estado = estadoFinal,
            Notas = notasLimpias,
            IniciadaAt = ahora,
            CerradaAt = estadoFinal == clsEstadoInspeccion.CERRADA ? ahora : null,
            Version = version,
            CorrigeInspeccionId = corrigeInspeccionId,
            MotivoSinInspector = motivo,
            MotivoSinInspectorTexto = motivoTexto,
        };
        inspeccion.Inicializar(ahora);
        return inspeccion;
    }

    /// <summary>Una inspeccion cerrada es inmutable: no admite hallazgos ni evidencias nuevos.</summary>
    /// <summary>
    /// Guarda el resultado de la confirmacion «en sitio» y la foto (y su distancia) que lo determino. Se escribe mientras la inspeccion esta en borrador: una cerrada es inmutable (409).
    /// </summary>
    public void RegistrarConfirmacionSitio(string resultado, double? distanciaM, Guid evidenciaId, DateTimeOffset ahora)
    {
        AsegurarEditable();
        if (!clsResultadoConfirmacionSitio.VALORES.Contains(resultado))
        {
            throw new ArgumentException($"Resultado de confirmación desconocido: {resultado}", nameof(resultado));
        }

        ConfirmacionSitio = resultado;
        ConfirmacionDistanciaM = distanciaM;
        ConfirmacionEvidenciaId = evidenciaId;
        Tocar(ahora);
    }

    public void AsegurarEditable()
    {
        if (EstaCerrada)
        {
            throw new clsConflictoException("La inspección está cerrada y es inmutable; no admite cambios");
        }
    }

    /// <summary>
    /// Cierra la inspeccion como evidencia inmutable. Exige: las fotos de todas las vistas obligatorias (<paramref name="vistasFaltantes"/>,
    /// que la aplicacion calcula segun la etapa y el tipo de vehiculo), una foto propia por cada hallazgo y que toda foto tenga ubicacion.
    /// Las fotos adicionales (piezas en el baul y cojineria) nunca bloquean el cierre. Donde no hay vistas obligatorias (Transporte) el registro
    /// del conductor es su propia hora y ubicacion: con <paramref name="exigeUbicacionPropia"/> la inspeccion debe traer su GPS.
    /// </summary>
    public void Cerrar(clsResumenCierre resumen, IReadOnlyList<string> vistasFaltantes, bool exigeUbicacionPropia, DateTimeOffset ahora, IReadOnlyList<string>? otrosFaltantes = null)
    {
        if (EstaCerrada)
        {
            throw new clsConflictoException("La inspección ya está cerrada");
        }

        var errores = new List<string>();
        errores.AddRange(vistasFaltantes.Select(vista => $"Falta la foto de la vista «{vista}»"));
        if (otrosFaltantes is not null)
        {
            errores.AddRange(otrosFaltantes);
        }

        if (exigeUbicacionPropia && (Lat is null || Lng is null))
        {
            errores.Add("El registro no tiene ubicación (GPS): regístrelo con la ubicación del dispositivo");
        }

        if (resumen.HallazgosSinEvidencia > 0)
        {
            errores.Add($"{resumen.HallazgosSinEvidencia} hallazgo(s) sin foto propia: cada hallazgo exige su foto");
        }

        if (resumen.EvidenciasSinUbicacion > 0)
        {
            errores.Add($"{resumen.EvidenciasSinUbicacion} foto(s) sin ubicación: repítalas con GPS antes de cerrar");
        }

        if (errores.Count > 0)
        {
            throw new clsValidacionException("La inspección no cumple los requisitos para cerrarse", errores);
        }

        Estado = clsEstadoInspeccion.CERRADA;
        CerradaAt = ahora;
        Tocar(ahora);
    }
}
