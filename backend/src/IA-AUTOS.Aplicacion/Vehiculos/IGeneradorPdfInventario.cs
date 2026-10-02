using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;

namespace IAAutos.Aplicacion.Vehiculos;

/// <summary>Genera el inventario del VIN en PDF (soporte de rechazo).</summary>
public interface IGeneradorPdfInventario
{
    byte[] Generar(InventarioDto inventario);
}
