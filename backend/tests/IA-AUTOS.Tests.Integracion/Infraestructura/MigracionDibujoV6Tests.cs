using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de resiembra (database/cambios-esquema/34-resembrar-dibujo-v6.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-dibujo-v6.sql, el que ya esta desplegado) con un
/// dibujo viejo, hallazgos en sus zonas (numeradas y provisionales) y un poligono de respaldo: desvincula los hallazgos sin tocar su parte, borra el dibujo viejo, siembra los 7 paneles, 45 cuadrantes, 80 zonas y 251 regiones
/// del archivo, la base impone la regla de lado desde el cuadrante, y repetirlo no cambia nada, actualiza en sitio si solo cambian textos (sin desvincular) y resiembra si cambia la estructura.
/// </summary>
public sealed partial class MigracionDibujoV6Tests : IAsyncLifetime
{
    private const string HALLAZGO_NUMERADO = "77777777-7777-7777-7777-777777777771";
    private const string HALLAZGO_PROVISIONAL = "77777777-7777-7777-7777-777777777772";
    private const string INSPECCION = "77777777-7777-7777-7777-777777777761";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionDibujo");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-dibujo-v6.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un dibujo viejo (cuadrantes 7 con lado y 30 sin lado, las zonas provisionales del ddl), dos hallazgos (uno en la zona izquierdo 7 y otro en «frente») y un poligono del respaldo.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @c7 uniqueidentifier = NEWID(), @c30 uniqueidentifier = NEWID();
        INSERT dbo.tblCuadrantes (Id, TipoVehiculo, Numero, Nombre) VALUES (@c7, NULL, 7, N'Puerta'), (@c30, NULL, 30, N'Techo');
        DECLARE @z7 uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE CuadranteId = @c7 AND Lado = 'izquierdo');
        DECLARE @frente uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente');
        DECLARE @vista uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblVistasFoto);
        INSERT dbo.tblCuadranteVistas (Id, CuadranteId, VistaFotoId, Geometria) VALUES (NEWID(), @c7, @vista, N'{"puntos":[[0,0],[1,0],[1,1]]}');
        DECLARE @vj uniqueidentifier = NEWID(), @v uniqueidentifier = NEWID(), @e uniqueidentifier = NEWID(), @u uniqueidentifier = NEWID();
        DECLARE @tipo uniqueidentifier = (SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera');
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES (@u, N'Marcos Inspector', N'marcos@ejemplo.com', 'inspector');
        INSERT dbo.tblViajes (Id, CodigoMtrack, TipoUnidadId, Placa, Estado) VALUES (@vj, 'MTK-D1', @tipo, 'PLA001', 'programado');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) SELECT @v, 'VIN-DIB-1', 'Renault', 'nacional', Id FROM dbo.tblClientes WHERE Codigo = 'sofasa';
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e, @v, @vj, @cargue, 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Lat, Lng, Estado) VALUES ('{{INSPECCION}}', @e, @vj, @u, 'llegada', SYSDATETIMEOFFSET(), 4.6, -74.1, 'borrador');
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad, ParteVehiculo) VALUES
            ('{{HALLAZGO_NUMERADO}}', '{{INSPECCION}}', @z7, 'golpe', '1_5cm', 'v1', N'Puerta'),
            ('{{HALLAZGO_PROVISIONAL}}', '{{INSPECCION}}', @frente, 'golpe', '1_5cm', 'v1', NULL);
        """;

    private async Task EjecutarAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion);
        await preparar.ExecuteNonQueryAsync();
        await using var principal = new SqlCommand(sql, conexion) { CommandTimeout = 180 };
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "34-resembrar-dibujo-v6.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    private async Task AfirmarDibujoV6Async()
    {
        Assert.Equal(7, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPanelesDano"));
        Assert.Equal(45, await ContarAsync("SELECT COUNT(*) FROM dbo.tblCuadrantes"));
        Assert.Equal(80, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano"));
        Assert.Equal(251, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano"));
    }

    [Fact]
    public async Task Resembrar_deja_el_dibujo_del_archivo_desvincula_los_hallazgos_y_borra_el_dibujo_viejo()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        await AfirmarDibujoV6Async();
        // 35 cuadrantes laterales con lado (70 zonas) y 10 centrales sin lado; ninguna zona provisional ni del dibujo viejo.
        Assert.Equal(35, await ContarAsync("SELECT COUNT(*) FROM dbo.tblCuadrantes WHERE LlevaLado = 1"));
        Assert.Equal(70, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE Lado IS NOT NULL"));
        Assert.Equal(10, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE Lado IS NULL AND Numero BETWEEN 50 AND 59"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblCuadrantes WHERE Nombre IN (N'Puerta', N'Techo')"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblCuadranteVistas"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.tblZonasDano') AND name IN (N'Codigo', N'Nombre')"));
        // Paneles con su tamano de referencia y su version; el guardafango delantero izquierdo con su nombre completo y su codigo AIAG (texto).
        Assert.Equal(7, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPanelesDano WHERE Version = 6 AND AnchoRef > 0 AND AltoRef > 0 AND ImagenPath IS NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE Numero = 4 AND Lado = 'izquierdo' AND NombreCompleto = N'Guardafango delantero izquierdo' AND AiagArea = N'14/16'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano z JOIN dbo.tblCuadrantes c ON c.Id = z.CuadranteId WHERE z.Numero = 54 AND z.Lado IS NULL AND c.Nombre = N'Parabrisas' AND z.AiagArea = N'20'"));
        // Las ruedas: llanta en 6 y 26 y rin en 34 y 35.
        Assert.Equal(4, await ContarAsync("SELECT COUNT(DISTINCT Numero) FROM dbo.tblZonasDano WHERE Numero IN (6, 26, 34, 35)"));
        // Los hallazgos quedan sin zona y pendientes de ubicar, conservando su parte.
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id IN ('{HALLAZGO_NUMERADO}', '{HALLAZGO_PROVISIONAL}') AND ZonaId IS NULL AND MigracionPendiente = 1"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = '{HALLAZGO_NUMERADO}' AND ParteVehiculo = N'Puerta'"));
    }

    [Fact]
    public async Task La_base_impone_la_regla_de_lado_desde_el_cuadrante_y_no_hay_ningun_rango_de_numeros()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Una zona sin lado en un cuadrante con lado, o con lado en uno sin el, no se puede guardar.
        var cuadranteConLado = "(SELECT Id FROM dbo.tblCuadrantes WHERE Numero = 4)";
        var cuadranteSinLado = "(SELECT Id FROM dbo.tblCuadrantes WHERE Numero = 54)";
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblZonasDano (Id, CuadranteId, Numero, LlevaLado, Lado) VALUES (NEWID(), {cuadranteConLado}, 4, 1, NULL)"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblZonasDano (Id, CuadranteId, Numero, LlevaLado, Lado) VALUES (NEWID(), {cuadranteSinLado}, 54, 0, 'izquierdo')"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblZonasDano (Id, CuadranteId, Numero, LlevaLado, Lado) VALUES (NEWID(), {cuadranteSinLado}, 54, 1, 'derecho')"));
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblCuadrantes SET LlevaLado = 0 WHERE Numero = 4"));
        // Un cuadrante nuevo con numero alto que lleva lado produce dos zonas y uno bajo que no lo lleva, una: el disparador lee el atributo.
        Assert.Equal(0, await ErrorDeAsync("INSERT dbo.tblCuadrantes (Id, Numero, Nombre, LlevaLado) VALUES (NEWID(), 90, N'Pieza nueva con lado', 1), (NEWID(), 91, N'Pieza nueva central', 0)"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE Numero = 90 AND Lado IN ('izquierdo', 'derecho')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE Numero = 91 AND Lado IS NULL"));
        // Ya no existen las zonas provisionales: una zona sin cuadrante no es una forma valida.
        Assert.Equal(515, await ErrorDeAsync("INSERT dbo.tblZonasDano (Id, Numero, LlevaLado, Lado) VALUES (NEWID(), 99, 0, NULL)"));
    }

    [Fact]
    public async Task Repetir_el_script_sin_cambios_no_toca_nada_ni_desvincula_los_hallazgos_ubicados_en_el_dibujo_nuevo()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync("UPDATE dbo.tblHallazgos SET ZonaId = (SELECT Id FROM dbo.tblZonasDano WHERE Numero = 4 AND Lado = 'izquierdo'), MigracionPendiente = 0 WHERE Id = '" + HALLAZGO_NUMERADO + "'");

        await EjecutarAsync(await ScriptMigracionAsync());

        await AfirmarDibujoV6Async();
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = '{HALLAZGO_NUMERADO}' AND ZonaId IS NOT NULL AND MigracionPendiente = 0"));
    }

    [Fact]
    public async Task Si_el_archivo_solo_cambia_textos_se_actualizan_en_sitio_y_los_hallazgos_conservan_su_zona()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync("UPDATE dbo.tblHallazgos SET ZonaId = (SELECT Id FROM dbo.tblZonasDano WHERE Numero = 4 AND Lado = 'izquierdo'), MigracionPendiente = 0 WHERE Id = '" + HALLAZGO_NUMERADO + "'");
        // Como si la base tuviera el archivo anterior a la correccion del codigo AIAG (sigue en la version 6).
        await EjecutarAsync("UPDATE dbo.tblZonasDano SET AiagArea = N'viejo', NombreCompleto = N'viejo' WHERE Numero = 4 AND Lado = 'izquierdo'; UPDATE dbo.tblPanelesDano SET AnchoRef = 1 WHERE Codigo = 'frontal'");

        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE Numero = 4 AND Lado = 'izquierdo' AND AiagArea = N'14/16' AND NombreCompleto = N'Guardafango delantero izquierdo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPanelesDano WHERE Codigo = 'frontal' AND AnchoRef = 1027"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = '{HALLAZGO_NUMERADO}' AND ZonaId IS NOT NULL AND MigracionPendiente = 0"));
        await AfirmarDibujoV6Async();
    }

    [Fact]
    public async Task Si_cambia_la_estructura_del_dibujo_se_resiembra_y_los_hallazgos_quedan_sin_zona()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync("UPDATE dbo.tblHallazgos SET ZonaId = (SELECT Id FROM dbo.tblZonasDano WHERE Numero = 4 AND Lado = 'izquierdo'), MigracionPendiente = 0 WHERE Id = '" + HALLAZGO_NUMERADO + "'");
        await EjecutarAsync("UPDATE TOP (1) dbo.tblRegionesDano SET Prioridad = 9");

        await EjecutarAsync(await ScriptMigracionAsync());

        await AfirmarDibujoV6Async();
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano WHERE Prioridad = 9"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = '{HALLAZGO_NUMERADO}' AND ZonaId IS NULL AND MigracionPendiente = 1"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        // Antes de migrar el ddl no toca el dibujo viejo: sigue con sus zonas provisionales y su disparador.
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE Codigo IS NOT NULL"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        await AfirmarDibujoV6Async();
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblZonasDano_Forma' AND definition LIKE '%LlevaLado%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblZonasDano_Cuadrante'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.vwAveriasPorConfiguracion') AND name = N'ZonaCodigo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.vwAveriasPorConfiguracion') AND name = N'AiagArea'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.vwHallazgosVentanaUmbral') AND name = N'ZonaCodigo'"));
    }

    [Fact]
    public async Task Una_base_nueva_creada_con_el_ddl_y_luego_resembrada_queda_igual_que_una_migrada()
    {
        // Una base creada de cero con el ddl nuevo no trae dibujo: el mismo guion lo siembra.
        var cadenaNueva = await clsServidorSql.CrearBaseAsync("IAAutosDibujoNuevo");
        try
        {
            await clsServidorSql.EjecutarScriptAsync(cadenaNueva, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
            await using (var conexion = new SqlConnection(cadenaNueva))
            {
                await conexion.OpenAsync();
                await using var vacio = new SqlCommand("SELECT (SELECT COUNT(*) FROM dbo.tblPanelesDano) + (SELECT COUNT(*) FROM dbo.tblZonasDano)", conexion);
                Assert.Equal(0, Convert.ToInt32(await vacio.ExecuteScalarAsync()));
                await using var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion);
                await preparar.ExecuteNonQueryAsync();
                await using var guion = new SqlCommand(await ScriptMigracionAsync(), conexion) { CommandTimeout = 180 };
                await guion.ExecuteNonQueryAsync();
                await using var conteos = new SqlCommand("SELECT CONCAT((SELECT COUNT(*) FROM dbo.tblPanelesDano), '/', (SELECT COUNT(*) FROM dbo.tblCuadrantes), '/', (SELECT COUNT(*) FROM dbo.tblZonasDano), '/', (SELECT COUNT(*) FROM dbo.tblRegionesDano))", conexion);
                Assert.Equal("7/45/80/251", (string)(await conteos.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            await clsServidorSql.EliminarBaseAsync(cadenaNueva);
        }
    }
}
