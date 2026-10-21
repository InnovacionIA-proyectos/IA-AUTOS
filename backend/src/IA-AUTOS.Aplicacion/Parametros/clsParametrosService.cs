using System.Globalization;
using IAAutos.Dominio.Parametros;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Parametros;

/// <summary>El valor vigente de los parametros que leen los procesos (alertas, reportes, URL firmadas...). Un valor ausente o que no se puede leer devuelve el valor por defecto.</summary>
public interface IParametros
{
    /// <summary>El valor del parametro; nulo si no existe o no tiene valor.</summary>
    Task<string?> ValorAsync(string clave, CancellationToken ct = default);

    /// <summary>El valor como entero; <paramref name="porDefecto"/> si no existe, no tiene valor o no es un numero entero.</summary>
    Task<int> EnteroAsync(string clave, int porDefecto, CancellationToken ct = default);

    /// <summary>Los codigos de un parametro de tipo lista (separados por coma, sin espacios sobrantes ni repetidos); <paramref name="porDefecto"/> si no existe o no tiene valor.</summary>
    Task<IReadOnlyList<string>> ListaAsync(string clave, IReadOnlyList<string> porDefecto, CancellationToken ct = default);
}

/// <summary>
/// Lee los parametros de la base con una cache de un minuto: un cambio de valor se aplica en un minuto o menos, sin desplegar y sin consultar la tabla en cada lectura. Si la tabla aun
/// no existe (la base sin migrar) o no se puede leer, devuelve los valores por defecto en lugar de romper el proceso que los pide.
/// </summary>
public sealed class clsParametrosService(IParametroRepository repositorio, IMemoryCache cache, ILogger<clsParametrosService> registro) : IParametros
{
    public static readonly TimeSpan VIGENCIA = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan REINTENTO = TimeSpan.FromSeconds(15);
    private const string CLAVE_CACHE = "parametros:valores";

    public async Task<string?> ValorAsync(string clave, CancellationToken ct = default) =>
        (await ObtenerAsync(ct)).GetValueOrDefault(clave);

    public async Task<int> EnteroAsync(string clave, int porDefecto, CancellationToken ct = default) =>
        int.TryParse(await ValorAsync(clave, ct), NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor) ? valor : porDefecto;

    public async Task<IReadOnlyList<string>> ListaAsync(string clave, IReadOnlyList<string> porDefecto, CancellationToken ct = default)
    {
        var lista = (await ValorAsync(clave, ct))?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
        return lista is { Count: > 0 } ? lista : porDefecto;
    }

    private async Task<IReadOnlyDictionary<string, string?>> ObtenerAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CLAVE_CACHE, out IReadOnlyDictionary<string, string?>? valores) && valores is not null)
        {
            return valores;
        }

        try
        {
            var leidos = (await repositorio.ListarAsync(ct)).ToDictionary(p => p.Clave, p => p.Valor, StringComparer.Ordinal);
            cache.Set(CLAVE_CACHE, (IReadOnlyDictionary<string, string?>)leidos, VIGENCIA);
            return leidos;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La tabla puede no existir todavia o la base no responder: los procesos siguen con sus valores por defecto y se reintenta en unos segundos.
            registro.LogWarning(ex, "No se pudieron leer los parametros: se usan los valores por defecto");
            var vacio = (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>();
            cache.Set(CLAVE_CACHE, vacio, REINTENTO);
            return vacio;
        }
    }
}
