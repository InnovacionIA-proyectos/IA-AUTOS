using System.Globalization;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Dominio.Custodia;

namespace IAAutos.Infraestructura.Reportes;

/// <summary>
/// Constancia de la transferencia de custodia en PDF, sin dependencias de terceros (ver <see cref="clsDocumentoPdf"/>): las firmas (como imagen incrustada o como trazo dibujado con lineas),
/// las reservas o el motivo de la no firma, el testigo y las fotos.
/// </summary>
internal sealed class clsGeneradorPdfConstancia : IGeneradorPdfConstancia
{
    private const double FIRMA_ANCHO = 220;
    private const double FIRMA_ALTO = 70;
    private const double FOTO_ANCHO = 300;
    private const double FOTO_ALTO = 220;

    public byte[] Generar(DatosConstancia datos)
    {
        var doc = new clsDocumentoPdf();
        Componer(doc, datos);
        return doc.Generar();
    }

    private static void Componer(clsDocumentoPdf doc, DatosConstancia d)
    {
        doc.Escribir("Traza Autos 360° — Botero Soto", 11);
        doc.Escribir("Constancia de transferencia de custodia", 16, true);
        doc.Espaciar(6);
        doc.Escribir($"VIN: {d.Vin}", 12, true);
        doc.Escribir($"Marca: {d.Marca}   Modelo: {d.Modelo ?? "—"}", 11);
        doc.Escribir($"Etapa: {d.Proceso} — {d.MomentoNombre}", 11);
        doc.Escribir($"Viaje (MTrack): {d.CodigoMtrack ?? "—"}   Placa: {d.Placa ?? "—"}", 11);
        doc.Espaciar(6);
        doc.Escribir($"Cierre de la transferencia: {NombreCierre(d.TipoCierre)}", 13, true);
        if (d.FirmadaAt is { } firmada)
        {
            doc.Escribir($"Firmada por ambas partes el {Fecha(firmada)}", 11);
        }

        doc.Espaciar(8);
        AgregarFirma(doc, "Entrega", d.Entrega, d.NoFirmaParte == clsParteFirmante.ENTREGA);
        doc.Espaciar(6);
        AgregarFirma(doc, "Recibe", d.Recibe, d.NoFirmaParte == clsParteFirmante.RECIBE);

        if (d.Reservas is not null)
        {
            doc.Espaciar(8);
            doc.Escribir("Reservas de quien recibe", 13, true);
            doc.Escribir(d.Reservas, 11);
        }

        if (d.TipoCierre == clsTipoCierre.NO_FIRMA)
        {
            doc.Espaciar(8);
            doc.Escribir("Constancia de no firma", 13, true);
            doc.Escribir($"Parte que no firmó: {(d.NoFirmaParte == clsParteFirmante.ENTREGA ? "quien entrega" : "quien recibe")}", 11);
            doc.Escribir($"Motivo: {d.MotivoNoFirma}", 11);
            doc.Escribir($"Testigo: {d.TestigoNombre ?? "—"}", 11);
        }

        foreach (var foto in d.Fotos)
        {
            doc.Espaciar(6);
            doc.Imagen(foto.Contenido, FOTO_ANCHO, FOTO_ALTO);
            doc.Escribir(foto.Pie, 9);
        }

        doc.Espaciar(10);
        doc.Escribir($"Documento generado el {Fecha(d.GeneradaAt)}", 9);
    }

    private static void AgregarFirma(clsDocumentoPdf doc, string rol, FirmaConstancia firma, bool seNego)
    {
        doc.Escribir($"{rol}: {firma.Persona}", 12, true);
        if (firma.Imagen is null && firma.Trazo is null)
        {
            doc.Escribir(seNego ? "Se negó a firmar (ver la constancia de no firma)" : "No ha firmado", 11);
            return;
        }

        var ip = firma.Ip is null ? string.Empty : $" · IP {firma.Ip}";
        doc.Escribir($"Firmó el {(firma.Fecha is { } f ? Fecha(f) : "—")}{ip}", 11);
        if (firma.Imagen is not null)
        {
            doc.Imagen(firma.Imagen, FIRMA_ANCHO, FIRMA_ALTO);
        }
        else if (!doc.Firma(firma.Trazo!))
        {
            doc.Escribir("(Firma manuscrita registrada; el trazo no se pudo dibujar)", 10);
        }
    }

    private static string NombreCierre(string? tipo) => tipo switch
    {
        clsTipoCierre.FIRMADA => "firmada",
        clsTipoCierre.CON_RESERVAS => "firmada con reservas",
        clsTipoCierre.NO_FIRMA => "no firma",
        _ => "abierta (sin tipo de cierre)",
    };

    private static string Fecha(DateTimeOffset fecha) => fecha.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
