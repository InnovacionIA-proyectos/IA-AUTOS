using IAAutos.Aplicacion.Catalogos;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>La capacidad con que nace un viaje: la que se indique o, si no, los puestos por defecto de su tipo de unidad (y 7 si el tipo no los define). Queda guardada en el viaje: cambiar despues los puestos del tipo no la mueve.</summary>
internal static class clsCapacidadDeViajes
{
    public static async Task<byte> InicialAsync(ICatalogosLectura catalogos, Guid tipoUnidadId, int? indicada, CancellationToken ct)
    {
        if (indicada is not null)
        {
            return clsViaje.CapacidadValida(indicada);
        }

        var tipo = (await catalogos.ValoresAsync(clsNombresCatalogo.TIPOS_UNIDAD, ct)).FirstOrDefault(v => v.Id == tipoUnidadId);
        var puestos = tipo is not null && tipo.TryGetValue("puestos", out var valor) && valor is not null ? Convert.ToInt32(valor, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
        return (byte)(puestos ?? clsViaje.CAPACIDAD_POR_DEFECTO);
    }
}
