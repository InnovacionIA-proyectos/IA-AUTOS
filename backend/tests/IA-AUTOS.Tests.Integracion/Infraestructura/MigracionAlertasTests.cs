using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/17-parametros-de-alertas.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-alertas.sql, el que ya
/// esta desplegado, con tblParametros) y con datos: se agregan los parametros de las alertas y la vista de la ventana de umbral, sin tocar ningun dato ni sobrescribir un valor ya cambiado.
/// </summary>
public sealed partial class MigracionAlertasTests : IAsyncLifetime
{
    private const string USUARIO = "33333333-3333-3333-3333-333333333333";
    private const string INSPECCION_CARGUE = "11111111-1111-1111-1111-111111111111";
    private const string INSPECCION_TRANSPORTE = "22222222-2222-2222-2222-222222222222";
    private const string INSPECCION_BORRADOR = "55555555-5555-5555-5555-555555555555";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionAlertas");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-alertas.sql"));
        await EjecutarAsync(_cadena, DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

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

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "17-parametros-de-alertas.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Una unidad con la salida de su Cargue (cerrada, con un hallazgo del inspector en una zona provisional), la salida de su Transporte (cerrada, con una novedad del conductor sin revisar)
    /// y una inspeccion de llegada en borrador con un hallazgo.
    /// </summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID(), @ec uniqueidentifier = NEWID(), @et uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINALERT28000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES (@vj, 'MTK-41', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@ec, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@et, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte'), 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_CARGUE}}', @ec, @vj, '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_TRANSPORTE}}', @et, @vj, '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_BORRADOR}}', @ec, @vj, '{{USUARIO}}', 'llegada', SYSDATETIMEOFFSET());
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad)
        VALUES (NEWID(), '{{INSPECCION_CARGUE}}', (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'), 'golpe', '5_10cm', 'v2');
        INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad) VALUES (NEWID(), '{{INSPECCION_TRANSPORTE}}', '5_10cm', 'conductor', 'golpe');
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad)
        VALUES (NEWID(), '{{INSPECCION_BORRADOR}}', (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'), 'golpe', '5_10cm', 'v1');
        UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id IN ('{{INSPECCION_CARGUE}}', '{{INSPECCION_TRANSPORTE}}');
        """;

    [Fact]
    public async Task La_migracion_agrega_los_cinco_parametros_y_la_vista_sin_cambiar_ningun_dato()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwHallazgosVentanaUmbral'"));
        var parametrosAntes = await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwHallazgosVentanaUmbral'"));
        Assert.Equal(parametrosAntes + 5, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'niveles_criticos' AND Valor = N'v1_plus,v1' AND Tipo = 'lista'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'umbral_patron_cantidad' AND Valor = N'10' AND Tipo = 'numero'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'umbral_patron_ventana_dias' AND Valor = N'30' AND Tipo = 'numero'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'captura_incompleta_minutos' AND Valor = N'30' AND Tipo = 'numero'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'transferencia_pendiente_minutos' AND Valor = N'240' AND Tipo = 'numero'"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task La_vista_trae_los_hallazgos_de_inspecciones_cerradas_con_cliente_zona_proceso_y_fecha()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Solo el hallazgo del inspector del Cargue cerrado: la novedad sin revisar y el hallazgo de la inspeccion en borrador no entran.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwHallazgosVentanaUmbral"));
        Assert.Equal(1, await ContarAsync(
            $"SELECT COUNT(*) FROM dbo.vwHallazgosVentanaUmbral w JOIN dbo.tblVehiculos v ON v.Id = w.VehiculoId WHERE w.InspeccionId = '{INSPECCION_CARGUE}' AND w.ClienteId = v.ClienteId "
            + "AND w.ZonaCodigo = 'frente' AND w.Numero IS NULL AND w.Lado IS NULL AND w.Proceso = 'cargue' AND w.Fecha IS NOT NULL AND w.ViajeId IS NOT NULL"));

        // La novedad revisada entra con su proceso; al cerrar la inspeccion en borrador entra su hallazgo.
        await EjecutarAsync(_cadena, "UPDATE dbo.tblHallazgos SET ClaseDano = 'rotura', NivelSeveridad = 'v1', ZonaId = (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'), RevisadoAt = SYSDATETIMEOFFSET() WHERE Origen = 'conductor'");
        await EjecutarAsync(_cadena, $"UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = '{INSPECCION_BORRADOR}'");
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.vwHallazgosVentanaUmbral"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwHallazgosVentanaUmbral WHERE Proceso = 'transporte'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwHallazgosVentanaUmbral WHERE Proceso = 'cargue'"));
    }

    [Fact]
    public async Task Repetir_la_migracion_o_el_ddl_no_sobrescribe_un_valor_ya_cambiado()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, "UPDATE dbo.tblParametros SET Valor = N'v1_plus' WHERE Clave = 'niveles_criticos'; DELETE FROM dbo.tblParametros WHERE Clave = 'umbral_patron_cantidad'");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'niveles_criticos' AND Valor = N'v1_plus'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'umbral_patron_cantidad' AND Valor = N'10'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwHallazgosVentanaUmbral'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwHallazgosVentanaUmbral'"));
        Assert.Equal(15, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwHallazgosVentanaUmbral"));
    }

    [Fact]
    public async Task Sin_la_tabla_de_parametros_la_migracion_se_detiene_sin_cambiar_nada()
    {
        await EjecutarAsync(_cadena, "DROP TABLE dbo.tblParametros");

        var ex = await Assert.ThrowsAsync<SqlException>(async () => await EjecutarAsync(_cadena, await ScriptMigracionAsync()));

        Assert.Contains("16-tabla-parametros.sql", ex.Message);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwHallazgosVentanaUmbral'"));
    }
}
