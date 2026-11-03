using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/06-identidad.sql) contra un SQL Server 2019 con el esquema PREVIO a la
/// identidad por cedula y PIN (Esquemas/ddl-previo-a-identidad.sql, el que ya esta desplegado) y con usuarios y vehiculos reales, para
/// confirmar que la migracion conserva los datos, da el cliente actual a todos y no rompe el ingreso de nadie.
/// </summary>
public sealed partial class MigracionIdentidadTests : IAsyncLifetime
{
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionIdentidad");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-identidad.sql"));
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

    private async Task<T?> EscalarAsync<T>(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        var resultado = await comando.ExecuteScalarAsync();
        return resultado is null or DBNull ? default : (T)Convert.ChangeType(resultado, typeof(T));
    }

    private static async Task<string> ScriptMigracionAsync()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "06-identidad.sql");
        return (await File.ReadAllTextAsync(ruta)).Replace("N'db_IA_AUTOS'", "N'dbo'");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un super administrador, un admin, un inspector y un conductor (con cedula) con contrasena, y dos vehiculos.</summary>
    private const string DATOS_PREVIOS = """
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, Cedula, PasswordHash) VALUES
         (NEWID(), N'Sara Super', 'super@e.com', 'super_admin', NULL, 'hash-super'),
         (NEWID(), N'Ada Admin', 'adm@e.com', 'admin', NULL, 'hash-admin'),
         (NEWID(), N'Ines Inspectora', 'insp@e.com', 'inspector', NULL, 'hash-insp'),
         (NEWID(), N'Carlos Conductor', 'cond@e.com', 'conductor', '1234567', 'hash-cond');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion) VALUES
         (NEWID(), 'VINPREVIO00000001', N'Renault', 'nacional'),
         (NEWID(), 'VINPREVIO00000002', N'Renault', 'importacion');
        """;

    [Fact]
    public async Task La_migracion_da_el_cliente_actual_conserva_los_usuarios_y_es_repetible()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Cliente actual sembrado.
        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblClientes WHERE Codigo = 'sofasa'"));

        // Todos los vehiculos y todos los usuarios, salvo el super administrador, quedan en el cliente actual.
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE ClienteId IS NULL"));
        Assert.Equal(2, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblVehiculos v JOIN dbo.tblClientes c ON c.Id = v.ClienteId WHERE c.Codigo = 'sofasa'"));
        Assert.Equal(3, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblUsuarios u JOIN dbo.tblClientes c ON c.Id = u.ClienteId WHERE c.Codigo = 'sofasa' AND u.Rol <> 'super_admin'"));
        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE Rol = 'super_admin' AND ClienteId IS NULL"));

        // Nadie pierde correo, contrasena ni cedula, y todos siguen entrando con contrasena.
        Assert.Equal(4, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE TipoCredencial = 'password' AND Email IS NOT NULL AND PasswordHash IS NOT NULL"));
        Assert.Equal("1234567", await EscalarAsync<string>("SELECT Cedula FROM dbo.tblUsuarios WHERE Email = 'cond@e.com'"));
        Assert.Equal(4, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE DebeCambiarPin = 0 AND IntentosFallidos = 0 AND BloqueadoHasta IS NULL AND PinHash IS NULL AND UltimoAccesoAt IS NULL"));

        // Ahora el correo es opcional y unico entre quienes lo tienen; las reglas de credencial estan activas.
        await EjecutarAsync(_cadena, "INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, Cedula, TipoCredencial, PinHash) VALUES (NEWID(), N'A', NULL, 'conductor', '1111111', 'pin', 'h'), (NEWID(), N'B', NULL, 'conductor', '2222222', 'pin', 'h')");
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, "INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES (NEWID(), N'Repetido', 'ADM@E.COM', 'admin')"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, "INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, TipoCredencial) VALUES (NEWID(), N'Sin correo', NULL, 'admin', 'password')"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, "INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, TipoCredencial, PinHash) VALUES (NEWID(), N'Sin cedula', 'x@e.com', 'conductor', 'pin', 'h')"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, "INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (NEWID(), 'VINSINCLIENTE0001', N'X', 'nacional', NULL)"));

        // Un cliente en uso no se borra.
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, "DELETE FROM dbo.tblClientes WHERE Codigo = 'sofasa'"));

        // Repetir el script no cambia nada (ya esta aplicado).
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblClientes"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_encima_de_una_base_ya_migrada_sin_cambiar_nada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblClientes"));
        Assert.Equal(4, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblUsuarios"));
        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblUsuarios_Email' AND has_filter = 1"));
    }
}
