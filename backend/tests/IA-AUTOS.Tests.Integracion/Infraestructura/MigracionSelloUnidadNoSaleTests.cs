using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/14-sello-y-unidad-no-sale.sql) contra un SQL Server 2019 con el esquema PREVIO
/// (Esquemas/ddl-previo-a-sello.sql, el que ya esta desplegado) y con una unidad cuya inspeccion esta cerrada: gana la marca de la foto del sello (que se
/// admite en una inspeccion cerrada, a diferencia de cualquier otra evidencia), y una unidad que no sale deja de contar como completa en su viaje.
/// </summary>
public sealed partial class MigracionSelloUnidadNoSaleTests : IAsyncLifetime
{
    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionSello");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionSello" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-sello.sql"));
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "14-sello-y-unidad-no-sale.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    private const string SELLO_EN_CERRADA = """
        INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, EsSello)
        VALUES (NEWID(), '11111111-1111-1111-1111-111111111111', 'sello.png', SYSDATETIMEOFFSET(), 4.6, -74.1, 1)
        """;

    private const string FOTO_EN_CERRADA = """
        INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng)
        VALUES (NEWID(), '11111111-1111-1111-1111-111111111111', 'otra.png', SYSDATETIMEOFFSET(), 4.6, -74.1)
        """;

    /// <summary>Una unidad con una sola etapa y un informe cerrado con un hallazgo V1+, en su viaje: cuenta como completa.</summary>
    private const string DATOS_PREVIOS = """
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @u uniqueidentifier = NEWID(), @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID(), @e uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@u, N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINSELLO28000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES (@vj, 'MTK-30', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('11111111-1111-1111-1111-111111111111', @e, @vj, @u, 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad)
        VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'), 'golpe', '5_10cm', 'v1_plus');
        UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = '11111111-1111-1111-1111-111111111111';
        """;

    private const string MARCAR_NO_SALE = """
        UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'no_sale', NoSaleMotivo = N'Golpe en el frente', NoSaleHallazgoId = '22222222-2222-2222-2222-222222222222',
            NoSaleMarcadoPor = (SELECT TOP 1 Id FROM dbo.tblUsuarios), NoSaleAt = SYSDATETIMEOFFSET()
        """;

    [Fact]
    public async Task La_migracion_admite_la_foto_del_sello_en_una_inspeccion_cerrada_y_sigue_rechazando_las_demas()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblEvidencias') AND name = 'EsSello'"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblEvidencias') AND name = 'EsSello'"));
        Assert.Equal(0, await ErrorDeAsync(SELLO_EN_CERRADA));
        Assert.Equal(51002, await ErrorDeAsync(FOTO_EN_CERRADA));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsSello = 1"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsSello = 0"));
    }

    [Fact]
    public async Task La_foto_del_sello_no_es_de_una_vista_un_hallazgo_ni_una_constancia()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        var deHallazgo = await ErrorDeAsync("""
            INSERT dbo.tblEvidencias (Id, InspeccionId, HallazgoId, StoragePath, SelloFecha, SelloLat, SelloLng, EsSello)
            VALUES (NEWID(), '11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 's.png', SYSDATETIMEOFFSET(), 4.6, -74.1, 1)
            """);
        var deVista = await ErrorDeAsync("""
            INSERT dbo.tblEvidencias (Id, InspeccionId, VistaId, StoragePath, SelloFecha, SelloLat, SelloLng, EsSello)
            VALUES (NEWID(), '11111111-1111-1111-1111-111111111111', (SELECT TOP 1 Id FROM dbo.tblVistasFoto), 's.png', SYSDATETIMEOFFSET(), 4.6, -74.1, 1)
            """);
        var deConstancia = await ErrorDeAsync("""
            INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, EsSello, EsConstancia)
            VALUES (NEWID(), '11111111-1111-1111-1111-111111111111', 's.png', SYSDATETIMEOFFSET(), 4.6, -74.1, 1, 1)
            """);

        Assert.Equal(547, deHallazgo);
        Assert.Equal(547, deVista);
        Assert.Equal(547, deConstancia);
    }

    [Fact]
    public async Task Una_unidad_que_no_sale_deja_de_contar_como_completa_en_su_viaje_y_vuelve_a_contar_si_se_reactiva()
    {
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos"));
        await EjecutarAsync(_cadena, MARCAR_NO_SALE);
        // Con la vista anterior, la unidad que no sale seguia contando como completa.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos"));
        await EjecutarAsync(_cadena, "UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'activa', NoSaleMotivo = NULL, NoSaleHallazgoId = NULL, NoSaleMarcadoPor = NULL, NoSaleAt = NULL");
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos"));
    }

    [Fact]
    public async Task Repetir_la_migracion_no_cambia_nada_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar()
    {
        // Antes de migrar, el ddl nuevo ya deja el esquema nuevo (la base previa conserva sus datos).
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblEvidencias') AND name = 'EsSello'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblEvidencias_Sello'"));
        Assert.Equal(0, await ErrorDeAsync(SELLO_EN_CERRADA));
        Assert.Equal(51002, await ErrorDeAsync(FOTO_EN_CERRADA));
        await EjecutarAsync(_cadena, MARCAR_NO_SALE);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }
}
