using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/07-inspecciones-salida-llegada.sql) contra un SQL Server 2019 con el
/// esquema PREVIO (Esquemas/ddl-previo-a-inspecciones.sql, el que ya esta desplegado) y con inspecciones y fotos reales, para confirmar que
/// los momentos historicos pasan a salida o llegada sin perder ninguna inspeccion, que las repetidas quedan como versiones encadenadas y
/// que las inspecciones cerradas siguen cerradas (el trigger de inmutabilidad vuelve a quedar activo).
/// </summary>
public sealed partial class MigracionInspeccionesTests : IAsyncLifetime
{
    private static readonly Guid INFORME_CERRADO = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CHECK_IN_BORRADOR = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CHECK_OUT_CERRADO = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid INFORME_BORRADOR = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionInspecciones");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-inspecciones.sql"));
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
        var ruta = Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "07-inspecciones-salida-llegada.sql");
        return (await File.ReadAllTextAsync(ruta)).Replace("N'db_IA_AUTOS'", "N'dbo'");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Un viaje con un VIN y sus 3 etapas. En el cargue: un informe cerrado, un check_in en borrador (mismo momento nuevo, salida) y un
    /// check_out cerrado (llegada). En el transporte: un informe en borrador. Dos fotos: una con ubicacion y otra sin ella.
    /// </summary>
    private const string DATOS_PREVIOS = """
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @u uniqueidentifier = NEWID(), @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@u, N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINPREVIO24000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES (@vj, 'MTK-24', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        DECLARE @transp uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte');
        DECLARE @e1 uniqueidentifier = NEWID(), @e2 uniqueidentifier = NEWID();
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e1, @v, @vj, @cargue, 1), (@e2, @v, @vj, @transp, 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Estado, CreatedAt, UpdatedAt) VALUES
         ('11111111-1111-1111-1111-111111111111', @e1, @vj, @u, 'informe', '2026-07-01T08:00:00+00:00', 'borrador', '2026-07-01T08:00:00+00:00', '2026-07-01T09:00:00+00:00'),
         ('22222222-2222-2222-2222-222222222222', @e1, @vj, @u, 'check_in', '2026-07-02T08:00:00+00:00', 'borrador', '2026-07-02T08:00:00+00:00', '2026-07-02T08:00:00+00:00'),
         ('33333333-3333-3333-3333-333333333333', @e1, @vj, @u, 'check_out', '2026-07-03T08:00:00+00:00', 'borrador', '2026-07-03T08:00:00+00:00', '2026-07-03T09:00:00+00:00'),
         ('44444444-4444-4444-4444-444444444444', @e2, @vj, @u, 'informe', '2026-07-04T08:00:00+00:00', 'borrador', '2026-07-04T08:00:00+00:00', '2026-07-04T08:00:00+00:00');
        INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES
         (NEWID(), '33333333-3333-3333-3333-333333333333', N'a.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1),
         (NEWID(), '44444444-4444-4444-4444-444444444444', N'b.jpg', SYSDATETIMEOFFSET(), NULL, NULL);
        UPDATE dbo.tblInspecciones SET Estado = 'cerrada' WHERE Id IN ('11111111-1111-1111-1111-111111111111', '33333333-3333-3333-3333-333333333333');
        """;

    [Fact]
    public async Task La_migracion_pasa_los_momentos_a_salida_y_llegada_encadena_las_repetidas_y_conserva_todo()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Ninguna inspeccion se pierde ni se reabre.
        Assert.Equal(4, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(2, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Estado = 'cerrada'"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Momento NOT IN ('salida', 'llegada')"));

        // informe y check_in -> salida (la mas antigua es la original); check_out -> llegada.
        Assert.Equal("salida", await EscalarAsync<string>($"SELECT Momento FROM dbo.tblInspecciones WHERE Id = '{INFORME_CERRADO}'"));
        Assert.Equal(1, await EscalarAsync<int>($"SELECT Version FROM dbo.tblInspecciones WHERE Id = '{INFORME_CERRADO}'"));
        Assert.Null(await EscalarAsync<Guid?>($"SELECT CorrigeInspeccionId FROM dbo.tblInspecciones WHERE Id = '{INFORME_CERRADO}'"));
        Assert.Equal("salida", await EscalarAsync<string>($"SELECT Momento FROM dbo.tblInspecciones WHERE Id = '{CHECK_IN_BORRADOR}'"));
        Assert.Equal(2, await EscalarAsync<int>($"SELECT Version FROM dbo.tblInspecciones WHERE Id = '{CHECK_IN_BORRADOR}'"));
        Assert.Equal(INFORME_CERRADO, await EscalarAsync<Guid>($"SELECT CorrigeInspeccionId FROM dbo.tblInspecciones WHERE Id = '{CHECK_IN_BORRADOR}'"));
        Assert.Equal("llegada", await EscalarAsync<string>($"SELECT Momento FROM dbo.tblInspecciones WHERE Id = '{CHECK_OUT_CERRADO}'"));
        Assert.Equal(1, await EscalarAsync<int>($"SELECT Version FROM dbo.tblInspecciones WHERE Id = '{CHECK_OUT_CERRADO}'"));
        Assert.Equal("salida", await EscalarAsync<string>($"SELECT Momento FROM dbo.tblInspecciones WHERE Id = '{INFORME_BORRADOR}'"));

        // Hora de inicio = creacion; hora de cierre solo en las cerradas (= su ultima modificacion).
        Assert.Equal(2, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Estado = 'cerrada' AND CerradaAt = UpdatedAt"));
        Assert.Equal(2, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Estado = 'borrador' AND CerradaAt IS NULL"));
        Assert.Equal(4, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE IniciadaAt = CreatedAt"));

        // Las fotos conservan su ubicacion; la que no tenia queda en (0, 0) = sin ubicacion; el sello del servidor es su fecha de creacion.
        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE SelloLat = 4.6 AND SelloLng = -74.1"));
        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE SelloLat = 0 AND SelloLng = 0"));
        Assert.Equal(2, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE SelloServidorAt = CreatedAt AND VistaId IS NULL AND HashSha256 IS NULL"));

        // El trigger de inmutabilidad volvio a quedar activo: una cerrada no se modifica.
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM sys.triggers WHERE name = 'TR_tblInspecciones_Inmutable' AND is_disabled = 1"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, $"UPDATE dbo.tblInspecciones SET Notas = N'cambio' WHERE Id = '{INFORME_CERRADO}'"));

        // Reglas nuevas: una sola original por etapa y momento, cada version se corrige una vez, el momento anterior ya no existe.
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena,
            "INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) SELECT NEWID(), EtapaId, ViajeId, UsuarioId, 'salida', SYSDATETIMEOFFSET() FROM dbo.tblInspecciones WHERE Id = '" + INFORME_CERRADO + "'"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena,
            "INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Version, CorrigeInspeccionId) SELECT NEWID(), EtapaId, ViajeId, UsuarioId, 'salida', SYSDATETIMEOFFSET(), 3, '" + INFORME_CERRADO + "' FROM dbo.tblInspecciones WHERE Id = '" + INFORME_CERRADO + "'"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena,
            "INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) SELECT NEWID(), EtapaId, ViajeId, UsuarioId, 'informe', SYSDATETIMEOFFSET() FROM dbo.tblInspecciones WHERE Id = '" + INFORME_CERRADO + "'"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena,
            "INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), '" + INFORME_BORRADOR + "', N'c.jpg', SYSDATETIMEOFFSET(), NULL, NULL)"));

        // Repetir el script no cambia nada (ya esta aplicado).
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(4, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_encima_de_una_base_ya_migrada_sin_cambiar_nada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(4, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblInspecciones_Etapa_Momento_Original' AND has_filter = 1"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_sobre_la_base_anterior_sin_fallar()
    {
        // Antes de migrar: el ddl no toca las tablas anteriores (la migracion las cambia) pero tampoco se rompe.
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(4, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblInspecciones') AND name = 'IniciadaAt'"));
    }
}
