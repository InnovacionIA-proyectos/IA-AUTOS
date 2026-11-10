using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// El usuario de la aplicacion no borra evidencia ni historial (S6-HU-03.2): ejecuta el script real (database/cambios-esquema/41-usuario-de-la-app-sin-borrado.sql) sobre una base con el usuario ya creado (miembro
/// de db_datawriter, que incluye DELETE sobre todo) y comprueba, conectando COMO ese usuario, que ya no puede borrar de las 8 tablas de evidencia e historial, que conserva leer, insertar y actualizar en ellas y que
/// sigue borrando lo que la API si borra. Lo mismo para una base nueva con database/01-crear-usuario-app.sql, y el script se puede repetir.
/// </summary>
public sealed partial class MigracionUsuarioDeLaAppSinBorradoTests : IAsyncLifetime
{
    private const string CLAVE = "Clave-App-Segura-1";

    private static readonly string[] CON_EVIDENCIA =
    [
        "tblEvidencias", "tblHallazgos", "tblHallazgoSeguimiento", "tblNotasAclaratorias", "tblTransferenciasCustodia", "tblNotificaciones", "tblAuditLog", "tblVehiculos",
    ];

    private static readonly string[] QUE_SI_BORRA = ["tblViajes", "tblEtapas", "tblViajeVehiculos", "tblUsuarios"];

    private readonly string _usuario = $"iaautos_app_{Guid.NewGuid():N}"[..24];
    private string _cadena = string.Empty;
    private string _maestra = string.Empty;
    private string _baseDeDatos = string.Empty;

