using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/44-muelles-destino-cambiado-y-avisos-del-patio.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-muelles-y-avisos-del-patio.sql, el que ya
/// esta desplegado) y con un viaje: nace la tabla de muelles de cada lugar (el codigo es unico dentro del lugar, no se siembra ninguno), el viaje gana su muelle y la unidad la marca de cuando se cambio su destino (nulos
/// en lo que ya existia), la notificacion admite los dos tipos de aviso y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionMuellesYAvisosDelPatioTests : IAsyncLifetime
{
    private const string LUGAR_A = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string LUGAR_B = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionMuelles");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-muelles-y-avisos-del-patio.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Dos lugares y un viaje con una unidad: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @tipo uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (NEWID(), N'Diana', 'diana@e.com', 'coordinador_calidad', @cli, 'h');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{LUGAR_A}}', N'CEDI Envigado', @tipo, N'Envigado');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{LUGAR_B}}', N'Patio de Prueba', @tipo, N'Medellin');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('66666666-6666-6666-6666-666666666666', 'VINMUELLE0000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, LugarCargueId) VALUES ('{{VIAJE}}', 'MTK-44', N'ABC123', '{{LUGAR_A}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '66666666-6666-6666-6666-666666666666');
        """;

    private static string Muelle(string lugar, string codigo, string nombre) =>
        $"INSERT dbo.tblMuelles (Id, LugarId, Codigo, Nombre) VALUES (NEWID(), '{lugar}', '{codigo}', N'{nombre}')";

    private static string Notificacion(string clave, string tipo) =>
        $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), '{tipo}', 'media', (SELECT TOP 1 Id FROM dbo.tblUsuarios), N'T', N'M', 'bandeja', '{clave}')";

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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "44-muelles-destino-cambiado-y-avisos-del-patio.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_no_hay_muelles_ni_marca_de_destino_ni_los_tipos_de_aviso()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblMuelles'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE name IN ('MuelleId', 'LugarDestinoCambiadoAt')"));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k0", "cambio_destino_unidad")));
    }

    [Fact]
    public async Task Migrar_crea_la_tabla_y_las_columnas_nulas_admite_los_tipos_y_no_cambia_lo_que_existia()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblMuelles'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMuelles"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajes') AND name = 'MuelleId' AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajeVehiculos') AND name = 'LugarDestinoCambiadoAt' AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajes WHERE Id = '{VIAJE}' AND MuelleId IS NULL AND LugarCargueId = '{LUGAR_A}'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE LugarDestinoCambiadoAt IS NULL"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k1", "cambio_cargue_inspector")));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k2", "cambio_destino_unidad")));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k3", "firma_cargue_pendiente")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k4", "inventado")));
    }

    [Fact]
    public async Task El_codigo_del_muelle_es_unico_dentro_del_lugar_y_el_viaje_solo_apunta_a_un_muelle_que_existe()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(Muelle(LUGAR_A, "m1", "Muelle 1")));
        Assert.Equal(2627, await ErrorDeAsync(Muelle(LUGAR_A, "m1", "Otro nombre")));
        Assert.Equal(0, await ErrorDeAsync(Muelle(LUGAR_B, "m1", "Muelle 1")));
        Assert.Equal(547, await ErrorDeAsync(Muelle(Guid.NewGuid().ToString(), "m9", "Muelle 9")));
        Assert.Equal(547, await ErrorDeAsync(Muelle(LUGAR_A, "m2", "   ")));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblViajes SET MuelleId = (SELECT Id FROM dbo.tblMuelles WHERE LugarId = '{LUGAR_A}' AND Codigo = 'm1') WHERE Id = '{VIAJE}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblViajes SET MuelleId = NEWID() WHERE Id = '{VIAJE}'"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblMuelles'"));
        await EjecutarAsync(Muelle(LUGAR_A, "m1", "Muelle 1"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMuelles"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblViajes_Muelle'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
    }
}
