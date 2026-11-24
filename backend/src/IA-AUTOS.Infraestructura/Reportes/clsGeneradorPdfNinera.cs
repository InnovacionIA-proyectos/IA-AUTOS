using System.Globalization;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Dominio.Custodia;

namespace IAAutos.Infraestructura.Reportes;

/// <summary>
/// El informe de la niñera de un viaje en PDF (ver <see cref="clsDocumentoPdf"/>): el viaje, los puestos con el carro de cada uno, los daños de cada carro, las fotos de la niñera y la firma unica del cargue de cada
/// parte con la hora y el GPS del momento (o la constancia de no firma). Lo que aun no existe se dice donde corresponde: «Acomodación sin cerrar» y «Cargue sin firmar».
/// </summary>
internal sealed class clsGeneradorPdfNinera : IGeneradorPdfNinera
{
    private const double FOTO_ALTO = 170;
    private const double FIRMA_ANCHO = 220;
    private const double FIRMA_ALTO = 70;

    public byte[] Generar(DatosNinera d)
    {
        var doc = new clsDocumentoPdf();
        doc.Escribir("Traza Autos 360° — Botero Soto", 11);
        doc.Escribir("Informe de la niñera", 16, true);
        doc.Escribir($"Viaje {d.CodigoMtrack ?? "—"} · {d.Estado}", 11);
        doc.Espaciar(6);
        Viaje(doc, d);
        Puestos(doc, d);
        Danos(doc, d);
        Fotos(doc, d);
        Firmas(doc, d);
        doc.Espaciar(10);
        doc.Escribir($"Documento generado el {clsPieFecha.Fecha(d.GeneradaAt)}", 9);
        return doc.Generar();
    }

    private static void Seccion(clsDocumentoPdf doc, string titulo)
    {
        doc.Espaciar(8);
        doc.Escribir(titulo, 13, true);
    }

    private static void Viaje(clsDocumentoPdf doc, DatosNinera d)
    {
        Seccion(doc, "Viaje");
        doc.Escribir($"Código MTrack: {d.CodigoMtrack ?? "—"}   Placa: {d.Placa ?? "—"}   Tráiler: {d.Trailer ?? "—"}   Unidad: {d.Unidad ?? "—"}", 11);
        doc.Escribir($"Conductor: {d.Conductor ?? "—"}   Inspector: {d.Inspector ?? "—"}", 11);
        doc.Escribir($"Lugar de cargue: {d.LugarCargue ?? "—"}   Capacidad de la niñera: {d.Capacidad} puestos", 11);
    }

    /// <summary>Los puestos en orden con el VIN de cada uno, los carros sin puesto y los que no salen marcados.</summary>
    private static void Puestos(clsDocumentoPdf doc, DatosNinera d)
    {
        Seccion(doc, "Acomodación en la niñera");
        if (!d.AcomodacionCerrada)
        {
            doc.Escribir("Acomodación sin cerrar: los puestos pueden cambiar.", 11, true);
        }

        foreach (var puesto in d.Puestos)
        {
            var carro = puesto.Vin is null ? null : d.Carros.FirstOrDefault(c => c.Vin == puesto.Vin);
            doc.Escribir(carro is null ? $"Puesto {puesto.Etiqueta} — libre" : $"Puesto {puesto.Etiqueta} — VIN {carro.Vin} · {Descripcion(carro)}", 11);
        }

        var sinPuesto = d.Carros.Where(c => !d.Puestos.Any(p => p.Vin == c.Vin)).ToList();
        if (sinPuesto.Count > 0)
        {
            doc.Espaciar(4);
            doc.Escribir("Carros sin puesto", 12, true);
            foreach (var carro in sinPuesto)
            {
                doc.Escribir($"VIN {carro.Vin} · {Descripcion(carro)}{(carro.NoSale ? string.Empty : " — sin posición")}", 11, false, 10);
            }
        }

        var noSalen = d.Carros.Where(c => c.NoSale).ToList();
        if (noSalen.Count > 0)
        {
            doc.Espaciar(4);
            doc.Escribir("Carros que no salen del viaje", 12, true);
            foreach (var carro in noSalen)
            {
                doc.Escribir($"NO SALE — VIN {carro.Vin}: {carro.MotivoNoSale ?? "sin motivo"}", 11, false, 10);
            }
        }
    }

    private static string Descripcion(CarroNinera carro)
    {
        var partes = new[] { carro.Marca, carro.Modelo, carro.Color }.Where(p => !string.IsNullOrWhiteSpace(p));
        return $"{string.Join(" ", partes)}{(carro.NoSale ? " (NO SALE)" : string.Empty)}";
    }

