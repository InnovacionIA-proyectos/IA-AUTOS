using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/13-acciones-niveles-severidad.sql) contra un SQL Server 2019 con el esquema PREVIO
/// (Esquemas/ddl-previo-a-acciones.sql, donde los niveles no tienen accion): se completan las cuatro acciones, no se pisa una accion ya editada y
/// repetir el script o ejecutar el ddl nuevo no cambia nada.
/// </summary>
public sealed partial class MigracionAccionesNivelesTests : IAsyncLifetime
{
    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionAcciones");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionAcciones" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-acciones.sql"));
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

    private async Task<string?> AccionAsync(string codigo)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand("SELECT Accion FROM dbo.tblNivelesSeveridad WHERE Codigo = @codigo", conexion);
        comando.Parameters.AddWithValue("@codigo", codigo);
        var resultado = await comando.ExecuteScalarAsync();
        return resultado is null or DBNull ? null : (string)resultado;
    }

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "13-acciones-niveles-severidad.sql")))
            .Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    [Fact]
    public async Task La_migracion_completa_las_cuatro_acciones_y_repetirla_no_cambia_nada()
    {
        Assert.Null(await AccionAsync("v1_plus"));
        Assert.Null(await AccionAsync("v3"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal("Se recupera antes del despacho", await AccionAsync("v1_plus"));
        Assert.Equal("Se rechaza el vehículo", await AccionAsync("v1"));
        Assert.Equal("Validación por Sofasa", await AccionAsync("v2"));
        Assert.Equal("Se deroga (se acepta el defecto)", await AccionAsync("v3"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal("Se rechaza el vehículo", await AccionAsync("v1"));
    }

    [Fact]
    public async Task La_migracion_no_pisa_una_accion_que_ya_se_edito()
    {
        await EjecutarAsync(_cadena, "UPDATE dbo.tblNivelesSeveridad SET Accion = N'Texto propio' WHERE Codigo = 'v2'");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal("Texto propio", await AccionAsync("v2"));
        Assert.Equal("Se deroga (se acepta el defecto)", await AccionAsync("v3"));
    }

    [Fact]
    public async Task El_ddl_nuevo_completa_las_acciones_en_una_base_anterior_y_en_una_ya_migrada_no_cambia_nada()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal("Se recupera antes del despacho", await AccionAsync("v1_plus"));
        Assert.Equal("Validación por Sofasa", await AccionAsync("v2"));

        await EjecutarAsync(_cadena, "UPDATE dbo.tblNivelesSeveridad SET Accion = N'Texto propio' WHERE Codigo = 'v1'");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal("Texto propio", await AccionAsync("v1"));
    }
}
