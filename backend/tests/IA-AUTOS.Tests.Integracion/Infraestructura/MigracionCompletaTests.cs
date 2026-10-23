using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/21-indices-de-filtros-y-completa.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-completa.sql, el que ya
/// esta desplegado) y con datos: crea los indices de los filtros, la columna <c>tblViajeVehiculos.Completa</c> (llenada con la misma regla de <c>vwViajesCompletos</c>), el procedimiento que la
/// recalcula y los triggers que la mantienen, sin tocar ningun otro dato; ejecutarlo dos veces no cambia nada.
/// </summary>
public sealed partial class MigracionCompletaTests : IAsyncLifetime
{
    private const string USUARIO = "33333333-3333-3333-3333-333333333333";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionCompleta");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionCompleta" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-completa.sql"));
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

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "21-indices-de-filtros-y-completa.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    private const string INDICES = "'IX_tblVehiculos_Filtros', 'IX_tblVehiculos_Activo', 'IX_tblOrdenesSofasa_Filtros', 'IX_tblInspecciones_Filtros', 'IX_tblViajeVehiculos_EstadoUnidad'";

    private const string DIFERENCIAS_CON_LA_VISTA = """
        SELECT COUNT(*) FROM dbo.tblViajeVehiculos vv
        WHERE vv.Completa <> CASE WHEN EXISTS (SELECT 1 FROM dbo.vwViajesCompletos c WHERE c.ViajeId = vv.ViajeId AND c.VehiculoId = vv.VehiculoId) THEN 1 ELSE 0 END
        """;

    /// <summary>
    /// Cinco unidades: V1 (viaje A) con sus tres etapas cerradas; V2 (viaje A) con su unica etapa cerrada; V3 (viaje B) marcada "no sale" aunque su etapa esta cerrada; V4 (viaje C) con una
    /// etapa sin informes; V5 (viaje D) sin etapas.
    /// </summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        DECLARE @transporte uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte');
        DECLARE @descargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue');
        DECLARE @zona uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');

        DECLARE @v1 uniqueidentifier = NEWID(), @v2 uniqueidentifier = NEWID(), @v3 uniqueidentifier = NEWID(), @v4 uniqueidentifier = NEWID(), @v5 uniqueidentifier = NEWID();
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v1, 'VINCOM0000000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v2, 'VINCOM0000000002', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v3, 'VINCOM0000000003', N'Toyota', 'importacion', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v4, 'VINCOM0000000004', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v5, 'VINCOM0000000005', N'Renault', 'nacional', @cli);

