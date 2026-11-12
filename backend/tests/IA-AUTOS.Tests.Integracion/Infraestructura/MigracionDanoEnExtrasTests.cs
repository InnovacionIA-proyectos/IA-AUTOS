using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/37-dano-en-los-extras.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-dano-en-extras.sql, el que ya esta desplegado) y
/// con datos: el hallazgo gana ExtraId (el extra del que nace el dano), una llave foranea, la restriccion de que el dano de un extra no tiene zona y es del inspector, y un indice unico (un extra tiene a lo sumo
/// un hallazgo); los extras y hallazgos que ya existian se conservan sin dano; el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionDanoEnExtrasTests : IAsyncLifetime
{
    private const string USUARIO = "11111111-1111-1111-1111-111111111111";
    private const string INSPECCION = "33333333-3333-3333-3333-333333333333";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string VEHICULO = "66666666-6666-6666-6666-666666666666";
    private const string ETAPA = "77777777-7777-7777-7777-777777777777";
    private const string EXTRA = "88888888-8888-8888-8888-888888888888";
    private const string EXTRA_2 = "99999999-9999-9999-9999-999999999999";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionDanoEnExtras");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-dano-en-extras.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Una inspeccion en borrador con un extra reportado antes del cambio (sin dano) y un hallazgo de siempre en una zona: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINEXTRADANO000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado) VALUES ('{{VIAJE}}', 'MTK-37', N'ABC123', 'en_curso');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION}}', '{{ETAPA}}', '{{VIAJE}}', '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspeccionExtras (Id, InspeccionId, Tipo, Observacion) VALUES ('{{EXTRA}}', '{{INSPECCION}}', 'platon', N'reportado antes');
        INSERT dbo.tblInspeccionExtras (Id, InspeccionId, Tipo) VALUES ('{{EXTRA_2}}', '{{INSPECCION}}', 'volco');
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano, NivelSeveridad) VALUES (NEWID(), '{{INSPECCION}}', N'Capot', 'golpe', '1_5cm', 'v1');
        """;

    private static string HallazgoDeExtra(string extra, string columnasExtra = "", string valoresExtra = "") =>
        $"INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano, NivelSeveridad, ExtraId{columnasExtra}) VALUES (NEWID(), '{INSPECCION}', N'Platon', 'golpe', '5_10cm', 'v3', '{extra}'{valoresExtra})";

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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "37-dano-en-los-extras.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_un_hallazgo_no_tiene_extra()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblHallazgos') AND name = 'ExtraId'"));
        Assert.Equal(207, await ErrorDeAsync(HallazgoDeExtra(EXTRA)));
    }

    [Fact]
    public async Task Migrar_agrega_la_columna_la_llave_la_restriccion_y_el_indice_sin_cambiar_los_extras_ni_los_hallazgos_de_antes()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblHallazgos') AND name = 'ExtraId' AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblHallazgos_Extra' AND parent_object_id = OBJECT_ID('dbo.tblHallazgos')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblHallazgos_Extra' AND parent_object_id = OBJECT_ID('dbo.tblHallazgos')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UQ_tblHallazgos_Extra' AND is_unique = 1 AND has_filter = 1"));
        // Los extras de antes siguen validos y sin dano; el hallazgo de siempre no tiene extra.
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE ExtraId IS NULL"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE ExtraId IS NOT NULL"));
    }

    [Fact]
    public async Task El_dano_de_un_extra_no_tiene_zona_es_del_inspector_y_un_extra_tiene_a_lo_sumo_uno()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Un extra que no existe o de origen conductor no pasan (y la restriccion exige que no tenga zona). Dos danos para el mismo extra tampoco.
        Assert.Equal(547, await ErrorDeAsync(HallazgoDeExtra(Guid.NewGuid().ToString())));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblHallazgos_Extra' AND definition LIKE '%ZonaId%IS NULL%'"));
        Assert.Equal(547, await ErrorDeAsync(HallazgoDeExtra(EXTRA, ", Origen", ", 'conductor'")));

        Assert.Equal(0, await ErrorDeAsync(HallazgoDeExtra(EXTRA)));
        Assert.Equal(2601, await ErrorDeAsync(HallazgoDeExtra(EXTRA)));
        Assert.Equal(0, await ErrorDeAsync(HallazgoDeExtra(EXTRA_2)));
        // Con dano guardado el extra no se borra (la llave foranea lo impide).
        Assert.Equal(547, await ErrorDeAsync($"DELETE FROM dbo.tblInspeccionExtras WHERE Id = '{EXTRA}'"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblHallazgos_Extra'"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblHallazgos_Extra'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblHallazgos_Extra'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UQ_tblHallazgos_Extra'"));
        Assert.Equal(0, await ErrorDeAsync(HallazgoDeExtra(EXTRA)));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras"));
    }
}
