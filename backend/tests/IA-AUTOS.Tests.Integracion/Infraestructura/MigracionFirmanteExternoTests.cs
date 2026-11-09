using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/35-firmante-externo-y-datos-del-aviso.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-firmante-externo.sql, el que ya esta
/// desplegado) y con datos: la transferencia admite a una persona externa que recibe (nombre y cedula, sin usuario) sin romper las que ya existen, la restriccion nueva decide quien recibe, y la notificacion gana sus
/// datos propios en JSON protegidos por el trigger. El script y el ddl nuevo se pueden repetir sin cambiar nada.
/// </summary>
public sealed partial class MigracionFirmanteExternoTests : IAsyncLifetime
{
    private const string ENTREGA = "11111111-1111-1111-1111-111111111111";
    private const string RECIBE = "22222222-2222-2222-2222-222222222222";
    private const string INSPECCION_SALIDA = "33333333-3333-3333-3333-333333333333";
    private const string INSPECCION_LLEGADA = "44444444-4444-4444-4444-444444444444";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string VEHICULO = "66666666-6666-6666-6666-666666666666";
    private const string ETAPA_CARGUE = "77777777-7777-7777-7777-777777777777";
    private const string ETAPA_DESCARGUE = "88888888-8888-8888-8888-888888888888";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionFirmanteExterno");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-firmante-externo.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Una unidad con la salida de su Cargue (con una transferencia FIRMADA ya cerrada), la llegada de su Descargue (sin transferencia) y una notificacion: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{ENTREGA}}', N'Carlos', 'cond@e.com', 'conductor', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{RECIBE}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINEXTERNO00000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES ('{{VIAJE}}', 'MTK-35', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA_CARGUE}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA_DESCARGUE}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue'), 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_SALIDA}}', '{{ETAPA_CARGUE}}', '{{VIAJE}}', '{{RECIBE}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_LLEGADA}}', '{{ETAPA_DESCARGUE}}', '{{VIAJE}}', '{{RECIBE}}', 'llegada', SYSDATETIMEOFFSET());
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaRecibeAt, TipoCierre)
        VALUES (NEWID(), '{{INSPECCION_SALIDA}}', '{{ENTREGA}}', '{{RECIBE}}', N'firma-entrega', N'firma-recibe', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'firmada');
        INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'novedad_proceso', 'alta', '{{RECIBE}}', N'T', N'M', 'bandeja', 'previa-1');
        """;

    private const string TRANSFERENCIA_PREVIA = "SELECT CONCAT(TipoCierre, '|', EntregaUsuarioId, '|', RecibeUsuarioId, '|', FirmaEntrega, '|', FirmaRecibe) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '" + INSPECCION_SALIDA + "'";

    private static string ConExterno(string columnas, string valores) =>
        $"INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId{columnas}) VALUES (NEWID(), '{INSPECCION_LLEGADA}', '{ENTREGA}'{valores})";

    private static string Notificacion(string clave, string datos) =>
        $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe, Datos) VALUES (NEWID(), 'novedad_proceso', 'alta', '{RECIBE}', N'T', N'M', 'bandeja', '{clave}', {datos})";

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
            await EjecutarAsync(sql);
            return 0;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    private static async Task<string> ScriptMigracionAsync() =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "35-firmante-externo-y-datos-del-aviso.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_quien_recibe_es_siempre_un_usuario_y_la_notificacion_no_tiene_datos()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name IN ('RecibeExternoNombre', 'RecibeExternoCedula')"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNotificaciones') AND name = 'Datos'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name = 'RecibeUsuarioId' AND is_nullable = 0"));
        Assert.Equal(515, await ErrorDeAsync(ConExterno("", "")));
    }

    [Fact]
    public async Task Migrar_agrega_las_columnas_la_restriccion_y_el_indice_sin_cambiar_la_transferencia_que_ya_existia()
    {
        var antes = await TextoAsync(TRANSFERENCIA_PREVIA);

        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name IN ('RecibeExternoNombre', 'RecibeExternoCedula') AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name = 'RecibeUsuarioId' AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_tblTransferencias_Recibe' AND object_id = OBJECT_ID('dbo.tblTransferenciasCustodia')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Testigo' AND parent_object_id = OBJECT_ID('dbo.tblTransferenciasCustodia')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Recibe' AND parent_object_id = OBJECT_ID('dbo.tblTransferenciasCustodia')"));
        Assert.Equal(antes, await TextoAsync(TRANSFERENCIA_PREVIA));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE RecibeExternoNombre IS NULL AND RecibeExternoCedula IS NULL"));
    }

    [Fact]
    public async Task Despues_de_migrar_recibe_un_usuario_o_una_persona_externa_con_nombre_y_cedula_juntos_y_nunca_las_dos()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Nombre sin cedula, cedula sin nombre, vacios, y a la vez un usuario y una persona externa: no pasan. Una transferencia abierta tampoco queda sin quien reciba.
        Assert.Equal(547, await ErrorDeAsync(ConExterno(", RecibeExternoNombre", ", N'Ana Restrepo'")));
        Assert.Equal(547, await ErrorDeAsync(ConExterno(", RecibeExternoCedula", ", '1020304050'")));
        Assert.Equal(547, await ErrorDeAsync(ConExterno(", RecibeExternoNombre, RecibeExternoCedula", ", N'', '1020304050'")));
        Assert.Equal(547, await ErrorDeAsync(ConExterno(", RecibeExternoNombre, RecibeExternoCedula", ", N'Ana Restrepo', ''")));
        Assert.Equal(547, await ErrorDeAsync(ConExterno(", RecibeUsuarioId, RecibeExternoNombre, RecibeExternoCedula", $", '{RECIBE}', N'Ana Restrepo', '1020304050'")));
        Assert.Equal(547, await ErrorDeAsync(ConExterno("", "")));
        Assert.Equal(547, await ErrorDeAsync(ConExterno(", TipoCierre", ", 'firmada'")));

        // La persona externa firma como quien recibe: las dos firmas la cierran como firmada.
        Assert.Equal(
            0,
            await ErrorDeAsync(ConExterno(
                ", RecibeExternoNombre, RecibeExternoCedula, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaRecibeAt, TipoCierre",
                ", N'Ana Restrepo', '1 020 304 050', N'firma-entrega', N'firma-externa', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'firmada'")));
        Assert.Equal(
            "Ana Restrepo|1 020 304 050|firmada",
            await TextoAsync($"SELECT CONCAT(RecibeExternoNombre, '|', RecibeExternoCedula, '|', TipoCierre) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_LLEGADA}'"));
        Assert.Equal(0, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_LLEGADA}' AND RecibeUsuarioId IS NOT NULL"));
    }

    [Fact]
    public async Task Despues_de_migrar_solo_falta_quien_recibe_en_el_cierre_con_la_sola_firma_de_quien_entrega()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Cerrada con una firma pero sin que firme quien entrega, o con la parte equivocada: no pasa.
        Assert.Equal(
            547,
            await ErrorDeAsync(ConExterno(
                ", FirmaEntrega, NoFirmaParte, MotivoNoFirma, TipoCierre", ", N'firma-entrega', 'entrega', N'El concesionario cerro', 'recibo_sin_entrega'")));
        Assert.Equal(
            0,
            await ErrorDeAsync(ConExterno(
                ", FirmaEntrega, NoFirmaParte, MotivoNoFirma, TipoCierre", ", N'firma-entrega', 'recibe', N'El concesionario cerro', 'recibo_sin_entrega'")));
        Assert.Equal(
            "recibe|El concesionario cerro",
            await TextoAsync($"SELECT CONCAT(NoFirmaParte, '|', MotivoNoFirma) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_LLEGADA}' AND RecibeUsuarioId IS NULL AND RecibeExternoNombre IS NULL"));
    }

    [Fact]
    public async Task Migrar_agrega_los_datos_del_aviso_en_json_protegidos_por_el_trigger_y_conserva_las_notificaciones()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNotificaciones') AND name = 'Datos' AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1' AND Datos IS NULL"));

        Assert.Equal(0, await ErrorDeAsync(Notificacion("nueva-1", "N'{\"lugar\":\"Concesionario Norte\",\"parte_que_firmo\":\"entrega\"}'")));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("nueva-2", "NULL")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("nueva-3", "N'esto no es json'")));

        // El trigger sigue permitiendo leer y responder, y ahora tambien protege Datos.
        Assert.Equal(0, await ErrorDeAsync("UPDATE dbo.tblNotificaciones SET LeidaAt = SYSDATETIMEOFFSET() WHERE ClaveDedupe = 'nueva-1'"));
        Assert.Equal(51006, await ErrorDeAsync("UPDATE dbo.tblNotificaciones SET Datos = N'{}' WHERE ClaveDedupe = 'nueva-1'"));
        Assert.Equal(51006, await ErrorDeAsync("UPDATE dbo.tblNotificaciones SET Titulo = N'Otro' WHERE ClaveDedupe = 'nueva-1'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_deja_una_sola_restriccion_de_cada_una()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Recibe'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Testigo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Datos'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_tblTransferencias_Recibe'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        // Sobre la base antigua el ddl trae solo lo que falta (columnas, nulabilidad, restricciones y el trigger con Datos).
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name = 'RecibeUsuarioId' AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Recibe'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Testigo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Datos'"));
        Assert.Equal(51006, await ErrorDeAsync("UPDATE dbo.tblNotificaciones SET Datos = N'{}' WHERE ClaveDedupe = 'previa-1'"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblTransferencias_Recibe'"));
        Assert.Equal(0, await ErrorDeAsync(ConExterno(", RecibeExternoNombre, RecibeExternoCedula", ", N'Ana Restrepo', '1020304050'")));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '{INSPECCION_SALIDA}' AND RecibeUsuarioId = '{RECIBE}'"));
    }
}
