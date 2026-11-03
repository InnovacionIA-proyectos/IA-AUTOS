using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Un unico SQL Server 2019 (la version de produccion) en un contenedor para toda la ejecucion de pruebas. Cada fixture o prueba crea SU PROPIA base en el, asi que nada se comparte entre ellas y pueden
/// correr en paralelo: arrancar un contenedor por prueba costaba mas que la prueba misma. Toda base se crea con el mismo script que usa produccion (<c>database/00-crear-base.sql</c>: colacion
/// Modern_Spanish_CI_AS, nivel de compatibilidad 150 y READ_COMMITTED_SNAPSHOT), para que las pruebas se comporten como la aplicacion desplegada y no con los valores por defecto del servidor.
/// El contenedor no se reutiliza entre ejecuciones (nada de estado heredado): se destruye al terminar el proceso.
/// </summary>
public static partial class clsServidorSql
{
    private const string IMAGEN = "mcr.microsoft.com/mssql/server:2019-latest";
    private const string BASE_DE_PRODUCCION = "IAAutos";

    private static readonly Lazy<Task<MsSqlContainer>> _contenedor = new(IniciarAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private static async Task<MsSqlContainer> IniciarAsync()
    {
        var contenedor = new MsSqlBuilder(IMAGEN).Build();
        await contenedor.StartAsync();
        // Sin Ryuk (algunos entornos de CI lo desactivan) el contenedor quedaria vivo: se destruye al cerrar el proceso.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => contenedor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return contenedor;
    }

    /// <summary>La cadena de conexion a la base master del servidor (para crear y borrar bases).</summary>
    public static async Task<string> CadenaMaestraAsync() => (await _contenedor.Value).GetConnectionString();

    /// <summary>Crea una base nueva, con nombre unico a partir de <paramref name="prefijo"/> y con la configuracion de produccion, y devuelve su cadena de conexion. Sin ddl: quien llama aplica el que necesite.</summary>
    public static async Task<string> CrearBaseAsync(string prefijo)
    {
        var maestra = await CadenaMaestraAsync();
        var nombre = NombreUnico(prefijo);
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "00-crear-base.sql"));
        // El script de produccion crea [IAAutos]; se usa tal cual cambiando solo el nombre de la base.
        await EjecutarLotesAsync(maestra, NombreDeBase().Replace(script, nombre));
        return new SqlConnectionStringBuilder(maestra) { InitialCatalog = nombre }.ConnectionString;
    }

    private static string NombreUnico(string prefijo) => $"{prefijo}_{Guid.NewGuid():N}"[..Math.Min(prefijo.Length + 9, 100)];

    /// <summary>Borra una base creada con <see cref="CrearBaseAsync"/>, aunque tenga conexiones abiertas.</summary>
    public static async Task EliminarBaseAsync(string cadena)
    {
        var nombre = new SqlConnectionStringBuilder(cadena).InitialCatalog;
        await using var conexion = new SqlConnection(await CadenaMaestraAsync());
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(
            $"IF DB_ID(N'{nombre}') IS NOT NULL BEGIN ALTER DATABASE [{nombre}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{nombre}]; END", conexion) { CommandTimeout = 120 };
        await comando.ExecuteNonQueryAsync();
    }

    /// <summary>Ejecuta un script con separadores GO por lotes, con las opciones de sesion de sqlcmd -I (QUOTED_IDENTIFIER y ANSI_NULLS en ON).</summary>
    public static async Task EjecutarScriptAsync(string cadena, string ruta) => await EjecutarLotesAsync(cadena, await File.ReadAllTextAsync(ruta));

    private static async Task EjecutarLotesAsync(string cadena, string contenido)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        await using (var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion))
        {
            await preparar.ExecuteNonQueryAsync();
        }

        foreach (var lote in SeparadorGo().Split(contenido).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            await using var comando = new SqlCommand(lote, conexion) { CommandTimeout = 180 };
            await comando.ExecuteNonQueryAsync();
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    [GeneratedRegex(@"\bIAAutos\b")]
    private static partial Regex NombreDeBase();
}
