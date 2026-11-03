using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/28-sin-personal-de-apoyo.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-sin-personal-de-apoyo.sql, el que ya esta
/// desplegado) y con un cargue cuya etapa tenia la marca de personal de apoyo encendida: la columna y su restriccion por defecto desaparecen, el resto de la etapa (y de la base) queda igual, el script se puede
/// repetir y el catalogo de motivos de la excepcion sigue intacto.
/// </summary>
public sealed partial class MigracionSinPersonalDeApoyoTests : IAsyncLifetime
{
    private const string VIAJE = "11111111-1111-1111-1111-111111111111";
    private const string VEHICULO = "22222222-2222-2222-2222-222222222222";
    private const string ETAPA_CARGUE = "44444444-4444-4444-4444-444444444441";
    private const string ETAPA_TRANSPORTE = "44444444-4444-4444-4444-444444444442";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionSinApoyo");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-sin-personal-de-apoyo.sql"));
        await EjecutarAsync(_cadena, DATOS_PREVIOS);
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "28-sin-personal-de-apoyo.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un viaje con una unidad y su Cargue con la marca de personal de apoyo encendida y su Transporte: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINSINAPOYO000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES ('{{VIAJE}}', 'M-7001', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden, ConPersonalApoyo) VALUES ('{{ETAPA_CARGUE}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1, 1);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA_TRANSPORTE}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte'), 2);
        """;

    [Fact]
    public async Task La_migracion_elimina_la_marca_y_su_restriccion_sin_cambiar_el_resto_de_la_etapa_y_se_puede_repetir()
    {
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblEtapas') AND name = 'ConPersonalApoyo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_tblEtapas_ConPersonalApoyo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas WHERE ConPersonalApoyo = 1"));

        // CA-1: elimina la columna y su constraint; corre dos veces seguidas sin error.
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblEtapas') AND name = 'ConPersonalApoyo'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.default_constraints WHERE name = 'DF_tblEtapas_ConPersonalApoyo'"));
        // Las etapas siguen ahi, con su orden, su viaje y su catalogo; nada mas cambio.
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblEtapas WHERE ViajeId = '{VIAJE}' AND VehiculoId = '{VEHICULO}'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblEtapas WHERE Id = '{ETAPA_CARGUE}' AND Orden = 1"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajes WHERE Id = '{VIAJE}' AND CodigoMtrack = 'M-7001'"));
        // Una etapa nueva se crea sin la marca.
        Assert.Equal(0, await ErrorDeAsync($"INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (NEWID(), '{VEHICULO}', '{VIAJE}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue'), 3)"));
        Assert.Equal(207, await ErrorDeAsync($"UPDATE dbo.tblEtapas SET ConPersonalApoyo = 1 WHERE Id = '{ETAPA_CARGUE}'"));
    }

    [Fact]
    public async Task El_catalogo_de_motivos_de_la_excepcion_queda_intacto_y_administrable()
    {
        var antes = await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosSinInspector WHERE Codigo IN ('inspector_no_llego', 'inspector_no_disponible', 'otro') AND Activo = 1");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(3, antes);
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosSinInspector WHERE Codigo IN ('inspector_no_llego', 'inspector_no_disponible', 'otro') AND Activo = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosSinInspector WHERE Codigo = 'otro' AND RequiereTexto = 1"));
        // Administrable: un motivo nuevo se agrega y se inactiva sin tocar el esquema.
        Assert.Equal(0, await ErrorDeAsync("INSERT dbo.tblMotivosSinInspector (Id, Codigo, Nombre, Orden) VALUES (NEWID(), 'sin_transporte_del_inspector', N'El inspector no tiene como llegar', 4)"));
        Assert.Equal(0, await ErrorDeAsync("UPDATE dbo.tblMotivosSinInspector SET Activo = 0 WHERE Codigo = 'sin_transporte_del_inspector'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblEtapas') AND name = 'ConPersonalApoyo'"));
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblEtapas WHERE ViajeId = '{VIAJE}'"));
    }
}
