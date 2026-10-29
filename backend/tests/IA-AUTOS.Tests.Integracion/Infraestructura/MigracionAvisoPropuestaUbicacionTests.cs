using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/33-tipo-de-aviso-propuesta-de-ubicacion.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-aviso-propuesta-ubicacion.sql, el que ya
/// esta desplegado): antes de migrar la base rechaza el tipo de notificacion propuesta_ubicacion; despues lo admite sin quitar ninguno de los anteriores ni dejar pasar uno inventado, las notificaciones que ya existian se
/// conservan y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionAvisoPropuestaUbicacionTests : IAsyncLifetime
{
    private const string USUARIO = "66666666-6666-6666-6666-666666666681";

    private static readonly string[] TIPOS_ANTERIORES =
    [
        "novedad_conductor", "respuesta_conductor", "hallazgo_critico", "umbral_patron", "captura_incompleta", "transferencia_pendiente", "reporte_diario", "novedad_proceso", "reclamo_sin_actividad", "fuera_de_rango", "cargue_sin_reclamar",
    ];

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionAvisoPropuesta");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-aviso-propuesta-ubicacion.sql"));
        await EjecutarAsync($"INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES ('{USUARIO}', N'Diana Coordinadora', N'diana@ejemplo.com', 'coordinador_calidad');");
        await EjecutarAsync(Notificacion("fuera_de_rango", "previa"));
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

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

    private static string Notificacion(string tipo, string clave) =>
        $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), '{tipo}', 'media', '{USUARIO}', N'T', N'M', 'bandeja', '{clave}')";

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "33-tipo-de-aviso-propuesta-de-ubicacion.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_la_base_rechaza_el_tipo_propuesta_ubicacion()
    {
        Assert.Equal(547, await ErrorDeAsync(Notificacion("propuesta_ubicacion", "k0")));
    }

    [Fact]
    public async Task Migrar_admite_propuesta_ubicacion_conserva_los_tipos_y_las_notificaciones_y_rechaza_uno_inventado()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(Notificacion("propuesta_ubicacion", "k1")));
        foreach (var (tipo, i) in TIPOS_ANTERIORES.Select((t, i) => (t, i)))
        {
            Assert.Equal(0, await ErrorDeAsync(Notificacion(tipo, $"anterior-{i}")));
        }

        Assert.Equal(547, await ErrorDeAsync(Notificacion("inventado", "k2")));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa'"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_deja_una_sola_restriccion_con_los_doce_tipos()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo' AND definition LIKE '%propuesta_ubicacion%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo' AND definition LIKE '%propuesta_ubicacion%'"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo' AND definition LIKE '%propuesta_ubicacion%'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("propuesta_ubicacion", "k3")));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa'"));
    }
}
