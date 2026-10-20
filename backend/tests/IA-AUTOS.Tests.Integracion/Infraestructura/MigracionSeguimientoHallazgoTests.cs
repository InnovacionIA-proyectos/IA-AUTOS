using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/11-seguimiento-hallazgo.sql) contra un SQL Server 2019 con el esquema PREVIO
/// (Esquemas/ddl-previo-a-seguimiento.sql, el que ya esta desplegado) y con hallazgos reales: se crea el historial de seguimiento (que nunca se
/// modifica ni se borra), el estado del hallazgo sigue al ultimo cambio y la vista de patrones cuenta por zona (lado + numero) solo los hallazgos
/// de inspecciones cerradas, sin las novedades del conductor sin revisar.
/// </summary>
public sealed partial class MigracionSeguimientoHallazgoTests : IAsyncLifetime
{
    private static readonly Guid H_IZQUIERDO = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid H_DERECHO = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    private static readonly Guid H_CONDUCTOR = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");
    private static readonly Guid H_BORRADOR = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4");

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionSeguimiento");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionSeguimiento" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-seguimiento.sql"));
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

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "11-seguimiento-hallazgo.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Un cuadrante 7 (con sus zonas izquierdo y derecho) y una inspeccion de cargue con hallazgos en izquierdo 7, derecho 7 y una novedad del conductor
    /// sin revisar; la inspeccion se cierra. Una segunda inspeccion queda en borrador con un hallazgo en izquierdo 7 (no debe contar).
    /// </summary>
    private const string DATOS_PREVIOS = """
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @u uniqueidentifier = NEWID(), @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID(), @e1 uniqueidentifier = NEWID(), @e2 uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@u, N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINPREVIO27000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES (@vj, 'MTK-28', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES
         (@e1, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1),
         (@e2, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte'), 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES
         ('11111111-1111-1111-1111-111111111111', @e1, @vj, @u, 'salida', SYSDATETIMEOFFSET()),
         ('22222222-2222-2222-2222-222222222222', @e2, @vj, @u, 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblCuadrantes (Id, TipoVehiculo, Numero, Nombre) VALUES ('cccccccc-cccc-cccc-cccc-cccccccccccc', NULL, 7, N'Puerta delantera');
        DECLARE @zi uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE CuadranteId = 'cccccccc-cccc-cccc-cccc-cccccccccccc' AND Lado = 'izquierdo');
        DECLARE @zd uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE CuadranteId = 'cccccccc-cccc-cccc-cccc-cccccccccccc' AND Lado = 'derecho');
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES
         ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', '11111111-1111-1111-1111-111111111111', @zi, 'golpe', '1_5cm', 'v1_plus'),
         ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2', '11111111-1111-1111-1111-111111111111', @zd, 'golpe', '1_5cm', 'v1_plus'),
         ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4', '22222222-2222-2222-2222-222222222222', @zi, 'golpe', '1_5cm', 'v2');
        INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad) VALUES
         ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', '11111111-1111-1111-1111-111111111111', '1_5cm', 'conductor', 'granizo');
        UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = '11111111-1111-1111-1111-111111111111';
        """;

    [Fact]
    public async Task La_migracion_crea_el_historial_con_sus_triggers_y_la_vista_sin_tocar_los_datos()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblHallazgoSeguimiento'"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblHallazgoSeguimiento'"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.tblHallazgoSeguimiento')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwPatronesPorCuadrante'"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE EstadoSeguimiento = 'pendiente'"));

