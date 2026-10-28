using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/25-destino-por-unidad-y-cierre-con-una-firma.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-destino-por-unidad.sql,
/// el que ya esta desplegado) y con viajes que tienen destino: cada unidad recibe el destino de su viaje (por un lugar historico inactivo por cada tipo de destino), los recibos sin entrega ya registrados
/// quedan con su parte y su motivo, y el cierre recibo_sin_entrega admite las dos formas de una sola firma con su motivo.
/// </summary>
public sealed partial class MigracionDestinoPorUnidadTests : IAsyncLifetime
{
    private const string VIAJE_CON_DESTINO = "11111111-1111-1111-1111-111111111111";
    private const string VIAJE_OTRO_DESTINO = "11111111-1111-1111-1111-222222222222";
    private const string VIAJE_SIN_DESTINO = "11111111-1111-1111-1111-333333333333";
    private const string VEHICULO_1 = "22222222-2222-2222-2222-222222222221";
    private const string VEHICULO_2 = "22222222-2222-2222-2222-222222222222";
    private const string VEHICULO_3 = "22222222-2222-2222-2222-222222222223";
    private const string VEHICULO_4 = "22222222-2222-2222-2222-222222222224";
    private const string ENTREGA = "33333333-3333-3333-3333-333333333331";
    private const string RECIBE = "33333333-3333-3333-3333-333333333332";
    private const string ETAPA_CARGUE = "44444444-4444-4444-4444-444444444441";
    private const string ETAPA_TRANSPORTE = "44444444-4444-4444-4444-444444444442";
    private const string INSPECCION_CARGUE_SALIDA = "55555555-5555-5555-5555-555555555551";
    private const string INSPECCION_TRANSPORTE_SALIDA = "55555555-5555-5555-5555-555555555552";
    private const string INSPECCION_CARGUE_LLEGADA = "55555555-5555-5555-5555-555555555553";
    private const string INSPECCION_TRANSPORTE_LLEGADA = "55555555-5555-5555-5555-555555555554";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionDestino");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionDestino" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-destino-por-unidad.sql"));
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "25-destino-por-unidad-y-cierre-con-una-firma.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Lo que ya estaba guardado antes de migrar: un viaje con destino «Concesionario» y dos unidades, otro con destino «CEDI 2» y una unidad, uno sin destino y una unidad; y, de la primera unidad, su Cargue
    /// y su Transporte con la salida de cada uno, un recibo sin entrega (el del Cargue) y una transferencia firmada (la del Transporte).
    /// </summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @concesionario uniqueidentifier = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'concesionario');
        DECLARE @cedi2 uniqueidentifier = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_2');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{ENTREGA}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{RECIBE}}', N'Carlos', 'cond@e.com', 'conductor', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO_1}}', 'VINDESTINO0000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO_2}}', 'VINDESTINO0000002', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO_3}}', 'VINDESTINO0000003', N'Renault', 'nacional', @cli);
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO_4}}', 'VINDESTINO0000004', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, DestinoId) VALUES ('{{VIAJE_CON_DESTINO}}', 'M-5001', N'ABC123', @concesionario);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, DestinoId) VALUES ('{{VIAJE_OTRO_DESTINO}}', 'M-5002', N'ABC124', @cedi2);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES ('{{VIAJE_SIN_DESTINO}}', 'M-5003', N'ABC125');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE_CON_DESTINO}}', '{{VEHICULO_1}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE_CON_DESTINO}}', '{{VEHICULO_2}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE_OTRO_DESTINO}}', '{{VEHICULO_3}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE_SIN_DESTINO}}', '{{VEHICULO_4}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA_CARGUE}}', '{{VEHICULO_1}}', '{{VIAJE_CON_DESTINO}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA_TRANSPORTE}}', '{{VEHICULO_1}}', '{{VIAJE_CON_DESTINO}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte'), 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_CARGUE_SALIDA}}', '{{ETAPA_CARGUE}}', '{{VIAJE_CON_DESTINO}}', '{{ENTREGA}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_TRANSPORTE_SALIDA}}', '{{ETAPA_TRANSPORTE}}', '{{VIAJE_CON_DESTINO}}', '{{ENTREGA}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_CARGUE_LLEGADA}}', '{{ETAPA_CARGUE}}', '{{VIAJE_CON_DESTINO}}', '{{ENTREGA}}', 'llegada', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_TRANSPORTE_LLEGADA}}', '{{ETAPA_TRANSPORTE}}', '{{VIAJE_CON_DESTINO}}', '{{ENTREGA}}', 'llegada', SYSDATETIMEOFFSET());
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, RecibeUsuarioId, FirmaRecibe, FirmaRecibeAt, TipoCierre)
        VALUES (NEWID(), '{{INSPECCION_CARGUE_SALIDA}}', '{{RECIBE}}', N'firma-recibe', SYSDATETIMEOFFSET(), 'recibo_sin_entrega');
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaRecibeAt, TipoCierre)
        VALUES (NEWID(), '{{INSPECCION_TRANSPORTE_SALIDA}}', '{{ENTREGA}}', '{{RECIBE}}', N'firma-entrega', N'firma-recibe', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'firmada');
        """;

    private static string Transferencia(string inspeccion, string? entrega, string? firmaEntrega, string? firmaRecibe, string? parte, string? motivo, string tipo = "recibo_sin_entrega")
    {
        static string Texto(string? v) => v is null ? "NULL" : $"N'{v}'";
        return "INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaRecibeAt, TipoCierre, NoFirmaParte, MotivoNoFirma) " +
               $"VALUES (NEWID(), '{inspeccion}', {(entrega is null ? "NULL" : $"'{entrega}'")}, '{RECIBE}', {Texto(firmaEntrega)}, {Texto(firmaRecibe)}, " +
               $"{(firmaEntrega is null ? "NULL" : "SYSDATETIMEOFFSET()")}, {(firmaRecibe is null ? "NULL" : "SYSDATETIMEOFFSET()")}, '{tipo}', {(parte is null ? "NULL" : $"'{parte}'")}, {Texto(motivo)})";
    }

    // ------------------------------------------------------------------------------------------------ migracion

    [Fact]
    public async Task Cada_unidad_recibe_el_destino_de_su_viaje_y_los_recibos_sin_entrega_ya_registrados_quedan_con_su_parte_y_su_motivo()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE LugarDestinoId IS NOT NULL"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // CA-2: ninguna unidad de un viaje con destino queda sin destino y el de cada una coincide con el de su viaje.
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblViajes vj ON vj.Id = vv.ViajeId WHERE vj.DestinoId IS NOT NULL AND vv.LugarDestinoId IS NULL"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblViajes vj ON vj.Id = vv.ViajeId JOIN dbo.tblLugares l ON l.Id = vv.LugarDestinoId WHERE l.TipoId <> vj.DestinoId"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE LugarDestinoId IS NOT NULL"));
        // Las dos unidades del mismo viaje comparten lugar; el viaje sin destino no inventa uno.
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(DISTINCT LugarDestinoId) FROM dbo.tblViajeVehiculos WHERE ViajeId = '{VIAJE_CON_DESTINO}'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = '{VIAJE_SIN_DESTINO}' AND LugarDestinoId IS NULL"));
        // Un lugar historico inactivo por cada tipo de destino; el viaje conserva su DestinoId como dato historico.
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE Activo = 0 AND Ciudad = N'Sin definir' AND Nombre LIKE N'% (por definir)'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblViajes WHERE Id = '{VIAJE_CON_DESTINO}' AND DestinoId = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'concesionario')"));

        // El recibo sin entrega que ya existia queda con la parte que no firmo (quien entrega) y su motivo; la transferencia firmada no cambia.
        Assert.Equal(
            "entrega|El conductor hizo la salida del cargue y nadie de Botero Soto entregó la unidad",
            await TextoAsync($"SELECT CONCAT(NoFirmaParte, '|', MotivoNoFirma) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_CARGUE_SALIDA}'"));
        Assert.Equal(
            "firmada|NULL|NULL",
            await TextoAsync($"SELECT CONCAT(TipoCierre, '|', ISNULL(NoFirmaParte, 'NULL'), '|', ISNULL(MotivoNoFirma, 'NULL')) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_TRANSPORTE_SALIDA}'"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_sin_duplicar_lugares_ni_cambiar_lo_ya_migrado()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, "UPDATE dbo.tblLugares SET Activo = 1, Ciudad = N'Bogota' WHERE Nombre LIKE N'Concesionario%'");
        var destinos = await TextoAsync("SELECT STRING_AGG(CAST(LugarDestinoId AS varchar(36)), ',') WITHIN GROUP (ORDER BY Id) FROM dbo.tblViajeVehiculos");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares"));
        Assert.Equal(destinos, await TextoAsync("SELECT STRING_AGG(CAST(LugarDestinoId AS varchar(36)), ',') WITHIN GROUP (ORDER BY Id) FROM dbo.tblViajeVehiculos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE Nombre LIKE N'Concesionario%' AND Activo = 1 AND Ciudad = N'Bogota'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Cierre' AND definition LIKE '%recibo_sin_entrega%NoFirmaParte%'"));
    }

    [Fact]
    public async Task El_script_exige_antes_el_script_de_lugares()
    {
        await EjecutarAsync(_cadena, "ALTER TABLE dbo.tblViajeVehiculos DROP CONSTRAINT FK_tblViajeVehiculos_LugarDestino; DROP INDEX IX_tblViajeVehiculos_LugarDestino ON dbo.tblViajeVehiculos; ALTER TABLE dbo.tblViajeVehiculos DROP COLUMN LugarDestinoId");

        Assert.Equal(50002, await ErrorDeAsync(await ScriptMigracionAsync()));
    }

    // ------------------------------------------------------------------------------------------------ el cierre con una sola firma

    [Fact]
    public async Task El_recibo_sin_entrega_admite_una_sola_firma_con_su_parte_y_su_motivo_y_los_demas_cierres_siguen_igual()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // CA-4: las dos firmas, o sin motivo, o sin la parte, falla por el check.
        Assert.Equal(547, await ErrorDeAsync(Transferencia(INSPECCION_CARGUE_LLEGADA, ENTREGA, "firma-entrega", "firma-recibe", "recibe", "no firmo")));
        Assert.Equal(547, await ErrorDeAsync(Transferencia(INSPECCION_CARGUE_LLEGADA, ENTREGA, "firma-entrega", "firma-recibe", "entrega", "no firmo")));
        Assert.Equal(547, await ErrorDeAsync(Transferencia(INSPECCION_CARGUE_LLEGADA, null, null, "firma-recibe", "entrega", null)));
        Assert.Equal(547, await ErrorDeAsync(Transferencia(INSPECCION_CARGUE_LLEGADA, null, null, "firma-recibe", "entrega", "  ")));
        Assert.Equal(547, await ErrorDeAsync(Transferencia(INSPECCION_CARGUE_LLEGADA, null, null, "firma-recibe", null, "nadie entrego")));
        Assert.Equal(547, await ErrorDeAsync(Transferencia(INSPECCION_CARGUE_LLEGADA, ENTREGA, "firma-entrega", null, "entrega", "no firmo quien recibe")));
        Assert.Equal(547, await ErrorDeAsync(Transferencia(INSPECCION_CARGUE_LLEGADA, ENTREGA, null, null, "recibe", "no firmo quien recibe")));
        Assert.Equal(547, await ErrorDeAsync(Transferencia(INSPECCION_CARGUE_LLEGADA, null, null, null, "entrega", "nadie entrego")));

        // CA-3: firma solo quien recibe (nadie entrego) o firma solo quien entrega (quien recibe no firmo), con su parte y su motivo.
        Assert.Equal(0, await ErrorDeAsync(Transferencia(INSPECCION_CARGUE_LLEGADA, null, null, "firma-recibe", "entrega", "nadie entrego")));
        Assert.Equal(0, await ErrorDeAsync(Transferencia(INSPECCION_TRANSPORTE_LLEGADA, ENTREGA, "firma-entrega", null, "recibe", "quien recibe no esta en el punto")));
        Assert.Equal(
            "recibo_sin_entrega|recibe|firma-entrega|NULL",
            await TextoAsync($"SELECT CONCAT(TipoCierre, '|', NoFirmaParte, '|', FirmaEntrega, '|', ISNULL(FirmaRecibe, 'NULL')) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_TRANSPORTE_LLEGADA}'"));
        Assert.Equal(51003, await ErrorDeAsync($"DELETE FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_TRANSPORTE_LLEGADA}'"));

        // Los cierres firmada, con_reservas y no_firma siguen exigiendo lo mismo.
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblTransferenciasCustodia SET FirmaRecibe = NULL WHERE TipoCierre = 'firmada'"));
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblTransferenciasCustodia SET TipoCierre = 'con_reservas' WHERE TipoCierre = 'firmada'"));
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblTransferenciasCustodia SET TipoCierre = 'no_firma', NoFirmaParte = 'recibe', MotivoNoFirma = N'se nego' WHERE TipoCierre = 'firmada'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Cierre' AND definition LIKE '%recibo_sin_entrega%NoFirmaParte%MotivoNoFirma%'"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE LugarDestinoId IS NOT NULL"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares"));
    }
}
