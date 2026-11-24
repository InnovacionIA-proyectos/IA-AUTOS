using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/46-entrega-por-parada-rechazo-y-motivos-del-cierre.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-entrega-por-parada.sql, el que ya
/// esta desplegado) y con reclamos de entrega activos: cada uno queda con su parada (copiado una vez por cada parada del viaje que es punto de su inspector, y liberado si su viaje no tiene destinos), el indice unico pasa a ser por
/// viaje, proceso y parada, la revision de llegada admite otro ciclo, la transferencia gana el motivo codificado, el testigo sin usuario y el rechazo, y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionEntregaPorParadaTests : IAsyncLifetime
{
    private const string ANA = "aaaa0000-0000-0000-0000-000000000001";
    private const string PEDRO = "aaaa0000-0000-0000-0000-000000000002";
    private const string NORTE = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string SUR = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string CARGUE = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string VIAJE_DOS_PARADAS = "55555555-5555-5555-5555-555555555555";
    private const string VIAJE_SIN_DESTINOS = "66666666-6666-6666-6666-666666666666";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionParada");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-entrega-por-parada.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Lo que ya estaba guardado antes de migrar: dos inspectores, tres lugares, dos viajes y sus reclamos (uno de entrega por viaje, uno del cargue y uno de entrega ya liberado).</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa');
        DECLARE @tipo uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{ANA}}', N'Ana', 'ana@e.com', 'inspector', @cli, 'h'), ('{{PEDRO}}', N'Pedro', 'pedro@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{NORTE}}', N'Norte', @tipo, N'Bogota'), ('{{SUR}}', N'Sur', @tipo, N'Cali'), ('{{CARGUE}}', N'Cargue', @tipo, N'Medellin');
        INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), '{{ANA}}', '{{NORTE}}'), (NEWID(), '{{ANA}}', '{{SUR}}');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('11111111-1111-1111-1111-111111111111', 'VINPARADA0000001', N'R', 'nacional', @cli), ('22222222-2222-2222-2222-222222222222', 'VINPARADA0000002', N'R', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, LugarCargueId) VALUES ('{{VIAJE_DOS_PARADAS}}', 'MTK-46', N'ABC123', '{{CARGUE}}'), ('{{VIAJE_SIN_DESTINOS}}', 'MTK-47', N'DEF456', '{{CARGUE}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId, LugarDestinoId) VALUES
            (NEWID(), '{{VIAJE_DOS_PARADAS}}', '11111111-1111-1111-1111-111111111111', '{{NORTE}}'),
            (NEWID(), '{{VIAJE_DOS_PARADAS}}', '22222222-2222-2222-2222-222222222222', '{{SUR}}');
        INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso) VALUES ('d1000000-0000-0000-0000-000000000001', '{{VIAJE_DOS_PARADAS}}', '{{ANA}}', 'entrega');
        INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso) VALUES ('d2000000-0000-0000-0000-000000000002', '{{VIAJE_SIN_DESTINOS}}', '{{PEDRO}}', 'entrega');
        INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso) VALUES ('d3000000-0000-0000-0000-000000000003', '{{VIAJE_DOS_PARADAS}}', '{{ANA}}', 'cargue');
        INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso, LiberadoAt, LiberadoPor, ReclamadoAt) VALUES ('d4000000-0000-0000-0000-000000000004', '{{VIAJE_SIN_DESTINOS}}', '{{ANA}}', 'entrega', SYSDATETIMEOFFSET(), '{{ANA}}', DATEADD(HOUR, -1, SYSDATETIMEOFFSET()));
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "46-entrega-por-parada-rechazo-y-motivos-del-cierre.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_el_reclamo_no_tiene_parada_ni_la_transferencia_admite_el_rechazo()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE name IN ('LugarId') AND object_id = OBJECT_ID('dbo.tblReclamos')"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE name IN ('MotivoCodigo', 'TestigoNombre', 'RechazoHallazgoId') AND object_id = OBJECT_ID('dbo.tblTransferenciasCustodia')"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE name = 'Ciclo' AND object_id = OBJECT_ID('dbo.tblInspecciones')"));
    }

    [Fact]
    public async Task Migrar_da_su_parada_a_cada_entrega_activa_copiandola_por_cada_parada_del_inspector_y_libera_la_de_un_viaje_sin_destinos()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Ana tenia un solo reclamo de entrega y dos paradas suyas en el viaje: queda una por parada.
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = '{VIAJE_DOS_PARADAS}' AND Proceso = 'entrega' AND LiberadoAt IS NULL AND LugarId = '{NORTE}' AND InspectorId = '{ANA}'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = '{VIAJE_DOS_PARADAS}' AND Proceso = 'entrega' AND LiberadoAt IS NULL AND LugarId = '{SUR}' AND InspectorId = '{ANA}'"));
        // El de un viaje sin destinos no queda huerfano: se libera.
        Assert.Equal(0, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = '{VIAJE_SIN_DESTINOS}' AND Proceso = 'entrega' AND LiberadoAt IS NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE Id = 'd2000000-0000-0000-0000-000000000002' AND LiberadoAt IS NOT NULL AND LiberadoPor = InspectorId"));
        // El del cargue no lleva parada y el historial liberado no cambia.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE Proceso = 'cargue' AND LugarId IS NULL AND LiberadoAt IS NULL"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos WHERE Proceso = 'entrega' AND LiberadoAt IS NULL AND LugarId IS NULL"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM dbo.tblReclamos"));
    }

    [Fact]
    public async Task Despues_de_migrar_hay_un_reclamo_activo_por_viaje_proceso_y_parada_y_la_entrega_activa_exige_su_parada()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(2601, await ErrorDeAsync($"INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso, LugarId) VALUES (NEWID(), '{VIAJE_DOS_PARADAS}', '{PEDRO}', 'entrega', '{NORTE}')"));
        Assert.Equal(0, await ErrorDeAsync($"INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso, LugarId) VALUES (NEWID(), '{VIAJE_SIN_DESTINOS}', '{PEDRO}', 'entrega', '{SUR}')"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso) VALUES (NEWID(), '{VIAJE_SIN_DESTINOS}', '{PEDRO}', 'entrega')"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso, LugarId) VALUES (NEWID(), '{VIAJE_SIN_DESTINOS}', '{PEDRO}', 'cargue', '{SUR}')"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso, LugarId) VALUES (NEWID(), '{VIAJE_SIN_DESTINOS}', '{PEDRO}', 'entrega', NEWID())"));
    }

    [Fact]
    public async Task Despues_de_migrar_la_etapa_admite_una_segunda_revision_original_de_otro_ciclo_y_no_dos_del_mismo()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        var etapa = await PrepararEtapaAsync();

        string Inspeccion(int ciclo) =>
            $"INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Estado, Ciclo) VALUES (NEWID(), '{etapa}', '{VIAJE_DOS_PARADAS}', '{ANA}', 'llegada', SYSDATETIMEOFFSET(), 'borrador', {ciclo})";

        Assert.Equal(0, await ErrorDeAsync(Inspeccion(1)));
        Assert.Equal(2601, await ErrorDeAsync(Inspeccion(1)));
        Assert.Equal(0, await ErrorDeAsync(Inspeccion(2)));
        Assert.Equal(547, await ErrorDeAsync(Inspeccion(0)));
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = '{etapa}'"));
    }

    private async Task<Guid> PrepararEtapaAsync()
    {
        var etapa = Guid.NewGuid();
        await EjecutarAsync(
            $"INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{etapa}', '11111111-1111-1111-1111-111111111111', '{VIAJE_DOS_PARADAS}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue'), 3)");
        return etapa;
    }

    [Fact]
    public async Task La_transferencia_gana_el_motivo_el_testigo_sin_usuario_y_el_rechazo_con_sus_reglas_y_el_aviso_nuevo_se_admite()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name IN ('MotivoCodigo', 'TestigoNombre', 'TestigoCedula', 'RechazoHallazgoId', 'RechazoMarcadoPor')"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name IN ('CK_tblTransferencias_TipoCierre', 'CK_tblTransferencias_Cierre') AND definition LIKE '%rechazado%'"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosSinInspector WHERE Codigo IN ('se_nego_a_firmar', 'no_habia_nadie', 'sin_inspector_del_punto') AND Activo = 0"));
        Assert.Equal(0, await ErrorDeAsync($"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'carro_rechazado_en_destino', 'alta', '{ANA}', N'T', N'M', 'bandeja', 'k1')"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'inventado', 'alta', '{ANA}', N'T', N'M', 'bandeja', 'k2')"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE name = 'LugarId' AND object_id = OBJECT_ID('dbo.tblReclamos')"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        // Ana sigue con sus dos paradas (el segundo paso no copia otra vez) y los motivos no se repiten.
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblReclamos WHERE ViajeId = '{VIAJE_DOS_PARADAS}' AND Proceso = 'entrega' AND LiberadoAt IS NULL"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosSinInspector WHERE Codigo IN ('se_nego_a_firmar', 'no_habia_nadie', 'sin_inspector_del_punto')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblReclamos_Parada'"));
    }
}
