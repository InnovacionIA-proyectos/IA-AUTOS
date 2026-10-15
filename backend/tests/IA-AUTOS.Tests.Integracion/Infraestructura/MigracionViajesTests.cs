using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/05-viajes.sql) contra un SQL Server 2019 con el esquema PREVIO a los
/// viajes (Esquemas/ddl-previo-a-viajes.sql, el que ya esta desplegado) y datos reales de asignaciones, etapas e informes, para
/// confirmar que la migracion no pierde ni corrompe datos. Los demas tests usan ddl.sql ya migrado (base nueva, sin datos previos) y
/// nunca ejercitan el script de migracion en si mismo.
/// </summary>
public sealed partial class MigracionViajesTests : IAsyncLifetime
{
    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionViajes");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionViajes" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-viajes.sql"));
        await EjecutarAsync(_cadena, DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await _contenedor.DisposeAsync();

    private static async Task EjecutarAsync(string cadena, string sql)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion);
        await comando.ExecuteNonQueryAsync();
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
        var ruta = Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "05-viajes.sql");
        return (await File.ReadAllTextAsync(ruta)).Replace("N'db_IA_AUTOS'", "N'dbo'");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// 6 vehiculos con sus 3 etapas y usuarios de cada rol. V1 y V2 comparten placa, conductor y dia de asignacion; V3 tiene otra;
    /// V6 tiene dos asignaciones (gana la reciente); V4 tiene informes y ninguna asignacion; V5 no tiene nada. V1 y V3 tienen todos sus
    /// informes cerrados; V2 solo el de cargue; V4 el de cargue cerrado y el de transporte en borrador.
    /// </summary>
    private const string DATOS_PREVIOS = """
        DECLARE @insp uniqueidentifier = NEWID(), @cond1 uniqueidentifier = NEWID(), @cond2 uniqueidentifier = NEWID(), @adm uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES
         (@insp, N'Ines Inspectora', 'insp@e.com', 'inspector'), (@cond1, N'Carlos Conductor', 'c1@e.com', 'conductor'),
         (@cond2, N'Cora Conductora', 'c2@e.com', 'conductor'), (@adm, N'Ada Admin', 'adm@e.com', 'admin');

        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        DECLARE @transp uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte');
        DECLARE @descar uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue');

        DECLARE @v TABLE (n int, Id uniqueidentifier);
        INSERT @v VALUES (1, NEWID()), (2, NEWID()), (3, NEWID()), (4, NEWID()), (5, NEWID()), (6, NEWID());
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion) SELECT Id, 'VINPREVIO0000000' + CAST(n AS varchar(2)), N'Renault', 'nacional' FROM @v;
        INSERT dbo.tblEtapas (Id, VehiculoId, EtapaCatalogoId, Orden)
        SELECT NEWID(), v.Id, x.Etapa, x.Orden FROM @v v CROSS JOIN (VALUES (@cargue, 1), (@transp, 2), (@descar, 3)) AS x (Etapa, Orden);

        DECLARE @id1 uniqueidentifier = (SELECT Id FROM @v WHERE n = 1), @id2 uniqueidentifier = (SELECT Id FROM @v WHERE n = 2),
                @id3 uniqueidentifier = (SELECT Id FROM @v WHERE n = 3), @id6 uniqueidentifier = (SELECT Id FROM @v WHERE n = 6);
        INSERT dbo.tblAsignaciones (Id, VehiculoId, Ninera, ConductorId, AsignadoPor, CreatedAt) VALUES
         (NEWID(), @id1, N'ABC123', @cond1, @adm, '2026-07-01T08:00:00+00:00'),
         (NEWID(), @id2, N'ABC123', @cond1, @adm, '2026-07-01T09:30:00+00:00'),
         (NEWID(), @id3, N'XYZ789', @cond2, @adm, '2026-07-02T08:00:00+00:00'),
         (NEWID(), @id6, N'VIEJA01', @cond1, @adm, '2026-06-01T08:00:00+00:00'),
         (NEWID(), @id6, N'NUEVA02', NULL, @adm, '2026-07-03T08:00:00+00:00');

        DECLARE @e TABLE (VehiculoN int, Orden int, Id uniqueidentifier);
        INSERT @e SELECT v.n, e.Orden, e.Id FROM dbo.tblEtapas e JOIN @v v ON v.Id = e.VehiculoId;
        INSERT dbo.tblInspecciones (Id, EtapaId, UsuarioId, Momento, Fecha, Estado, Notas)
        SELECT NEWID(), e.Id, @cond1, 'informe', SYSDATETIMEOFFSET(), 'borrador', N'V1 etapa ' + CAST(e.Orden AS varchar(2)) FROM @e e WHERE e.VehiculoN = 1;
        INSERT dbo.tblInspecciones (Id, EtapaId, UsuarioId, Momento, Fecha, Estado) SELECT NEWID(), e.Id, @cond1, 'informe', SYSDATETIMEOFFSET(), 'borrador' FROM @e e WHERE e.VehiculoN = 2 AND e.Orden = 1;
        INSERT dbo.tblInspecciones (Id, EtapaId, UsuarioId, Momento, Fecha, Estado) SELECT NEWID(), e.Id, @cond2, 'informe', SYSDATETIMEOFFSET(), 'borrador' FROM @e e WHERE e.VehiculoN = 3;
        INSERT dbo.tblInspecciones (Id, EtapaId, UsuarioId, Momento, Fecha, Estado, Placa) SELECT NEWID(), e.Id, @insp, 'informe', SYSDATETIMEOFFSET(), 'borrador', N'SUELTA9' FROM @e e WHERE e.VehiculoN = 4 AND e.Orden IN (1, 2);
        UPDATE dbo.tblInspecciones SET Estado = 'cerrada' WHERE EtapaId IN (SELECT Id FROM @e WHERE VehiculoN IN (1, 2, 3) OR (VehiculoN = 4 AND Orden = 1));

        INSERT dbo.tblOrdenesSofasa (Id, SourceId, Vin) VALUES (NEWID(), 1001, 'VINPREVIO00000001'), (NEWID(), 1002, 'VINQUENOEXISTE00');
        """;

    [Fact]
    public async Task La_migracion_agrupa_las_asignaciones_en_viajes_y_conserva_informes_etapas_y_accesos()
    {
        var inspeccionesAntes = await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones");
        var cerradasAntes = await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Estado = 'cerrada'");
        var notaDeV1Antes = await EscalarAsync<string>("SELECT TOP 1 Notas FROM dbo.tblInspecciones WHERE Notas LIKE 'V1 etapa 1'");
        var actualizadaAntes = await EscalarAsync<string>("SELECT CONVERT(varchar(40), UpdatedAt, 127) FROM dbo.tblInspecciones WHERE Notas LIKE 'V1 etapa 1'");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Tres viajes de asignacion (V1 y V2 juntos; V3; V6 con su asignacion reciente) y uno del vehiculo con informes y sin asignacion.
        Assert.Equal(4, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblViajes"));
        Assert.Equal(5, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
        Assert.Equal(2, await EscalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblViajes v ON v.Id = vv.ViajeId WHERE v.Placa = N'ABC123'"));
        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblViajes WHERE Placa = N'NUEVA02'"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblViajes WHERE Placa = N'VIEJA01'"));
        Assert.Equal(3, await EscalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.tblViajes v JOIN dbo.tblTiposUnidadTransportadora t ON t.Id = v.TipoUnidadId WHERE t.Codigo = 'ninera'"));
        Assert.Equal("Carlos Conductor", await EscalarAsync<string>("SELECT u.Nombre FROM dbo.tblViajes v JOIN dbo.tblUsuarios u ON u.Id = v.ConductorId WHERE v.Placa = N'ABC123'"));
        Assert.Equal("Ada Admin", await EscalarAsync<string>("SELECT u.Nombre FROM dbo.tblViajes v JOIN dbo.tblUsuarios u ON u.Id = v.CreadoPor WHERE v.Placa = N'XYZ789'"));

        // El vehiculo con informes y sin asignacion queda en un viaje propio con su placa y el inspector que hizo sus informes.
        Assert.Equal("Ines Inspectora", await EscalarAsync<string>("SELECT u.Nombre FROM dbo.tblViajes v JOIN dbo.tblUsuarios u ON u.Id = v.InspectorId WHERE v.Placa = N'SUELTA9'"));
        Assert.Null(await EscalarAsync<string>("SELECT TOP 1 CAST(TipoUnidadId AS varchar(40)) FROM dbo.tblViajes WHERE Placa = N'SUELTA9'"));

        // Estados segun sus informes: V3 completo = finalizado; V1/V2 y el suelto = en curso; V6 sin informes = programado.
        Assert.Equal("finalizado", await EscalarAsync<string>("SELECT Estado FROM dbo.tblViajes WHERE Placa = N'XYZ789'"));
        Assert.Equal("en_curso", await EscalarAsync<string>("SELECT Estado FROM dbo.tblViajes WHERE Placa = N'ABC123'"));
        Assert.Equal("en_curso", await EscalarAsync<string>("SELECT Estado FROM dbo.tblViajes WHERE Placa = N'SUELTA9'"));
        Assert.Equal("programado", await EscalarAsync<string>("SELECT Estado FROM dbo.tblViajes WHERE Placa = N'NUEVA02'"));

        // Los informes y sus datos siguen intactos (incluidos los cerrados, que son inmutables) y todos cuelgan de un viaje.
        Assert.Equal(inspeccionesAntes, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(cerradasAntes, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Estado = 'cerrada'"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE ViajeId IS NULL"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblInspecciones i JOIN dbo.tblEtapas e ON e.Id = i.EtapaId WHERE i.ViajeId <> e.ViajeId"));
        Assert.Equal(notaDeV1Antes, await EscalarAsync<string>("SELECT TOP 1 Notas FROM dbo.tblInspecciones WHERE Notas LIKE 'V1 etapa 1'"));
        Assert.Equal(actualizadaAntes, await EscalarAsync<string>("SELECT CONVERT(varchar(40), UpdatedAt, 127) FROM dbo.tblInspecciones WHERE Notas LIKE 'V1 etapa 1'"));
        Assert.Equal("SUELTA9", await EscalarAsync<string>("SELECT TOP 1 Placa FROM dbo.tblInspecciones WHERE Placa IS NOT NULL"));

        // Las etapas de vehiculos con asignacion o informes pasan a su viaje; el esqueleto de V5 (sin nada) se borra.
        Assert.Equal(15, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblEtapas"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblEtapas WHERE ViajeId IS NULL"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = 'VINPREVIO00000005'"));
        Assert.Equal(1, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Vin = 'VINPREVIO00000005'"));

        // La orden Sofasa de un VIN en viaje queda vinculada; la de un VIN desconocido no.
        Assert.Equal("ABC123", await EscalarAsync<string>("SELECT v.Placa FROM dbo.tblOrdenesSofasa o JOIN dbo.tblViajes v ON v.Id = o.ViajeId WHERE o.SourceId = 1001"));
        Assert.Null(await EscalarAsync<string>("SELECT CAST(ViajeId AS varchar(40)) FROM dbo.tblOrdenesSofasa WHERE SourceId = 1002"));

        // Vista nueva, vista vieja y tabla retiradas; el trigger de inmutabilidad vuelve a estar activo.
        Assert.Equal(2, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.vwViajesCompletos"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM sys.views WHERE name = 'vwVehiculosMovilizados'"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblAsignaciones'"));
        Assert.Equal(0, await EscalarAsync<int>("SELECT is_disabled FROM sys.triggers WHERE name = 'TR_tblInspecciones_Inmutable'"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, "UPDATE dbo.tblInspecciones SET Notas = N'cambio' WHERE Estado = 'cerrada'"));
    }

    [Fact]
    public async Task La_migracion_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_sobre_la_base_migrada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        var viajes = await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblViajes");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(viajes, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblViajes"));
        Assert.Equal(15, await EscalarAsync<int>("SELECT COUNT(*) FROM dbo.tblEtapas"));
    }

    [Fact]
    public async Task La_base_migrada_acepta_los_datos_nuevos_con_las_mismas_reglas_que_una_base_nueva()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Codigo MTrack unico, VIN unico por viaje y un VIN en dos viajes (como la base nueva).
        await EjecutarAsync(_cadena, "INSERT dbo.tblViajes (Id, CodigoMtrack) VALUES (NEWID(), 'MTK-M1')");
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, "INSERT dbo.tblViajes (Id, CodigoMtrack) VALUES (NEWID(), 'mtk-m1')"));
        await EjecutarAsync(_cadena, "INSERT dbo.tblViajes (Id) VALUES (NEWID()), (NEWID())");
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, "INSERT dbo.tblViajes (Id, Estado) VALUES (NEWID(), 'volando')"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena,
            "INSERT dbo.tblEtapas (Id, VehiculoId, EtapaCatalogoId, ViajeId, Orden) SELECT NEWID(), VehiculoId, EtapaCatalogoId, NEWID(), 9 FROM dbo.tblEtapas"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena, "INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, Cedula) VALUES (NEWID(), N'A', 'a1@e.com', 'conductor', '123456')"
            + "; INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, Cedula) VALUES (NEWID(), N'B', 'a2@e.com', 'conductor', '123456')"));
    }
}
