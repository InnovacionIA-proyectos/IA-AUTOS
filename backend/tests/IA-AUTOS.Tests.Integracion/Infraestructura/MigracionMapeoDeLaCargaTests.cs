using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/45-mapeo-de-columnas-de-la-carga-de-viajes.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-mapeo-de-la-carga.sql, el que ya esta
/// desplegado): nace la tabla del mapeo de columnas, unica por cliente y forma del archivo, que solo admite JSON y apunta a un cliente y a un usuario que existen; el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionMapeoDeLaCargaTests : IAsyncLifetime
{
    private const string USUARIO = "77777777-7777-7777-7777-777777777777";
    private static readonly string HUELLA_A = new('a', 64);
    private static readonly string HUELLA_B = new('b', 64);

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionMapeo");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-mapeo-de-la-carga.sql"));
        await EjecutarAsync($"INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{USUARIO}', N'Diana', 'diana@e.com', 'coordinador_calidad', (SELECT TOP 1 Id FROM dbo.tblClientes), 'h');");
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    private static string Mapeo(string cliente, string huella, string columnas = "[]", string campos = "[]") =>
        $"INSERT dbo.tblMapeosCargaViajes (Id, ClienteId, Huella, Columnas, Campos, ActualizadoPor) VALUES (NEWID(), {cliente}, '{huella}', N'{columnas}', N'{campos}', '{USUARIO}')";

    private const string CLIENTE = "(SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa')";

    private async Task EjecutarAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion);
        await preparar.ExecuteNonQueryAsync();
        await using var principal = new SqlCommand(sql, conexion) { CommandTimeout = 120 };
        await principal.ExecuteNonQueryAsync();
    }

    private async Task EjecutarScriptConGoAsync(string ruta)
    {
        foreach (var lote in SeparadorGo().Split(await File.ReadAllTextAsync(ruta)).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            await EjecutarAsync(lote);
        }
    }

    private async Task<int> ContarAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        return Convert.ToInt32(await comando.ExecuteScalarAsync());
    }

    /// <summary>El numero de error de SQL Server que devuelve la sentencia (0 si se ejecuta sin error).</summary>
    private async Task<int> ErrorDeAsync(string sql)
    {
        try
        {
            await EjecutarAsync(sql);
            return 0;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "45-mapeo-de-columnas-de-la-carga-de-viajes.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_no_existe_la_tabla_del_mapeo()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblMapeosCargaViajes'"));
    }

    [Fact]
    public async Task Migrar_crea_la_tabla_vacia_con_su_llave_unica_y_su_regla_de_json()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblMapeosCargaViajes'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMapeosCargaViajes"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.key_constraints WHERE name = 'UQ_tblMapeosCargaViajes_Cliente_Huella'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblMapeosCargaViajes_Json'"));
    }

    [Fact]
    public async Task Hay_un_mapeo_por_cliente_y_forma_pero_dos_formas_del_mismo_cliente_conviven()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES ('99999999-9999-9999-9999-999999999999', 'otro', N'Otro', 9)");

        Assert.Equal(0, await ErrorDeAsync(Mapeo(CLIENTE, HUELLA_A)));
        Assert.Equal(2627, await ErrorDeAsync(Mapeo(CLIENTE, HUELLA_A)));
        Assert.Equal(0, await ErrorDeAsync(Mapeo(CLIENTE, HUELLA_B)));
        Assert.Equal(0, await ErrorDeAsync(Mapeo("'99999999-9999-9999-9999-999999999999'", HUELLA_A)));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMapeosCargaViajes"));
    }

    [Fact]
    public async Task Solo_admite_json_y_un_cliente_y_un_usuario_que_existen()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(547, await ErrorDeAsync(Mapeo(CLIENTE, HUELLA_A, columnas: "esto no es json")));
        Assert.Equal(547, await ErrorDeAsync(Mapeo(CLIENTE, HUELLA_A, campos: "{roto")));
        Assert.Equal(547, await ErrorDeAsync(Mapeo("NEWID()", HUELLA_A)));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblMapeosCargaViajes (Id, ClienteId, Huella, Columnas, Campos, ActualizadoPor) VALUES (NEWID(), {CLIENTE}, '{HUELLA_A}', N'[]', N'[]', NEWID())"));
        Assert.Equal(0, await ErrorDeAsync(Mapeo(CLIENTE, HUELLA_A, "[{\"encabezado\":\"vin\",\"ocurrencia\":1}]", "[\"vin\"]")));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblMapeosCargaViajes'"));
        await EjecutarAsync(Mapeo(CLIENTE, HUELLA_A));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMapeosCargaViajes"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblMapeosCargaViajes'"));
    }
}
