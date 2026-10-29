using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/31-extras-del-vehiculo.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-extras.sql, el que ya esta desplegado) y con
/// una inspeccion en borrador, otra cerrada y una foto de vista: el catalogo de tipos de extra nace sembrado, los datos de antes no cambian, la base impone el texto libre solo en otro, la foto de un extra, el
/// tipo en uso y la inmutabilidad al cierre, y el script se puede repetir.
/// </summary>
public sealed partial class MigracionExtrasTests : IAsyncLifetime
{
    private const string BORRADOR = "33333333-3333-3333-3333-333333333331";
    private const string CERRADA = "33333333-3333-3333-3333-333333333332";
    private const string EXTRA_OTRO = "44444444-4444-4444-4444-444444444441";
    private const string EXTRA_BARRAS = "44444444-4444-4444-4444-444444444442";
    private const string EXTRA_CERRADO = "44444444-4444-4444-4444-444444444443";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionExtras");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionExtras" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-extras.sql"));
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "31-extras-del-vehiculo.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Una unidad con dos inspecciones (una en borrador y otra cerrada) y una foto de vista: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @tipo uniqueidentifier = (SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera');
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        DECLARE @u uniqueidentifier = NEWID(), @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID(), @e uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES (@u, N'Inspector Extras', N'extras@ejemplo.com', 'inspector');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINEXTRAS0000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, TipoUnidadId, Placa, InspectorId) VALUES (@vj, 'M-48213', @tipo, N'ABC123', @u);
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e, @v, @vj, @cargue, 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{BORRADOR}}', @e, @vj, @u, 'llegada', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{CERRADA}}', @e, @vj, @u, 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblEvidencias (Id, InspeccionId, VistaId, StoragePath, SelloFecha, SelloLat, SelloLng)
        SELECT NEWID(), '{{BORRADOR}}', Id, N'frontal.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1 FROM dbo.tblVistasFoto WHERE Codigo = 'frontal';
        UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = '{{CERRADA}}';
        """;

    private static string Extra(string id, string inspeccion, string tipo, string? texto = null, string? observacion = null) =>
        $"INSERT dbo.tblInspeccionExtras (Id, InspeccionId, Tipo, Texto, Observacion) VALUES ('{id}', '{inspeccion}', '{tipo}', {(texto is null ? "NULL" : $"N'{texto}'")}, {(observacion is null ? "NULL" : $"N'{observacion}'")})";

    private static string Foto(string extra, string inspeccion, string columnasExtra = "", string valoresExtra = "") =>
        $"INSERT dbo.tblEvidencias (Id, InspeccionId, ExtraId, StoragePath, SelloFecha, SelloLat, SelloLng{columnasExtra}) VALUES (NEWID(), '{inspeccion}', '{extra}', N'extra.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1{valoresExtra})";

    [Fact]
    public async Task La_migracion_siembra_el_catalogo_y_crea_las_tablas_sin_cambiar_lo_de_antes_y_se_puede_repetir()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblTiposExtra', 'tblInspeccionExtras')"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblEvidencias') AND name = 'ExtraId'"));

        // Dos veces seguidas, sin error.
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // El catalogo sembrado, con su orden y todo activo.
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposExtra WHERE Activo = 1 AND Codigo IN ('barras_techo', 'volco', 'platon', 'otro')"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposExtra"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposExtra WHERE Codigo = 'platon' AND Orden = 3 AND Nombre = N'Plat' + NCHAR(243) + N'n'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UQ_tblTiposExtra_Codigo' AND is_unique_constraint = 1"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblEvidencias') AND name = 'ExtraId' AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name = 'TR_tblInspeccionExtras_NoEnCerrada'"));
        // Lo de antes sigue igual: las dos inspecciones, la foto de vista y su estado.
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Id = '{CERRADA}' AND Estado = 'cerrada'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE VistaId IS NOT NULL AND ExtraId IS NULL"));
    }

    [Fact]
    public async Task La_migracion_no_pisa_un_tipo_que_ya_existe()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, "UPDATE dbo.tblTiposExtra SET Nombre = N'Platon con volco', Activo = 0 WHERE Codigo = 'platon'");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposExtra WHERE Codigo = 'platon' AND Nombre = N'Platon con volco' AND Activo = 0"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposExtra"));
    }

    [Fact]
    public async Task La_base_impone_el_texto_libre_solo_en_otro_la_observacion_la_foto_del_extra_y_el_tipo_en_uso()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Otro con texto vale; otro tipo con texto, texto en blanco, observacion en blanco o de 501 caracteres, tipo inexistente o sin inspeccion fallan.
        Assert.Equal(0, await ErrorDeAsync(Extra(EXTRA_OTRO, BORRADOR, "otro", "portabicicletas", "en el techo")));
        Assert.Equal(547, await ErrorDeAsync(Extra(Guid.NewGuid().ToString(), BORRADOR, "barras_techo", "algo")));
        Assert.Equal(547, await ErrorDeAsync(Extra(Guid.NewGuid().ToString(), BORRADOR, "otro", "   ")));
        Assert.Equal(547, await ErrorDeAsync(Extra(Guid.NewGuid().ToString(), BORRADOR, "platon", null, "   ")));
        Assert.Equal(2628, await ErrorDeAsync(Extra(Guid.NewGuid().ToString(), BORRADOR, "platon", null, new string('x', 501))));
        Assert.Equal(0, await ErrorDeAsync(Extra(EXTRA_BARRAS, BORRADOR, "barras_techo", null, new string('x', 500))));
        Assert.Equal(547, await ErrorDeAsync(Extra(Guid.NewGuid().ToString(), BORRADOR, "no_existe")));
        Assert.Equal(547, await ErrorDeAsync(Extra(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "platon")));

        // La foto de un extra, con su sello; no es a la vez de una vista, una constancia ni un sello; el extra debe existir.
        Assert.Equal(0, await ErrorDeAsync(Foto(EXTRA_BARRAS, BORRADOR)));
        Assert.Equal(547, await ErrorDeAsync(Foto(Guid.NewGuid().ToString(), BORRADOR)));
        Assert.Equal(547, await ErrorDeAsync("INSERT dbo.tblEvidencias (Id, InspeccionId, ExtraId, VistaId, StoragePath, SelloFecha, SelloLat, SelloLng) "
            + $"SELECT NEWID(), '{BORRADOR}', '{EXTRA_BARRAS}', Id, N'extra.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1 FROM dbo.tblVistasFoto WHERE Codigo = 'frontal'"));
        Assert.Equal(547, await ErrorDeAsync(Foto(EXTRA_BARRAS, BORRADOR, ", EsConstancia", ", 1")));
        Assert.Equal(547, await ErrorDeAsync(Foto(EXTRA_BARRAS, BORRADOR, ", EsSello", ", 1")));

        // Un tipo con extras no se borra; con la foto del extra tampoco se borra el extra.
        Assert.Equal(547, await ErrorDeAsync("DELETE FROM dbo.tblTiposExtra WHERE Codigo = 'barras_techo'"));
        Assert.Equal(547, await ErrorDeAsync($"DELETE FROM dbo.tblInspeccionExtras WHERE Id = '{EXTRA_BARRAS}'"));
        // El extra de un borrador sin fotos se cambia y se quita; un tipo sin extras se puede borrar.
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblInspeccionExtras SET Observacion = N'cambiada' WHERE Id = '{EXTRA_OTRO}'"));
        Assert.Equal(0, await ErrorDeAsync($"DELETE FROM dbo.tblInspeccionExtras WHERE Id = '{EXTRA_OTRO}'"));
        Assert.Equal(0, await ErrorDeAsync("DELETE FROM dbo.tblTiposExtra WHERE Codigo = 'volco'"));
    }

    [Fact]
    public async Task Un_extra_no_se_agrega_cambia_ni_quita_en_una_inspeccion_cerrada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        // El extra de la inspeccion cerrada se siembra antes de cerrarla.
        await EjecutarAsync(_cadena, $"ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL; UPDATE dbo.tblInspecciones SET Estado = 'borrador', CerradaAt = NULL WHERE Id = '{CERRADA}'; ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL");
        await EjecutarAsync(_cadena, Extra(EXTRA_CERRADO, CERRADA, "volco", null, "sin novedad"));
        await EjecutarAsync(_cadena, $"UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = '{CERRADA}'");

        Assert.Equal(51009, await ErrorDeAsync(Extra(Guid.NewGuid().ToString(), CERRADA, "platon")));
        Assert.Equal(51009, await ErrorDeAsync($"UPDATE dbo.tblInspeccionExtras SET Observacion = N'otra' WHERE Id = '{EXTRA_CERRADO}'"));
        Assert.Equal(51009, await ErrorDeAsync($"DELETE FROM dbo.tblInspeccionExtras WHERE Id = '{EXTRA_CERRADO}'"));
        // Inactivar el tipo no toca el extra ya reportado.
        await EjecutarAsync(_cadena, "UPDATE dbo.tblTiposExtra SET Activo = 0 WHERE Codigo = 'volco'");
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblInspeccionExtras WHERE Id = '{EXTRA_CERRADO}' AND Tipo = 'volco'"));
        // Las fotos de un extra tampoco se agregan a una inspeccion cerrada (la defensa de las evidencias).
        Assert.Equal(51002, await ErrorDeAsync(Foto(EXTRA_CERRADO, CERRADA)));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposExtra"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposExtra"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblEvidencias_Extra'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblEvidencias_Extra'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }
}
