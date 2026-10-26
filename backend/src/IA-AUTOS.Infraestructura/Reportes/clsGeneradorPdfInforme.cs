using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Dominio.Custodia;

namespace IAAutos.Infraestructura.Reportes;

/// <summary>
/// El informe de una inspeccion en PDF (ver <see cref="clsDocumentoPdf"/>): la unidad, el viaje y la etapa; las fotos de las vistas con su sello; los hallazgos con sus fotos; el panel de danos con
/// las zonas marcadas; la transferencia con sus firmas, reservas o constancia; las notas aclaratorias y el historial de seguimiento.
/// </summary>
internal sealed class clsGeneradorPdfInforme : IGeneradorPdfInforme
{
    private const double FOTO_ALTO = 170;
    private const double PANEL_ANCHO = 380;
    private const double PANEL_ALTO = 260;
    private const double FIRMA_ANCHO = 220;
    private const double FIRMA_ALTO = 70;

    public byte[] Generar(DatosInforme d)
    {
        var doc = new clsDocumentoPdf();
        Encabezado(doc, d);
        Unidad(doc, d);
        Viaje(doc, d);
        Etapa(doc, d);
        FotosDeVistas(doc, d);
        Hallazgos(doc, d);
        Paneles(doc, d);
        Transferencia(doc, d.Transferencia);
        Notas(doc, "Notas aclaratorias de la inspección", d.NotasAclaratorias);
        Seguimiento(doc, d);
        if (d.FotosOmitidas > 0)
        {
            doc.Espaciar(8);
            doc.Escribir($"Este PDF no incluye {d.FotosOmitidas} foto(s) más para no pasar el máximo por documento: se ven en el informe.", 10);
        }

        doc.Espaciar(10);
        doc.Escribir($"Documento generado el {clsPieFecha.Fecha(d.GeneradaAt)}", 9);
        return doc.Generar();
    }

    private static void Encabezado(clsDocumentoPdf doc, DatosInforme d)
    {
        doc.Escribir("Traza Autos 360° — Botero Soto", 11);
        doc.Escribir($"Informe de inspección — {d.MomentoNombre}", 16, true);
        doc.Escribir($"{d.Proceso} · versión {d.Version} · {(d.Estado == "cerrada" ? "cerrada" : "en borrador")}", 11);
        doc.Espaciar(6);
    }

    private static void Seccion(clsDocumentoPdf doc, string titulo)
    {
        doc.Espaciar(8);
        doc.Escribir(titulo, 13, true);
    }

    private static void Unidad(clsDocumentoPdf doc, DatosInforme d)
    {
        Seccion(doc, "Unidad");
        doc.Escribir($"VIN: {d.Vin}", 12, true);
        doc.Escribir($"Marca: {d.Marca}   Modelo: {d.Modelo ?? "—"}   Tipo: {d.TipoVehiculo ?? "—"}   Color: {d.Color ?? "—"}", 11);
        doc.Escribir($"Operación: {d.Operacion ?? "—"}   Cliente: {d.Cliente ?? "—"}", 11);
    }

    private static void Viaje(clsDocumentoPdf doc, DatosInforme d)
    {
        Seccion(doc, "Viaje");
        doc.Escribir($"Código MTrack: {d.CodigoMtrack ?? "—"}   Placa: {d.Placa ?? "—"}   Tráiler: {d.Trailer ?? "—"}   Unidad: {d.Unidad ?? "—"}", 11);
        doc.Escribir($"Conductor: {d.Conductor ?? "—"}   Cédula: {d.ConductorCedula ?? "—"}", 11);
    }

    private static void Etapa(clsDocumentoPdf doc, DatosInforme d)
    {
        Seccion(doc, "Etapa");
        doc.Escribir($"Proceso: {d.Proceso}   Lugar: {d.Lugar ?? "—"}   Momento: {d.MomentoNombre}", 11);
        doc.Escribir($"Inicio: {clsPieFecha.Fecha(d.IniciadaAt)}   Cierre: {(d.CerradaAt is { } cierre ? clsPieFecha.Fecha(cierre) : "—")}", 11);
        doc.Escribir($"Inspector: {d.Inspector ?? "—"}", 11);
        if (!string.IsNullOrWhiteSpace(d.Observaciones))
        {
            doc.Escribir($"Observaciones: {d.Observaciones}", 11);
        }
    }

