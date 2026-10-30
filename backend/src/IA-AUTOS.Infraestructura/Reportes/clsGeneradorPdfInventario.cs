using System.Globalization;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Vehiculos;

namespace IAAutos.Infraestructura.Reportes;

/// <summary>
/// El inventario del VIN en PDF (ver <see cref="clsDocumentoPdf"/>): el texto del inventario original (cada proceso con sus inspecciones, hallazgos y evidencias) mas las fotos de cada inspeccion
/// con su sello y, al final, los paneles del dibujo de danos con las zonas con hallazgos marcadas (cada una con cuantos tiene).
/// </summary>
internal sealed class clsGeneradorPdfInventario : IGeneradorPdfInventario
{
    private const double FOTO_ALTO = 150;
    private const double PANEL_ANCHO = 380;
    private const double PANEL_ALTO = 260;

    public byte[] Generar(DatosInventarioPdf datos)
    {
        var inv = datos.Inventario;
        var doc = new clsDocumentoPdf();
        doc.Escribir("Traza Autos 360° — Botero Soto", 16, true);
        doc.Espaciar(4);
        doc.Escribir($"VIN: {inv.Vin}", 12, true);
        doc.Escribir($"Marca: {inv.Marca}   Operación: {inv.Operacion}", 11);
        doc.Escribir($"Modelo: {inv.Modelo ?? "—"}   Color: {inv.Color ?? "—"}", 11);
        if (!inv.Activo)
        {
            doc.Escribir("Vehículo dado de baja (inactivo)", 11, true);
        }

        doc.Espaciar(6);

        foreach (var etapa in inv.Etapas)
        {
            doc.Escribir($"Proceso: {etapa.Proceso} (orden {etapa.Orden})", 12, true);
            if (etapa.Posicion is { } posicion)
            {
                doc.Escribir($"  Posición en niñera: {posicion}", 11);
            }

            foreach (var inspeccion in etapa.Inspecciones)
            {
                var fecha = inspeccion.Fecha.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                doc.Escribir($"  {inspeccion.MomentoNombre} — {inspeccion.Estado} — {fecha}", 11);
                foreach (var hallazgo in inspeccion.Hallazgos)
                {
                    doc.Escribir($"    • {hallazgo.Zona ?? "Sin ubicar"}: {hallazgo.ClaseDano ?? "Sin clase"} ({hallazgo.TamanoDano})", 11);
                }

                doc.Escribir($"    Evidencias: {inspeccion.Evidencias.Count}", 11);
                var fotos = inspeccion.Evidencias.Where(e => e.ExtraId is null && datos.Fotos.ContainsKey(e.Id)).Select(e => datos.Fotos[e.Id]).ToList();
                if (fotos.Count > 0)
                {
                    doc.Galeria(fotos.Select(AFoto).ToList(), 2, FOTO_ALTO);
                }

                // Los extras del vehiculo, cada uno con su observacion y su galeria de fotos selladas.
                foreach (var extra in inspeccion.Extras ?? [])
                {
                    doc.Escribir($"    Extra: {extra.Nombre}", 11, true);
                    if (!string.IsNullOrWhiteSpace(extra.Observacion))
                    {
                        doc.Escribir($"      Observación: {extra.Observacion}", 11);
                    }

                    var fotosDelExtra = extra.Fotos.Where(e => datos.Fotos.ContainsKey(e.Id)).Select(e => datos.Fotos[e.Id]).ToList();
                    if (fotosDelExtra.Count > 0)
                    {
                        doc.Galeria(fotosDelExtra.Select(AFoto).ToList(), 2, FOTO_ALTO);
                    }
                }
            }

            doc.Espaciar(4);
        }

        Paneles(doc, datos);
        if (datos.FotosOmitidas > 0)
        {
            doc.Espaciar(6);
            doc.Escribir($"Este PDF no incluye {datos.FotosOmitidas} foto(s) más para no pasar el máximo por documento: se ven en el inventario.", 10);
        }

        return doc.Generar();
    }

    private static void Paneles(clsDocumentoPdf doc, DatosInventarioPdf datos)
    {
        if (datos.Paneles.Count == 0 && datos.ZonasSinRegion.Count == 0)
        {
            return;
        }

        doc.Espaciar(6);
        doc.Escribir("Panel de daños", 13, true);
        doc.Escribir("Cada zona con hallazgos va marcada con la cantidad de hallazgos que tiene.", 10);
        foreach (var panel in datos.Paneles)
        {
            doc.Espaciar(4);
            doc.PanelConRegiones(panel.Nombre, panel.Imagen, panel.Regiones.Select(r => new RegionPdf(r.Puntos.Select(p => (p.X, p.Y)).ToList(), r.Etiqueta)).ToList(), PANEL_ANCHO, PANEL_ALTO);
        }

        foreach (var sinRegion in datos.ZonasSinRegion)
        {
            doc.Escribir($"Sin dibujo en los paneles — {sinRegion} hallazgo(s)", 10, false, 10);
        }
    }

    private static FotoPdf AFoto(FotoInforme foto) =>
        foto.Contenido is null ? new FotoPdf([], ["Archivo no disponible en el almacén", .. foto.Pie]) : new FotoPdf(foto.Contenido, foto.Pie);
}
