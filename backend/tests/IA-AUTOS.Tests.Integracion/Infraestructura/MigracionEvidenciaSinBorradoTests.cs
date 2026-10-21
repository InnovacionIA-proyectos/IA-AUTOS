using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/09-evidencia-sin-borrado.sql) contra un SQL Server 2019 con el esquema
/// PREVIO (Esquemas/ddl-previo-a-evidencia.sql, el que ya esta desplegado) y con datos reales: no se pierde ningun dato, los vehiculos
/// quedan activos, las relaciones dejan de borrar en cascada y la evidencia deja de poder borrarse (error 51003).
/// </summary>
public sealed partial class MigracionEvidenciaSinBorradoTests : IAsyncLifetime
{
    private static readonly Guid VEHICULO_CON_ETAPAS = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid VEHICULO_SIN_ETAPAS = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CERRADA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BORRADOR_VACIO = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BORRADOR_CON_HALLAZGO = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionEvidencia");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionEvidencia" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-evidencia.sql"));
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

    /// <summary>El error SQL que produce una sentencia (nulo si no falla).</summary>
    private async Task<int?> ErrorDeAsync(string sql)
    {
        try
        {
            await EjecutarAsync(_cadena, sql);
            return null;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    private static async Task<string> ScriptMigracionAsync()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "09-evidencia-sin-borrado.sql");
        return (await File.ReadAllTextAsync(ruta)).Replace("N'db_IA_AUTOS'", "N'dbo'");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Un viaje con un vehiculo y dos etapas (cargue y transporte) y otro vehiculo sin etapas. En el cargue: una salida cerrada con una foto y
    /// una transferencia, y una llegada vacia en borrador. En el transporte: una salida en borrador con un hallazgo.
    /// </summary>
    private const string DATOS_PREVIOS = """
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @u uniqueidentifier = NEWID(), @c uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@u, N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@c, N'Carlos', 'cond@e.com', 'conductor', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES
         ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'VINCONETAPAS00001', N'Renault', 'nacional', @cli),
         ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'VINSINETAPAS00001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, ConductorId, InspectorId) VALUES (@vj, 'MTK-26', N'ABC123', @c, @u);
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa');
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        DECLARE @transp uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte');
        DECLARE @e1 uniqueidentifier = NEWID(), @e2 uniqueidentifier = NEWID();
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES
         (@e1, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', @vj, @cargue, 1), (@e2, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', @vj, @transp, 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES
         ('11111111-1111-1111-1111-111111111111', @e1, @vj, @u, 'salida', '2026-07-01T08:00:00+00:00'),
         ('22222222-2222-2222-2222-222222222222', @e1, @vj, @u, 'llegada', '2026-07-02T08:00:00+00:00'),
         ('33333333-3333-3333-3333-333333333333', @e2, @vj, @u, 'salida', '2026-07-03T08:00:00+00:00');
        INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'a.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1);
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES
         (NEWID(), '33333333-3333-3333-3333-333333333333', N'Capot', 'golpe', '1_5cm');
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmadaAt, TipoCierre) VALUES
         (NEWID(), '11111111-1111-1111-1111-111111111111', @u, @c, N'M0 0', N'M1 1', SYSDATETIMEOFFSET(), 'firmada');
        INSERT dbo.tblAuditLog (Id, UsuarioId, Accion) VALUES (NEWID(), @u, N'prueba');
        UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = '2026-07-01T09:00:00+00:00' WHERE Id = '11111111-1111-1111-1111-111111111111';
        """;

    [Fact]
    public async Task La_migracion_conserva_los_datos_activa_los_vehiculos_y_quita_las_cascadas_que_borraban_la_evidencia()
    {
        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action = 1 AND name IN (N'FK_tblViajeVehiculos_Vehiculo', N'FK_tblEtapas_Vehiculo', N'FK_tblInspecciones_Etapa', N'FK_tblHallazgos_Inspeccion', N'FK_tblEvidencias_Inspeccion', N'FK_tblTransferencias_Inspeccion')"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Ningun dato se pierde y todos los vehiculos quedan activos.
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Activo = 1"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_tblVehiculos_Activo' AND object_id = OBJECT_ID(N'dbo.tblVehiculos')"));

