using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Custodia;

/// <summary>
/// Evento de entrega y recibo de la unidad, firmado de forma manuscrita (la firma se guarda como ruta de imagen).
/// Admite firma parcial: quien entrega y quien recibe pueden firmar por separado, incluso desde dispositivos distintos.
/// </summary>
public sealed class clsTransferenciaCustodia : clsEntidadBase
{
    private clsTransferenciaCustodia()
    {
    }

    public Guid InspeccionId { get; private set; }

    public Guid EntregaUsuarioId { get; private set; }

    public Guid RecibeUsuarioId { get; private set; }

    public string? FirmaEntrega { get; private set; }

    public string? FirmaRecibe { get; private set; }

    /// <summary>Se sella solo cuando ambas firmas estan presentes.</summary>
    public DateTimeOffset? FirmadaAt { get; private set; }

    public bool EstaCompleta => !string.IsNullOrEmpty(FirmaEntrega) && !string.IsNullOrEmpty(FirmaRecibe);

    public static clsTransferenciaCustodia Crear(
        Guid inspeccionId,
        Guid entregaUsuarioId,
        Guid recibeUsuarioId,
        string? firmaEntrega,
        string? firmaRecibe,
        DateTimeOffset ahora)
    {
        AsegurarUsuariosDistintos(entregaUsuarioId, recibeUsuarioId);
        var transferencia = new clsTransferenciaCustodia
        {
            InspeccionId = inspeccionId,
            EntregaUsuarioId = entregaUsuarioId,
            RecibeUsuarioId = recibeUsuarioId,
            FirmaEntrega = NormalizarFirma(firmaEntrega),
            FirmaRecibe = NormalizarFirma(firmaRecibe),
        };
        transferencia.Inicializar(ahora);
        transferencia.FirmadaAt = transferencia.EstaCompleta ? ahora : null;
        return transferencia;
    }

    /// <summary>
    /// Acumula firmas sobre la transferencia existente. Mientras este incompleta se puede completar o corregir;
    /// una vez completa es inmutable: solo se acepta repetir exactamente los mismos datos (reintento idempotente).
    /// </summary>
    public void AplicarFirma(
        Guid entregaUsuarioId,
        Guid recibeUsuarioId,
        string? firmaEntrega,
        string? firmaRecibe,
        DateTimeOffset ahora)
    {
        AsegurarUsuariosDistintos(entregaUsuarioId, recibeUsuarioId);
        var nuevaEntrega = NormalizarFirma(firmaEntrega) ?? FirmaEntrega;
        var nuevaRecibe = NormalizarFirma(firmaRecibe) ?? FirmaRecibe;

        if (EstaCompleta)
        {
            var identica = entregaUsuarioId == EntregaUsuarioId
                && recibeUsuarioId == RecibeUsuarioId
                && nuevaEntrega == FirmaEntrega
                && nuevaRecibe == FirmaRecibe;
            if (!identica)
            {
                throw new clsConflictoException("La transferencia ya está firmada por ambas partes y es inmutable");
            }

            return;
        }

        EntregaUsuarioId = entregaUsuarioId;
        RecibeUsuarioId = recibeUsuarioId;
        FirmaEntrega = nuevaEntrega;
        FirmaRecibe = nuevaRecibe;
        FirmadaAt = EstaCompleta ? FirmadaAt ?? ahora : null;
        Tocar(ahora);
    }

    private static string? NormalizarFirma(string? firma) => string.IsNullOrEmpty(firma) ? null : firma;

    private static void AsegurarUsuariosDistintos(Guid entregaUsuarioId, Guid recibeUsuarioId)
    {
        if (entregaUsuarioId == recibeUsuarioId)
        {
            throw new clsValidacionException("Quien entrega y quien recibe deben ser usuarios distintos");
        }
    }
}
