using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/48-parametros-de-avisos-y-tiempos.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-parametros-y-avisos.sql, el que ya esta
/// desplegado) y con datos: crea las seis filas de parametros sin sobrescribir ninguna, guarda el plazo de cada permiso (los que ya existian reciben su fecha mas 10 minutos), el reloj nunca concede antes de ese plazo, el
/// lugar gana la precision del GPS y la notificacion admite el tipo ubicacion_fijada; ningun otro dato cambia y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionParametrosYAvisosTests : IAsyncLifetime
{
    private const string CONDUCTOR = "11111111-1111-1111-1111-111111111111";
    private const string COORDINADOR = "22222222-2222-2222-2222-222222222222";
    private const string VIAJE_PENDIENTE = "55555555-5555-5555-5555-555555555555";
    private const string VIAJE_CONCEDIDO = "66666666-6666-6666-6666-666666666666";
    private const string VIAJE_NUEVO = "99999999-9999-9999-9999-999999999999";
    private const string LUGAR = "88888888-8888-8888-8888-888888888888";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionParametrosYAvisos");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-parametros-y-avisos.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Un lugar con dos viajes que ya tenian su solicitud de permiso: una pendiente pedida hace una hora y otra concedida por tiempo a los 12 minutos; el parametro de la firma del cargue ya cambiado por TI (45) y un
    /// aviso: lo que ya estaba guardado antes de migrar.
    /// </summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{CONDUCTOR}}', N'Carlos', 'cond@e.com', 'conductor', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{COORDINADOR}}', N'Diana', 'diana@e.com', 'coordinador_calidad', @cli, 'h');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{LUGAR}}', N'Puerto de Cartagena', (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia), N'Cartagena');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado, ConductorId, LugarCargueId) VALUES ('{{VIAJE_PENDIENTE}}', 'MTK-48A', N'ABC123', 'en_curso', '{{CONDUCTOR}}', '{{LUGAR}}');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado, ConductorId, LugarCargueId) VALUES ('{{VIAJE_CONCEDIDO}}', 'MTK-48B', N'ABC124', 'en_curso', '{{CONDUCTOR}}', '{{LUGAR}}');
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado, ConductorId, LugarCargueId) VALUES ('{{VIAJE_NUEVO}}', 'MTK-48C', N'ABC125', 'en_curso', '{{CONDUCTOR}}', '{{LUGAR}}');
        INSERT dbo.tblPermisosSinInspector (Id, ViajeId, ConductorId, LugarId, MotivoCodigo, Fecha, CreatedAt, UpdatedAt)
        VALUES (NEWID(), '{{VIAJE_PENDIENTE}}', '{{CONDUCTOR}}', '{{LUGAR}}', 'inspector_no_llego', SYSDATETIMEOFFSET(), DATEADD(HOUR, -1, SYSDATETIMEOFFSET()), DATEADD(HOUR, -1, SYSDATETIMEOFFSET()));
        INSERT dbo.tblPermisosSinInspector (Id, ViajeId, ConductorId, LugarId, MotivoCodigo, Fecha, Estado, ConcedidoAt, PorTiempo, CreatedAt, UpdatedAt)
        VALUES (NEWID(), '{{VIAJE_CONCEDIDO}}', '{{CONDUCTOR}}', '{{LUGAR}}', 'inspector_no_llego', SYSDATETIMEOFFSET(), 'concedido', DATEADD(MINUTE, 12, DATEADD(HOUR, -2, SYSDATETIMEOFFSET())), 1, DATEADD(HOUR, -2, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET());
        UPDATE dbo.tblParametros SET Valor = N'45' WHERE Clave = 'firma_cargue_pendiente_minutos';
        INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'novedad_proceso', 'alta', '{{COORDINADOR}}', N'T', N'M', 'bandeja', 'previa-1');
        """;

    private const string CLAVES_NUEVAS = "'permiso_sin_inspector_minutos', 'espera_inspector_aviso_minutos', 'espera_inspector_plan_b_minutos', 'viaje_sin_movimiento_horas', 'firma_cargue_pendiente_minutos', 'propuesta_ubicacion_visitas'";

    private static string PermisoNuevo(string vence, string extra = "", string valoresExtra = "") =>
        $"INSERT dbo.tblPermisosSinInspector (Id, ViajeId, ConductorId, LugarId, MotivoCodigo, Fecha, VenceAt{extra}) VALUES (NEWID(), '{VIAJE_NUEVO}', '{CONDUCTOR}', '{LUGAR}', 'inspector_no_llego', SYSDATETIMEOFFSET(), {vence}{valoresExtra})";

    private static string Notificacion(string clave, string tipo) =>
        $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), '{tipo}', 'alta', '{COORDINADOR}', N'T', N'M', 'bandeja', '{clave}')";

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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "48-parametros-de-avisos-y-tiempos.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_no_hay_plazo_ni_precision_ni_los_parametros_ni_el_tipo_de_aviso()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblPermisosSinInspector') AND name = 'VenceAt'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLugares') AND name = 'PrecisionM'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave IN ({CLAVES_NUEVAS})"));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k0", "ubicacion_fijada")));
    }

    [Fact]
    public async Task Migrar_crea_los_parametros_sin_sobrescribir_el_que_ya_estaba_y_conserva_lo_que_ya_existia()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(6, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblParametros WHERE Tipo = 'numero' AND Clave IN ({CLAVES_NUEVAS})"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'permiso_sin_inspector_minutos' AND Valor = N'10'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'espera_inspector_aviso_minutos' AND Valor = N'15'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'espera_inspector_plan_b_minutos' AND Valor = N'30'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'viaje_sin_movimiento_horas' AND Valor = N'24'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'propuesta_ubicacion_visitas' AND Valor = N'3'"));
        // El valor que TI ya habia cambiado no se sobrescribe.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'firma_cargue_pendiente_minutos' AND Valor = N'45'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
    }

    [Fact]
    public async Task Migrar_da_a_cada_permiso_que_ya_existia_su_fecha_mas_10_minutos_como_plazo_y_el_plazo_pasa_a_ser_obligatorio()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblPermisosSinInspector') AND name = 'VenceAt' AND is_nullable = 0"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector WHERE VenceAt = DATEADD(MINUTE, 10, CreatedAt)"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector WHERE Estado = 'concedido' AND PorTiempo = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector WHERE Estado = 'pendiente'"));
        Assert.Equal(515, await ErrorDeAsync(PermisoNuevo("NULL")));
    }

    [Fact]
    public async Task Despues_de_migrar_el_reloj_nunca_concede_antes_del_plazo_guardado_en_la_solicitud_ni_el_plazo_es_anterior_a_ella()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Un plazo de 15 minutos: a los 10 no se concede por tiempo, a los 15 si.
        Assert.Equal(0, await ErrorDeAsync(PermisoNuevo("DATEADD(MINUTE, 15, SYSDATETIMEOFFSET())")));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblPermisosSinInspector SET Estado = 'concedido', PorTiempo = 1, ConcedidoAt = DATEADD(MINUTE, 10, CreatedAt) WHERE ViajeId = '{VIAJE_NUEVO}'"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblPermisosSinInspector SET Estado = 'concedido', PorTiempo = 1, ConcedidoAt = VenceAt WHERE ViajeId = '{VIAJE_NUEVO}'"));
        // Un plazo anterior a la solicitud no tiene sentido.
        await EjecutarAsync($"DELETE FROM dbo.tblPermisosSinInspector WHERE ViajeId = '{VIAJE_NUEVO}'");
        Assert.Equal(547, await ErrorDeAsync(PermisoNuevo("DATEADD(MINUTE, -1, SYSDATETIMEOFFSET())")));
        // Concedido por un usuario antes del plazo si se puede: el plazo solo frena al reloj.
        Assert.Equal(0, await ErrorDeAsync(PermisoNuevo("DATEADD(MINUTE, 30, SYSDATETIMEOFFSET())", ", Estado, ConcedidoAt, ConcedidoPor", $", 'concedido', SYSDATETIMEOFFSET(), '{COORDINADOR}'")));
    }

    [Fact]
    public async Task Despues_de_migrar_el_lugar_guarda_la_precision_sin_negativos_y_la_notificacion_admite_ubicacion_fijada_sin_perder_los_tipos_anteriores()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE PrecisionM IS NULL"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblLugares SET PrecisionM = 12.5 WHERE Id = '{LUGAR}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblLugares SET PrecisionM = -1 WHERE Id = '{LUGAR}'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k1", "ubicacion_fijada")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k2", "inventado")));
        foreach (var (tipo, i) in new[] { "novedad_conductor", "permiso_sin_inspector", "firma_cargue_pendiente", "cambio_cargue_inspector", "cambio_destino_unidad", "carro_rechazado_en_destino", "propuesta_ubicacion" }.Select((t, i) => (t, i)))
        {
            Assert.Equal(0, await ErrorDeAsync(Notificacion($"anterior-{i}", tipo)));
        }
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblPermisosSinInspector') AND name = 'VenceAt' AND is_nullable = 0"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector WHERE VenceAt = DATEADD(MINUTE, 10, CreatedAt)"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblPermisosSinInspector_Reloj' AND definition LIKE '%VenceAt%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblPermisosSinInspector_Plazo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblLugares_Precision'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo'"));
        Assert.Equal(6, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave IN ({CLAVES_NUEVAS})"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'firma_cargue_pendiente_minutos' AND Valor = N'45'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k3", "ubicacion_fijada")));
    }
}
