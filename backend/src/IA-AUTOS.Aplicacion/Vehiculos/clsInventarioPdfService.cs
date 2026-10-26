using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

namespace IAAutos.Aplicacion.Vehiculos;

/// <summary>
/// El inventario del VIN en PDF, con las fotos de sus inspecciones (con su sello) y los paneles del dibujo de danos con las zonas marcadas (cada una con cuantos hallazgos tiene). Lo ve quien ve el
/// inventario (403 si el VIN es de otro cliente o de un viaje ajeno; el conductor, sin los hallazgos que no registro). Un PDF no lleva mas de <see cref="MAXIMO_FOTOS"/> fotos (primero las de
/// los hallazgos): las demas se indican y se ven en el inventario.
/// </summary>
public sealed class clsInventarioPdfService(
    clsVehiculosService vehiculos,
    ICatalogosLectura catalogos,
    clsPanelesDeDano paneles,
    IAlmacenArchivos almacen,
    IGeneradorPdfInventario generador)
{
    public const int MAXIMO_FOTOS = 60;

    public async Task<byte[]> GenerarAsync(string vin, CancellationToken ct = default)
    {
        var inventario = await vehiculos.ObtenerInventarioAsync(vin, ct);
        var vistas = (await catalogos.ValoresAsync(clsNombresCatalogo.VISTAS_FOTO, ct))
            .Where(v => v.TryGetValue("nombre", out var nombre) && nombre is string)
            .ToDictionary(v => v.Id, v => (string)v["nombre"]!);
        var inspecciones = inventario.Etapas.SelectMany(e => e.Inspecciones).ToList();

        // Primero las fotos de los hallazgos (las mas importantes) y despues las de las vistas, hasta llenar el PDF.
        var candidatas = inspecciones.SelectMany(i => i.Evidencias).Where(e => !e.EsSello && !e.EsConstancia).OrderBy(e => e.HallazgoId is null ? 1 : 0).ToList();
        var fotos = new Dictionary<Guid, FotoInforme>();
        foreach (var foto in candidatas.Take(MAXIMO_FOTOS))
        {
            var titulo = foto.HallazgoId is not null ? "Foto del hallazgo" : foto.VistaId is { } vistaId ? $"Vista: {vistas.GetValueOrDefault(vistaId) ?? "sin nombre"}" : "Foto adicional";
            fotos[foto.Id] = new FotoInforme(foto.RutaArchivo is null ? null : await almacen.LeerAsync(foto.RutaArchivo, ct), titulo, clsPieDeFoto.De(titulo, foto));
        }

        var zonas = inspecciones.SelectMany(i => i.Hallazgos)
            .GroupBy(h => (h.CuadranteNumero, h.Lado, Nombre: h.Zona ?? h.ParteVehiculo ?? "Sin ubicar"))
            .Select(g => new ZonaConHallazgos(g.Key.CuadranteNumero, g.Key.Lado, g.Key.Nombre, g.Count().ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        var dibujados = await paneles.ArmarAsync(zonas, ct);
        return generador.Generar(new DatosInventarioPdf(inventario, fotos, dibujados.Paneles, dibujados.ZonasSinRegion, Math.Max(candidatas.Count - MAXIMO_FOTOS, 0)));
    }
}
