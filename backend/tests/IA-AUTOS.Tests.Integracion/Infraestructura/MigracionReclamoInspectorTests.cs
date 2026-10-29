using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/26-reclamo-inspector.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-reclamo-inspector.sql, el que ya esta
/// desplegado) y con un viaje que ya tiene su inspector: crea los puntos del inspector y el reclamo (sin tocar nada de lo que ya estaba), el indice que impide dos reclamos activos del mismo viaje y proceso, el
/// historial que no se borra y el parametro del aviso de inactividad.
/// </summary>
public sealed partial class MigracionReclamoInspectorTests : IAsyncLifetime
{
    private const string VIAJE = "11111111-1111-1111-1111-111111111111";
    private const string OTRO_VIAJE = "11111111-1111-1111-1111-222222222222";
    private const string INSPECTOR = "33333333-3333-3333-3333-333333333331";
    private const string COORDINADOR = "33333333-3333-3333-3333-333333333332";
    private const string OTRO_INSPECTOR = "33333333-3333-3333-3333-333333333333";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionReclamo");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionReclamo" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-reclamo-inspector.sql"));
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

    /// <summary>El numero de error de SQL Server que devuelve la sentencia (0 si se ejecuta sin error).</summary>
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "26-reclamo-inspector.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un viaje que ya tiene su inspector (lo que ya estaba guardado antes de migrar), otro sin inspector, tres usuarios y un lugar del catalogo.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{INSPECTOR}}', N'Esleider', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{COORDINADOR}}', N'Luisa', 'coord@e.com', 'coordinador_calidad', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{OTRO_INSPECTOR}}', N'Andres', 'andres@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, InspectorId) VALUES ('{{VIAJE}}', 'M-6001', N'ABC123', '{{INSPECTOR}}');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES ('{{OTRO_VIAJE}}', 'M-6002', N'DEF456');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES (NEWID(), N'CEDI Envigado', (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1'), N'Envigado');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES (NEWID(), N'Puerto Cartagena', (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_2'), N'Cartagena');
        """;

    private static string Reclamo(string viaje, string inspector, string proceso, string extra = "") =>
        $"INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso) VALUES (NEWID(), '{viaje}', '{inspector}', '{proceso}'){extra}";

    private static string Punto(string usuario, string lugar) =>
        $"INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), '{usuario}', (SELECT Id FROM dbo.tblLugares WHERE Nombre = N'{lugar}'))";

    // ------------------------------------------------------------------------------------------------ migracion

    [Fact]
    public async Task La_migracion_crea_las_tablas_el_indice_el_historial_y_el_parametro_sin_cambiar_ningun_dato_y_se_puede_repetir()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblInspectorLugares', 'tblReclamos')"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'reclamo_sin_actividad_minutos'"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblInspectorLugares', 'tblReclamos')"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspectorLugares"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblReclamos_Activo' AND is_unique = 1 AND has_filter = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name = 'TR_tblReclamos_NoBorrar' AND is_disabled = 0"));
        // CA-3: el parametro es un numero con descripcion y valor inicial 30.
        Assert.Equal(
            "30|numero",
            await TextoAsync("SELECT CONCAT(Valor, '|', Tipo) FROM dbo.tblParametros WHERE Clave = 'reclamo_sin_actividad_minutos' AND LEN(Descripcion) > 0"));
        // Lo que ya existia queda igual: el viaje conserva su inspector y nadie se asigna a un punto ni reclama nada.
        Assert.Equal(INSPECTOR, await TextoAsync($"SELECT CAST(InspectorId AS varchar(36)) FROM dbo.tblViajes WHERE Id = '{VIAJE}'"), ignoreCase: true);
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajes WHERE Id = '{OTRO_VIAJE}' AND InspectorId IS NULL"));

        // CA-4: repetir el script no cambia nada, ni siquiera un valor del parametro que Calidad ya ajusto.
        await EjecutarAsync(_cadena, "UPDATE dbo.tblParametros SET Valor = N'45' WHERE Clave = 'reclamo_sin_actividad_minutos'");
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblInspectorLugares', 'tblReclamos')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'reclamo_sin_actividad_minutos'"));
        Assert.Equal("45", await TextoAsync("SELECT Valor FROM dbo.tblParametros WHERE Clave = 'reclamo_sin_actividad_minutos'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblInspectorLugares', 'tblReclamos')"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblInspectorLugares', 'tblReclamos')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblReclamos_Activo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'reclamo_sin_actividad_minutos'"));
        Assert.Equal(INSPECTOR, await TextoAsync($"SELECT CAST(InspectorId AS varchar(36)) FROM dbo.tblViajes WHERE Id = '{VIAJE}'"), ignoreCase: true);
    }

    // ------------------------------------------------------------------------------------------------ lo nuevo funciona

    [Fact]
    public async Task Un_inspector_puede_tener_varios_puntos_pero_no_el_mismo_dos_veces_y_el_punto_es_un_lugar_del_catalogo()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // CA-1: la relacion usuario y lugar del catalogo, unica por pareja, con mas de un punto por inspector.
        Assert.Equal(0, await ErrorDeAsync(Punto(INSPECTOR, "CEDI Envigado")));
        Assert.Equal(0, await ErrorDeAsync(Punto(INSPECTOR, "Puerto Cartagena")));
        Assert.Equal(0, await ErrorDeAsync(Punto(OTRO_INSPECTOR, "CEDI Envigado")));
        Assert.Equal(2627, await ErrorDeAsync(Punto(INSPECTOR, "CEDI Envigado")));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), '{INSPECTOR}', NEWID())"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), NEWID(), (SELECT TOP 1 Id FROM dbo.tblLugares))"));
        // El punto es un lugar real, no un tipo del catalogo generico de puntos de custodia.
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), '{COORDINADOR}', (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1'))"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspectorLugares"));
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblInspectorLugares WHERE UsuarioId = '{INSPECTOR}'"));
    }

    [Fact]
    public async Task El_reclamo_guarda_quien_y_cuando_y_un_indice_impide_dos_reclamos_activos_del_mismo_viaje_y_proceso()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // CA-2: un reclamo activo por viaje y proceso; el proceso es cargue o entrega.
        Assert.Equal(547, await ErrorDeAsync(Reclamo(VIAJE, INSPECTOR, "descargue")));
        Assert.Equal(547, await ErrorDeAsync(Reclamo(OTRO_VIAJE, "33333333-3333-3333-3333-999999999999", "cargue")));
        Assert.Equal(547, await ErrorDeAsync(Reclamo("11111111-1111-1111-1111-999999999999", INSPECTOR, "cargue")));
        Assert.Equal(0, await ErrorDeAsync(Reclamo(VIAJE, INSPECTOR, "cargue")));
        Assert.Equal(2601, await ErrorDeAsync(Reclamo(VIAJE, OTRO_INSPECTOR, "cargue")));
        Assert.Equal(0, await ErrorDeAsync(Reclamo(VIAJE, OTRO_INSPECTOR, "entrega")));
        Assert.Equal(0, await ErrorDeAsync(Reclamo(OTRO_VIAJE, OTRO_INSPECTOR, "cargue")));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = '{VIAJE}' AND Proceso = 'cargue' AND InspectorId = '{INSPECTOR}' AND ReclamadoAt IS NOT NULL AND LiberadoAt IS NULL AND LiberadoPor IS NULL"));

        // Liberar guarda quien y cuando (juntos); el reclamo liberado queda como historial y el cargue se puede reclamar de nuevo.
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblReclamos SET LiberadoAt = SYSDATETIMEOFFSET() WHERE ViajeId = '{VIAJE}' AND Proceso = 'cargue'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblReclamos SET LiberadoPor = '{COORDINADOR}' WHERE ViajeId = '{VIAJE}' AND Proceso = 'cargue'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblReclamos SET LiberadoAt = DATEADD(DAY, -1, ReclamadoAt), LiberadoPor = '{COORDINADOR}' WHERE ViajeId = '{VIAJE}' AND Proceso = 'cargue'"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblReclamos SET LiberadoAt = SYSDATETIMEOFFSET(), LiberadoPor = '{COORDINADOR}' WHERE ViajeId = '{VIAJE}' AND Proceso = 'cargue' AND LiberadoAt IS NULL"));
        Assert.Equal(0, await ErrorDeAsync(Reclamo(VIAJE, OTRO_INSPECTOR, "cargue")));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = '{VIAJE}' AND Proceso = 'cargue' AND LiberadoPor = '{COORDINADOR}'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = '{VIAJE}' AND Proceso = 'cargue' AND LiberadoAt IS NULL"));
        // El historial no se borra.
        Assert.Equal(51003, await ErrorDeAsync($"DELETE FROM dbo.tblReclamos WHERE ViajeId = '{VIAJE}'"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos"));
    }

    [Fact]
    public async Task El_viaje_conserva_su_inspector_cuando_se_reclama_y_el_parametro_lo_ajusta_Calidad_sin_cambiar_el_esquema()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // El reclamo no reemplaza a tblViajes.InspectorId: el campo existe, con su llave foranea, y lo llena el reclamo del cargue.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblViajes_Inspector'"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblViajes SET InspectorId = '{OTRO_INSPECTOR}' WHERE Id = '{OTRO_VIAJE}'"));
        Assert.Equal(0, await ErrorDeAsync("UPDATE dbo.tblParametros SET Valor = N'20', UpdatedAt = SYSDATETIMEOFFSET() WHERE Clave = 'reclamo_sin_actividad_minutos'"));
        Assert.Equal("20", await TextoAsync("SELECT Valor FROM dbo.tblParametros WHERE Clave = 'reclamo_sin_actividad_minutos'"));
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblParametros SET Tipo = N'minutos' WHERE Clave = 'reclamo_sin_actividad_minutos'"));
    }
}
