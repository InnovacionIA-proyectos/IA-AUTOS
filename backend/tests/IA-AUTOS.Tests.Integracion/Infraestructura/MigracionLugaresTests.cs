using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/24-lugares-y-programacion.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-lugares.sql, el que ya esta
/// desplegado) y con un viaje: crea el catalogo de lugares (sin sembrar ninguno), agrega la fecha programada y el lugar de cargue al viaje y el destino a cada unidad (todo nulo, sin migrar nada) y
/// cambia la adopcion para que cuente el viaje en su dia programado y, sin fecha programada, en el de su creacion.
/// </summary>
public sealed partial class MigracionLugaresTests : IAsyncLifetime
{
    private const string VIAJE = "11111111-1111-1111-1111-111111111111";
    private const string VEHICULO = "22222222-2222-2222-2222-222222222222";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionLugares");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionLugares" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-lugares.sql"));
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

    private async Task<string?> TextoAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? null : Convert.ToString(valor, System.Globalization.CultureInfo.InvariantCulture);
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "24-lugares-y-programacion.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un viaje (creado el 2026-11-01 a las 20:00, hora de Colombia) con una unidad: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINLUGARES0000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Trailer, CreatedAt) VALUES ('{{VIAJE}}', 'M-4501', N'ABC123', N'TR-LUG', '2026-11-01T20:00:00-05:00');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}');
        """;

    private const string DIA_ADOPCION = "SELECT CONVERT(varchar(10), Fecha, 23) FROM dbo.vwAdopcion WHERE Trailer = N'TR-LUG'";

    private const string TIPO_CEDI = "(SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1')";

    private static string Lugar(string nombre, string ciudad = "N'Envigado'", string coordenadas = "NULL, NULL, NULL") =>
        $"INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad, Latitud, Longitud, RadioMetros) VALUES (NEWID(), {nombre}, {TIPO_CEDI}, {ciudad}, {coordenadas})";

    // ------------------------------------------------------------------------------------------------ migracion

    [Fact]
    public async Task La_migracion_crea_los_lugares_vacios_y_las_columnas_nulas_sin_cambiar_ningun_dato_y_se_puede_repetir()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblLugares'"));
        Assert.Equal("2026-11-01", await TextoAsync(DIA_ADOPCION));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblLugares'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajes') AND name IN ('FechaProgramada', 'LugarCargueId') AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajeVehiculos') AND name = 'LugarDestinoId' AND is_nullable = 1"));
        // Lo que ya existia queda igual: nada se migra y el viaje sigue contando en su dia de creacion.
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajes WHERE Id = '{VIAJE}' AND FechaProgramada IS NULL AND LugarCargueId IS NULL AND OrigenId IS NULL AND CodigoMtrack = 'M-4501'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = '{VIAJE}' AND LugarDestinoId IS NULL"));
        Assert.Equal("2026-11-01", await TextoAsync(DIA_ADOPCION));

        // Repetir el script no cambia nada (ya esta aplicado).
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblLugares'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwAdopcion'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblLugares'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblViajes_LugarCargue'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblViajeVehiculos_LugarDestino'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.sql_modules WHERE object_id = OBJECT_ID('dbo.vwAdopcion') AND definition LIKE '%FechaProgramada%'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_sobre_el_esquema_previo_sin_migrar_agrega_lo_nuevo_sin_fallar()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblLugares'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajes') AND name IN ('FechaProgramada', 'LugarCargueId')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajeVehiculos') AND name = 'LugarDestinoId'"));
        Assert.Equal("2026-11-01", await TextoAsync(DIA_ADOPCION));
    }

    // ------------------------------------------------------------------------------------------------ lo nuevo funciona

    [Fact]
    public async Task La_adopcion_cuenta_el_viaje_en_su_dia_programado_y_un_lugar_exige_nombre_tipo_ciudad_y_coordenadas_completas()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(Lugar("N'CEDI Envigado'")));
        Assert.Equal(0, await ErrorDeAsync(Lugar("N'Patio Norte'", "N'Medellin'", "6.2, -75.5, 150")));
        Assert.Equal(2627, await ErrorDeAsync(Lugar("N'cedi envigado'")));
        Assert.Equal(547, await ErrorDeAsync(Lugar("N'  '")));
        Assert.Equal(547, await ErrorDeAsync(Lugar("N'Sin ciudad'", "N' '")));
        Assert.Equal(547, await ErrorDeAsync(Lugar("N'Solo latitud'", coordenadas: "6, NULL, NULL")));
        Assert.Equal(547, await ErrorDeAsync(Lugar("N'Radio cero'", coordenadas: "6, -75, 0")));
        Assert.Equal(547, await ErrorDeAsync(Lugar("N'Fuera de rango'", coordenadas: "91, -75, NULL")));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES (NEWID(), N'Tipo inexistente', NEWID(), N'Envigado')"));

        // El viaje se programa para el 2026-11-03 a las 06:00: la adopcion lo cuenta ese dia y ya no el de su creacion.
        await EjecutarAsync(_cadena, $"UPDATE dbo.tblViajes SET FechaProgramada = '2026-11-03T06:00:00-05:00', LugarCargueId = (SELECT Id FROM dbo.tblLugares WHERE Nombre = N'CEDI Envigado') WHERE Id = '{VIAJE}'");
        await EjecutarAsync(_cadena, $"UPDATE dbo.tblViajeVehiculos SET LugarDestinoId = (SELECT Id FROM dbo.tblLugares WHERE Nombre = N'Patio Norte') WHERE ViajeId = '{VIAJE}'");

        Assert.Equal("2026-11-03", await TextoAsync(DIA_ADOPCION));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAdopcion WHERE Trailer = N'TR-LUG' AND Programados = 1"));
        Assert.Equal(547, await ErrorDeAsync("DELETE FROM dbo.tblLugares WHERE Nombre = N'CEDI Envigado'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblViajes SET LugarCargueId = NEWID() WHERE Id = '{VIAJE}'"));
    }
}
