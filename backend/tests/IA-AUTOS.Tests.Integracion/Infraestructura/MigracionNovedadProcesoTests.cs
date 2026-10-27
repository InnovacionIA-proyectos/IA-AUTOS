using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/23-novedad-de-proceso.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-novedad-proceso.sql, el que ya
/// esta desplegado) y con datos: crea el catalogo de motivos (con sus tres valores), la tabla append-only de novedades de proceso, el motivo en la inspeccion, el cierre «recibo sin entrega» de la
/// transferencia (que admite quien entrega nulo solo con ese cierre) y el tipo de notificacion novedad_proceso, sin cambiar ningun dato que ya existia.
/// </summary>
public sealed partial class MigracionNovedadProcesoTests : IAsyncLifetime
{
    private const string ENTREGA = "11111111-1111-1111-1111-111111111111";
    private const string RECIBE = "22222222-2222-2222-2222-222222222222";
    private const string INSPECCION_SALIDA = "33333333-3333-3333-3333-333333333333";
    private const string INSPECCION_TRANSPORTE = "44444444-4444-4444-4444-444444444444";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string VEHICULO = "66666666-6666-6666-6666-666666666666";
    private const string ETAPA_CARGUE = "77777777-7777-7777-7777-777777777777";
    private const string ETAPA_TRANSPORTE = "88888888-8888-8888-8888-888888888888";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionNovedadProceso");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionNovedadProceso" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-novedad-proceso.sql"));
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "23-novedad-de-proceso.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Una unidad con su salida de Cargue (con una transferencia FIRMADA ya cerrada), su salida de Transporte y una notificacion: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{ENTREGA}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{RECIBE}}', N'Carlos', 'cond@e.com', 'conductor', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINNOVEDAD0000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES ('{{VIAJE}}', 'MTK-41', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA_CARGUE}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA_TRANSPORTE}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte'), 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_SALIDA}}', '{{ETAPA_CARGUE}}', '{{VIAJE}}', '{{ENTREGA}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_TRANSPORTE}}', '{{ETAPA_TRANSPORTE}}', '{{VIAJE}}', '{{ENTREGA}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaRecibeAt, TipoCierre)
        VALUES (NEWID(), '{{INSPECCION_SALIDA}}', '{{ENTREGA}}', '{{RECIBE}}', N'firma-entrega', N'firma-recibe', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'firmada');
        INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'transferencia_pendiente', 'media', '{{RECIBE}}', N'T', N'M', 'bandeja', 'previa-1');
        """;

    private const string TRANSFERENCIA_FIRMADA = "SELECT CONCAT(TipoCierre, '|', EntregaUsuarioId, '|', FirmaEntrega, '|', FirmaRecibe) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '" + INSPECCION_SALIDA + "'";

    private static string Novedad(string tipo, string motivo = "NULL", string texto = "NULL", string inspeccion = INSPECCION_SALIDA, string etapa = ETAPA_CARGUE, string extra = "NULL, NULL") =>
        $"INSERT dbo.tblNovedadesProceso (Id, Tipo, ViajeId, VehiculoId, EtapaId, InspeccionId, MotivoCodigo, MotivoTexto, UsuarioId, Fecha, Lat, Lng) VALUES (NEWID(), '{tipo}', '{VIAJE}', '{VEHICULO}', '{etapa}', '{inspeccion}', {motivo}, {texto}, '{RECIBE}', SYSDATETIMEOFFSET(), {extra})";

    private static string ReciboSinEntrega(string inspeccion, string columnasExtra = "", string valoresExtra = "") =>
        $"INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, RecibeUsuarioId, FirmaRecibe, FirmaRecibeAt, TipoCierre{columnasExtra}) VALUES (NEWID(), '{inspeccion}', '{RECIBE}', N'firma-recibe', SYSDATETIMEOFFSET(), 'recibo_sin_entrega'{valoresExtra})";

    // ------------------------------------------------------------------------------------------------ migracion

    [Fact]
    public async Task La_migracion_crea_lo_nuevo_sin_cambiar_ningun_dato_y_se_puede_repetir()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblMotivosSinInspector', 'tblNovedadesProceso')"));
        var transferencia = await TextoAsync(TRANSFERENCIA_FIRMADA);

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblMotivosSinInspector', 'tblNovedadesProceso')"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosSinInspector WHERE Activo = 1"));
        Assert.Equal("inspector_no_llego=0;inspector_no_disponible=0;otro=1", await TextoAsync("SELECT STRING_AGG(CONCAT(Codigo, '=', RequiereTexto), ';') WITHIN GROUP (ORDER BY Orden) FROM dbo.tblMotivosSinInspector"));
        Assert.Equal(
            "El inspector no llegó;El inspector no está disponible;Otro",
            await TextoAsync("SELECT STRING_AGG(Nombre, ';') WITHIN GROUP (ORDER BY Orden) FROM dbo.tblMotivosSinInspector"));
        Assert.Equal(3, await ContarAsync(
            "SELECT COUNT(*) FROM sys.triggers WHERE name IN ('TR_tblNovedadesProceso_NoModificar', 'TR_tblNovedadesProceso_NoBorrar', 'TR_tblNovedadesProceso_TextoDelMotivo')"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblInspecciones') AND name IN ('MotivoSinInspector', 'MotivoSinInspectorTexto') AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name = 'EntregaUsuarioId' AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo' AND definition LIKE '%novedad_proceso%'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso"));

        // Lo que ya estaba guardado no cambia.
        Assert.Equal(transferencia, await TextoAsync(TRANSFERENCIA_FIRMADA));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE MotivoSinInspector IS NULL AND MotivoSinInspectorTexto IS NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'transferencia_pendiente'"));

        // Repetir el script no cambia nada (ya esta aplicado), ni siquiera una etiqueta de motivo editada.
        await EjecutarAsync(_cadena, "UPDATE dbo.tblMotivosSinInspector SET Nombre = N'Otro motivo' WHERE Codigo = 'otro'");
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosSinInspector"));
        Assert.Equal("Otro motivo", await TextoAsync("SELECT Nombre FROM dbo.tblMotivosSinInspector WHERE Codigo = 'otro'"));
        Assert.Equal(transferencia, await TextoAsync(TRANSFERENCIA_FIRMADA));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblMotivosSinInspector', 'tblNovedadesProceso')"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosSinInspector"));
        Assert.Equal(3, await ContarAsync(
            "SELECT COUNT(*) FROM sys.triggers WHERE name IN ('TR_tblNovedadesProceso_NoModificar', 'TR_tblNovedadesProceso_NoBorrar', 'TR_tblNovedadesProceso_TextoDelMotivo')"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.tblInspecciones') AND name IN ('CK_tblInspecciones_MotivoSinInspector', 'CK_tblInspecciones_MotivoTexto')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Entrega'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_sobre_el_esquema_previo_sin_migrar_crea_lo_nuevo_sin_fallar()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblMotivosSinInspector', 'tblNovedadesProceso')"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosSinInspector"));
    }

    // ------------------------------------------------------------------------------------------------ lo nuevo funciona

    [Fact]
    public async Task Un_recibo_sin_entrega_solo_lleva_la_firma_de_quien_recibe_y_los_demas_cierres_no_cambian()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Quien entrega falta solo en el recibo sin entrega, que exige la firma de quien recibe y no trae nada de la entrega.
        Assert.Equal(547, await ErrorDeAsync(ReciboSinEntrega(INSPECCION_TRANSPORTE, ", EntregaUsuarioId", $", '{ENTREGA}'")));
        Assert.Equal(547, await ErrorDeAsync(ReciboSinEntrega(INSPECCION_TRANSPORTE, ", FirmaEntrega", ", N'firma-entrega'")));
        Assert.Equal(547, await ErrorDeAsync(ReciboSinEntrega(INSPECCION_TRANSPORTE, ", FirmaEntregaAt", ", SYSDATETIMEOFFSET()")));
        Assert.Equal(547, await ErrorDeAsync(ReciboSinEntrega(INSPECCION_TRANSPORTE, ", ReservasTexto", ", N'reserva'")));
        Assert.Equal(547, await ErrorDeAsync(
            $"INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, RecibeUsuarioId, TipoCierre) VALUES (NEWID(), '{INSPECCION_TRANSPORTE}', '{RECIBE}', 'recibo_sin_entrega')"));
        Assert.Equal(547, await ErrorDeAsync($"INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, RecibeUsuarioId) VALUES (NEWID(), '{INSPECCION_TRANSPORTE}', '{RECIBE}')"));
        Assert.Equal(547, await ErrorDeAsync(
            $"INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, TipoCierre) VALUES (NEWID(), '{INSPECCION_TRANSPORTE}', '{RECIBE}', N'a', N'b', 'firmada')"));
        Assert.Equal(547, await ErrorDeAsync(
            $"INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, TipoCierre) VALUES (NEWID(), '{INSPECCION_TRANSPORTE}', '{ENTREGA}', '{RECIBE}', 'otro')"));

        Assert.Equal(0, await ErrorDeAsync(ReciboSinEntrega(INSPECCION_TRANSPORTE)));
        Assert.Equal(
            "recibo_sin_entrega|NULL|NULL|firma-recibe",
            await TextoAsync($"SELECT CONCAT(TipoCierre, '|', ISNULL(CAST(EntregaUsuarioId AS varchar(36)), 'NULL'), '|', ISNULL(FirmaEntrega, 'NULL'), '|', FirmaRecibe) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_TRANSPORTE}'"));
        Assert.Equal(51003, await ErrorDeAsync($"DELETE FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_TRANSPORTE}'"));
    }

    [Fact]
    public async Task La_novedad_de_proceso_es_append_only_exige_su_motivo_y_su_texto_cuando_toca()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(547, await ErrorDeAsync(Novedad("cargue_sin_inspector")));                                                  // el cargue sin inspector dice por que
        Assert.Equal(547, await ErrorDeAsync(Novedad("recibo_sin_entrega", texto: "N'texto sin motivo'")));                      // el texto acompana a un motivo
        Assert.Equal(547, await ErrorDeAsync(Novedad("cargue_sin_inspector", "'otro'", "N'   '")));                               // y no queda en blanco
        Assert.Equal(547, await ErrorDeAsync(Novedad("otra")));
        Assert.Equal(547, await ErrorDeAsync(Novedad("recibo_sin_entrega", extra: "91, NULL")));
        Assert.Equal(547, await ErrorDeAsync(Novedad("recibo_sin_entrega", extra: "NULL, -181")));
        Assert.Equal(547, await ErrorDeAsync(Novedad("cargue_sin_inspector", "'no_existe'")));
        Assert.Equal(51008, await ErrorDeAsync(Novedad("cargue_sin_inspector", "'otro'")));                                       // «otro» exige el texto
        Assert.Equal(51008, await ErrorDeAsync(Novedad("cargue_sin_inspector", "'inspector_no_llego'", "N'no deberia'")));       // un motivo de la lista no lo admite

        Assert.Equal(0, await ErrorDeAsync(Novedad("cargue_sin_inspector", "'inspector_no_llego'", extra: "4.6, -74.1")));
        Assert.Equal(0, await ErrorDeAsync(Novedad("cargue_sin_inspector", "'otro'", "N'el inspector se enfermo'", INSPECCION_TRANSPORTE, ETAPA_TRANSPORTE)));
        Assert.Equal(0, await ErrorDeAsync(Novedad("recibo_sin_entrega")));                                                       // el recibo sin entrega no necesita motivo
        Assert.Equal(2601, await ErrorDeAsync(Novedad("cargue_sin_inspector", "'inspector_no_disponible'")));                     // una por tipo e inspeccion
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso"));

        Assert.Equal(51005, await ErrorDeAsync("UPDATE dbo.tblNovedadesProceso SET Lat = 1"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblNovedadesProceso"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso"));
    }

    [Fact]
    public async Task La_inspeccion_guarda_el_motivo_solo_en_una_salida_y_la_notificacion_admite_el_tipo_nuevo()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET MotivoSinInspector = 'otro', MotivoSinInspectorTexto = N'el inspector se enfermo' WHERE Id = '{INSPECCION_SALIDA}'"));
        Assert.Equal("otro|el inspector se enfermo", await TextoAsync($"SELECT CONCAT(MotivoSinInspector, '|', MotivoSinInspectorTexto) FROM dbo.tblInspecciones WHERE Id = '{INSPECCION_SALIDA}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET MotivoSinInspector = 'no_existe' WHERE Id = '{INSPECCION_TRANSPORTE}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET MotivoSinInspectorTexto = N'sin motivo' WHERE Id = '{INSPECCION_TRANSPORTE}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET MotivoSinInspector = 'otro', MotivoSinInspectorTexto = N'  ' WHERE Id = '{INSPECCION_TRANSPORTE}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET Momento = 'llegada' WHERE Id = '{INSPECCION_SALIDA}'"));

        Assert.Equal(0, await ErrorDeAsync(
            $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'novedad_proceso', 'alta', '{RECIBE}', N'T', N'M', 'push', 'nueva-1')"));
        Assert.Equal(547, await ErrorDeAsync(
            $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'otro_tipo', 'alta', '{RECIBE}', N'T', N'M', 'push', 'nueva-2')"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones"));
    }
}