        // Repetir el script no cambia nada (ya esta aplicado).
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.tblHallazgoSeguimiento')"));
    }

    [Fact]
    public async Task El_seguimiento_cambia_el_estado_del_hallazgo_y_el_historial_no_se_modifica_ni_se_borra()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        var usuario = "(SELECT Id FROM dbo.tblUsuarios WHERE Email = 'insp@e.com')";

        await EjecutarAsync(_cadena, $"INSERT dbo.tblHallazgoSeguimiento (Id, HallazgoId, Accion, EstadoAnterior, EstadoNuevo, UsuarioId, Nota) VALUES (NEWID(), '{H_IZQUIERDO}', 'seguimiento', 'pendiente', 'validado', {usuario}, N'Validado')");
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = '{H_IZQUIERDO}' AND EstadoSeguimiento = 'validado'"));

        // Una revision o un no sale quedan en el historial pero no cambian el estado.
        await EjecutarAsync(_cadena, $"INSERT dbo.tblHallazgoSeguimiento (Id, HallazgoId, Accion, UsuarioId) VALUES (NEWID(), '{H_IZQUIERDO}', 'no_sale', {usuario})");
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = '{H_IZQUIERDO}' AND EstadoSeguimiento = 'validado'"));
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblHallazgoSeguimiento WHERE HallazgoId = '{H_IZQUIERDO}'"));

        // El historial es append-only.
        Assert.Equal(51005, await ErrorDeAsync("UPDATE dbo.tblHallazgoSeguimiento SET Nota = N'otra'"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblHallazgoSeguimiento"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgoSeguimiento"));

        // La base comprueba la forma de cada fila.
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblHallazgoSeguimiento (Id, HallazgoId, Accion, UsuarioId) VALUES (NEWID(), '{H_IZQUIERDO}', 'seguimiento', {usuario})"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblHallazgoSeguimiento (Id, HallazgoId, Accion, EstadoNuevo, UsuarioId) VALUES (NEWID(), '{H_IZQUIERDO}', 'seguimiento', 'archivado', {usuario})"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblHallazgoSeguimiento (Id, HallazgoId, Accion, UsuarioId, Detalle) VALUES (NEWID(), '{H_IZQUIERDO}', 'revision', {usuario}, N'no es json')"));
    }

    [Fact]
    public async Task La_vista_de_patrones_cuenta_por_lado_y_numero_solo_de_inspecciones_cerradas_y_sin_novedades_sin_revisar()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Izquierdo 7 y derecho 7 son filas distintas; el borrador del transporte y la novedad del conductor sin revisar no cuentan.
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwPatronesPorCuadrante"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwPatronesPorCuadrante WHERE Lado = 'izquierdo' AND Numero = 7 AND Total = 1 AND Proceso = 'cargue' AND NivelSeveridad = 'v1_plus' AND ClaseDano = 'golpe' AND Operacion = 'nacional'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwPatronesPorCuadrante WHERE Lado = 'derecho' AND Numero = 7 AND Total = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwPatronesPorCuadrante WHERE ZonaNombre = N'Izquierdo 7 ' + NCHAR(183) + N' Puerta delantera'"));

        // El borrador pasa a contar cuando se cierra la inspeccion, aunque la unidad no haya terminado el viaje.
        await EjecutarAsync(_cadena, "UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = '22222222-2222-2222-2222-222222222222'");
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwPatronesPorCuadrante WHERE Lado = 'izquierdo' AND Numero = 7 AND Proceso = 'transporte' AND Total = 1"));

        // La novedad del conductor cuenta cuando un inspector la revisa y le asigna zona, clase y nivel.
        await EjecutarAsync(_cadena, $@"UPDATE dbo.tblHallazgos SET ZonaId = (SELECT Id FROM dbo.tblZonasDano WHERE Lado = 'derecho' AND Numero = 7), ClaseDano = 'golpe', NivelSeveridad = 'v1',
            RevisadoAt = SYSDATETIMEOFFSET(), RevisadoPor = (SELECT Id FROM dbo.tblUsuarios WHERE Email = 'insp@e.com') WHERE Id = '{H_CONDUCTOR}'");
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwPatronesPorCuadrante WHERE Lado = 'derecho' AND Numero = 7 AND NivelSeveridad = 'v1' AND Total = 1"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_encima_de_una_base_ya_migrada_sin_cambiar_nada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.tblHallazgoSeguimiento')"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwPatronesPorCuadrante"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_sobre_la_base_anterior_y_la_migracion_se_completa_o_no_hace_nada_despues()
    {
        // Antes de migrar, el ddl ya crea el historial, sus triggers y la vista (la base anterior ya tiene el hallazgo con zona).
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblHallazgoSeguimiento'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.vwPatronesPorCuadrante"));

        // La migracion detecta que ya esta todo y no hace nada.
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.tblHallazgoSeguimiento')"));
    }
}
