using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/47-dano-del-conductor-en-extras-y-conteo-vigente.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-calificacion-de-danos.sql,
/// el que ya esta desplegado) y con datos: el dano de un extra puede ser del conductor (sin nivel) pero nunca lleva zona, el indicador diario cuenta lo sin calificar solo con la version vigente de cada informe,
/// no cambia ningun dato y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionCalificacionDeDanosTests : IAsyncLifetime
{
    private const string USUARIO = "11111111-1111-1111-1111-111111111111";
    private const string INSPECCION = "33333333-3333-3333-3333-333333333333";
    private const string CORRECCION = "44444444-4444-4444-4444-444444444444";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string VEHICULO = "66666666-6666-6666-6666-666666666666";
    private const string ETAPA = "77777777-7777-7777-7777-777777777777";
    private const string EXTRA = "88888888-8888-8888-8888-888888888888";
    private const string EXTRA_2 = "99999999-9999-9999-9999-999999999999";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionCalificacionDanos");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-calificacion-de-danos.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Una unidad despachada (la salida de su Cargue cerrada) cuyo informe se corrigio: la version 1 tiene dos danos sin nivel y la version 2 (la vigente) uno; mas dos extras reportados. Los triggers de hallazgos y extras
    /// quedan desactivados para poder agregar filas a un informe cerrado.
    /// </summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINCALIFICACION0001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado) VALUES ('{{VIAJE}}', 'MTK-47', N'ABC123', 'en_curso');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL;
        ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
        ALTER TABLE dbo.tblInspeccionExtras DISABLE TRIGGER ALL;
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, IniciadaAt, Estado, CerradaAt)
        VALUES ('{{INSPECCION}}', '{{ETAPA}}', '{{VIAJE}}', '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET(), DATEADD(MINUTE, -20, SYSDATETIMEOFFSET()), 'cerrada', DATEADD(MINUTE, -10, SYSDATETIMEOFFSET()));
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, IniciadaAt, Estado, CerradaAt, Version, CorrigeInspeccionId)
        VALUES ('{{CORRECCION}}', '{{ETAPA}}', '{{VIAJE}}', '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET(), DATEADD(MINUTE, -5, SYSDATETIMEOFFSET()), 'cerrada', SYSDATETIMEOFFSET(), 2, '{{INSPECCION}}');
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (NEWID(), '{{INSPECCION}}', N'Capot', 'golpe', '1_5cm');
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (NEWID(), '{{INSPECCION}}', N'Puerta', 'golpe', '5_10cm');
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (NEWID(), '{{CORRECCION}}', N'Capot', 'golpe', '1_5cm');
        INSERT dbo.tblInspeccionExtras (Id, InspeccionId, Tipo) VALUES ('{{EXTRA}}', '{{CORRECCION}}', 'platon');
        INSERT dbo.tblInspeccionExtras (Id, InspeccionId, Tipo) VALUES ('{{EXTRA_2}}', '{{CORRECCION}}', 'volco');
        ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
        EXEC dbo.usp_RecalcularCompleta;
        """;

    private const string SIN_CALIFICAR_DEL_INDICADOR = "SELECT ISNULL(SUM(SinCalificar), 0) FROM dbo.vwIndicadorDiario";

    private static string HallazgoDeExtra(string extra, string origen, string? zona = null) =>
        $"INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano, ExtraId, Origen, ZonaId) VALUES (NEWID(), '{CORRECCION}', N'Platon', 'golpe', '5_10cm', '{extra}', '{origen}', {zona ?? "NULL"})";

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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "47-dano-del-conductor-en-extras-y-conteo-vigente.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_el_dano_de_un_extra_solo_es_del_inspector_y_el_indicador_cuenta_las_dos_versiones()
    {
        Assert.Equal(547, await ErrorDeAsync(HallazgoDeExtra(EXTRA, "conductor")));
        Assert.Equal(3, await ContarAsync(SIN_CALIFICAR_DEL_INDICADOR));
    }

    [Fact]
    public async Task Migrar_admite_el_dano_de_un_extra_del_conductor_sin_zona_y_el_indicador_cuenta_solo_la_version_vigente()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblHallazgos_Extra' AND definition NOT LIKE '%Origen%'"));
        Assert.Equal(0, await ErrorDeAsync(HallazgoDeExtra(EXTRA, "conductor")));
        Assert.Equal(0, await ErrorDeAsync(HallazgoDeExtra(EXTRA_2, "inspector")));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgos WHERE ExtraId = '{EXTRA}' AND Origen = 'conductor' AND NivelSeveridad IS NULL AND ZonaId IS NULL"));
        // Los dos danos sin nivel de la version 1 (reemplazada) ya no cuentan: quedan el de la version vigente y los dos danos de extras, que tampoco tienen nivel en estos datos.
        Assert.Equal(3, await ContarAsync(SIN_CALIFICAR_DEL_INDICADOR));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgos WHERE InspeccionId = '{CORRECCION}' AND ExtraId IS NULL"));
    }

    [Fact]
    public async Task Despues_de_migrar_el_dano_de_un_extra_sigue_sin_poder_llevar_zona()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        // Un cuadrante nuevo trae sus zonas: el dano de un extra no puede apuntar a ninguna.
        await EjecutarAsync("INSERT dbo.tblCuadrantes (Id, TipoVehiculo, Numero, Nombre, Orden, LlevaLado) VALUES (NEWID(), NULL, 7, N'Puerta delantera', 7, 1)");
        Assert.True(await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE Numero = 7") > 0);

        Assert.Equal(547, await ErrorDeAsync(HallazgoDeExtra(EXTRA, "conductor", "(SELECT TOP 1 Id FROM dbo.tblZonasDano WHERE Numero = 7)")));
        Assert.Equal(547, await ErrorDeAsync(HallazgoDeExtra(EXTRA, "inspector", "(SELECT TOP 1 Id FROM dbo.tblZonasDano WHERE Numero = 7)")));
    }

    [Fact]
    public async Task Migrar_no_cambia_ningun_dato_y_la_cifra_del_indicador_baja_a_la_version_vigente()
    {
        var antes = await ContarAsync(SIN_CALIFICAR_DEL_INDICADOR);

        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(3, antes);
        Assert.Equal(1, await ContarAsync(SIN_CALIFICAR_DEL_INDICADOR));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras"));
        Assert.Equal(1, await ContarAsync("SELECT ISNULL(SUM(Despachados), 0) FROM dbo.vwIndicadorDiario"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblHallazgos_Extra' AND definition NOT LIKE '%Origen%'"));
        Assert.Equal(1, await ContarAsync(SIN_CALIFICAR_DEL_INDICADOR));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblHallazgos_Extra'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblHallazgos_Extra' AND definition NOT LIKE '%Origen%'"));
        Assert.Equal(1, await ContarAsync(SIN_CALIFICAR_DEL_INDICADOR));
        Assert.Equal(0, await ErrorDeAsync(HallazgoDeExtra(EXTRA, "conductor")));
    }
}
