using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/20-vistas-de-indicadores.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-indicadores.sql, el que ya
/// esta desplegado) y con datos: se crean las cinco vistas de los indicadores sin tocar ningun dato, y cada una calcula su formula (dias en hora de Colombia, unidades "no sale" fuera de la
/// cobertura, hallazgos V1 del dia en el indicador diario...).
/// </summary>
public sealed partial class MigracionIndicadoresTests : IAsyncLifetime
{
    private const string USUARIO = "33333333-3333-3333-3333-333333333333";
    private const string CONDUCTOR = "44444444-4444-4444-4444-444444444444";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionIndicadores");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionIndicadores" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-indicadores.sql"));
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "20-vistas-de-indicadores.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    private const string VISTAS = "'vwCobertura', 'vwAdopcion', 'vwTiempoCaptura', 'vwAveriasPorConfiguracion', 'vwIndicadorDiario'";

    /// <summary>
    /// Cinco unidades en cuatro viajes creados el 20 de octubre (hora de Colombia): V1 (viaje A) con sus seis registros cerrados; V2 (viaje A) con solo la salida del Cargue cerrada y un
    /// hallazgo V1 ese dia; V3 (viaje B, creado a las 23:30 de Colombia) marcada "no sale"; V4 en un viaje cancelado y V5 en un viaje programado, sin inspecciones. Cierres a las 21:30 de
    /// Colombia (02:30 UTC del 21) para comprobar que el dia es el de Colombia. V1 tambien tiene un hallazgo V3 el dia del despacho y un V1+ tres dias despues, y V1 una novedad del
    /// conductor sin revisar en el Transporte (no cuenta).
    /// </summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        DECLARE @transporte uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte');
        DECLARE @descargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue');
        DECLARE @zona uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente');
        DECLARE @unidad uniqueidentifier = (SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera');
        DECLARE @origen uniqueidentifier = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1');
        DECLARE @destino uniqueidentifier = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'concesionario');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Rol, ClienteId, Cedula, TipoCredencial, PinHash) VALUES ('{{CONDUCTOR}}', N'Carlos Conductor', 'conductor', @cli, '1032456789', 'pin', 'h');

        DECLARE @v1 uniqueidentifier = NEWID(), @v2 uniqueidentifier = NEWID(), @v3 uniqueidentifier = NEWID(), @v4 uniqueidentifier = NEWID(), @v5 uniqueidentifier = NEWID();
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v1, 'VININD0000000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v2, 'VININD0000000002', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v3, 'VININD0000000003', N'Toyota', 'importacion', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v4, 'VININD0000000004', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v5, 'VININD0000000005', N'Renault', 'nacional', @cli);

        DECLARE @a uniqueidentifier = NEWID(), @b uniqueidentifier = NEWID(), @c uniqueidentifier = NEWID(), @d uniqueidentifier = NEWID();
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Trailer, TipoUnidadId, ConductorId, OrigenId, DestinoId, Estado, CreatedAt)
        VALUES (@a, 'MTK-A', N'ABC123', N'TR-1', @unidad, '{{CONDUCTOR}}', @origen, @destino, 'en_curso', '2026-10-20T15:00:00-05:00');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Trailer, Estado, CreatedAt) VALUES (@b, 'MTK-B', N'DEF456', N'TR-2', 'en_curso', '2026-10-20T23:30:00-05:00');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado, CreatedAt) VALUES (@c, 'MTK-C', N'GHI789', 'cancelado', '2026-10-20T09:00:00-05:00');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado, CreatedAt) VALUES (@d, 'MTK-D', N'JKL012', 'programado', '2026-10-20T10:00:00-05:00');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @a, @v1);
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @a, @v2);
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @c, @v4);
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @d, @v5);

        DECLARE @e1c uniqueidentifier = NEWID(), @e1t uniqueidentifier = NEWID(), @e1d uniqueidentifier = NEWID(), @e2c uniqueidentifier = NEWID(), @e3c uniqueidentifier = NEWID();
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e1c, @v1, @a, @cargue, 1), (@e1t, @v1, @a, @transporte, 2), (@e1d, @v1, @a, @descargue, 3);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e2c, @v2, @a, @cargue, 1);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e3c, @v3, @b, @cargue, 1);

        CREATE TABLE #i (Id uniqueidentifier, EtapaId uniqueidentifier, ViajeId uniqueidentifier, Momento varchar(20), Iniciada datetimeoffset(3), Cerrada datetimeoffset(3));
        INSERT #i VALUES
            ('11111111-0000-0000-0000-000000000001', @e1c, @a, 'salida',  '2026-10-21T02:10:00+00:00', '2026-10-21T02:30:00+00:00'),
            ('11111111-0000-0000-0000-000000000002', @e1c, @a, 'llegada', '2026-10-21T03:00:00+00:00', '2026-10-21T03:12:30+00:00'),
            ('11111111-0000-0000-0000-000000000003', @e1t, @a, 'salida',  '2026-10-21T14:00:00+00:00', '2026-10-21T14:05:00+00:00'),
            ('11111111-0000-0000-0000-000000000004', @e1t, @a, 'llegada', '2026-10-22T16:00:00+00:00', '2026-10-22T16:05:00+00:00'),
            ('11111111-0000-0000-0000-000000000005', @e1d, @a, 'salida',  '2026-10-23T15:00:00+00:00', '2026-10-23T15:08:00+00:00'),
            ('11111111-0000-0000-0000-000000000006', @e1d, @a, 'llegada', '2026-10-23T16:00:00+00:00', '2026-10-23T16:06:00+00:00'),
            ('22222222-0000-0000-0000-000000000001', @e2c, @a, 'salida',  '2026-10-21T03:30:00+00:00', '2026-10-21T04:00:00+00:00'),
            ('33333333-0000-0000-0000-000000000001', @e3c, @b, 'salida',  '2026-10-21T05:00:00+00:00', '2026-10-21T05:10:00+00:00');
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, IniciadaAt) SELECT Id, EtapaId, ViajeId, '{{USUARIO}}', Momento, Iniciada, Iniciada FROM #i;

        -- V1: un hallazgo V3 el dia del despacho (llegada del Cargue) y uno V1+ tres dias despues (salida del Descargue).
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (NEWID(), '11111111-0000-0000-0000-000000000002', @zona, 'golpe', '1_5cm', 'v3');
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (NEWID(), '11111111-0000-0000-0000-000000000005', @zona, 'rotura', '5_10cm', 'v1_plus');
        -- V2: un hallazgo V1 el dia del despacho. V1: una novedad del conductor sin revisar en la salida del Transporte (no cuenta en ninguna vista).
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (NEWID(), '22222222-0000-0000-0000-000000000001', @zona, 'golpe', '5_10cm', 'v1');
        INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad) VALUES (NEWID(), '11111111-0000-0000-0000-000000000003', '5_10cm', 'conductor', 'golpe');
        -- V3: el hallazgo grave que la saco del viaje.
        DECLARE @h3 uniqueidentifier = NEWID();
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (@h3, '33333333-0000-0000-0000-000000000001', @zona, 'rotura', '10_20cm', 'v1_plus');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId, EstadoUnidad, NoSaleMotivo, NoSaleHallazgoId, NoSaleMarcadoPor, NoSaleAt)
        VALUES (NEWID(), @b, @v3, 'no_sale', N'Golpe estructural', @h3, '{{USUARIO}}', SYSDATETIMEOFFSET());

        UPDATE i SET Estado = 'cerrada', CerradaAt = t.Cerrada FROM dbo.tblInspecciones i JOIN #i t ON t.Id = i.Id;
        DROP TABLE #i;
        """;

    [Fact]
    public async Task La_migracion_crea_las_cinco_vistas_sin_cambiar_ningun_dato()
    {
        Assert.Equal(0, await ContarAsync($"SELECT COUNT(*) FROM sys.views WHERE name IN ({VISTAS})"));
        var tablas = await TextoAsync(
            "SELECT CONCAT((SELECT COUNT(*) FROM dbo.tblInspecciones), '|', (SELECT COUNT(*) FROM dbo.tblHallazgos), '|', (SELECT COUNT(*) FROM dbo.tblViajeVehiculos), '|', (SELECT COUNT(*) FROM dbo.tblVehiculos))");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(5, await ContarAsync($"SELECT COUNT(*) FROM sys.views WHERE name IN ({VISTAS})"));
        Assert.Equal(
            tablas,
            await TextoAsync("SELECT CONCAT((SELECT COUNT(*) FROM dbo.tblInspecciones), '|', (SELECT COUNT(*) FROM dbo.tblHallazgos), '|', (SELECT COUNT(*) FROM dbo.tblViajeVehiculos), '|', (SELECT COUNT(*) FROM dbo.tblVehiculos))"));
    }

    [Fact]
    public async Task La_cobertura_trae_una_fila_por_unidad_despachada_sin_las_que_no_salen_y_cuenta_sus_registros_cerrados()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // V1 y V2 se despacharon; V3 no sale, V4 (viaje cancelado) y V5 (sin inspecciones) nunca salieron.
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwCobertura"));
        Assert.Equal("VININD0000000001", await TextoAsync("SELECT Vin FROM dbo.vwCobertura WHERE Completa = 1"));
        Assert.Equal("6", await TextoAsync("SELECT RegistrosCerrados FROM dbo.vwCobertura WHERE Vin = 'VININD0000000001'"));
        Assert.Equal("1", await TextoAsync("SELECT RegistrosCerrados FROM dbo.vwCobertura WHERE Vin = 'VININD0000000002'"));
        Assert.Equal("0", await TextoAsync("SELECT CAST(Completa AS int) FROM dbo.vwCobertura WHERE Vin = 'VININD0000000002'"));
        // El dia es el de Colombia: el cierre fue a las 02:30 UTC del 21, o sea las 21:30 del 20.
        Assert.Equal("2026-10-20", await TextoAsync("SELECT CONVERT(varchar(10), Fecha, 23) FROM dbo.vwCobertura WHERE Vin = 'VININD0000000001'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwCobertura WHERE FechaDespacho IS NOT NULL AND Placa = N'ABC123' AND Trailer = N'TR-1' AND Marca = N'Renault' AND Operacion = 'nacional'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwCobertura c JOIN dbo.tblVehiculos v ON v.Id = c.VehiculoId WHERE c.ClienteId = v.ClienteId AND c.OrigenId IS NOT NULL AND c.DestinoId IS NOT NULL AND c.ConductorId IS NOT NULL AND c.TipoUnidadId IS NOT NULL"));
    }

    [Fact]
    public async Task Una_correccion_cerrada_no_suma_un_registro_mas()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Una correccion cerrada de la salida del Cargue de V1 (la misma etapa y momento) no cambia los seis registros.
        await EjecutarAsync(
            _cadena,
            $"""
            INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Version, CorrigeInspeccionId)
            SELECT '55555555-0000-0000-0000-000000000001', EtapaId, ViajeId, '{USUARIO}', Momento, SYSDATETIMEOFFSET(), 2, Id FROM dbo.tblInspecciones WHERE Id = '11111111-0000-0000-0000-000000000001';
            UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = '55555555-0000-0000-0000-000000000001';
            """);

        Assert.Equal("6", await TextoAsync("SELECT RegistrosCerrados FROM dbo.vwCobertura WHERE Vin = 'VININD0000000001'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwCobertura"));
    }

    [Fact]
    public async Task La_adopcion_cuenta_los_vin_de_los_viajes_del_dia_no_cancelados_y_los_que_tienen_salida_en_el_sistema()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Programados del 20 (hora de Colombia): V1, V2, V3 y V5; V4 esta en un viaje cancelado. Con salida registrada: V1, V2 y V3 (V5 no).
        Assert.Equal("4", await TextoAsync("SELECT SUM(Programados) FROM dbo.vwAdopcion WHERE Fecha = '2026-10-20'"));
        Assert.Equal("3", await TextoAsync("SELECT SUM(ConSalidaDigital) FROM dbo.vwAdopcion WHERE Fecha = '2026-10-20'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAdopcion WHERE Fecha = '2026-10-20' AND Operacion = 'importacion' AND Programados = 1 AND ConSalidaDigital = 1 AND Porcentaje = 100"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAdopcion WHERE Fecha = '2026-10-20' AND Operacion = 'nacional' AND Trailer IS NULL AND Programados = 1 AND ConSalidaDigital = 0 AND Porcentaje = 0"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAdopcion WHERE Fecha = '2026-10-20' AND Trailer = N'TR-1' AND Programados = 2 AND ConSalidaDigital = 2 AND Porcentaje = 100.00"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAdopcion WHERE Fecha <> '2026-10-20'"));
    }

    [Fact]
    public async Task El_tiempo_de_captura_trae_las_inspecciones_cerradas_de_cargue_y_descargue_y_ninguna_de_transporte()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // V1: salida y llegada del Cargue y del Descargue (4); V2: salida del Cargue; V3: salida del Cargue. Las dos de Transporte de V1 no entran.
        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.vwTiempoCaptura"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.vwTiempoCaptura WHERE Proceso = 'transporte'"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.vwTiempoCaptura WHERE Proceso = 'cargue'"));
        Assert.Equal("20.00", await TextoAsync("SELECT Minutos FROM dbo.vwTiempoCaptura WHERE InspeccionId = '11111111-0000-0000-0000-000000000001'"));
        Assert.Equal("12.50", await TextoAsync("SELECT Minutos FROM dbo.vwTiempoCaptura WHERE InspeccionId = '11111111-0000-0000-0000-000000000002'"));
        Assert.Equal("30.00", await TextoAsync("SELECT Minutos FROM dbo.vwTiempoCaptura WHERE InspeccionId = '22222222-0000-0000-0000-000000000001'"));
        Assert.Equal(6, await ContarAsync($"SELECT COUNT(*) FROM dbo.vwTiempoCaptura WHERE UsuarioId = '{USUARIO}' AND ClienteId IS NOT NULL AND Fecha IS NOT NULL AND Momento IN ('salida', 'llegada')"));
        Assert.Equal("2026-10-20", await TextoAsync("SELECT CONVERT(varchar(10), Fecha, 23) FROM dbo.vwTiempoCaptura WHERE InspeccionId = '11111111-0000-0000-0000-000000000001'"));
    }

    [Fact]
    public async Task Las_averias_traen_una_fila_por_hallazgo_revisado_con_la_configuracion_del_viaje_y_la_zona()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Cuatro hallazgos revisados (V3 y V1+ de V1, V1 de V2 y V1+ de V3); la novedad del conductor sin revisar no entra.
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion"));
        Assert.Equal(3, await ContarAsync(
            $"SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion WHERE Placa = N'ABC123' AND Trailer = N'TR-1' AND UnidadCodigo = 'ninera' AND ConductorId = '{CONDUCTOR}' "
            + "AND ConductorNombre = N'Carlos Conductor' AND ConductorCedula = '1032456789' AND ZonaCodigo = 'frente' AND Numero IS NULL AND Lado IS NULL AND ZonaNombre IS NOT NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion WHERE Vin = 'VININD0000000002' AND NivelSeveridad = 'v1' AND ClaseDano = 'golpe' AND TamanoDano = '5_10cm' AND Proceso = 'cargue' AND Momento = 'salida'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion WHERE Vin = 'VININD0000000003' AND Marca = N'Toyota' AND Operacion = 'importacion' AND ConductorId IS NULL AND Unidad IS NULL"));
        Assert.Equal("2026-10-23", await TextoAsync("SELECT CONVERT(varchar(10), Fecha, 23) FROM dbo.vwAveriasPorConfiguracion WHERE NivelSeveridad = 'v1_plus' AND Vin = 'VININD0000000001'"));
    }

    [Fact]
    public async Task El_indicador_diario_cuenta_los_despachados_del_dia_y_los_que_tuvieron_un_hallazgo_v1_ese_dia()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // El 20: despachados V1 y V2; con novedad solo V2 (V1 tuvo un V3 ese dia y un V1+ tres dias despues, que no cuentan). V3 no sale y no se despacha.
        Assert.Equal("2", await TextoAsync("SELECT SUM(Despachados) FROM dbo.vwIndicadorDiario WHERE Fecha = '2026-10-20'"));
        Assert.Equal("1", await TextoAsync("SELECT SUM(ConNovedad) FROM dbo.vwIndicadorDiario WHERE Fecha = '2026-10-20'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwIndicadorDiario WHERE Fecha = '2026-10-20' AND Despachados = 2 AND ConNovedad = 1 AND Porcentaje = 50"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwIndicadorDiario"));
    }

    [Fact]
    public async Task Un_hallazgo_v1_plus_del_mismo_dia_tambien_cuenta_como_novedad()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        // Un V1+ en la inspeccion de salida del Cargue de V1, el mismo dia del despacho: V1 pasa a tener novedad.
        await EjecutarAsync(_cadena, "UPDATE dbo.tblHallazgos SET NivelSeveridad = 'v1_plus' WHERE InspeccionId = '11111111-0000-0000-0000-000000000002'");

        Assert.Equal("2", await TextoAsync("SELECT SUM(ConNovedad) FROM dbo.vwIndicadorDiario WHERE Fecha = '2026-10-20'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwIndicadorDiario WHERE Despachados = 2 AND ConNovedad = 2 AND Porcentaje = 100"));
    }

    [Fact]
    public async Task Repetir_la_migracion_o_el_ddl_deja_lo_mismo_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(5, await ContarAsync($"SELECT COUNT(*) FROM sys.views WHERE name IN ({VISTAS})"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(5, await ContarAsync($"SELECT COUNT(*) FROM sys.views WHERE name IN ({VISTAS})"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwCobertura"));
        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.vwTiempoCaptura"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion"));
    }

    [Fact]
    public async Task Con_un_esquema_que_no_existe_la_migracion_se_detiene_sin_crear_nada()
    {
        var script = (await ScriptMigracionAsync()).Replace("N'dbo'", "N'esquema_inexistente'");

        var ex = await Assert.ThrowsAsync<SqlException>(async () => await EjecutarAsync(_cadena, script));

        Assert.Contains("@esquema", ex.Message);
        Assert.Equal(0, await ContarAsync($"SELECT COUNT(*) FROM sys.views WHERE name IN ({VISTAS})"));
    }
}
