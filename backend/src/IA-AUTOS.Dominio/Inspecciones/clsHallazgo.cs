using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>Quien registro el hallazgo: un inspector (o administrador) o el conductor. El conductor nunca asigna el nivel de severidad.</summary>
public static class clsOrigenHallazgo
{
    public const string INSPECTOR = "inspector";
    public const string CONDUCTOR = "conductor";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { INSPECTOR, CONDUCTOR };
}

/// <summary>Los tipos de novedad que el conductor elige de una lista (nunca escribe texto libre).</summary>
public static class clsTipoNovedad
{
    public const string GOLPE = "golpe";
    public const string PIEDRA = "piedra";
    public const string GRANIZO = "granizo";
    public const string INCIDENTE_VIAL = "incidente_vial";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { GOLPE, PIEDRA, GRANIZO, INCIDENTE_VIAL };
}

/// <summary>Estado del seguimiento de un hallazgo. Nace pendiente y lo cambia el coordinador.</summary>
public static class clsEstadoSeguimiento
{
    public const string PENDIENTE = "pendiente";
    public const string VALIDADO = "validado";
    public const string RECHAZADO = "rechazado";
    public const string RECUPERADO = "recuperado";
    public const string DEROGADO = "derogado";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { PENDIENTE, VALIDADO, RECHAZADO, RECUPERADO, DEROGADO };

    /// <summary>Los estados a los que el coordinador puede llevar un hallazgo (el pendiente es el de partida, no se asigna).</summary>
    public static readonly IReadOnlySet<string> ASIGNABLES = new HashSet<string> { VALIDADO, RECHAZADO, RECUPERADO, DEROGADO };
}

/// <summary>
/// Averia atribuida a una zona del vehiculo (lado + numero), con su clase, tamano y nivel de severidad. Un hallazgo
/// preexistente apunta al hallazgo previo del mismo VIN que ya lo tenia. <see cref="ParteVehiculo"/> y <see cref="CoordsDiagrama"/> son del modelo
/// anterior (una parte en texto y un punto sobre una sola imagen): solo quedan en los hallazgos historicos.
/// </summary>
public sealed class clsHallazgo : clsEntidadBase
{
    public const int NOTA_MAX = 500;

    /// <summary>La respuesta (400) cuando el conductor envia un nivel de severidad, en una zona o en un extra.</summary>
    public const string MENSAJE_NIVEL_DEL_CONDUCTOR = "El conductor no asigna el nivel de severidad: lo asigna el inspector";

    private clsHallazgo()
    {
    }

    public Guid InspeccionId { get; private set; }

    /// <summary>Solo en los hallazgos historicos: parte del vehiculo en texto legible (ej. "Guardafango delantero derecho").</summary>
    public string? ParteVehiculo { get; private set; }

    /// <summary>Nulo solo en una novedad del conductor aun sin revisar.</summary>
    public string? ClaseDano { get; private set; }

    public string TamanoDano { get; private set; } = string.Empty;

    /// <summary>Solo en los hallazgos historicos.</summary>
    public clsCoordsDiagrama? CoordsDiagrama { get; private set; }

    /// <summary>La zona del dano; nula solo en una novedad del conductor sin revisar, en un hallazgo historico aun sin ubicar o en el dano de un extra (<see cref="ExtraId"/>).</summary>
    public Guid? ZonaId { get; private set; }

    /// <summary>El extra del vehiculo del que nace este hallazgo (un extra marcado como danado); nulo en los demas. El dano de un extra no tiene zona: <see cref="ParteVehiculo"/> trae el nombre del extra.</summary>
    public Guid? ExtraId { get; private set; }

    public string? TipoNovedad { get; private set; }

    /// <summary>Codigo del nivel de severidad (V1+, V1, V2, V3); nulo mientras un inspector no lo asigne.</summary>
    public string? NivelSeveridad { get; private set; }

    /// <summary><see cref="clsOrigenHallazgo"/>.</summary>
    public string Origen { get; private set; } = clsOrigenHallazgo.INSPECTOR;

    /// <summary>El identificador del envio que el celular le puso a la novedad del conductor: un reenvio con el mismo identificador no crea otra. Nulo en los demas hallazgos y en los envios de la app anterior.</summary>
    public Guid? IdEnvio { get; private set; }

    public string? Nota { get; private set; }

    public bool EsPreexistente { get; private set; }

    /// <summary>El hallazgo previo del mismo VIN que ya tenia este dano; obligatorio si es preexistente.</summary>
    public Guid? HallazgoOrigenId { get; private set; }

    /// <summary>Quien confirmo que el dano ya existia.</summary>
    public Guid? ConfirmadoPor { get; private set; }

