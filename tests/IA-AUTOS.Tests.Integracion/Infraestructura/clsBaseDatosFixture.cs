using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Levanta un SQL Server 2019 real en un contenedor y aplica los scripts del proyecto
/// (database/00-crear-base.sql y database/ddl.sql). Se comparte entre todas las pruebas de la coleccion.
/// </summary>
public sealed partial class clsBaseDatosFixture : IAsyncLifetime
{
    private const string IMAGEN = "mcr.microsoft.com/mssql/server:2019-latest";
    private const string BASE = "IAAutos";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder(IMAGEN).Build();

    /// <summary>Cadena de conexion a la base IAAutos del contenedor.</summary>
    public string CadenaConexion { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();

        var maestra = _contenedor.GetConnectionString();
        await EjecutarScriptAsync(maestra, "00-crear-base.sql");

        var constructor = new SqlConnectionStringBuilder(maestra) { InitialCatalog = BASE };
        CadenaConexion = constructor.ConnectionString;
        await EjecutarScriptAsync(CadenaConexion, "ddl.sql");
    }

    public async Task DisposeAsync() => await _contenedor.DisposeAsync();

    /// <summary>Borra todos los datos de negocio (conserva el catalogo de puntos de custodia).</summary>
    public async Task LimpiarAsync()
    {
        const string SQL = """
            DELETE FROM dbo.tblAuditLog;
            DELETE FROM dbo.tblOrdenesSofasa;
            DELETE FROM dbo.tblTransferenciasCustodia;
            DELETE FROM dbo.tblEvidencias;
            DELETE FROM dbo.tblHallazgos;
            DELETE FROM dbo.tblInspecciones;
            DELETE FROM dbo.tblAsignaciones;
            DELETE FROM dbo.tblEtapas;
            DELETE FROM dbo.tblVehiculos;
            DELETE FROM dbo.tblUsuarios;
            """;
        await using var conexion = new SqlConnection(CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(SQL, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task EjecutarScriptAsync(string cadena, string archivo)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "database", archivo);
        var contenido = await File.ReadAllTextAsync(ruta);
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();

        // sqlcmd usa -I (QUOTED_IDENTIFIER ON); aqui se fija explicitamente para vistas e indices filtrados.
        await using (var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion))
        {
            await preparar.ExecuteNonQueryAsync();
        }

        foreach (var lote in SeparadorGo().Split(contenido).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            await using var comando = new SqlCommand(lote, conexion) { CommandTimeout = 120 };
            await comando.ExecuteNonQueryAsync();
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();
}

[CollectionDefinition(NOMBRE)]
public sealed class clsColeccionBaseDatos : ICollectionFixture<clsBaseDatosFixture>
{
    public const string NOMBRE = "BaseDatos";
}
