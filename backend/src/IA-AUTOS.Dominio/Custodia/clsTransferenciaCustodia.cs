using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Custodia;

/// <summary>Como se cerro una transferencia de custodia. Mientras no tiene tipo de cierre esta abierta y bloquea la etapa siguiente.</summary>
public static class clsTipoCierre
{
    /// <summary>Ambas partes firmaron.</summary>
    public const string FIRMADA = "firmada";

    /// <summary>Ambas firmaron y quien recibe dejo sus observaciones.</summary>
    public const string CON_RESERVAS = "con_reservas";

    /// <summary>Una parte se nego a firmar: queda la constancia (motivo, testigo y foto).</summary>
    public const string NO_FIRMA = "no_firma";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { FIRMADA, CON_RESERVAS, NO_FIRMA };
}

/// <summary>Las dos partes de una transferencia.</summary>
public static class clsParteFirmante
{
    public const string ENTREGA = "entrega";
    public const string RECIBE = "recibe";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { ENTREGA, RECIBE };
}

/// <summary>
/// Evento de entrega y recibo de la unidad, firmado de forma manuscrita (la firma se guarda como ruta de imagen o trazo). Cada firma la envia
/// su titular (quien entrega o quien recibe) y guarda su fecha e IP; las firmas pueden llegar por separado, incluso desde dispositivos
/// distintos. La transferencia se cierra con un <see cref="clsTipoCierre"/> y, cerrada, es inmutable.
/// </summary>
public sealed class clsTransferenciaCustodia : clsEntidadBase
{
    public const int RESERVAS_MAX = 1000;
    public const int MOTIVO_MAX = 1000;
    public const int IP_MAX = 45;

    private clsTransferenciaCustodia()
    {
    }

    public Guid InspeccionId { get; private set; }

    public Guid EntregaUsuarioId { get; private set; }

    public Guid RecibeUsuarioId { get; private set; }

    public string? FirmaEntrega { get; private set; }

    public string? FirmaRecibe { get; private set; }

    public DateTimeOffset? FirmaEntregaAt { get; private set; }

    public string? FirmaEntregaIp { get; private set; }

    public DateTimeOffset? FirmaRecibeAt { get; private set; }

    public string? FirmaRecibeIp { get; private set; }

    /// <summary>Se sella solo cuando ambas firmas estan presentes.</summary>
    public DateTimeOffset? FirmadaAt { get; private set; }

    /// <summary><see cref="clsTipoCierre"/>. Nulo mientras la transferencia esta abierta.</summary>
    public string? TipoCierre { get; private set; }

    /// <summary>Observaciones de quien recibe cuando firma «con reservas».</summary>
    public string? ReservasTexto { get; private set; }

    /// <summary>La parte que se nego a firmar (<see cref="clsParteFirmante"/>) en una constancia de no firma.</summary>
    public string? NoFirmaParte { get; private set; }

    public string? MotivoNoFirma { get; private set; }

    /// <summary>Tercera persona, con usuario, que presencio la negativa.</summary>
    public Guid? TestigoUsuarioId { get; private set; }

    /// <summary>La foto de la constancia (una evidencia marcada como constancia).</summary>
    public Guid? EvidenciaConstanciaId { get; private set; }

    public bool EstaCompleta => !string.IsNullOrEmpty(FirmaEntrega) && !string.IsNullOrEmpty(FirmaRecibe);

    /// <summary>True si ya tiene tipo de cierre: no admite mas cambios.</summary>
    public bool EstaCerrada => TipoCierre is not null;