    /// <summary><see cref="clsEstadoSeguimiento"/>.</summary>
    public string EstadoSeguimiento { get; private set; } = clsEstadoSeguimiento.PENDIENTE;

    public bool EvidenciaCompleta { get; private set; }

    public Guid? RevisadoPor { get; private set; }

    public DateTimeOffset? RevisadoAt { get; private set; }

    public bool MigracionPendiente { get; private set; }

    /// <summary>Guarda si el hallazgo tiene su evidencia completa (se recalcula al cambiar el seguimiento o al cerrarse la transferencia de su inspeccion).</summary>
    public void CambiarEvidenciaCompleta(bool completa, DateTimeOffset ahora)
    {
        if (EvidenciaCompleta == completa)
        {
            return;
        }

        EvidenciaCompleta = completa;
        Tocar(ahora);
    }

    /// <summary>
    /// Crea un hallazgo con su zona. El inspector (o administrador) siempre asigna el nivel de severidad; el conductor nunca lo asigna (queda nulo hasta que un
    /// inspector lo revise). <paramref name="hallazgoOrigenId"/> lo marca como preexistente y <paramref name="confirmadoPor"/> dice quien lo confirmo.
    /// </summary>
    public static clsHallazgo Crear(
        Guid inspeccionId,
        Guid zonaId,
        string claseDano,
        string tamanoDano,
        string? nivelSeveridad,
        string origen,
        string? nota,
        Guid? hallazgoOrigenId,
        Guid? confirmadoPor,
        DateTimeOffset ahora)
    {
        if (!clsOrigenHallazgo.VALORES.Contains(origen))
        {
            throw clsErroresCampos.De("origen", "El origen debe ser inspector o conductor");
        }

        // La clase y el tamano se validan contra los catalogos (en la aplicacion y con una FK en la base): aqui solo se exige que vengan.
        claseDano = clsValidador.Requerido(claseDano, "clase_dano es requerida");
        tamanoDano = clsValidador.Requerido(tamanoDano, "tamano_dano es requerido");

        var nivel = NivelSegunOrigen(origen, nivelSeveridad);

        var texto = clsValidador.Opcional(nota);
        if (texto is { Length: > NOTA_MAX })
        {
            throw clsErroresCampos.De("nota", $"La nota admite máximo {NOTA_MAX} caracteres");
        }

        var hallazgo = new clsHallazgo
        {
            InspeccionId = inspeccionId,
            ZonaId = zonaId,
            ClaseDano = claseDano,
            TamanoDano = tamanoDano,
            NivelSeveridad = nivel,
            Origen = origen,
            Nota = texto,
            EsPreexistente = hallazgoOrigenId is not null,
            HallazgoOrigenId = hallazgoOrigenId,
            ConfirmadoPor = hallazgoOrigenId is not null ? confirmadoPor : null,
        };
        hallazgo.Inicializar(ahora);
        return hallazgo;
    }

    /// <summary>
    /// La regla unica del nivel de severidad al crear un dano, en una zona o en un extra: el inspector (o administrador) siempre lo asigna y el conductor nunca (400 si lo envia): su dano nace sin nivel
    /// y lo califica un inspector despues.
    /// </summary>
    private static string? NivelSegunOrigen(string origen, string? nivelSeveridad)
    {
        var nivel = clsValidador.Opcional(nivelSeveridad);
        if (origen == clsOrigenHallazgo.INSPECTOR && nivel is null)
        {
            throw clsErroresCampos.De("nivel_severidad", "El nivel de severidad es requerido");
        }

        if (origen == clsOrigenHallazgo.CONDUCTOR && nivel is not null)
        {
            throw clsErroresCampos.De("nivel_severidad", MENSAJE_NIVEL_DEL_CONDUCTOR);
        }

        return nivel;
    }

