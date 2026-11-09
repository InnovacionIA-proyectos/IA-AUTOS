using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/40-adopcion-por-carro-y-por-camion.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-adopcion-por-camion.sql, el que ya esta
/// desplegado) y con datos: la adopcion deja de contar la salida en borrador, gana el destino de cada unidad y aparece la vista por camion (cada viaje una vez); ningun dato cambia y el script y el ddl nuevo se pueden
/// repetir.
/// </summary>
public sealed partial class MigracionAdopcionPorCamionTests : IAsyncLifetime
{
    private const string USUARIO = "11111111-1111-1111-1111-111111111111";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string VIAJE_CANCELADO = "55555555-5555-5555-5555-555555555556";
    private const string VEHICULO_1 = "66666666-6666-6666-6666-666666666661";
    private const string VEHICULO_2 = "66666666-6666-6666-6666-666666666662";
    private const string VEHICULO_3 = "66666666-6666-6666-6666-666666666663";
    private const string LUGAR = "88888888-8888-8888-8888-888888888888";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionAdopcion");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-adopcion-por-camion.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Un viaje programado con dos unidades a un mismo destino (una con la salida del Cargue cerrada y otra con la salida en borrador) y un viaje cancelado con una unidad con la salida cerrada: lo que ya estaba guardado
    /// antes de migrar.
    /// </summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{LUGAR}}', N'Concesionario Norte', (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia), N'Bogota');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES
            ('{{VEHICULO_1}}', 'VINADOPCION00001', N'Renault', 'nacional', @cli), ('{{VEHICULO_2}}', 'VINADOPCION00002', N'Renault', 'nacional', @cli), ('{{VEHICULO_3}}', 'VINADOPCION00003', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado, FechaProgramada) VALUES
            ('{{VIAJE}}', 'MTK-40', N'ABC123', 'en_curso', '2026-11-18T06:00:00-05:00'), ('{{VIAJE_CANCELADO}}', 'MTK-41', N'DEF456', 'cancelado', '2026-11-18T06:00:00-05:00');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId, LugarDestinoId) VALUES
            (NEWID(), '{{VIAJE}}', '{{VEHICULO_1}}', '{{LUGAR}}'), (NEWID(), '{{VIAJE}}', '{{VEHICULO_2}}', '{{LUGAR}}'), (NEWID(), '{{VIAJE_CANCELADO}}', '{{VEHICULO_3}}', '{{LUGAR}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES
            ('77777777-7777-7777-7777-777777777771', '{{VEHICULO_1}}', '{{VIAJE}}', @cargue, 1), ('77777777-7777-7777-7777-777777777772', '{{VEHICULO_2}}', '{{VIAJE}}', @cargue, 1),
            ('77777777-7777-7777-7777-777777777773', '{{VEHICULO_3}}', '{{VIAJE_CANCELADO}}', @cargue, 1);
        ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL;
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, IniciadaAt, Estado, CerradaAt) VALUES
            (NEWID(), '77777777-7777-7777-7777-777777777771', '{{VIAJE}}', '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET(), DATEADD(MINUTE, -10, SYSDATETIMEOFFSET()), 'cerrada', SYSDATETIMEOFFSET()),
            (NEWID(), '77777777-7777-7777-7777-777777777773', '{{VIAJE_CANCELADO}}', '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET(), DATEADD(MINUTE, -10, SYSDATETIMEOFFSET()), 'cerrada', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES (NEWID(), '77777777-7777-7777-7777-777777777772', '{{VIAJE}}', '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET());
        ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
        """;

    private async Task EjecutarAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion);
        await preparar.ExecuteNonQueryAsync();
        await using var principal = new SqlCommand(sql, conexion) { CommandTimeout = 120 };
        await principal.ExecuteNonQueryAsync();
    }

    private async Task EjecutarScriptConGoAsync(string ruta)
    {
        foreach (var lote in SeparadorGo().Split(await File.ReadAllTextAsync(ruta)).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            await EjecutarAsync(lote);
        }
    }

    private async Task<int> ContarAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        return Convert.ToInt32(await comando.ExecuteScalarAsync());
    }

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "40-adopcion-por-carro-y-por-camion.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    private const string PROGRAMADOS = "SELECT ISNULL(SUM(Programados), 0) FROM dbo.vwAdopcion";
    private const string CON_SALIDA = "SELECT ISNULL(SUM(ConSalidaDigital), 0) FROM dbo.vwAdopcion";

    [Fact]
    public async Task Antes_de_migrar_la_adopcion_cuenta_tambien_la_salida_en_borrador_y_no_hay_vista_por_camion()
    {
        Assert.Equal(2, await ContarAsync(PROGRAMADOS));
        Assert.Equal(2, await ContarAsync(CON_SALIDA));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwAdopcionPorCamion'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.vwAdopcion') AND name = 'LugarDestinoId'"));
    }

    [Fact]
    public async Task Migrar_deja_de_contar_el_borrador_agrega_el_destino_de_la_unidad_y_crea_la_vista_por_camion_sin_cambiar_ningun_dato()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Por carro: 2 programados (el cancelado no cuenta) y solo 1 con la salida cerrada (el borrador ya no suma), cada uno en su destino.
        Assert.Equal(2, await ContarAsync(PROGRAMADOS));
        Assert.Equal(1, await ContarAsync(CON_SALIDA));
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = '{VIAJE}' AND LugarDestinoId = '{LUGAR}'") );
        Assert.Equal(2, await ContarAsync($"SELECT ISNULL(SUM(Programados), 0) FROM dbo.vwAdopcion WHERE LugarDestinoId = '{LUGAR}'"));
        // Por camion: el viaje cuenta una vez, adoptado porque una de sus unidades tiene la salida cerrada, en el destino unico de sus unidades.
        Assert.Equal(1, await ContarAsync("SELECT ISNULL(SUM(Programados), 0) FROM dbo.vwAdopcionPorCamion"));
        Assert.Equal(1, await ContarAsync("SELECT ISNULL(SUM(ConSalidaDigital), 0) FROM dbo.vwAdopcionPorCamion"));
        Assert.Equal(1, await ContarAsync($"SELECT ISNULL(SUM(Programados), 0) FROM dbo.vwAdopcionPorCamion WHERE LugarDestinoId = '{LUGAR}'"));
        // No cambia ningun dato.
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync(CON_SALIDA));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwAdopcionPorCamion'"));
        Assert.Equal(1, await ContarAsync(CON_SALIDA));
        Assert.Equal(1, await ContarAsync("SELECT ISNULL(SUM(Programados), 0) FROM dbo.vwAdopcionPorCamion"));
    }
}