    private static void FotosDeVistas(clsDocumentoPdf doc, DatosInforme d)
    {
        Seccion(doc, "Fotos de las vistas");
        if (d.FotosDeVistas.Count == 0)
        {
            doc.Escribir("Sin fotos de vistas.", 11);
            return;
        }

        doc.Galeria(d.FotosDeVistas.Select(AFoto).ToList(), 2, FOTO_ALTO);
    }

    private static void Hallazgos(clsDocumentoPdf doc, DatosInforme d)
    {
        Seccion(doc, "Hallazgos");
        if (d.Hallazgos.Count == 0)
        {
            doc.Escribir("Sin hallazgos.", 11);
            return;
        }

        foreach (var h in d.Hallazgos)
        {
            doc.Espaciar(4);
            doc.Escribir($"Hallazgo {h.Numero} — {h.Zona}", 12, true);
            doc.Escribir($"Clase: {h.Clase ?? "—"}   Tamaño: {h.Tamano}   Nivel: {h.Nivel ?? "sin nivel"}{(h.NivelAccion is null ? string.Empty : $" ({h.NivelAccion})")}", 11, false, 10);
            doc.Escribir($"Origen: {h.Origen}   Preexistente: {(h.EsPreexistente ? "sí" : "no")}   Seguimiento: {h.EstadoSeguimiento}", 11, false, 10);
            if (!string.IsNullOrWhiteSpace(h.Nota))
            {
                doc.Escribir($"Nota: {h.Nota}", 11, false, 10);
            }

            if (h.Fotos.Count > 0)
            {
                doc.Galeria(h.Fotos.Select(AFoto).ToList(), 2, FOTO_ALTO);
            }

            Notas(doc, "Notas aclaratorias del hallazgo", h.NotasAclaratorias, 10);
        }
    }

    private static void Paneles(clsDocumentoPdf doc, DatosInforme d)
    {
        if (d.Paneles.Count == 0 && d.ZonasSinRegion.Count == 0)
        {
            return;
        }

        Seccion(doc, "Panel de daños");
        doc.Escribir("Cada zona con hallazgos va marcada con el número de su hallazgo.", 10);
        foreach (var panel in d.Paneles)
        {
            doc.Espaciar(4);
            doc.PanelConRegiones(panel.Nombre, panel.Imagen, panel.Regiones.Select(r => new RegionPdf(r.Puntos.Select(p => (p.X, p.Y)).ToList(), r.Etiqueta)).ToList(), PANEL_ANCHO, PANEL_ALTO);
        }

        foreach (var sinRegion in d.ZonasSinRegion)
        {
            doc.Escribir($"Sin dibujo en los paneles — hallazgo {sinRegion}", 10, false, 10);
        }
    }

    private static void Transferencia(clsDocumentoPdf doc, TransferenciaInforme? t)
    {
        Seccion(doc, "Transferencia de custodia");
        if (t is null)
        {
            doc.Escribir("Esta inspección no tiene transferencia de custodia.", 11);
            return;
        }

        doc.Escribir($"Cierre: {NombreCierre(t.TipoCierre)}", 12, true);
        if (t.FirmadaAt is { } firmada)
        {
            doc.Escribir($"Firmada por ambas partes el {clsPieFecha.Fecha(firmada)}", 11);
        }

        if (t.TipoCierre == clsTipoCierre.RECIBO_SIN_ENTREGA)
        {
            doc.Espaciar(4);
            doc.Escribir("Entrega: no hubo entrega", 12, true);
            doc.Escribir("El conductor hizo la salida del cargue: nadie de Botero Soto firma la entrega.", 11);
        }
        else
        {
            Firma(doc, "Entrega", t.Entrega, t.NoFirmaParte == clsParteFirmante.ENTREGA);
        }
        Firma(doc, "Recibe", t.Recibe, t.NoFirmaParte == clsParteFirmante.RECIBE);
        if (t.Reservas is not null)
        {
            doc.Espaciar(4);
            doc.Escribir("Reservas de quien recibe", 12, true);
            doc.Escribir(t.Reservas, 11);
        }

        if (t.TipoCierre == clsTipoCierre.NO_FIRMA)
        {
            doc.Espaciar(4);
            doc.Escribir("Constancia de no firma", 12, true);
            doc.Escribir($"Parte que no firmó: {(t.NoFirmaParte == clsParteFirmante.ENTREGA ? "quien entrega" : "quien recibe")}", 11);
            doc.Escribir($"Motivo: {t.MotivoNoFirma}", 11);
            doc.Escribir($"Testigo: {t.Testigo ?? "—"}", 11);
            if (t.Constancia is { } foto)
            {
                doc.Galeria([AFoto(foto)], 1, FOTO_ALTO);
            }
        }

        Notas(doc, "Notas aclaratorias de la transferencia", t.NotasAclaratorias);
    }