    /// <summary>Crea la transferencia vacia entre dos usuarios distintos; las firmas se agregan despues, cada una por su titular.</summary>
    public static clsTransferenciaCustodia Crear(Guid inspeccionId, Guid entregaUsuarioId, Guid recibeUsuarioId, DateTimeOffset ahora)
    {
        if (entregaUsuarioId == recibeUsuarioId)
        {
            throw clsErroresCampos.De("recibe_usuario_id", "Quien entrega y quien recibe deben ser usuarios distintos");
        }

        var transferencia = new clsTransferenciaCustodia
        {
            InspeccionId = inspeccionId,
            EntregaUsuarioId = entregaUsuarioId,
            RecibeUsuarioId = recibeUsuarioId,
        };
        transferencia.Inicializar(ahora);
        return transferencia;
    }

    /// <summary><see cref="clsParteFirmante"/> del usuario en esta transferencia, o nulo si no es ninguna de las dos partes.</summary>
    public string? ParteDe(Guid usuarioId) =>
        usuarioId == EntregaUsuarioId ? clsParteFirmante.ENTREGA : usuarioId == RecibeUsuarioId ? clsParteFirmante.RECIBE : null;

    /// <summary>
    /// El usuario firma su propia parte (entrega o recibe segun quien sea); nadie firma por otro (403). Mientras este abierta puede
    /// repetir su firma. Si quien recibe firma asi, se descartan unas reservas anteriores. Cuando ya estan ambas firmas se cierra como
    /// «firmada» (o «con reservas» si quien recibe dejo observaciones). Una transferencia cerrada solo acepta repetir exactamente la misma
    /// firma (reintento idempotente).
    /// </summary>
    public void Firmar(Guid usuarioId, string firma, string? ip, DateTimeOffset ahora)
    {
        var parte = ParteObligatoria(usuarioId, "firmar");
        var limpia = FirmaValida(firma);
        if (EstaCerrada)
        {
            var misma = TipoCierre == clsTipoCierre.FIRMADA
                && limpia == (parte == clsParteFirmante.ENTREGA ? FirmaEntrega : FirmaRecibe);
            if (!misma)
            {
                throw ConflictoPorCierre();
            }

            return;
        }

        PonerFirma(parte, limpia, ip, ahora);
        if (parte == clsParteFirmante.RECIBE)
        {
            ReservasTexto = null;
        }

        CerrarSiEstanAmbas(ahora);
        Tocar(ahora);
    }

    /// <summary>
    /// Quien recibe firma «con reservas»: deja sus observaciones (obligatorias) junto con su firma. Solo quien recibe puede hacerlo (403 para
    /// cualquier otro). Se cierra como «con reservas» cuando tambien esta la firma de quien entrega.
    /// </summary>
    public void FirmarConReservas(Guid usuarioId, string firma, string? reservas, string? ip, DateTimeOffset ahora)
    {
        if (ParteObligatoria(usuarioId, "recibir con reservas") != clsParteFirmante.RECIBE)
        {
            throw new clsProhibidoException("Solo quien recibe puede firmar con reservas");
        }

        var texto = clsValidador.Opcional(reservas)
            ?? throw clsErroresCampos.De("reservas", "Escriba las observaciones: no se puede recibir con reservas sin ellas");
        if (texto.Length > RESERVAS_MAX)
        {
            throw clsErroresCampos.De("reservas", $"Las observaciones admiten máximo {RESERVAS_MAX} caracteres");
        }

        var limpia = FirmaValida(firma);
        if (EstaCerrada)
        {
            throw ConflictoPorCierre();
        }

        PonerFirma(clsParteFirmante.RECIBE, limpia, ip, ahora);
        ReservasTexto = texto;
        CerrarSiEstanAmbas(ahora);
        Tocar(ahora);
    }

