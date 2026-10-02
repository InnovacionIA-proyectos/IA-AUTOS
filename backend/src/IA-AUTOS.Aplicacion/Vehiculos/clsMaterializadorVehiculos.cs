using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Vehiculos;

/// <summary>
/// Garantiza que un VIN exista como vehiculo con su ruta de custodia estandar (Cargue, Transporte, Descargue).
/// Es idempotente: re-procesar un VIN no duplica etapas. Precarga en bloque los vehiculos del lote para evitar
/// una consulta por fila; si una fila falla (se descartan los cambios del contexto) la precarga deja de ser
/// confiable y se pasa a consultar fila por fila.
/// </summary>
internal sealed class clsMaterializadorVehiculos(
    IVehiculoRepository vehiculos,
    IEtapaRepository etapas,
    IPuntoCustodiaRepository puntos,
    IReloj reloj)
{
    private IReadOnlyDictionary<string, Guid>? _puntos;
    private Dictionary<string, clsVehiculo> _cacheVehiculos = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<Guid, HashSet<int>> _cacheOrdenes = [];
    private bool _cacheValida;

    /// <summary>Carga el catalogo de puntos (falla de inmediato si falta alguno) y precarga los vehiculos del lote.</summary>
    public async Task PrepararAsync(IReadOnlyCollection<string> vines, CancellationToken ct)
    {
        _puntos = await puntos.ObtenerIdsPorNombreAsync(clsPuntoCustodiaNombre.RUTA_CARGA, ct);
        // Valida el catalogo antes de procesar cualquier fila: un catalogo incompleto es un error interno, no de una fila.
        clsRutaCustodia.CrearEtapasEstandar(Guid.Empty, _puntos, reloj.Ahora);

        var existentes = await vehiculos.ObtenerPorVinesAsync(vines, ct);
        _cacheVehiculos = new Dictionary<string, clsVehiculo>(existentes, StringComparer.OrdinalIgnoreCase);
        var ordenes = await etapas.ObtenerOrdenesExistentesAsync(existentes.Values.Select(v => v.Id).ToList(), ct);
        _cacheOrdenes = ordenes.ToDictionary(o => o.Key, o => o.Value.ToHashSet());
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

    /// <summary>Agrega al contexto las etapas de la ruta que el vehiculo aun no tiene.</summary>
    public async Task AgregarRutaFaltanteAsync(clsVehiculo vehiculo, bool esNuevo, CancellationToken ct)
    {
        HashSet<int> existentes;
        if (esNuevo)
        {
            existentes = [];
        }
        else if (_cacheValida && _cacheOrdenes.TryGetValue(vehiculo.Id, out var enCache))
        {
            existentes = enCache;
        }
        else
        {
            var consultadas = await etapas.ObtenerOrdenesExistentesAsync([vehiculo.Id], ct);
            existentes = consultadas.TryGetValue(vehiculo.Id, out var ordenes) ? ordenes.ToHashSet() : [];
        }

        foreach (var etapa in clsRutaCustodia.CrearEtapasEstandar(vehiculo.Id, _puntos!, reloj.Ahora))
        {
            if (existentes.Add(etapa.Orden))
            {
                etapas.Agregar(etapa);
            }
        }

        _cacheOrdenes[vehiculo.Id] = existentes;
    }

    /// <summary>Registra un vehiculo recien guardado para que las filas siguientes con el mismo VIN lo reutilicen.</summary>
    public void Registrar(clsVehiculo vehiculo) => _cacheVehiculos[vehiculo.Vin] = vehiculo;

    /// <summary>Tras descartar cambios, las entidades precargadas quedan desconectadas: se consulta fila por fila.</summary>
    public void InvalidarCache() => _cacheValida = false;
}