    private static void Firma(clsDocumentoPdf doc, string rol, FirmaConstancia firma, bool seNego)
    {
        doc.Espaciar(4);
        doc.Escribir($"{rol}: {firma.Persona}", 12, true);
        if (firma.Imagen is null && firma.Trazo is null)
        {
            doc.Escribir(seNego ? "Se negó a firmar (ver la constancia de no firma)" : "No ha firmado", 11);
            return;
        }

        var ip = firma.Ip is null ? string.Empty : $" · IP {firma.Ip}";
        doc.Escribir($"Firmó el {(firma.Fecha is { } f ? clsPieFecha.Fecha(f) : "—")}{ip}", 11);
        if (firma.Imagen is not null)
        {
            doc.Imagen(firma.Imagen, FIRMA_ANCHO, FIRMA_ALTO);
        }
        else if (!doc.Firma(firma.Trazo!))
        {
            doc.Escribir("(Firma manuscrita registrada; el trazo no se pudo dibujar)", 10);
        }
    }

    private static void Notas(clsDocumentoPdf doc, string titulo, IReadOnlyList<NotaInforme> notas, double sangria = 0)
    {
        if (notas.Count == 0)
        {
            return;
        }

        doc.Espaciar(4);
        doc.Escribir(titulo, 12, true, sangria);
        foreach (var nota in notas)
        {
            doc.Escribir($"{clsPieFecha.Fecha(nota.Fecha)} · {nota.Autor}: {nota.Texto}", 10, false, sangria);
        }
    }

    private static void Seguimiento(clsDocumentoPdf doc, DatosInforme d)
    {
        var conHistorial = d.Hallazgos.Where(h => h.Seguimiento.Count > 0).ToList();
        if (conHistorial.Count == 0)
        {
            return;
        }

        Seccion(doc, "Historial de seguimiento");
        foreach (var h in conHistorial)
        {
            doc.Espaciar(4);
            doc.Escribir($"Hallazgo {h.Numero} — {h.Zona}", 11, true);
            foreach (var paso in h.Seguimiento)
            {
                var cambio = paso.EstadoAnterior is null && paso.EstadoNuevo is null ? string.Empty : $" ({paso.EstadoAnterior ?? "—"} → {paso.EstadoNuevo ?? "—"})";
                var nota = string.IsNullOrWhiteSpace(paso.Nota) ? string.Empty : $": {paso.Nota}";
                doc.Escribir($"{clsPieFecha.Fecha(paso.Fecha)} · {paso.Accion}{cambio} · {paso.Usuario ?? "—"}{nota}", 10, false, 10);
                if (paso.Sello is { } sello)
                {
                    doc.Galeria([AFoto(sello)], 1, FOTO_ALTO);
                }
            }
        }
    }

    private static FotoPdf AFoto(FotoInforme foto) =>
        foto.Contenido is null ? new FotoPdf([], ["Archivo no disponible en el almacén", .. foto.Pie]) : new FotoPdf(foto.Contenido, foto.Pie);

    private static string NombreCierre(string? tipo) => tipo switch
    {
        clsTipoCierre.FIRMADA => "firmada",
        clsTipoCierre.CON_RESERVAS => "firmada con reservas",
        clsTipoCierre.NO_FIRMA => "no firma",
        clsTipoCierre.RECIBO_SIN_ENTREGA => "recibo sin entrega",
        _ => "abierta (sin tipo de cierre)",
    };
}

/// <summary>El formato de fecha de los PDF: UTC, hasta el segundo.</summary>
internal static class clsPieFecha
{
    public static string Fecha(DateTimeOffset fecha) => fecha.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
}
