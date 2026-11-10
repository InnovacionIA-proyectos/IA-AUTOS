using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/36-dano-sin-calificar-en-los-conteos.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-dano-sin-calificar.sql, el que ya esta
/// desplegado) y con datos: las vistas de los patrones, el mapa de calor y las averias dejan de excluir la novedad del conductor aun sin revisar (dano sin calificar), el indicador diario gana la cifra aparte
/// SinCalificar sin cambiar las unidades con novedad, no cambia ningun dato y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionDanoSinCalificarTests : IAsyncLifetime
{
    private const string USUARIO = "11111111-1111-1111-1111-111111111111";
    private const string INSPECCION = "33333333-3333-3333-3333-333333333333";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string VEHICULO = "66666666-6666-6666-6666-666666666666";
    private const string ETAPA = "77777777-7777-7777-7777-777777777777";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionDanoSinCalificar");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-dano-sin-calificar.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Una unidad despachada (la salida de su Cargue cerrada) con tres hallazgos: uno V1, un dano del inspector sin nivel y una novedad del conductor aun sin revisar (sin zona, clase ni nivel). Antes de migrar las vistas
    /// excluyen la novedad sin revisar.
    /// </summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINSINCALIFICAR001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado) VALUES ('{{VIAJE}}', 'MTK-36', N'ABC123', 'en_curso');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL;
        ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, IniciadaAt, Estado, CerradaAt)
        VALUES ('{{INSPECCION}}', '{{ETAPA}}', '{{VIAJE}}', '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET(), DATEADD(MINUTE, -10, SYSDATETIMEOFFSET()), 'cerrada', SYSDATETIMEOFFSET());
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano, NivelSeveridad) VALUES (NEWID(), '{{INSPECCION}}', N'Capot', 'golpe', '1_5cm', 'v1');
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (NEWID(), '{{INSPECCION}}', N'Puerta', 'golpe', '5_10cm');
        INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad) VALUES (NEWID(), '{{INSPECCION}}', '5_10cm', 'conductor', 'golpe');
        ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
        ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
        EXEC dbo.usp_RecalcularCompleta;
        """;

    private const string SIN_CALIFICAR_DEL_INDICADOR = "SELECT ISNULL(SUM(SinCalificar), 0) FROM dbo.vwIndicadorDiario";

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

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "36-dano-sin-calificar-en-los-conteos.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_las_vistas_excluyen_la_novedad_sin_revisar_y_el_indicador_no_tiene_la_cifra_de_sin_calificar()
    {
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwHallazgosVentanaUmbral"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.vwIndicadorDiario') AND name = 'SinCalificar'"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task Migrar_hace_que_las_vistas_cuenten_el_dano_sin_calificar_sin_cambiar_ningun_dato()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Los tres hallazgos cuentan: el V1, el dano del inspector sin nivel y la novedad del conductor aun sin revisar.
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.vwHallazgosVentanaUmbral"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion WHERE NivelSeveridad IS NULL"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }

    [Fact]
    public async Task El_indicador_diario_gana_la_cifra_aparte_y_las_unidades_con_novedad_no_cambian()
    {
        var conNovedadAntes = await ContarAsync("SELECT ISNULL(SUM(ConNovedad), 0) FROM dbo.vwIndicadorDiario");
        var despachadosAntes = await ContarAsync("SELECT ISNULL(SUM(Despachados), 0) FROM dbo.vwIndicadorDiario");

        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, conNovedadAntes);
        Assert.Equal(1, despachadosAntes);
        Assert.Equal(conNovedadAntes, await ContarAsync("SELECT ISNULL(SUM(ConNovedad), 0) FROM dbo.vwIndicadorDiario"));
        Assert.Equal(despachadosAntes, await ContarAsync("SELECT ISNULL(SUM(Despachados), 0) FROM dbo.vwIndicadorDiario"));
        Assert.Equal(2, await ContarAsync(SIN_CALIFICAR_DEL_INDICADOR));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion"));
        Assert.Equal(2, await ContarAsync(SIN_CALIFICAR_DEL_INDICADOR));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwAveriasPorConfiguracion'"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.vwHallazgosVentanaUmbral"));
        Assert.Equal(2, await ContarAsync(SIN_CALIFICAR_DEL_INDICADOR));
    }

    [Fact]
    public async Task Sin_el_dibujo_version_6_la_migracion_se_detiene_sin_cambiar_nada()
    {
        await EjecutarAsync("DROP VIEW dbo.vwAveriasPorConfiguracion; DROP VIEW dbo.vwHallazgosVentanaUmbral;");
        await EjecutarAsync("ALTER TABLE dbo.tblZonasDano DROP COLUMN AiagArea");

        var ex = await Assert.ThrowsAsync<SqlException>(async () => await EjecutarAsync(await ScriptMigracionAsync()));

        Assert.Contains("34-resembrar-dibujo-v6.sql", ex.Message);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.vwIndicadorDiario') AND name = 'SinCalificar'"));
    }
}
