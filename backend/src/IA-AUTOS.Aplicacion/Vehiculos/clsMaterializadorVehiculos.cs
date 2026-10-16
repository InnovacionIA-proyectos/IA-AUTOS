using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Vehiculos;

/// <summary>
/// Busca los vehiculos de un lote de VINs (carga masiva o ingesta Sofasa). Precarga en bloque los existentes para evitar una consulta
/// por fila; si una fila falla (se descartan los cambios del contexto) la precarga deja de ser confiable y se pasa a consultar fila
/// por fila. Las etapas no se crean aqui: nacen al agregar el VIN a un viaje.
/// </summary>
internal sealed class clsMaterializadorVehiculos(IVehiculoRepository vehiculos)
{
    private Dictionary<string, clsVehiculo> _cacheVehiculos = new(StringComparer.OrdinalIgnoreCase);
    private bool _cacheValida;

    /// <summary>Precarga los vehiculos del lote.</summary>
    public async Task PrepararAsync(IReadOnlyCollection<string> vines, CancellationToken ct)
    {
        var existentes = await vehiculos.ObtenerPorVinesAsync(vines, ct);
        _cacheVehiculos = new Dictionary<string, clsVehiculo>(existentes, StringComparer.OrdinalIgnoreCase);
        _cacheValida = true;
    }

    public async Task<clsVehiculo?> ObtenerAsync(string vin, CancellationToken ct)
    {
        var normalizado = clsVin.Normalizar(vin);
        if (_cacheValida)
        {
            return _cacheVehiculos.GetValueOrDefault(normalizado);
        }

        return await vehiculos.ObtenerPorVinAsync(normalizado, ct);
    }

    /// <summary>Registra un vehiculo recien guardado para que las filas siguientes con el mismo VIN lo reutilicen.</summary>
    public void Registrar(clsVehiculo vehiculo) => _cacheVehiculos[vehiculo.Vin] = vehiculo;

    /// <summary>Tras descartar cambios, las entidades precargadas quedan desconectadas: se consulta fila por fila.</summary>
    public void InvalidarCache() => _cacheValida = false;
}