    private static void Danos(clsDocumentoPdf doc, DatosNinera d)
    {
        Seccion(doc, "Daños de cada carro");
        if (d.Carros.Count == 0)
        {
            doc.Escribir("El viaje no tiene carros.", 11);
            return;
        }

        foreach (var carro in d.Carros)
        {
            doc.Espaciar(4);
            var puesto = carro.Puesto is { } p ? $"puesto {p}" : "sin puesto";
            doc.Escribir($"VIN {carro.Vin} — {puesto}{(carro.NoSale ? " — NO SALE" : string.Empty)}", 12, true);
            if (carro.CargueSinInforme)
            {
                doc.Escribir("Sin informe del cargue.", 11, false, 10);
                continue;
            }

            if (carro.Danos.Count == 0)
            {
                doc.Escribir("Sin daños.", 11, false, 10);
                continue;
            }

            foreach (var dano in carro.Danos)
            {
                var nivel = d.MostrarNivel && dano.Nivel is not null ? $"   Nivel: {dano.Nivel}" : string.Empty;
                doc.Escribir($"{dano.Zona} — Clase: {dano.Clase ?? "—"}   Tamaño: {dano.Tamano}{nivel}   ({dano.Informe})", 11, false, 10);
                if (d.MostrarNivel && !string.IsNullOrWhiteSpace(dano.Nota))
                {
                    doc.Escribir($"Nota: {dano.Nota}", 10, false, 20);
                }
            }
        }
    }

    private static void Fotos(clsDocumentoPdf doc, DatosNinera d)
    {
        Seccion(doc, "Fotos de la niñera");
        if (d.Fotos.Count == 0)
        {
            doc.Escribir("Sin fotos de la niñera.", 11);
            return;
        }

        doc.Galeria(d.Fotos.Select(f => f.Contenido is null ? new FotoPdf([], ["Archivo no disponible en el almacén", .. f.Pie]) : new FotoPdf(f.Contenido, f.Pie)).ToList(), 2, FOTO_ALTO);
        if (d.FotosOmitidas > 0)
        {
            doc.Escribir($"Este PDF no incluye {d.FotosOmitidas} foto(s) más para no pasar el máximo por documento.", 10);
        }
    }

    private static void Firmas(clsDocumentoPdf doc, DatosNinera d)
    {
        Seccion(doc, "Firma del cargue");
        if (d.Constancia is { } constancia)
        {
            doc.Escribir("Cargue sin firma completa: una parte se negó a firmar.", 12, true);
            doc.Escribir($"Parte que se negó: {(constancia.ParteQueSeNego == clsParteFirmante.ENTREGA ? "quien entrega (el inspector)" : "quien recibe (el conductor)")}", 11);
            doc.Escribir($"Motivo: {constancia.Motivo}", 11);
            doc.Escribir($"Testigo: {constancia.Testigo ?? "—"}", 11);
            if (constancia.Momento is { } momento)
            {
                doc.Escribir($"Constancia dejada el {clsPieFecha.Fecha(momento)}", 11);
            }
        }

        if (d.Entrega is null && d.Recibe is null && d.Constancia is null)
        {
            doc.Escribir("Cargue sin firmar.", 12, true);
            return;
        }

        Firma(doc, "Entrega (inspector)", d.Entrega, d.Constancia?.ParteQueSeNego == clsParteFirmante.ENTREGA);
        Firma(doc, "Recibe (conductor)", d.Recibe, d.Constancia?.ParteQueSeNego == clsParteFirmante.RECIBE);
    }

    private static void Firma(clsDocumentoPdf doc, string rol, FirmaNinera? firma, bool seNego)
    {
        doc.Espaciar(4);
        if (firma is null)
        {
            doc.Escribir(rol, 12, true);
            doc.Escribir(seNego ? "Se negó a firmar (ver la constancia de no firma)" : "Cargue sin firmar por esta parte", 11);
            return;
        }

        doc.Escribir($"{rol}: {firma.Persona}", 12, true);
        var hora = firma.Momento ?? firma.LlegoAt;
        doc.Escribir($"Firmó el {(hora is { } h ? clsPieFecha.Fecha(h) : "—")}{(firma.Momento is null ? string.Empty : " (hora del momento de la firma)")}", 11);
        if (firma.Lat is { } lat && firma.Lng is { } lng)
        {
            var precision = firma.PrecisionM is { } p ? $" (± {p.ToString("0.#", CultureInfo.InvariantCulture)} m)" : string.Empty;
            doc.Escribir($"Ubicación: {lat.ToString("0.#####", CultureInfo.InvariantCulture)}, {lng.ToString("0.#####", CultureInfo.InvariantCulture)}{precision}", 11);
        }

        if (firma.Imagen is not null)
        {
            doc.Imagen(firma.Imagen, FIRMA_ANCHO, FIRMA_ALTO);
        }
        else if (firma.Trazo is null || !doc.Firma(firma.Trazo))
        {
            doc.Escribir("(Firma manuscrita registrada; el trazo no se pudo dibujar)", 10);
        }
    }
}
