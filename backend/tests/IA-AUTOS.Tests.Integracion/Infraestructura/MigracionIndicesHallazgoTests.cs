using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/12-indices-hallazgo.sql) contra un SQL Server 2019 con el esquema PREVIO
/// (Esquemas/ddl-previo-a-indices.sql, el que ya esta desplegado) y con hallazgos: el indice por parte del vehiculo y clase se reemplaza por uno por
/// zona y clase (con la inspeccion incluida), se agrega el de la bandeja de seguimiento y no cambia ningun dato.
/// </summary>
public sealed partial class MigracionIndicesHallazgoTests : IAsyncLifetime
{
    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionIndices");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionIndices" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-indices.sql"));
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

    private async Task<string?> ColumnasDelIndiceAsync(string indice, bool incluidas)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(@"
            SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal, ic.index_column_id)
              FROM sys.indexes i
              JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
             WHERE i.object_id = OBJECT_ID('dbo.tblHallazgos') AND i.name = @indice AND ic.is_included_column = @incluidas", conexion);
        comando.Parameters.AddWithValue("@indice", indice);
        comando.Parameters.AddWithValue("@incluidas", incluidas);
        var resultado = await comando.ExecuteScalarAsync();
        return resultado is null or DBNull ? null : (string)resultado;
    }

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "12-indices-hallazgo.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Una inspeccion con tres hallazgos para que los indices se construyan sobre datos reales.</summary>
    private const string DATOS_PREVIOS = """
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @u uniqueidentifier = NEWID(), @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID(), @e uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@u, N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINPREVIO28000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES (@vj, 'MTK-29', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('11111111-1111-1111-1111-111111111111', @e, @vj, @u, 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'Capot', 'golpe', '1_5cm'),
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'Techo', 'rotura', '5_10cm'),
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'Parachoque delantero', 'golpe', '1_5cm');
        """;

    [Fact]
    public async Task La_migracion_reemplaza_el_indice_por_parte_por_los_de_zona_y_de_seguimiento_sin_tocar_los_datos()
    {
        Assert.Equal("ParteVehiculo,ClaseDano", await ColumnasDelIndiceAsync("IX_tblHallazgos_Parte_Clase", incluidas: false));
        Assert.Null(await ColumnasDelIndiceAsync("IX_tblHallazgos_Zona_Clase", incluidas: false));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Null(await ColumnasDelIndiceAsync("IX_tblHallazgos_Parte_Clase", incluidas: false));
        Assert.Equal("ZonaId,ClaseDano", await ColumnasDelIndiceAsync("IX_tblHallazgos_Zona_Clase", incluidas: false));
        Assert.Equal("InspeccionId", await ColumnasDelIndiceAsync("IX_tblHallazgos_Zona_Clase", incluidas: true));
        Assert.Equal("EstadoSeguimiento,Origen,RevisadoAt", await ColumnasDelIndiceAsync("IX_tblHallazgos_Seguimiento", incluidas: false));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));

        // Repetir el script no cambia nada (ya esta aplicado).
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal("ZonaId,ClaseDano", await ColumnasDelIndiceAsync("IX_tblHallazgos_Zona_Clase", incluidas: false));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_encima_de_una_base_ya_migrada_sin_cambiar_nada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Null(await ColumnasDelIndiceAsync("IX_tblHallazgos_Parte_Clase", incluidas: false));
        Assert.Equal("ZonaId,ClaseDano", await ColumnasDelIndiceAsync("IX_tblHallazgos_Zona_Clase", incluidas: false));
        Assert.Equal("EstadoSeguimiento,Origen,RevisadoAt", await ColumnasDelIndiceAsync("IX_tblHallazgos_Seguimiento", incluidas: false));
    }

    [Fact]
    public async Task El_ddl_nuevo_sobre_la_base_anterior_agrega_los_indices_nuevos_y_la_migracion_quita_el_viejo()
    {
        // Antes de migrar, el ddl agrega los indices nuevos pero no quita el anterior (eso lo hace la migracion).
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal("ParteVehiculo,ClaseDano", await ColumnasDelIndiceAsync("IX_tblHallazgos_Parte_Clase", incluidas: false));
        Assert.Equal("ZonaId,ClaseDano", await ColumnasDelIndiceAsync("IX_tblHallazgos_Zona_Clase", incluidas: false));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Null(await ColumnasDelIndiceAsync("IX_tblHallazgos_Parte_Clase", incluidas: false));
        Assert.Equal("EstadoSeguimiento,Origen,RevisadoAt", await ColumnasDelIndiceAsync("IX_tblHallazgos_Seguimiento", incluidas: false));
    }
}