    /// <summary>
    /// El hallazgo que nace de un extra marcado como danado: su clase, su tamano y su nivel de severidad, sin zona del dibujo. <paramref name="nombreDelExtra"/> es como se muestra el extra («Platon») y queda como la
    /// parte del vehiculo, para que las pantallas no lean la ausencia de zona como un error. El inspector (o administrador) da los tres datos y el origen es inspector; el conductor da la clase y el tamano, el
    /// hallazgo nace de origen conductor y sin nivel, y un inspector lo califica despues. Cuenta como cualquier otro hallazgo.
    /// </summary>
    public static clsHallazgo CrearDeExtra(
        Guid inspeccionId, Guid extraId, string nombreDelExtra, string claseDano, string tamanoDano, string? nivelSeveridad, DateTimeOffset ahora, string origen = clsOrigenHallazgo.INSPECTOR)
    {
        if (!clsOrigenHallazgo.VALORES.Contains(origen))
        {
            throw clsErroresCampos.De("origen", "El origen debe ser inspector o conductor");
        }

        var nombre = clsValidador.Requerido(nombreDelExtra, "El nombre del extra es requerido");
        var hallazgo = new clsHallazgo
        {
            InspeccionId = inspeccionId,
            ExtraId = extraId,
            ParteVehiculo = nombre.Length <= 200 ? nombre : nombre[..200],
            ClaseDano = clsValidador.Requerido(claseDano, "clase_dano es requerida"),
            TamanoDano = clsValidador.Requerido(tamanoDano, "tamano_dano es requerido"),
            NivelSeveridad = NivelSegunOrigen(origen, nivelSeveridad),
            Origen = origen,
        };
        hallazgo.Inicializar(ahora);
        return hallazgo;
    }

    /// <summary>
    /// Se califica un hallazgo sin nivel (de cualquier origen: dano de una zona del conductor, dano de un extra, novedad): se le asigna la clase y el nivel V (que el conductor nunca asigna) y, si hace
    /// falta, se corrige el tamano que eligio el conductor. <paramref name="zonaId"/> ubica el dano en una zona; nulo conserva la que ya tiene y un extra nunca lleva zona. Solo se califica una vez: 409 si el
    /// hallazgo ya tiene nivel y 409 HALLAZGO_YA_REVISADO si ya se califico.
    /// </summary>
    public void Revisar(
        Guid? zonaId,
        string claseDano,
        string nivelSeveridad,
        string? tamanoDano,
        string? nota,
        Guid revisadoPor,
        DateTimeOffset ahora,
        Guid? hallazgoOrigenId = null)
    {
        AsegurarRevisable();
        var texto = clsValidador.Opcional(nota);
        if (texto is { Length: > NOTA_MAX })
        {
            throw clsErroresCampos.De("nota", $"La nota admite máximo {NOTA_MAX} caracteres");
        }

        if (zonaId is not null && ExtraId is not null)
        {
            throw clsErroresCampos.De("cuadrante_numero", "El daño de un extra no lleva zona del dibujo");
        }

        ZonaId = zonaId ?? ZonaId;
        ClaseDano = clsValidador.Requerido(claseDano, "clase_dano es requerida");
        NivelSeveridad = clsValidador.Requerido(nivelSeveridad, "nivel_severidad es requerido");
        if (clsValidador.Opcional(tamanoDano) is { } tamano)
        {
            TamanoDano = tamano;
        }

        Nota = texto;
        RevisadoPor = revisadoPor;
        RevisadoAt = ahora;
        // Quien califica puede confirmar que el dano ya estaba registrado: queda preexistente, unido al hallazgo previo del mismo VIN, y quien lo confirmo es quien califico.
        if (hallazgoOrigenId is { } origen)
        {
            EsPreexistente = true;
            HallazgoOrigenId = origen;
            ConfirmadoPor = revisadoPor;
        }

        Tocar(ahora);
    }

    /// <summary>Solo se califica un hallazgo sin nivel y que nadie ha calificado: 409 HALLAZGO_YA_REVISADO si ya se califico y 409 si ya tiene nivel (el del inspector nace con el).</summary>
    public void AsegurarRevisable()
    {
        if (RevisadoAt is not null)
        {
            throw new clsHallazgoYaRevisadoException();
        }

        if (NivelSeveridad is not null)
        {
            throw new clsConflictoException("El hallazgo ya tiene nivel");
        }
    }

    /// <summary>
    /// Crea la novedad que registra el conductor en ruta: un tipo de la lista y un tamano, sin zona, clase, nivel ni texto libre. Esos datos los completa el inspector al
    /// revisarla; mientras tanto la novedad no tiene nivel V.
    /// </summary>
    public static clsHallazgo CrearNovedad(Guid inspeccionId, string tipoNovedad, string tamanoDano, DateTimeOffset ahora, Guid? idEnvio = null)
    {
        clsValidador.EnCatalogo(tipoNovedad, clsTipoNovedad.VALORES, "tipo_novedad");
        var hallazgo = new clsHallazgo
        {
            InspeccionId = inspeccionId,
            TipoNovedad = tipoNovedad,
            TamanoDano = clsValidador.Requerido(tamanoDano, "tamano_dano es requerido"),
            Origen = clsOrigenHallazgo.CONDUCTOR,
            IdEnvio = idEnvio,
        };
        hallazgo.Inicializar(ahora);
        return hallazgo;
    }
}
