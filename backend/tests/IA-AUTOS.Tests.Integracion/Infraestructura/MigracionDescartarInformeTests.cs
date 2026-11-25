using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/50-descartar-informe-en-borrador.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-descartar-informe.sql, el que ya esta desplegado) y con
/// informes: el estado admite descartada y superada sin fecha de cierre, la inspeccion original unica por etapa, momento y ciclo y la correccion unica ya no cuentan los informes descartados ni los superados, los informes que ya
/// existian siguen contando igual y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionDescartarInformeTests : IAsyncLifetime
{
    private const string ANA = "aaaa0000-0000-0000-0000-000000000001";
    private const string LUGAR = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string VEHICULO = "11111111-1111-1111-1111-111111111111";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string ETAPA_CON_BORRADOR = "e1000000-0000-0000-0000-000000000001";
    private const string ETAPA_CERRADA = "e2000000-0000-0000-0000-000000000002";
    private const string BORRADOR = "b1000000-0000-0000-0000-000000000001";
    private const string CERRADA = "b2000000-0000-0000-0000-000000000002";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionDescartar");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-descartar-informe.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Lo que ya estaba guardado antes de migrar: un viaje con un carro, una etapa con la salida en borrador y otra con la llegada cerrada.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa');
        DECLARE @tipo uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{ANA}}', N'Ana', 'ana@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{LUGAR}}', N'Cargue', @tipo, N'Medellin');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINDESCARTA00001', N'R', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, LugarCargueId) VALUES ('{{VIAJE}}', 'MTK-51', N'ABC123', '{{LUGAR}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId, LugarDestinoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}', '{{LUGAR}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES
            ('{{ETAPA_CON_BORRADOR}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1),
            ('{{ETAPA_CERRADA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue'), 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Estado) VALUES ('{{BORRADOR}}', '{{ETAPA_CON_BORRADOR}}', '{{VIAJE}}', '{{ANA}}', 'salida', SYSDATETIMEOFFSET(), 'borrador');
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Estado, CerradaAt) VALUES ('{{CERRADA}}', '{{ETAPA_CERRADA}}', '{{VIAJE}}', '{{ANA}}', 'llegada', SYSDATETIMEOFFSET(), 'cerrada', SYSDATETIMEOFFSET());
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

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "50-descartar-informe-en-borrador.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    private static string Inspeccion(string etapa, string momento, string estado, string? corrige = null, bool conCierre = false) =>
        $"INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Estado, CerradaAt, Version, CorrigeInspeccionId) VALUES (NEWID(), '{etapa}', '{VIAJE}', '{ANA}', '{momento}', SYSDATETIMEOFFSET(), '{estado}', "
        + $"{(conCierre ? "SYSDATETIMEOFFSET()" : "NULL")}, {(corrige is null ? 1 : 2)}, {(corrige is null ? "NULL" : $"'{corrige}'")})";

    [Fact]
    public async Task Antes_de_migrar_el_estado_solo_admite_borrador_y_cerrada_y_el_borrador_descartado_seguiria_ocupando_su_momento()
    {
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET Estado = 'descartada' WHERE Id = '{BORRADOR}'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblInspecciones_Etapa_Momento_Original' AND filter_definition LIKE '%descartada%'"));
    }

    [Fact]
    public async Task Migrar_admite_los_estados_nuevos_sin_fecha_de_cierre_y_no_cambia_los_informes_que_ya_existian()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Id = '{BORRADOR}' AND Estado = 'borrador' AND CerradaAt IS NULL"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Id = '{CERRADA}' AND Estado = 'cerrada' AND CerradaAt IS NOT NULL"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET Estado = 'descartada' WHERE Id = '{BORRADOR}'"));
        Assert.Equal(0, await ErrorDeAsync(Inspeccion(ETAPA_CERRADA, "salida", "superada")));
        // Un estado inventado y una descartada con fecha de cierre siguen rechazados.
        Assert.Equal(547, await ErrorDeAsync(Inspeccion(ETAPA_CERRADA, "salida", "inventada")));
        Assert.Equal(547, await ErrorDeAsync(Inspeccion(ETAPA_CON_BORRADOR, "llegada", "descartada", conCierre: true)));
        Assert.Equal(547, await ErrorDeAsync(Inspeccion(ETAPA_CON_BORRADOR, "llegada", "borrador", conCierre: true)));
    }

    [Fact]
    public async Task Despues_de_migrar_el_momento_de_un_borrador_descartado_o_superado_queda_libre_para_una_original_nueva_y_no_para_dos_vigentes()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Un borrador vigente sigue ocupando su momento.
        Assert.Equal(2601, await ErrorDeAsync(Inspeccion(ETAPA_CON_BORRADOR, "salida", "borrador")));
        // Descartado, el momento queda libre; la nueva vuelve a ocuparlo y una cerrada tambien cuenta.
        await EjecutarAsync($"UPDATE dbo.tblInspecciones SET Estado = 'descartada' WHERE Id = '{BORRADOR}'");
        Assert.Equal(0, await ErrorDeAsync(Inspeccion(ETAPA_CON_BORRADOR, "salida", "borrador")));
        Assert.Equal(2601, await ErrorDeAsync(Inspeccion(ETAPA_CON_BORRADOR, "salida", "borrador")));
        Assert.Equal(2601, await ErrorDeAsync(Inspeccion(ETAPA_CON_BORRADOR, "salida", "cerrada", conCierre: true)));
        // Los descartados y los superados se acumulan sin chocar entre si.
        Assert.Equal(0, await ErrorDeAsync(Inspeccion(ETAPA_CON_BORRADOR, "salida", "descartada")));
        Assert.Equal(0, await ErrorDeAsync(Inspeccion(ETAPA_CON_BORRADOR, "salida", "superada")));
        Assert.Equal(4, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = '{ETAPA_CON_BORRADOR}' AND Momento = 'salida'"));
        // La cerrada que ya existia sigue ocupando su momento.
        Assert.Equal(2601, await ErrorDeAsync(Inspeccion(ETAPA_CERRADA, "llegada", "borrador")));
    }

    [Fact]
    public async Task Despues_de_migrar_la_version_corregida_puede_corregirse_de_nuevo_cuando_la_anterior_correccion_se_descarto()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // La correccion en borrador ocupa la correccion unica; descartada, la cerrada puede corregirse otra vez.
        Assert.Equal(0, await ErrorDeAsync(Inspeccion(ETAPA_CERRADA, "llegada", "borrador", CERRADA)));
        Assert.Equal(2601, await ErrorDeAsync(Inspeccion(ETAPA_CERRADA, "llegada", "borrador", CERRADA)));
        await EjecutarAsync($"UPDATE dbo.tblInspecciones SET Estado = 'descartada' WHERE CorrigeInspeccionId = '{CERRADA}'");
        Assert.Equal(0, await ErrorDeAsync(Inspeccion(ETAPA_CERRADA, "llegada", "borrador", CERRADA)));
        Assert.Equal(2601, await ErrorDeAsync(Inspeccion(ETAPA_CERRADA, "llegada", "borrador", CERRADA)));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblInspecciones_Etapa_Momento_Original' AND is_unique = 1 AND filter_definition LIKE '%descartada%'"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblInspecciones_Estado' AND definition LIKE '%descartada%' AND definition LIKE '%superada%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblInspecciones_Cierre' AND definition LIKE '%descartada%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblInspecciones_Etapa_Momento_Original' AND is_unique = 1 AND filter_definition LIKE '%descartada%' AND filter_definition LIKE '%superada%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblInspecciones_Corrige' AND is_unique = 1 AND filter_definition LIKE '%descartada%' AND filter_definition LIKE '%superada%'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }
}
