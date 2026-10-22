using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;

namespace IAAutos.Aplicacion.Vehiculos;

/// <summary>
/// Lo que lleva el PDF del inventario del VIN: el inventario, las fotos de sus inspecciones por id de evidencia (las que caben en el PDF), los paneles del dibujo de danos con las zonas marcadas
/// (cada una con cuantos hallazgos tiene) y cuantas fotos quedaron fuera.
/// </summary>
public sealed record DatosInventarioPdf(
    InventarioDto Inventario,
    IReadOnlyDictionary<Guid, FotoInforme> Fotos,
    IReadOnlyList<PanelInforme> Paneles,
    IReadOnlyList<string> ZonasSinRegion,
    int FotosOmitidas);

/// <summary>Genera el inventario del VIN en PDF (soporte de rechazo), con las fotos y los paneles.</summary>
public interface IGeneradorPdfInventario
{
    byte[] Generar(DatosInventarioPdf datos);
}
