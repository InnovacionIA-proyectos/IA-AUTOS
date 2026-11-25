using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/53-lugar-admite-cargue-sin-inspector.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-cargue-sin-inspector-por-lugar.sql, el que ya
/// esta desplegado) y con lugares: la columna nueva queda en falso para todos los lugares que ya existian, se puede activar lugar por lugar, el motivo de la excepcion "No hay inspector" se admite tambien en una
/// inspeccion de llegada y el script y el ddl nuevo se pueden repetir sin cambiar el dato.
/// </summary>
public sealed partial class MigracionLugarAdmiteCargueSinInspectorTests : IAsyncLifetime
{
    private const string CEDI = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string PATIO = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string ANA = "aaaa0000-0000-0000-0000-000000000001";
    private const string VEHICULO = "11111111-1111-1111-1111-111111111111";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string ETAPA = "e1000000-0000-0000-0000-000000000001";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionLugarCargue");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-cargue-sin-inspector-por-lugar.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Lo que ya estaba guardado antes de migrar: dos lugares, un viaje con un carro y la etapa de cargue.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @tipo uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia);
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{CEDI}}', N'CEDI Envigado', @tipo, N'Envigado');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{PATIO}}', N'Patio de Prueba', @tipo, N'Medellin');
        DECLARE @cli uniqueidentifier = (SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{ANA}}', N'Ana', 'ana@e.com', 'conductor', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINCARGUELUGAR001', N'R', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, LugarCargueId) VALUES ('{{VIAJE}}', 'MTK-52', N'ABC123', '{{PATIO}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId, LugarDestinoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}', '{{PATIO}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        """;

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

    private static string InspeccionConMotivo(string momento) =>
        $"INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Estado, MotivoSinInspector) VALUES (NEWID(), '{ETAPA}', '{VIAJE}', '{ANA}', '{momento}', SYSDATETIMEOFFSET(), 'borrador', 'inspector_no_llego')";

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "53-lugar-admite-cargue-sin-inspector.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_el_lugar_no_tiene_el_dato()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblLugares') AND name = N'AdmiteCargueSinInspector'"));
    }

    [Fact]
    public async Task Antes_de_migrar_el_motivo_de_la_excepcion_solo_se_guarda_en_la_salida()
    {
        Assert.Equal(547, await ErrorDeAsync(InspeccionConMotivo("llegada")));
        Assert.Equal(0, await ErrorDeAsync(InspeccionConMotivo("salida")));
    }

    [Fact]
    public async Task Migrar_admite_el_motivo_de_la_excepcion_tambien_en_la_llegada_sin_cambiar_las_inspecciones_que_ya_existian()
    {
        await EjecutarAsync(InspeccionConMotivo("salida"));

        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(InspeccionConMotivo("llegada")));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Momento = 'salida' AND MotivoSinInspector = 'inspector_no_llego'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Momento = 'llegada' AND MotivoSinInspector = 'inspector_no_llego'"));
    }

    [Fact]
    public async Task Migrar_deja_en_falso_todos_los_lugares_que_ya_existian_y_no_cambia_nada_mas()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblLugares') AND name = N'AdmiteCargueSinInspector' AND is_nullable = 0"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE AdmiteCargueSinInspector = 0"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE Nombre = N'CEDI Envigado' AND Ciudad = N'Envigado' AND AdmiteCargueSinInspector = 0"));
    }

    [Fact]
    public async Task Un_lugar_nuevo_nace_sin_admitir_el_cargue_sin_inspector_y_se_puede_activar_lugar_por_lugar()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync($"INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES (NEWID(), N'Otro', (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia), N'Cali')");
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE AdmiteCargueSinInspector = 0"));

        await EjecutarAsync($"UPDATE dbo.tblLugares SET AdmiteCargueSinInspector = 1 WHERE Id = '{PATIO}'");
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE AdmiteCargueSinInspector = 1"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_sin_cambiar_el_dato()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblLugares') AND name = N'AdmiteCargueSinInspector' AND is_nullable = 0"));
        await EjecutarAsync($"UPDATE dbo.tblLugares SET AdmiteCargueSinInspector = 1 WHERE Id = '{PATIO}'");

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.default_constraints WHERE name = N'DF_tblLugares_AdmiteCargueSinInspector'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE AdmiteCargueSinInspector = 1"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblLugares WHERE Id = '{CEDI}' AND AdmiteCargueSinInspector = 0"));
    }
}
