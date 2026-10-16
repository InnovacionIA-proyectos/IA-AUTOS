namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// Una firma de la constancia. <c>Imagen</c> es la firma cuando se guardo como archivo (se incrusta en el PDF); <c>Trazo</c> es la firma cuando
/// se guardo como trazo (ruta SVG), que el PDF dibuja. Sin ninguno de los dos, la parte no firmo.
/// </summary>
public sealed record FirmaConstancia(string Persona, DateTimeOffset? Fecha, string? Ip, string? Trazo, byte[]? Imagen);

/// <summary>Una foto de la constancia con su pie (por ejemplo, su sello).</summary>
public sealed record FotoConstancia(byte[] Contenido, string Pie);

/// <summary>Todo lo que lleva el PDF de la constancia de una transferencia. <c>TipoCierre</c> es nulo mientras la transferencia esta abierta.</summary>
public sealed record DatosConstancia(
    string Vin,
    string Marca,
    string? Modelo,
    string Proceso,
    string MomentoNombre,
    string? CodigoMtrack,
    string? Placa,
    string? TipoCierre,
    FirmaConstancia Entrega,
    FirmaConstancia Recibe,
    string? Reservas,
    string? NoFirmaParte,
    string? MotivoNoFirma,
    string? TestigoNombre,
    DateTimeOffset? FirmadaAt,
    IReadOnlyList<FotoConstancia> Fotos,
    DateTimeOffset GeneradaAt);

/// <summary>Genera la constancia de una transferencia en PDF: las firmas con su fecha e IP, las reservas o el motivo de la no firma, el testigo y las fotos.</summary>
public interface IGeneradorPdfConstancia
{
    byte[] Generar(DatosConstancia datos);
}
