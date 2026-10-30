using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/30-parametro-fotos-de-acomodacion.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-fotos-de-acomodacion.sql, el que ya
/// esta desplegado): agrega el parametro del minimo de fotos de la acomodacion con su valor inicial, no sobrescribe el valor que el negocio ya haya ajustado y el script se puede repetir.
/// </summary>
public sealed partial class MigracionFotosDeAcomodacionTests : IAsyncLifetime
{
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionAcomodacion");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-fotos-de-acomodacion.sql"));
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

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

    private async Task<int> ContarAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        return Convert.ToInt32(await comando.ExecuteScalarAsync());
    }

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "30-parametro-fotos-de-acomodacion.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    [Fact]
    public async Task La_migracion_agrega_el_parametro_con_su_valor_inicial_sin_tocar_los_demas_y_se_puede_repetir()
    {
        var antes = await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros");
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'acomodacion_fotos_minimas'"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'acomodacion_fotos_minimas' AND Tipo = 'numero' AND Valor = '1'"));
        Assert.Equal(antes + 1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros"));
    }

    [Fact]
    public async Task La_migracion_no_sobrescribe_el_valor_que_el_negocio_ya_ajusto()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, "UPDATE dbo.tblParametros SET Valor = N'3' WHERE Clave = 'acomodacion_fotos_minimas'");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'acomodacion_fotos_minimas' AND Valor = '3'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'acomodacion_fotos_minimas' AND Valor = '1'"));
        var total = await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'acomodacion_fotos_minimas'"));
        Assert.Equal(total, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros"));
    }
}
