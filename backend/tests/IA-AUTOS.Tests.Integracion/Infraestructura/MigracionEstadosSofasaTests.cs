using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/22-catalogo-estados-sofasa.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-estados-sofasa.sql, el que ya
/// esta desplegado) y con ordenes: crea <c>tblEstadosSofasa</c> y la llena con los StatusCode distintos de las ordenes (sin distinguir mayusculas ni espacios de los bordes), con el codigo como
/// etiqueta provisional, sin tocar ninguna orden; repetirlo solo agrega los que falten y nunca cambia una etiqueta.
/// </summary>
public sealed partial class MigracionEstadosSofasaTests : IAsyncLifetime
{
    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionEstadosSofasa");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionEstadosSofasa" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-estados-sofasa.sql"));
    }

    public async Task DisposeAsync() => await _contenedor.DisposeAsync();

    private static async Task EjecutarAsync(string cadena, string sql)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        await using var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion);
        await preparar.ExecuteNonQueryAsync();
        await using var principal = new SqlCommand(sql, conexion) { CommandTimeout = 120 };
        await principal.ExecuteNonQueryAsync();
    }

    private static async Task EjecutarScriptConGoAsync(string cadena, string ruta)
    {
        var contenido = await File.ReadAllTextAsync(ruta);
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
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

    private async Task<string?> TextoAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? null : Convert.ToString(valor, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "22-catalogo-estados-sofasa.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    private const string ORDENES = """
        INSERT dbo.tblOrdenesSofasa (Id, SourceId, StatusCode) VALUES
            (NEWID(), 1, N'ENTREGADA'), (NEWID(), 2, N'entregada '), (NEWID(), 3, N'EN_RUTA'), (NEWID(), 4, N'  EN_RUTA'), (NEWID(), 5, N'Cargada'),
            (NEWID(), 6, NULL), (NEWID(), 7, N'   '), (NEWID(), 8, N'');
        """;

    private const string ESTADOS = "SELECT STRING_AGG(CONCAT(Codigo, '=', Nombre, '#', Orden), ';') WITHIN GROUP (ORDER BY Orden) FROM dbo.tblEstadosSofasa";

    [Fact]
    public async Task La_migracion_crea_la_tabla_y_sin_ordenes_el_catalogo_queda_vacio()
    {
        Assert.Equal("0", await TextoAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblEstadosSofasa'"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal("1", await TextoAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblEstadosSofasa'"));
        Assert.Equal("0", await TextoAsync("SELECT COUNT(*) FROM dbo.tblEstadosSofasa"));
        Assert.Equal("1", await TextoAsync("SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'UQ_tblEstadosSofasa_Codigo'"));
        Assert.Equal("3", await TextoAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.tblEstadosSofasa')"));
    }

    [Fact]
    public async Task Carga_los_statuscode_distintos_de_las_ordenes_sin_distinguir_mayusculas_ni_espacios_con_el_codigo_como_etiqueta_y_sin_tocar_las_ordenes()
    {
        await EjecutarAsync(_cadena, ORDENES);
        var ordenes = await TextoAsync("SELECT STRING_AGG(CONCAT(SourceId, ':', ISNULL(StatusCode, '~')), '|') WITHIN GROUP (ORDER BY SourceId) FROM dbo.tblOrdenesSofasa");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Tres estados: ENTREGADA (la forma «entregada » se junta con ella), EN_RUTA y Cargada, en orden alfabetico; las ordenes sin estado o con estado vacio no cuentan.
        Assert.Equal("3", await TextoAsync("SELECT COUNT(*) FROM dbo.tblEstadosSofasa"));
        var estados = (await TextoAsync(ESTADOS))!.Split(';');
        Assert.Equal(3, estados.Length);
        Assert.Contains(estados, e => e.EndsWith("#1", StringComparison.Ordinal));
        Assert.Equal("3", await TextoAsync("SELECT COUNT(*) FROM dbo.tblEstadosSofasa WHERE Codigo = Nombre AND Activo = 1 AND Codigo = LTRIM(RTRIM(Codigo))"));
        Assert.Equal("3", await TextoAsync("SELECT COUNT(DISTINCT Orden) FROM dbo.tblEstadosSofasa"));
        Assert.Equal(ordenes, await TextoAsync("SELECT STRING_AGG(CONCAT(SourceId, ':', ISNULL(StatusCode, '~')), '|') WITHIN GROUP (ORDER BY SourceId) FROM dbo.tblOrdenesSofasa"));
    }

    [Fact]
    public async Task Ninguna_orden_queda_con_un_statuscode_fuera_del_catalogo()
    {
        await EjecutarAsync(_cadena, ORDENES);

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(
            "0",
            await TextoAsync(
                "SELECT COUNT(*) FROM dbo.tblOrdenesSofasa o WHERE LEN(LTRIM(RTRIM(ISNULL(o.StatusCode, '')))) > 0 AND NOT EXISTS (SELECT 1 FROM dbo.tblEstadosSofasa e WHERE e.Codigo = LTRIM(RTRIM(o.StatusCode)))"));
    }

    [Fact]
    public async Task Repetir_la_migracion_solo_agrega_los_estados_que_faltan_y_nunca_cambia_una_etiqueta()
    {
        await EjecutarAsync(_cadena, ORDENES);
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, "UPDATE dbo.tblEstadosSofasa SET Nombre = N'Entregada al cliente', Activo = 0 WHERE Codigo = N'ENTREGADA'");
        await EjecutarAsync(_cadena, "INSERT dbo.tblOrdenesSofasa (Id, SourceId, StatusCode) VALUES (NEWID(), 9, N'DEVUELTA')");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal("4", await TextoAsync("SELECT COUNT(*) FROM dbo.tblEstadosSofasa"));
        Assert.Equal("Entregada al cliente", await TextoAsync("SELECT Nombre FROM dbo.tblEstadosSofasa WHERE Codigo = N'ENTREGADA'"));
        Assert.Equal("0", await TextoAsync("SELECT CAST(Activo AS int) FROM dbo.tblEstadosSofasa WHERE Codigo = N'ENTREGADA'"));
        Assert.Equal("DEVUELTA", await TextoAsync("SELECT Nombre FROM dbo.tblEstadosSofasa WHERE Codigo = N'DEVUELTA'"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal("4", await TextoAsync("SELECT COUNT(*) FROM dbo.tblEstadosSofasa"));
    }
}
