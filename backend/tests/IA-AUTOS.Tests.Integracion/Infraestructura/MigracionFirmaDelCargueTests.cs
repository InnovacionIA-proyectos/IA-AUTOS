using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/42-firma-del-cargue-por-ninera.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-firma-del-cargue.sql, el que ya esta desplegado)
/// y con datos: la transferencia gana la hora y el GPS del momento de cada firma (por parte, con la latitud y la longitud juntas y dentro de rango) y la marca de firma por niñera; la notificacion admite el tipo
/// firma_cargue_pendiente sin quitar ninguno de los trece que ya existian; el parametro firma_cargue_pendiente_minutos queda en 30 sin pisar uno que ya exista; ningun dato cambia y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionFirmaDelCargueTests : IAsyncLifetime
{
    private const string ENTREGA = "11111111-1111-1111-1111-111111111111";
    private const string RECIBE = "22222222-2222-2222-2222-222222222222";
    private const string INSPECCION = "33333333-3333-3333-3333-333333333333";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string VEHICULO = "66666666-6666-6666-6666-666666666666";
    private const string ETAPA = "77777777-7777-7777-7777-777777777777";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionFirmaCargue");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-firma-del-cargue.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Una unidad con la salida de su cargue y una transferencia FIRMADA carro por carro (como se firmaba antes), una notificacion y un parametro: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{ENTREGA}}', N'Carlos', 'cond@e.com', 'conductor', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{RECIBE}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINCARGUE00000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES ('{{VIAJE}}', 'MTK-42', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION}}', '{{ETAPA}}', '{{VIAJE}}', '{{RECIBE}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaRecibeAt, TipoCierre)
        VALUES (NEWID(), '{{INSPECCION}}', '{{ENTREGA}}', '{{RECIBE}}', N'firma-entrega', N'firma-recibe', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'firmada');
        INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'novedad_proceso', 'alta', '{{RECIBE}}', N'T', N'M', 'bandeja', 'previa-1');
        """;

    private const string TRANSFERENCIA_PREVIA = "SELECT CONCAT(TipoCierre, '|', EntregaUsuarioId, '|', RecibeUsuarioId, '|', FirmaEntrega, '|', FirmaRecibe) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = '" + INSPECCION + "'";

    private static string Notificacion(string clave, string tipo) =>
        $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), '{tipo}', 'media', '{RECIBE}', N'T', N'M', 'bandeja', '{clave}')";

    private static string ActualizarTransferencia(string asignaciones) =>
        $"UPDATE dbo.tblTransferenciasCustodia SET {asignaciones} WHERE InspeccionId = '{INSPECCION}'";

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

    private async Task<string> TextoAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        return Convert.ToString(await comando.ExecuteScalarAsync()) ?? string.Empty;
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "42-firma-del-cargue-por-ninera.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_no_hay_columnas_de_momento_ni_el_tipo_de_aviso_ni_el_parametro()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name IN ('FirmaEntregaMomentoAt', 'FirmaRecibeLat', 'FirmaPorNinera')"));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k0", "firma_cargue_pendiente")));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'firma_cargue_pendiente_minutos'"));
    }

    [Fact]
    public async Task Migrar_agrega_las_columnas_el_tipo_y_el_parametro_y_conserva_lo_que_ya_existia()
    {
        var antes = await TextoAsync(TRANSFERENCIA_PREVIA);
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(8, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND is_nullable = 1 AND name IN ('FirmaEntregaMomentoAt', 'FirmaEntregaLat', 'FirmaEntregaLng', 'FirmaEntregaPrecisionM', 'FirmaRecibeMomentoAt', 'FirmaRecibeLat', 'FirmaRecibeLng', 'FirmaRecibePrecisionM')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name = 'FirmaPorNinera' AND is_nullable = 0"));
        // Lo ya firmado carro por carro se sigue leyendo igual: no es de niñera y no tiene momento.
        Assert.Equal(antes, await TextoAsync(TRANSFERENCIA_PREVIA));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE FirmaPorNinera = 1 OR FirmaEntregaMomentoAt IS NOT NULL OR FirmaRecibeLat IS NOT NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
        // El tipo nuevo se admite, uno inventado no, y los trece de antes siguen admitidos.
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k1", "firma_cargue_pendiente")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k2", "inventado")));
        foreach (var (tipo, i) in new[] { "novedad_conductor", "respuesta_conductor", "hallazgo_critico", "umbral_patron", "captura_incompleta", "transferencia_pendiente", "reporte_diario", "novedad_proceso", "reclamo_sin_actividad", "fuera_de_rango", "cargue_sin_reclamar", "propuesta_ubicacion", "permiso_sin_inspector" }.Select((t, i) => (t, i)))
        {
            Assert.Equal(0, await ErrorDeAsync(Notificacion($"anterior-{i}", tipo)));
        }

        Assert.Equal("30", await TextoAsync("SELECT Valor FROM dbo.tblParametros WHERE Clave = 'firma_cargue_pendiente_minutos'"));
    }

    [Fact]
    public async Task El_momento_y_el_gps_de_cada_parte_van_completos_y_dentro_de_rango()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Hora, latitud, longitud y precision de quien entrega: todo junto, o nada.
        Assert.Equal(0, await ErrorDeAsync(ActualizarTransferencia("FirmaEntregaMomentoAt = SYSDATETIMEOFFSET(), FirmaEntregaLat = 4.711, FirmaEntregaLng = -74.0721, FirmaEntregaPrecisionM = 8.5, FirmaPorNinera = 1")));
        Assert.Equal(0, await ErrorDeAsync(ActualizarTransferencia("FirmaEntregaPrecisionM = NULL")));
        Assert.Equal(547, await ErrorDeAsync(ActualizarTransferencia("FirmaEntregaLng = NULL")));
        Assert.Equal(547, await ErrorDeAsync(ActualizarTransferencia("FirmaEntregaLat = 91")));
        Assert.Equal(547, await ErrorDeAsync(ActualizarTransferencia("FirmaEntregaLng = -181")));
        Assert.Equal(547, await ErrorDeAsync(ActualizarTransferencia("FirmaEntregaPrecisionM = -1")));
        // Lo mismo para quien recibe.
        Assert.Equal(0, await ErrorDeAsync(ActualizarTransferencia("FirmaRecibeMomentoAt = SYSDATETIMEOFFSET(), FirmaRecibeLat = 10.4, FirmaRecibeLng = -75.5, FirmaRecibePrecisionM = 12")));
        Assert.Equal(547, await ErrorDeAsync(ActualizarTransferencia("FirmaRecibeLat = NULL")));
        Assert.Equal(547, await ErrorDeAsync(ActualizarTransferencia("FirmaRecibeLat = -91")));
        // Sin GPS (una firma vieja) tambien es valido.
        Assert.Equal(0, await ErrorDeAsync(ActualizarTransferencia("FirmaRecibeMomentoAt = NULL, FirmaRecibeLat = NULL, FirmaRecibeLng = NULL, FirmaRecibePrecisionM = NULL")));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name = 'FirmaPorNinera'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k3", "firma_cargue_pendiente")));

        // Un valor que el negocio ya ajusto no se pisa al repetir el script.
        await EjecutarAsync("UPDATE dbo.tblParametros SET Valor = N'45' WHERE Clave = 'firma_cargue_pendiente_minutos'");
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal("45", await TextoAsync("SELECT Valor FROM dbo.tblParametros WHERE Clave = 'firma_cargue_pendiente_minutos'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'firma_cargue_pendiente_minutos'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name IN ('CK_tblTransferencias_GpsEntrega', 'CK_tblTransferencias_GpsRecibe')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }
}