    /// <summary>
    /// Deja constancia de que una parte se nego a firmar. La registra la otra parte (nadie declara la negativa de si mismo), y exige el motivo,
    /// un testigo con usuario (que no sea ninguna de las dos partes) y la foto de la constancia. Cierra la transferencia como «no firma».
    /// La parte que se nego no debe haber firmado ya.
    /// </summary>
    public void RegistrarNoFirma(Guid usuarioId, string? parte, string? motivo, Guid? testigoUsuarioId, Guid? evidenciaConstanciaId, DateTimeOffset ahora)
    {
        var quienRegistra = ParteObligatoria(usuarioId, "registrar la no firma");
        if (parte is null || !clsParteFirmante.VALORES.Contains(parte))
        {
            throw clsErroresCampos.De("parte", $"Indique quien no firmó: {clsParteFirmante.ENTREGA} o {clsParteFirmante.RECIBE}");
        }

        if (parte == quienRegistra)
        {
            throw new clsProhibidoException("La no firma la registra la otra parte: nadie declara su propia negativa");
        }

        var texto = clsValidador.Opcional(motivo) ?? throw clsErroresCampos.De("motivo", "El motivo de la no firma es obligatorio");
        if (texto.Length > MOTIVO_MAX)
        {
            throw clsErroresCampos.De("motivo", $"El motivo admite máximo {MOTIVO_MAX} caracteres");
        }

        if (testigoUsuarioId is not { } testigo || testigo == Guid.Empty)
        {
            throw clsErroresCampos.De("testigo_usuario_id", "La constancia exige un testigo con usuario");
        }

        if (testigo == EntregaUsuarioId || testigo == RecibeUsuarioId)
        {
            throw clsErroresCampos.De("testigo_usuario_id", "El testigo debe ser una tercera persona: ni quien entrega ni quien recibe");
        }

        if (evidenciaConstanciaId is not { } foto)
        {
            throw clsErroresCampos.De("evidencia", "La constancia exige una foto");
        }

        if (EstaCerrada)
        {
            throw ConflictoPorCierre();
        }

        var yaFirmo = parte == clsParteFirmante.ENTREGA ? FirmaEntrega : FirmaRecibe;
        if (!string.IsNullOrEmpty(yaFirmo))
        {
            throw new clsConflictoException($"Quien {(parte == clsParteFirmante.ENTREGA ? "entrega" : "recibe")} ya firmó: no se puede registrar su no firma");
        }

        NoFirmaParte = parte;
        MotivoNoFirma = texto;
        TestigoUsuarioId = testigo;
        EvidenciaConstanciaId = foto;
        TipoCierre = clsTipoCierre.NO_FIRMA;
        Tocar(ahora);
    }

    private void PonerFirma(string parte, string firma, string? ip, DateTimeOffset ahora)
    {
        var direccion = clsValidador.Opcional(ip) is { } d ? d[..Math.Min(d.Length, IP_MAX)] : null;
        if (parte == clsParteFirmante.ENTREGA)
        {
            FirmaEntrega = firma;
            FirmaEntregaAt = ahora;
            FirmaEntregaIp = direccion;
        }
        else
        {
            FirmaRecibe = firma;
            FirmaRecibeAt = ahora;
            FirmaRecibeIp = direccion;
        }
    }

    private void CerrarSiEstanAmbas(DateTimeOffset ahora)
    {
        if (!EstaCompleta)
        {
            return;
        }

        FirmadaAt = ahora;
        TipoCierre = ReservasTexto is null ? clsTipoCierre.FIRMADA : clsTipoCierre.CON_RESERVAS;
    }

    private string ParteObligatoria(Guid usuarioId, string accion) =>
        ParteDe(usuarioId) ?? throw new clsProhibidoException($"Solo quien entrega o quien recibe puede {accion}: nadie actúa por otro");

    private static string FirmaValida(string? firma) =>
        string.IsNullOrEmpty(firma) ? throw clsErroresCampos.De("firma", "La firma es obligatoria") : firma;

    private clsConflictoException ConflictoPorCierre() => TipoCierre switch
    {
        clsTipoCierre.FIRMADA => new("La transferencia ya está firmada por ambas partes y es inmutable"),
        clsTipoCierre.CON_RESERVAS => new("La transferencia ya está cerrada con reservas y es inmutable"),
        _ => new("La transferencia ya tiene constancia de no firma y es inmutable"),
    };
}
