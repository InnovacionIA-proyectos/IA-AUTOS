using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/38-capacidad-de-la-ninera.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-capacidad-de-la-ninera.sql, el que ya esta
/// desplegado) y con datos: el viaje gana su capacidad (nula en los de antes: se comportan como de 7), el tipo de unidad gana sus puestos por defecto, la posicion de una unidad llega hasta 20 en vez de 7 y la
/// evidencia gana la marca de acomodacion; ningun dato cambia y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionCapacidadDeLaNineraTests : IAsyncLifetime
{
    private const string USUARIO = "11111111-1111-1111-1111-111111111111";
    private const string INSPECCION = "33333333-3333-3333-3333-333333333333";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string VEHICULO = "66666666-6666-6666-6666-666666666666";
    private const string VEHICULO_2 = "66666666-6666-6666-6666-666666666667";
    private const string ETAPA = "77777777-7777-7777-7777-777777777777";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionCapacidadNinera");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-capacidad-de-la-ninera.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un viaje de antes con dos unidades (una en el puesto 7), una inspeccion en borrador con una foto de acomodacion y el tipo de unidad de la ninera: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINNINERA000000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO_2}}', 'VINNINERA000000002', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado) VALUES ('{{VIAJE}}', 'MTK-38', N'ABC123', 'en_curso');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId, Posicion) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}', 7);
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO_2}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION}}', '{{ETAPA}}', '{{VIAJE}}', '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), '{{INSPECCION}}', N'acomodacion.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1);
        """;

    private static string Posicion(int posicion) =>
        $"UPDATE dbo.tblViajeVehiculos SET Posicion = {posicion} WHERE VehiculoId = '{VEHICULO_2}'";

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

    /// <summary>El numero de error de SQL Server que devuelve la sentencia (0 si se ejecuta sin error).</summary>
    private async Task<int> ErrorDeAsync(string sql)
    {
        try
        {
            await EjecutarAsync(sql);
            return 0;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "38-capacidad-de-la-ninera.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_la_posicion_llega_solo_hasta_7_y_no_hay_capacidad_ni_puestos_ni_marca()
    {
        Assert.Equal(547, await ErrorDeAsync(Posicion(8)));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE (object_id = OBJECT_ID('dbo.tblViajes') AND name = 'Capacidad') OR (object_id = OBJECT_ID('dbo.tblTiposUnidadTransportadora') AND name = 'Puestos') OR (object_id = OBJECT_ID('dbo.tblEvidencias') AND name = 'EsAcomodacion')"));
    }

    [Fact]
    public async Task Migrar_agrega_las_columnas_y_amplia_la_posicion_sin_cambiar_ningun_dato()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Los viajes de antes quedan sin capacidad (se comportan como de 7) y los tipos sin puestos definidos; las fotos de antes no estan marcadas.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes WHERE Capacidad IS NULL"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposUnidadTransportadora WHERE Puestos IS NOT NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsAcomodacion = 0"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE Posicion = 7"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblViajeVehiculos_Posicion' AND definition LIKE '%(20)%'"));
    }

    [Fact]
    public async Task Despues_de_migrar_la_posicion_la_capacidad_y_los_puestos_se_validan_de_1_a_20_y_la_foto_de_acomodacion_no_es_de_otra_cosa()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(Posicion(8)));
        Assert.Equal(0, await ErrorDeAsync(Posicion(20)));
        Assert.Equal(547, await ErrorDeAsync(Posicion(21)));
        Assert.Equal(547, await ErrorDeAsync(Posicion(0)));
        Assert.Equal(2601, await ErrorDeAsync(Posicion(7)));

        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblViajes SET Capacidad = 9 WHERE Id = '{VIAJE}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblViajes SET Capacidad = 0 WHERE Id = '{VIAJE}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblViajes SET Capacidad = 21 WHERE Id = '{VIAJE}'"));
        Assert.Equal(0, await ErrorDeAsync("UPDATE dbo.tblTiposUnidadTransportadora SET Puestos = 9 WHERE Codigo = 'ninera'"));
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblTiposUnidadTransportadora SET Puestos = 25 WHERE Codigo = 'ninera'"));

        // La foto de la acomodacion se marca; una constancia no puede ser a la vez de acomodacion.
        Assert.Equal(0, await ErrorDeAsync("UPDATE dbo.tblEvidencias SET EsAcomodacion = 1 WHERE StoragePath = N'acomodacion.jpg'"));
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblEvidencias SET EsConstancia = 1 WHERE StoragePath = N'acomodacion.jpg'"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblViajeVehiculos_Posicion' AND definition LIKE '%(20)%'"));
        Assert.Equal(0, await ErrorDeAsync(Posicion(8)));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblViajes_Capacidad'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTiposUnidadTransportadora_Puestos'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblEvidencias_Acomodacion'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblViajeVehiculos_Posicion'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
    }
}