    public async Task InitializeAsync()
    {
        _maestra = await clsServidorSql.CadenaMaestraAsync();
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionSinBorrado");
        _baseDeDatos = new SqlConnectionStringBuilder(_cadena).InitialCatalog;
        await clsServidorSql.EjecutarScriptAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await clsServidorSql.EliminarBaseAsync(_cadena);
        await using var conexion = new SqlConnection(_maestra);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand($"IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{_usuario}') DROP LOGIN [{_usuario}];", conexion);
        await comando.ExecuteNonQueryAsync();
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Ejecuta un script con GO en UNA conexion (como sqlcmd: el USE de un lote vale para los siguientes).</summary>
    private static async Task EjecutarEnUnaConexionAsync(string cadena, string contenido)
    {
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

    /// <summary>Lo que hacia el 01 antes: el usuario existe y es miembro de db_datareader y db_datawriter, sin DENY.</summary>
    private async Task CrearUsuarioSinDenyAsync() =>
        await EjecutarEnUnaConexionAsync(
            _maestra,
            $"""
            CREATE LOGIN [{_usuario}] WITH PASSWORD = N'{CLAVE}', CHECK_POLICY = OFF, DEFAULT_DATABASE = [{_baseDeDatos}];
            GO
            USE [{_baseDeDatos}];
            GO
            CREATE USER [{_usuario}] FOR LOGIN [{_usuario}] WITH DEFAULT_SCHEMA = [dbo];
            ALTER ROLE db_datareader ADD MEMBER [{_usuario}];
            ALTER ROLE db_datawriter ADD MEMBER [{_usuario}];
            """);

    private async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "41-usuario-de-la-app-sin-borrado.sql")))
            .Replace("N'db_IA_AUTOS'", "N'dbo'").Replace("N'iaautos_app'", $"N'{_usuario}'");

    /// <summary>La conexion de la aplicacion: con el login del usuario de la app, no con sa.</summary>
    private SqlConnection ComoLaApp() =>
        new(new SqlConnectionStringBuilder(_cadena) { UserID = _usuario, Password = CLAVE, Pooling = false }.ConnectionString);

    private async Task<bool> TienePermisoAsync(string tabla, string permiso)
    {
        await using var conexion = ComoLaApp();
        await conexion.OpenAsync();
        await using var comando = new SqlCommand($"SELECT CAST(HAS_PERMS_BY_NAME(N'dbo.{tabla}', N'OBJECT', N'{permiso}') AS int)", conexion);
        return Convert.ToInt32(await comando.ExecuteScalarAsync()) == 1;
    }

    private async Task<int?> NumeroDeErrorAlBorrarAsync(string tabla)
    {
        await using var conexion = ComoLaApp();
        await conexion.OpenAsync();
        try
        {
            await using var comando = new SqlCommand($"DELETE FROM dbo.{tabla} WHERE 1 = 0", conexion);
            await comando.ExecuteNonQueryAsync();
            return null;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    private async Task<(int Tablas, int SinDelete)> ResultadoAsync()
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(
            "SELECT (SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(@u) AND permission_name = N'DELETE' AND state = N'D'), 0", conexion);
        comando.Parameters.AddWithValue("@u", _usuario);
        await using var lector = await comando.ExecuteReaderAsync();
        await lector.ReadAsync();
        return (CON_EVIDENCIA.Length, lector.GetInt32(0));
    }

    [Fact]
    public async Task Antes_de_migrar_el_usuario_de_la_app_puede_borrar_de_todo()
    {
        await CrearUsuarioSinDenyAsync();

        foreach (var tabla in CON_EVIDENCIA)
        {
            Assert.True(await TienePermisoAsync(tabla, "DELETE"), $"{tabla} deberia poder borrarse antes de migrar");
        }
    }

    [Fact]
    public async Task Migrar_le_quita_el_delete_sobre_la_evidencia_y_el_historial_y_conserva_lo_demas()
    {
        await CrearUsuarioSinDenyAsync();

        await EjecutarEnUnaConexionAsync(_cadena, await ScriptMigracionAsync());

        foreach (var tabla in CON_EVIDENCIA)
        {
            Assert.False(await TienePermisoAsync(tabla, "DELETE"), $"{tabla} ya no deberia poder borrarse");
            Assert.Equal(229, await NumeroDeErrorAlBorrarAsync(tabla));
            foreach (var permiso in new[] { "SELECT", "INSERT", "UPDATE" })
            {
                Assert.True(await TienePermisoAsync(tabla, permiso), $"{tabla} deberia conservar {permiso}");
            }
        }

        // Lo que la API si borra (etapas y viajes sin inspecciones, usuarios sin historial) sigue permitido.
        foreach (var tabla in QUE_SI_BORRA)
        {
            Assert.True(await TienePermisoAsync(tabla, "DELETE"), $"{tabla} deberia seguir pudiendose borrar");
            Assert.Null(await NumeroDeErrorAlBorrarAsync(tabla));
        }

        Assert.Equal(CON_EVIDENCIA.Length, (await ResultadoAsync()).SinDelete);
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_falla_claro_si_el_usuario_no_existe()
    {
        await CrearUsuarioSinDenyAsync();

        await EjecutarEnUnaConexionAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarEnUnaConexionAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(CON_EVIDENCIA.Length, (await ResultadoAsync()).SinDelete);
        var inexistente = (await ScriptMigracionAsync()).Replace($"N'{_usuario}'", "N'no_existe_nadie'");
        var error = await Assert.ThrowsAsync<SqlException>(() => EjecutarEnUnaConexionAsync(_cadena, inexistente));
        Assert.Equal(50002, error.Number);
    }

    [Fact]
    public async Task Una_base_nueva_con_el_01_ya_deja_al_usuario_sin_delete_y_repetirlo_no_cambia_nada()
    {
        var script = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "01-crear-usuario-app.sql")))
            .Replace("$(USUARIO_APP)", _usuario).Replace("$(PASSWORD_APP)", CLAVE).Replace("$(BASE)", _baseDeDatos).Replace("CHECK_POLICY = ON", "CHECK_POLICY = OFF");

        await EjecutarEnUnaConexionAsync(_maestra, script);
        await EjecutarEnUnaConexionAsync(_maestra, script);

        foreach (var tabla in CON_EVIDENCIA)
        {
            Assert.Equal(229, await NumeroDeErrorAlBorrarAsync(tabla));
            Assert.True(await TienePermisoAsync(tabla, "INSERT"), $"{tabla} deberia conservar INSERT");
        }

        Assert.Null(await NumeroDeErrorAlBorrarAsync("tblViajes"));
        Assert.Equal(CON_EVIDENCIA.Length, (await ResultadoAsync()).SinDelete);
    }
}