        DECLARE @a uniqueidentifier = NEWID(), @b uniqueidentifier = NEWID(), @c uniqueidentifier = NEWID(), @d uniqueidentifier = NEWID();
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado) VALUES (@a, 'MTK-A', N'ABC123', 'en_curso');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado) VALUES (@b, 'MTK-B', N'DEF456', 'en_curso');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado) VALUES (@c, 'MTK-C', N'GHI789', 'en_curso');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado) VALUES (@d, 'MTK-D', N'JKL012', 'programado');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @a, @v1), (NEWID(), @a, @v2), (NEWID(), @c, @v4), (NEWID(), @d, @v5);

        DECLARE @e1c uniqueidentifier = NEWID(), @e1t uniqueidentifier = NEWID(), @e1d uniqueidentifier = NEWID(), @e2c uniqueidentifier = NEWID(), @e3c uniqueidentifier = NEWID(), @e4c uniqueidentifier = NEWID();
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e1c, @v1, @a, @cargue, 1), (@e1t, @v1, @a, @transporte, 2), (@e1d, @v1, @a, @descargue, 3);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e2c, @v2, @a, @cargue, 1);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e3c, @v3, @b, @cargue, 1);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e4c, @v4, @c, @cargue, 1);

        CREATE TABLE #i (Id uniqueidentifier, EtapaId uniqueidentifier, ViajeId uniqueidentifier, Momento varchar(20));
        INSERT #i VALUES
            ('11111111-0000-0000-0000-000000000001', @e1c, @a, 'salida'),
            ('11111111-0000-0000-0000-000000000003', @e1t, @a, 'salida'),
            ('11111111-0000-0000-0000-000000000005', @e1d, @a, 'salida'),
            ('22222222-0000-0000-0000-000000000001', @e2c, @a, 'salida'),
            ('33333333-0000-0000-0000-000000000001', @e3c, @b, 'salida');
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) SELECT Id, EtapaId, ViajeId, '{{USUARIO}}', Momento, SYSDATETIMEOFFSET() FROM #i;

        DECLARE @h3 uniqueidentifier = NEWID();
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (@h3, '33333333-0000-0000-0000-000000000001', @zona, 'rotura', '10_20cm', 'v1_plus');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId, EstadoUnidad, NoSaleMotivo, NoSaleHallazgoId, NoSaleMarcadoPor, NoSaleAt)
        VALUES (NEWID(), @b, @v3, 'no_sale', N'Golpe estructural', @h3, '{{USUARIO}}', SYSDATETIMEOFFSET());

        UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id IN (SELECT Id FROM #i);
        DROP TABLE #i;
        """;

    private Task<string?> CompletaAsync(string vin) =>
        TextoAsync($"SELECT CAST(vv.Completa AS int) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId WHERE v.Vin = '{vin}'");

    [Fact]
    public async Task La_migracion_crea_los_indices_la_columna_el_procedimiento_y_los_triggers_sin_cambiar_ningun_dato()
    {
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM sys.indexes WHERE name IN ({INDICES})"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblViajeVehiculos') AND name = 'Completa'"));
        var datos = await TextoAsync(
            "SELECT CONCAT((SELECT COUNT(*) FROM dbo.tblInspecciones), '|', (SELECT COUNT(*) FROM dbo.tblHallazgos), '|', (SELECT COUNT(*) FROM dbo.tblViajeVehiculos), '|', (SELECT COUNT(*) FROM dbo.tblVehiculos), '|', (SELECT COUNT(*) FROM dbo.tblEtapas))");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(5, await ContarAsync($"SELECT COUNT(*) FROM sys.indexes WHERE name IN ({INDICES})"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblViajeVehiculos') AND name = 'Completa'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.procedures WHERE name = 'usp_RecalcularCompleta'"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name IN ('TR_tblInspecciones_Completa', 'TR_tblEtapas_Completa', 'TR_tblViajeVehiculos_Completa')"));
        Assert.Equal(
            datos,
            await TextoAsync("SELECT CONCAT((SELECT COUNT(*) FROM dbo.tblInspecciones), '|', (SELECT COUNT(*) FROM dbo.tblHallazgos), '|', (SELECT COUNT(*) FROM dbo.tblViajeVehiculos), '|', (SELECT COUNT(*) FROM dbo.tblVehiculos), '|', (SELECT COUNT(*) FROM dbo.tblEtapas))"));
    }

    [Fact]
    public async Task Los_indices_tienen_las_columnas_de_la_tarea_en_su_orden()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        async Task<string?> ColumnasAsync(string indice) => await TextoAsync(
            $"SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.index_id = (SELECT index_id FROM sys.indexes WHERE name = '{indice}') AND ic.object_id = (SELECT object_id FROM sys.indexes WHERE name = '{indice}') AND ic.is_included_column = 0");

        Assert.Equal("ClienteId,Operacion,Marca,CreatedAt", await ColumnasAsync("IX_tblVehiculos_Filtros"));
        Assert.Equal("Activo", await ColumnasAsync("IX_tblVehiculos_Activo"));
        Assert.Equal("StatusCode,DestinationCity,DatetimeEvent", await ColumnasAsync("IX_tblOrdenesSofasa_Filtros"));
        Assert.Equal("ViajeId,Momento,Estado,Fecha", await ColumnasAsync("IX_tblInspecciones_Filtros"));
        Assert.Equal("EstadoUnidad", await ColumnasAsync("IX_tblViajeVehiculos_EstadoUnidad"));
    }

    [Fact]
    public async Task Completa_se_llena_con_la_misma_regla_de_la_vista_para_cada_unidad_y_viaje()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // V1 y V2 tienen todas sus etapas con informe cerrado; V3 no sale; V4 tiene una etapa sin informes y V5 ninguna etapa.
        Assert.Equal("1", await CompletaAsync("VINCOM0000000001"));
        Assert.Equal("1", await CompletaAsync("VINCOM0000000002"));
        Assert.Equal("0", await CompletaAsync("VINCOM0000000003"));
        Assert.Equal("0", await CompletaAsync("VINCOM0000000004"));
        Assert.Equal("0", await CompletaAsync("VINCOM0000000005"));
        Assert.Equal("0", await TextoAsync(DIFERENCIAS_CON_LA_VISTA));
        Assert.Equal("2", await TextoAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos"));
    }

    [Fact]
    public async Task Ejecutar_la_migracion_dos_veces_no_cambia_nada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        var antes = await TextoAsync("SELECT CONCAT((SELECT COUNT(*) FROM sys.indexes WHERE object_id > 0 AND name LIKE 'IX_%'), '|', (SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE Completa = 1))");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(antes, await TextoAsync("SELECT CONCAT((SELECT COUNT(*) FROM sys.indexes WHERE object_id > 0 AND name LIKE 'IX_%'), '|', (SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE Completa = 1))"));
    }

    [Fact]
    public async Task Los_triggers_mantienen_Completa_al_agregar_o_quitar_una_etapa_y_al_cambiar_el_estado_de_la_unidad()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Una etapa nueva sin informe deja a V2 incompleta; quitarla la completa otra vez.
        await EjecutarAsync(
            _cadena,
            """
            INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden)
            SELECT '99999999-0000-0000-0000-000000000001', v.Id, vv.ViajeId, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte'), 2
            FROM dbo.tblVehiculos v JOIN dbo.tblViajeVehiculos vv ON vv.VehiculoId = v.Id WHERE v.Vin = 'VINCOM0000000002';
            """);
        Assert.Equal("0", await CompletaAsync("VINCOM0000000002"));
        await EjecutarAsync(_cadena, "DELETE FROM dbo.tblEtapas WHERE Id = '99999999-0000-0000-0000-000000000001'");
        Assert.Equal("1", await CompletaAsync("VINCOM0000000002"));

        // Una unidad que se marca "no sale" deja de contar como completa y vuelve a contar cuando queda activa.
        await EjecutarAsync(
            _cadena,
            $"""
            UPDATE vv SET EstadoUnidad = 'no_sale', NoSaleMotivo = N'Golpe', NoSaleHallazgoId = h.Id, NoSaleMarcadoPor = '{USUARIO}', NoSaleAt = SYSDATETIMEOFFSET()
            FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId, (SELECT TOP 1 Id FROM dbo.tblHallazgos) h WHERE v.Vin = 'VINCOM0000000001';
            """);
        Assert.Equal("0", await CompletaAsync("VINCOM0000000001"));
        await EjecutarAsync(
            _cadena,
            """
            UPDATE vv SET EstadoUnidad = 'activa', NoSaleMotivo = NULL, NoSaleHallazgoId = NULL, NoSaleMarcadoPor = NULL, NoSaleAt = NULL
            FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId WHERE v.Vin = 'VINCOM0000000001';
            """);
        Assert.Equal("1", await CompletaAsync("VINCOM0000000001"));
        Assert.Equal("0", await TextoAsync(DIFERENCIAS_CON_LA_VISTA));
    }

    [Fact]
    public async Task Cerrar_el_informe_de_la_unica_etapa_de_una_unidad_la_marca_completa()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        await EjecutarAsync(
            _cadena,
            $"""
            INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha)
            SELECT '44444444-0000-0000-0000-000000000001', e.Id, e.ViajeId, '{USUARIO}', 'salida', SYSDATETIMEOFFSET()
            FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = 'VINCOM0000000004';
            """);
        Assert.Equal("0", await CompletaAsync("VINCOM0000000004"));

        await EjecutarAsync(_cadena, "UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = '44444444-0000-0000-0000-000000000001'");

        Assert.Equal("1", await CompletaAsync("VINCOM0000000004"));
        Assert.Equal("0", await TextoAsync(DIFERENCIAS_CON_LA_VISTA));
    }

    [Fact]
    public async Task El_procedimiento_corrige_lo_que_quedo_desactualizado_porque_se_apagaron_los_triggers()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Una carga con los triggers apagados deja una fila que no coincide con la vista; el procedimiento la vuelve a calcular.
        await EjecutarAsync(
            _cadena,
            """
            ALTER TABLE dbo.tblViajeVehiculos DISABLE TRIGGER ALL;
            UPDATE vv SET Completa = 1 FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId WHERE v.Vin = 'VINCOM0000000005';
            ALTER TABLE dbo.tblViajeVehiculos ENABLE TRIGGER ALL;
            """);
        Assert.Equal("1", await TextoAsync(DIFERENCIAS_CON_LA_VISTA));

        await EjecutarAsync(_cadena, "EXEC dbo.usp_RecalcularCompleta");

        Assert.Equal("0", await TextoAsync(DIFERENCIAS_CON_LA_VISTA));
        Assert.Equal("0", await CompletaAsync("VINCOM0000000005"));
    }
}