        // Las seis relaciones ya no borran en cascada; se conservan solo la de un viaje hacia sus vehiculos y la de un cuadrante hacia sus regiones.
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action = 1 AND name IN (N'FK_tblViajeVehiculos_Vehiculo', N'FK_tblEtapas_Vehiculo', N'FK_tblInspecciones_Etapa', N'FK_tblHallazgos_Inspeccion', N'FK_tblEvidencias_Inspeccion', N'FK_tblTransferencias_Inspeccion')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action = 1 AND name = N'FK_tblViajeVehiculos_Viaje'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action = 1 AND name = N'FK_tblCuadranteVistas_Cuadrante'"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name LIKE N'TR[_]%[_]NoBorrar' AND is_instead_of_trigger = 1 AND OBJECT_NAME(parent_id) NOT IN (N'tblHallazgoSeguimiento', N'tblNotificaciones', N'tblNotasAclaratorias')"));

        // Repetir el script no cambia nada (ya esta aplicado).
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Activo = 1"));
    }

    [Fact]
    public async Task Despues_de_migrar_la_evidencia_no_se_borra_y_solo_un_borrador_vacio_se_puede_quitar()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblEvidencias"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblHallazgos"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblAuditLog"));
        Assert.Equal(51003, await ErrorDeAsync($"DELETE FROM dbo.tblInspecciones WHERE Id = '{CERRADA}'"));
        Assert.Equal(51003, await ErrorDeAsync($"DELETE FROM dbo.tblInspecciones WHERE Id = '{BORRADOR_CON_HALLAZGO}'"));

        // El borrador vacio si se borra; lo demas sigue ahi.
        Assert.Null(await ErrorDeAsync($"DELETE FROM dbo.tblInspecciones WHERE Id = '{BORRADOR_VACIO}'"));
        Assert.Equal(0, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Id = '{BORRADOR_VACIO}'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias"));

        // Sin cascadas: un vehiculo o una etapa con registros debajo no se borra (547); un vehiculo sin etapas si.
        Assert.Equal(547, await ErrorDeAsync($"DELETE FROM dbo.tblVehiculos WHERE Id = '{VEHICULO_CON_ETAPAS}'"));
        Assert.Equal(547, await ErrorDeAsync("DELETE FROM dbo.tblEtapas WHERE Orden = 1"));
        Assert.Null(await ErrorDeAsync($"DELETE FROM dbo.tblVehiculos WHERE Id = '{VEHICULO_SIN_ETAPAS}'"));

        // Un viaje sin inspecciones sigue arrastrando a sus vehiculos (esa cascada se conserva).
        await EjecutarAsync(_cadena, "INSERT dbo.tblViajes (Id, CodigoMtrack) VALUES ('cccccccc-cccc-cccc-cccc-cccccccccccc', 'MTK-VACIO'); INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), 'cccccccc-cccc-cccc-cccc-cccccccccccc', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')");
        Assert.Null(await ErrorDeAsync("DELETE FROM dbo.tblViajes WHERE Id = 'cccccccc-cccc-cccc-cccc-cccccccccccc'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = 'cccccccc-cccc-cccc-cccc-cccccccccccc'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_encima_de_una_base_ya_migrada_sin_cambiar_nada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Activo = 1"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name LIKE N'TR[_]%[_]NoBorrar' AND is_instead_of_trigger = 1 AND OBJECT_NAME(parent_id) NOT IN (N'tblHallazgoSeguimiento', N'tblNotificaciones', N'tblNotasAclaratorias')"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblEvidencias"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_sobre_la_base_anterior_sin_fallar_y_no_crea_triggers_donde_aun_hay_cascadas()
    {
        // Antes de migrar: el ddl agrega la columna Activo, pero no puede crear un trigger de borrado en una tabla que aun tiene una relacion en cascada.
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Activo = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name LIKE N'TR[_]%[_]NoBorrar' AND is_instead_of_trigger = 1 AND OBJECT_NAME(parent_id) NOT IN (N'tblHallazgoSeguimiento', N'tblNotificaciones', N'tblNotasAclaratorias')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name = N'TR_tblAuditLog_NoBorrar'"));
    }
}
