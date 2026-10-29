using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/16-tabla-parametros.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-parametros.sql, el que ya
/// esta desplegado): se crea tblParametros con sus ocho filas iniciales, sin tocar ningun dato existente ni sobrescribir un valor ya cambiado.
/// </summary>
public sealed partial class MigracionParametrosTests : IAsyncLifetime
{
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionParametros");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-parametros.sql"));
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

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "16-tabla-parametros.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    [Fact]
    public async Task La_migracion_crea_la_tabla_con_sus_ocho_parametros_iniciales()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblParametros'"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblParametros'"));
        Assert.Equal(8, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave IN ('objetivo_diario_novedad', 'reporte_diario_destinatarios', 'reporte_diario_hora', 'horario_operacion_inicio', 'horario_operacion_fin', 'retencion_dias', 'gps_precision_minima_m', 'url_firmada_minutos')"));
        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Valor IS NOT NULL"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave IN ('reporte_diario_destinatarios', 'retencion_dias') AND Valor IS NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'objetivo_diario_novedad' AND Valor = N'1.2' AND Tipo = 'porcentaje'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Tipo = 'hora' AND Clave LIKE 'horario_operacion_%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'url_firmada_minutos' AND Valor = N'15' AND Tipo = 'numero'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave LIKE 'linea_base_rechazo%' OR Clave LIKE '%fotos_minimas%'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE ActualizadoPor IS NOT NULL"));
    }

    [Fact]
    public async Task Repetir_la_migracion_o_el_ddl_no_sobrescribe_un_valor_ya_cambiado()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, "UPDATE dbo.tblParametros SET Valor = N'2.5' WHERE Clave = 'objetivo_diario_novedad'; DELETE FROM dbo.tblParametros WHERE Clave = 'url_firmada_minutos'");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(8, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave IN ('objetivo_diario_novedad', 'reporte_diario_destinatarios', 'reporte_diario_hora', 'horario_operacion_inicio', 'horario_operacion_fin', 'retencion_dias', 'gps_precision_minima_m', 'url_firmada_minutos')"));
        // El valor cambiado se conserva y la fila que faltaba se vuelve a crear con su valor inicial.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'objetivo_diario_novedad' AND Valor = N'2.5'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'url_firmada_minutos' AND Valor = N'15'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblParametros'"));
        Assert.Equal(8, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave IN ('objetivo_diario_novedad', 'reporte_diario_destinatarios', 'reporte_diario_hora', 'horario_operacion_inicio', 'horario_operacion_fin', 'retencion_dias', 'gps_precision_minima_m', 'url_firmada_minutos')"));
    }

    [Fact]
    public async Task Cada_parametro_tiene_un_tipo_valido_una_clave_unica_y_un_valor_no_vacio()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        string Insertar(string clave, string valor, string tipo = "numero") =>
            $"INSERT dbo.tblParametros (Clave, Valor, Tipo, Descripcion) VALUES ('{clave}', {valor}, '{tipo}', N'prueba')";

        Assert.Equal(0, await ErrorDeAsync(Insertar("nuevo_1", "N'5'")));
        Assert.Equal(0, await ErrorDeAsync(Insertar("nuevo_2", "NULL", "lista")));
        Assert.Equal(2627, await ErrorDeAsync(Insertar("nuevo_1", "N'6'")));
        Assert.Equal(547, await ErrorDeAsync(Insertar("nuevo_3", "N'6'", "fecha")));
        Assert.Equal(547, await ErrorDeAsync(Insertar("nuevo_4", "N''", "texto")));
        foreach (var tipo in new[] { "numero", "porcentaje", "texto", "lista", "hora", "correos" })
        {
            Assert.Equal(0, await ErrorDeAsync(Insertar($"tipo_{tipo}", "N'x'", tipo)));
        }
    }

    [Fact]
    public async Task Quien_actualiza_un_parametro_es_un_usuario_que_existe()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        var cliente = "(SELECT TOP 1 Id FROM dbo.tblClientes)";
        await EjecutarAsync(_cadena, $"INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('44444444-4444-4444-4444-444444444444', N'Ana', 'ana@e.com', 'admin', {cliente}, 'h')");

        Assert.Equal(0, await ErrorDeAsync("UPDATE dbo.tblParametros SET Valor = N'9', ActualizadoPor = '44444444-4444-4444-4444-444444444444', UpdatedAt = SYSDATETIMEOFFSET() WHERE Clave = 'gps_precision_minima_m'"));
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblParametros SET ActualizadoPor = '99999999-9999-9999-9999-999999999999' WHERE Clave = 'gps_precision_minima_m'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'gps_precision_minima_m' AND Valor = N'9' AND ActualizadoPor IS NOT NULL"));
    }
}
