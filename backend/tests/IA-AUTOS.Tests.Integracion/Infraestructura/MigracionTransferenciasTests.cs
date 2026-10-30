using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/08-transferencias-custodia.sql) contra un SQL Server 2019 con el esquema
/// PREVIO (Esquemas/ddl-previo-a-transferencias.sql, el que ya esta desplegado) y con transferencias reales: las que tenian las dos firmas
/// quedan cerradas como firmadas (con la fecha de cada firma), las abiertas siguen abiertas y la foto de una constancia se admite en una
/// inspeccion cerrada sin abrir la puerta a las demas fotos.
/// </summary>
public sealed partial class MigracionTransferenciasTests : IAsyncLifetime
{
    private static readonly Guid SALIDA_CERRADA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LLEGADA_BORRADOR = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SALIDA_SIGUIENTE = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionTransferencias");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-transferencias.sql"));
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

    private async Task<int> ContarAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        return Convert.ToInt32(await comando.ExecuteScalarAsync());
    }

    private static async Task<string> ScriptMigracionAsync()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "08-transferencias-custodia.sql");
        return (await File.ReadAllTextAsync(ruta)).Replace("N'db_IA_AUTOS'", "N'dbo'");
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Un viaje con un VIN y 2 etapas. Cargue: una salida cerrada con una foto y su transferencia con las dos firmas, y una llegada en borrador con
    /// una transferencia firmada solo por quien entrega. Transporte: una salida en borrador con la transferencia aun sin firmas.
    /// </summary>
    private const string DATOS_PREVIOS = """
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @u uniqueidentifier = NEWID(), @c uniqueidentifier = NEWID(), @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@u, N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@c, N'Carlos', 'cond@e.com', 'conductor', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINPREVIO25000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, ConductorId, InspectorId) VALUES (@vj, 'MTK-25', N'ABC123', @c, @u);
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        DECLARE @transp uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte');
        DECLARE @e1 uniqueidentifier = NEWID(), @e2 uniqueidentifier = NEWID();
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e1, @v, @vj, @cargue, 1), (@e2, @v, @vj, @transp, 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES
         ('11111111-1111-1111-1111-111111111111', @e1, @vj, @u, 'salida', '2026-07-01T08:00:00+00:00'),
         ('22222222-2222-2222-2222-222222222222', @e1, @vj, @u, 'llegada', '2026-07-02T08:00:00+00:00'),
         ('33333333-3333-3333-3333-333333333333', @e2, @vj, @u, 'salida', '2026-07-03T08:00:00+00:00');
        INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'a.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1);
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmadaAt) VALUES
         (NEWID(), '11111111-1111-1111-1111-111111111111', @u, @c, N'M0 0 L1 1', N'M2 2 L3 3', '2026-07-05T10:00:00+00:00');
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, UpdatedAt) VALUES
         (NEWID(), '22222222-2222-2222-2222-222222222222', @u, @c, N'M0 0 L1 1', '2026-07-06T09:00:00+00:00');
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId) VALUES
         (NEWID(), '33333333-3333-3333-3333-333333333333', @u, @c);
        UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = '2026-07-01T09:00:00+00:00' WHERE Id = '11111111-1111-1111-1111-111111111111';
        """;

    [Fact]
    public async Task La_migracion_cierra_como_firmadas_las_completas_y_deja_abiertas_las_demas_con_la_fecha_de_cada_firma()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Ninguna transferencia se pierde ni cambia sus firmas.
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE FirmaEntrega IS NOT NULL"));

        // La que tenia las dos firmas queda firmada y la fecha de cada firma es la del sello que ya tenia.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{SALIDA_CERRADA}' AND TipoCierre = 'firmada' AND FirmaEntregaAt = FirmadaAt AND FirmaRecibeAt = FirmadaAt AND FirmaEntregaIp IS NULL AND FirmaRecibeIp IS NULL"));

        // La firmada solo por quien entrega sigue abierta, con la fecha de su ultima modificacion; la vacia no tiene fechas.
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{LLEGADA_BORRADOR}' AND TipoCierre IS NULL AND FirmaEntregaAt = UpdatedAt AND FirmaRecibeAt IS NULL"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{SALIDA_SIGUIENTE}' AND TipoCierre IS NULL AND FirmaEntregaAt IS NULL AND FirmaRecibeAt IS NULL"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre IS NULL"));

        // Las columnas nuevas de la constancia quedan vacias y las fotos de antes no son constancias.
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE ReservasTexto IS NOT NULL OR NoFirmaParte IS NOT NULL OR MotivoNoFirma IS NOT NULL OR TestigoUsuarioId IS NOT NULL OR EvidenciaConstanciaId IS NOT NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsConstancia = 0"));

        // Repetir el script no cambia nada (ya esta aplicado).
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada'"));
    }

    [Fact]
    public async Task Despues_de_migrar_el_cierre_exige_lo_suyo_y_el_trigger_solo_admite_la_foto_de_una_constancia_en_una_inspeccion_cerrada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // La foto de una constancia entra en la inspeccion cerrada; una foto cualquiera sigue sin poder entrar.
        await EjecutarAsync(_cadena,
            $"INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, EsConstancia) VALUES (NEWID(), '{SALIDA_CERRADA}', N'constancia.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1, 1)");
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena,
            $"INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), '{SALIDA_CERRADA}', N'otra.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1)"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name = 'TR_tblEvidencias_NoEnCerrada' AND is_disabled = 1"));

        // Cada tipo de cierre exige lo suyo: la firmada sin firmas, el testigo que es una de las partes o una no firma sin foto no entran.
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena,
            $"UPDATE dbo.tblTransferenciasCustodia SET TipoCierre = 'firmada' WHERE InspeccionId = '{SALIDA_SIGUIENTE}'"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena,
            $"UPDATE dbo.tblTransferenciasCustodia SET TipoCierre = 'no_firma', NoFirmaParte = 'recibe', MotivoNoFirma = N'Se niega', TestigoUsuarioId = EntregaUsuarioId, EvidenciaConstanciaId = (SELECT TOP 1 Id FROM dbo.tblEvidencias WHERE EsConstancia = 1) WHERE InspeccionId = '{SALIDA_SIGUIENTE}'"));
        await Assert.ThrowsAsync<SqlException>(() => EjecutarAsync(_cadena,
            $"UPDATE dbo.tblTransferenciasCustodia SET TipoCierre = 'no_firma', NoFirmaParte = 'recibe', MotivoNoFirma = N'Se niega', TestigoUsuarioId = (SELECT Id FROM dbo.tblUsuarios WHERE Email = 'insp@e.com') WHERE InspeccionId = '{SALIDA_SIGUIENTE}'"));

        // La constancia completa si entra: motivo, testigo (una tercera persona) y la foto.
        await EjecutarAsync(_cadena, "INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, PasswordHash) VALUES (NEWID(), N'Testigo', 'testigo@e.com', 'inspector', 'h')");
        await EjecutarAsync(_cadena,
            $"UPDATE dbo.tblTransferenciasCustodia SET TipoCierre = 'no_firma', NoFirmaParte = 'recibe', MotivoNoFirma = N'Se niega', TestigoUsuarioId = (SELECT Id FROM dbo.tblUsuarios WHERE Email = 'testigo@e.com'), EvidenciaConstanciaId = (SELECT TOP 1 Id FROM dbo.tblEvidencias WHERE EsConstancia = 1) WHERE InspeccionId = '{SALIDA_SIGUIENTE}'");
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'no_firma'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_encima_de_una_base_ya_migrada_sin_cambiar_nada()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Cierre'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_sobre_la_base_anterior_sin_fallar()
    {
        // Antes de migrar: el ddl no toca las tablas anteriores (la migracion las cambia) pero tampoco se rompe.
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name = 'TipoCierre'"));
    }
}
