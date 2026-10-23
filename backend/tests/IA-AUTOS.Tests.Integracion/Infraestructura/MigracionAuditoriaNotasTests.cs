using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta los scripts reales de migracion (database/cambios-esquema/18-auditoria-completa.sql y 19-notas-aclaratorias.sql) contra un SQL Server 2019 con el esquema PREVIO
/// (Esquemas/ddl-previo-a-auditoria.sql, el que ya esta desplegado) y con datos: la auditoria gana IP, dispositivo, detalle, viaje y cliente y deja de modificarse, y las notas aclaratorias
/// se crean como registros que no se editan ni se borran. No cambia ningun dato.
/// </summary>
public sealed partial class MigracionAuditoriaNotasTests : IAsyncLifetime
{
    private const string USUARIO = "33333333-3333-3333-3333-333333333333";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionAuditoria");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionAuditoria" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-auditoria.sql"));
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

    private static async Task<string> ScriptAsync(string nombre) =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", nombre))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un usuario y dos registros de auditoria de antes de la migracion.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblAuditLog (Id, UsuarioId, Accion, Entidad, EntidadId) VALUES (NEWID(), '{{USUARIO}}', N'inspeccion.crear', N'inspecciones', NEWID());
        INSERT dbo.tblAuditLog (Id, UsuarioId, Accion) VALUES (NEWID(), NULL, N'auth.login');
        """;

    private static string Auditoria(string columnas = "", string valores = "") =>
        $"INSERT dbo.tblAuditLog (Id, UsuarioId, Accion{columnas}) VALUES (NEWID(), '{USUARIO}', N'x.y'{valores})";

    // ----------------------------------------------------------------------------------- auditoria (18)

    [Fact]
    public async Task La_migracion_agrega_las_columnas_los_indices_y_el_trigger_sin_cambiar_ningun_dato()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAuditLog') AND name IN ('Ip', 'Dispositivo', 'Detalle', 'ViajeId', 'ClienteId')"));

        await EjecutarAsync(_cadena, await ScriptAsync("18-auditoria-completa.sql"));

        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAuditLog') AND name IN ('Ip', 'Dispositivo', 'Detalle', 'ViajeId', 'ClienteId') AND is_nullable = 1"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.tblAuditLog') AND name IN ('IX_tblAuditLog_Entidad', 'IX_tblAuditLog_Viaje', 'IX_tblAuditLog_Cliente', 'IX_tblAuditLog_CreatedAt')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name = 'TR_tblAuditLog_NoModificar'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Ip IS NULL AND Dispositivo IS NULL AND Detalle IS NULL AND ViajeId IS NULL AND ClienteId IS NULL"));

        // Repetir el script no cambia nada.
        await EjecutarAsync(_cadena, await ScriptAsync("18-auditoria-completa.sql"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog"));
    }

    [Fact]
    public async Task La_auditoria_guarda_ip_dispositivo_detalle_json_viaje_y_cliente_y_no_se_modifica_ni_se_borra()
    {
        await EjecutarAsync(_cadena, await ScriptAsync("18-auditoria-completa.sql"));

        Assert.Equal(0, await ErrorDeAsync(Auditoria(
            ", Ip, Dispositivo, Detalle, ViajeId, ClienteId",
            ", '190.1.2.3', N'Mozilla/5.0', N'{\"antes\":{\"estado\":\"borrador\"},\"despues\":{\"estado\":\"cerrada\"}}', NEWID(), NEWID()")));
        Assert.Equal(0, await ErrorDeAsync(Auditoria()));
        Assert.Equal(547, await ErrorDeAsync(Auditoria(", Detalle", ", N'no es json'")));
        Assert.Equal(51005, await ErrorDeAsync("UPDATE dbo.tblAuditLog SET Accion = N'otra'"));
        Assert.Equal(51005, await ErrorDeAsync("UPDATE dbo.tblAuditLog SET Ip = '1.1.1.1' WHERE Accion = N'x.y'"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblAuditLog"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = N'otra'"));
    }

    // ------------------------------------------------------------------------------ notas aclaratorias (19)

    [Fact]
    public async Task La_migracion_crea_las_notas_aclaratorias_con_sus_restricciones_y_triggers()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblNotasAclaratorias'"));

        await EjecutarAsync(_cadena, await ScriptAsync("19-notas-aclaratorias.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblNotasAclaratorias'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name IN ('TR_tblNotasAclaratorias_NoModificar', 'TR_tblNotasAclaratorias_NoBorrar')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_tblNotasAclaratorias_Entidad'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotasAclaratorias"));
        await EjecutarAsync(_cadena, await ScriptAsync("19-notas-aclaratorias.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblNotasAclaratorias'"));
    }

    [Fact]
    public async Task Una_nota_aclaratoria_es_de_una_inspeccion_un_hallazgo_o_una_transferencia_y_no_se_edita_ni_se_borra()
    {
        await EjecutarAsync(_cadena, await ScriptAsync("19-notas-aclaratorias.sql"));
        string Nota(string entidad, string texto = "N'Aclaracion'", string autor = USUARIO) =>
            $"INSERT dbo.tblNotasAclaratorias (Id, Entidad, EntidadId, AutorId, Texto) VALUES (NEWID(), '{entidad}', NEWID(), '{autor}', {texto})";

        foreach (var entidad in new[] { "inspeccion", "hallazgo", "transferencia" })
        {
            Assert.Equal(0, await ErrorDeAsync(Nota(entidad)));
        }

        Assert.Equal(547, await ErrorDeAsync(Nota("vehiculo")));
        Assert.Equal(547, await ErrorDeAsync(Nota("hallazgo", "N''")));
        Assert.Equal(547, await ErrorDeAsync(Nota("hallazgo", autor: "99999999-9999-9999-9999-999999999999")));
        Assert.Equal(51005, await ErrorDeAsync("UPDATE dbo.tblNotasAclaratorias SET Texto = N'Otra'"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblNotasAclaratorias"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotasAclaratorias WHERE CreatedAt IS NOT NULL AND Texto = N'Aclaracion'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(_cadena, await ScriptAsync("18-auditoria-completa.sql"));
        await EjecutarAsync(_cadena, await ScriptAsync("19-notas-aclaratorias.sql"));
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblAuditLog') AND name IN ('Ip', 'Dispositivo', 'Detalle', 'ViajeId', 'ClienteId')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name = 'TR_tblAuditLog_NoModificar'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblNotasAclaratorias'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name IN ('TR_tblNotasAclaratorias_NoModificar', 'TR_tblNotasAclaratorias_NoBorrar')"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog"));
    }
}
