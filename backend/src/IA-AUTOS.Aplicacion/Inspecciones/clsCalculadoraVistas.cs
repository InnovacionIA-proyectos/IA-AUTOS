using IAAutos.Aplicacion.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Las vistas de foto obligatorias que aun no tienen foto en una inspeccion: lo que impide cerrarla y lo que se avisa cuando una captura queda a medias.</summary>
public sealed class clsCalculadoraVistas(IVehiculoRepository vehiculos, ICatalogosLectura catalogos)
{
    /// <summary>Nombres de las vistas obligatorias (activas, del tipo del vehiculo o de cualquier tipo) que aun no tienen foto, en el orden del catalogo.</summary>
    public async Task<IReadOnlyList<string>> FaltantesAsync(clsEtapa etapa, clsResumenCierre resumen, CancellationToken ct = default)
    {
        var vehiculo = await vehiculos.ObtenerPorIdAsync(etapa.VehiculoId, ct)
            ?? throw new clsEntidadNoEncontradaException("El vehículo de la etapa no existe");
        return (await catalogos.ValoresAsync(clsNombresCatalogo.VISTAS_FOTO, ct))
            .Where(v => v.Activo && v["obligatoria"] is true
                && (v["tipo_vehiculo"] is not string tipo || string.Equals(tipo, vehiculo.TipoVehiculo, StringComparison.OrdinalIgnoreCase))
                && !resumen.VistasFotografiadas.Contains(v.Id))
            .OrderBy(v => v["orden"] as int? ?? 0)
            .Select(v => v["nombre"] as string ?? v.Codigo ?? v.Id.ToString())
            .ToList();
    }
}
