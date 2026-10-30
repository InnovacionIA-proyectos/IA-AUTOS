using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/29-posicion-de-la-unidad.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-posicion.sql, el que ya esta desplegado) y
/// con un viaje de tres unidades (una de ellas que no sale): la columna Posicion nace nula en todas, la base garantiza el rango de 1 a 7 y que dos unidades del mismo viaje no compartan posicion (la misma
/// posicion en viajes distintos es valida) y el script se puede repetir.
/// </summary>
public sealed partial class MigracionPosicionTests : IAsyncLifetime
{
    private const string VIAJE = "11111111-1111-1111-1111-111111111111";
    private const string OTRO_VIAJE = "11111111-1111-1111-1111-222222222222";
    private const string VEHICULO_1 = "22222222-2222-2222-2222-222222222221";
    private const string VEHICULO_2 = "22222222-2222-2222-2222-222222222222";
    private const string VEHICULO_3 = "22222222-2222-2222-2222-222222222223";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionPosicion");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionPosicion" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-posicion.sql"));
        await EjecutarAsync(_cadena, DATOS_PREVIOS);
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
            await EjecutarAsync(_cadena, sql);
            return 0;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "29-posicion-de-la-unidad.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un viaje con tres unidades (la tercera, de otro VIN, tambien va en otro viaje) y otro viaje con una unidad: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO_1}}', 'VINPOSICION000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO_2}}', 'VINPOSICION000002', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO_3}}', 'VINPOSICION000003', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES ('{{VIAJE}}', 'M-48213', N'ABC123');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES ('{{OTRO_VIAJE}}', 'M-48214', N'DEF456');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO_1}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO_2}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO_3}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{OTRO_VIAJE}}', '{{VEHICULO_1}}');
        """;

    private static string Posicion(string viaje, string vehiculo, string? posicion) =>
        $"UPDATE dbo.tblViajeVehiculos SET Posicion = {posicion ?? "NULL"} WHERE ViajeId = '{viaje}' AND VehiculoId = '{vehiculo}'";

    [Fact]
    public async Task La_migracion_agrega_la_posicion_nula_a_todas_las_unidades_sin_cambiar_nada_y_se_puede_repetir()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajeVehiculos') AND name = 'Posicion'"));

        // CA-1: la columna, su restriccion y el indice, en un guion que corre dos veces seguidas sin error.
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('dbo.tblViajeVehiculos') AND c.name = 'Posicion' AND c.is_nullable = 1 AND t.name = 'tinyint'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblViajeVehiculos_Posicion'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblViajeVehiculos_Posicion' AND is_unique = 1 AND has_filter = 1"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE Posicion IS NULL"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE Posicion IS NOT NULL"));
        Assert.Equal(3, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = '{VIAJE}'"));
    }

    [Fact]
    public async Task La_base_rechaza_las_posiciones_fuera_de_rango_o_repetidas_dentro_del_viaje_y_acepta_la_misma_en_viajes_distintos()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // CA-3: la 0, la 8 o una repetida dentro del mismo viaje fallan; la misma en otro viaje es valida.
        Assert.Equal(547, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_1, "0")));
        Assert.Equal(547, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_1, "8")));
        Assert.Equal(0, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_1, "4")));
        Assert.Equal(2601, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_2, "4")));
        Assert.Equal(0, await ErrorDeAsync(Posicion(OTRO_VIAJE, VEHICULO_1, "4")));
        Assert.Equal(0, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_2, "3")));
        Assert.Equal(0, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_3, "7")));
        // Las siete posiciones caben y la octava no existe; las unidades sin posicion no cuentan para la unicidad.
        Assert.Equal(0, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_3, null)));
        Assert.Equal(0, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_2, null)));
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = '{VIAJE}' AND Posicion IS NULL"));
        // Liberada la 4, otra unidad puede tomarla.
        Assert.Equal(0, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_1, null)));
        Assert.Equal(0, await ErrorDeAsync(Posicion(VIAJE, VEHICULO_2, "4")));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajeVehiculos') AND name = 'Posicion'"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblViajeVehiculos_Posicion'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblViajeVehiculos_Posicion'"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
    }
}
