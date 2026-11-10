using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/39-permiso-sin-inspector.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-permiso-sin-inspector.sql, el que ya esta
/// desplegado) y con datos: crea la tabla de permisos de la excepcion «No hay inspector» (uno por viaje, concedido por un usuario o por el reloj, y el reloj nunca antes de los 10 minutos), la novedad de proceso gana
/// el permiso con el que se activo y la notificacion admite el tipo permiso_sin_inspector; ningun dato cambia y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionPermisoSinInspectorTests : IAsyncLifetime
{
    private const string CONDUCTOR = "11111111-1111-1111-1111-111111111111";
    private const string COORDINADOR = "22222222-2222-2222-2222-222222222222";
    private const string INSPECCION = "33333333-3333-3333-3333-333333333333";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string VEHICULO = "66666666-6666-6666-6666-666666666666";
    private const string ETAPA = "77777777-7777-7777-7777-777777777777";
    private const string LUGAR = "88888888-8888-8888-8888-888888888888";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionPermisoSinInspector");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-permiso-sin-inspector.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un viaje con su cargue sin inspector ya activado antes del cambio (una novedad de proceso sin permiso) y una notificacion: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{CONDUCTOR}}', N'Carlos', 'cond@e.com', 'conductor', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{COORDINADOR}}', N'Diana', 'diana@e.com', 'coordinador_calidad', @cli, 'h');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{LUGAR}}', N'Puerto de Cartagena', (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia), N'Cartagena');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINPERMISO0000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado, ConductorId, LugarCargueId) VALUES ('{{VIAJE}}', 'MTK-39', N'ABC123', 'en_curso', '{{CONDUCTOR}}', '{{LUGAR}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, MotivoSinInspector) VALUES ('{{INSPECCION}}', '{{ETAPA}}', '{{VIAJE}}', '{{CONDUCTOR}}', 'salida', SYSDATETIMEOFFSET(), 'inspector_no_llego');
        INSERT dbo.tblNovedadesProceso (Id, Tipo, ViajeId, VehiculoId, EtapaId, InspeccionId, MotivoCodigo, UsuarioId, Fecha)
        VALUES (NEWID(), 'cargue_sin_inspector', '{{VIAJE}}', '{{VEHICULO}}', '{{ETAPA}}', '{{INSPECCION}}', 'inspector_no_llego', '{{CONDUCTOR}}', SYSDATETIMEOFFSET());
        INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'novedad_proceso', 'alta', '{{COORDINADOR}}', N'T', N'M', 'bandeja', 'previa-1');
        """;

    private static string Permiso(string columnasExtra = "", string valoresExtra = "") =>
        $"INSERT dbo.tblPermisosSinInspector (Id, ViajeId, ConductorId, LugarId, MotivoCodigo, Fecha{columnasExtra}) VALUES (NEWID(), '{VIAJE}', '{CONDUCTOR}', '{LUGAR}', 'inspector_no_llego', SYSDATETIMEOFFSET(){valoresExtra})";

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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "39-permiso-sin-inspector.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_no_hay_tabla_de_permisos_ni_el_tipo_de_aviso()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblPermisosSinInspector'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNovedadesProceso') AND name = 'PermisoId'"));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k0", "permiso_sin_inspector")));
    }

    [Fact]
    public async Task Migrar_crea_la_tabla_y_la_columna_admite_el_tipo_y_conserva_lo_que_ya_existia()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblPermisosSinInspector'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblNovedadesProceso') AND name = 'PermisoId' AND is_nullable = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblNovedadesProceso_Permiso'"));
        // La excepcion que ya se habia activado antes queda como estaba: una novedad sin permiso.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE PermisoId IS NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k1", "permiso_sin_inspector")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k2", "inventado")));
        foreach (var (tipo, i) in new[] { "novedad_conductor", "respuesta_conductor", "hallazgo_critico", "umbral_patron", "captura_incompleta", "transferencia_pendiente", "novedad_proceso", "reclamo_sin_actividad", "fuera_de_rango", "cargue_sin_reclamar", "propuesta_ubicacion" }.Select((t, i) => (t, i)))
        {
            Assert.Equal(0, await ErrorDeAsync(Notificacion($"anterior-{i}", tipo)));
        }
    }

    [Fact]
    public async Task Una_solicitud_nace_pendiente_se_concede_por_un_usuario_o_por_el_reloj_y_este_nunca_antes_de_los_10_minutos()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Una solicitud pendiente; una sola por viaje.
        Assert.Equal(0, await ErrorDeAsync(Permiso()));
        Assert.Equal(2627, await ErrorDeAsync(Permiso()));
        // Concedida por un coordinador, con su fecha; sin quien o sin fecha, no.
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblPermisosSinInspector SET Estado = 'concedido', ConcedidoAt = SYSDATETIMEOFFSET(), ConcedidoPor = '{COORDINADOR}' WHERE ViajeId = '{VIAJE}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblPermisosSinInspector SET ConcedidoPor = NULL WHERE ViajeId = '{VIAJE}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblPermisosSinInspector SET Estado = 'inventado' WHERE ViajeId = '{VIAJE}'"));

        // El reloj: concedida por tiempo sin usuario, y solo pasados los 10 minutos de la solicitud.
        await EjecutarAsync($"DELETE FROM dbo.tblPermisosSinInspector WHERE ViajeId = '{VIAJE}'");
        Assert.Equal(0, await ErrorDeAsync(Permiso()));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblPermisosSinInspector SET Estado = 'concedido', PorTiempo = 1, ConcedidoAt = DATEADD(MINUTE, 9, CreatedAt) WHERE ViajeId = '{VIAJE}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblPermisosSinInspector SET Estado = 'concedido', PorTiempo = 1, ConcedidoAt = DATEADD(MINUTE, 12, CreatedAt), ConcedidoPor = '{COORDINADOR}' WHERE ViajeId = '{VIAJE}'"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblPermisosSinInspector SET Estado = 'concedido', PorTiempo = 1, ConcedidoAt = DATEADD(MINUTE, 10, CreatedAt) WHERE ViajeId = '{VIAJE}'"));
        // La novedad de proceso se liga a su permiso.
        Assert.Equal(0, await ErrorDeAsync($"INSERT dbo.tblNovedadesProceso (Id, Tipo, ViajeId, VehiculoId, EtapaId, InspeccionId, MotivoCodigo, UsuarioId, Fecha, PermisoId) VALUES (NEWID(), 'recibo_sin_entrega', '{VIAJE}', '{VEHICULO}', '{ETAPA}', '{INSPECCION}', 'inspector_no_llego', '{CONDUCTOR}', SYSDATETIMEOFFSET(), (SELECT Id FROM dbo.tblPermisosSinInspector))"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblPermisosSinInspector'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k3", "permiso_sin_inspector")));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblPermisosSinInspector_Reloj'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_tblNovedadesProceso_Permiso'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso"));
        Assert.Equal(0, await ErrorDeAsync(Permiso()));
    }
}
