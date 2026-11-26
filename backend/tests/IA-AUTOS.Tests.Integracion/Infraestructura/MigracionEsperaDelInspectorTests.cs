using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/49-espera-del-inspector-del-punto.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-espera-del-inspector.sql, el que ya esta desplegado) y
/// con datos: crea la tabla de la espera con sus restricciones y su indice de una sola espera abierta por parada, la transferencia gana la marca «entregada sin inspector del punto» (los carros que ya existian quedan sin
/// ella), la notificacion admite el tipo espera_inspector_punto sin perder los anteriores, ningun dato existente cambia y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionEsperaDelInspectorTests : IAsyncLifetime
{
    private const string CONDUCTOR = "11111111-1111-1111-1111-111111111111";
    private const string INSPECTOR = "22222222-2222-2222-2222-222222222222";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string LUGAR = "88888888-8888-8888-8888-888888888888";
    private const string VEHICULO = "99999999-9999-9999-9999-999999999999";
    private const string ETAPA = "77777777-7777-7777-7777-777777777777";
    private const string INSPECCION = "66666666-6666-6666-6666-666666666666";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionEspera");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-espera-del-inspector.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Lo que ya estaba guardado antes de migrar: un conductor, un inspector, un lugar, un viaje con un carro, su revision de llegada con su transferencia y un aviso.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{CONDUCTOR}}', N'Luis', 'luis@e.com', 'conductor', @cli, 'h'), ('{{INSPECTOR}}', N'Pedro', 'pedro@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{LUGAR}}', N'Puerto de Cartagena', (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia), N'Cartagena');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINESPERA0000001', N'R', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado, ConductorId, LugarCargueId) VALUES ('{{VIAJE}}', 'MTK-49', N'ABC123', 'en_curso', '{{CONDUCTOR}}', '{{LUGAR}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId, LugarDestinoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}', '{{LUGAR}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue'), 3);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION}}', '{{ETAPA}}', '{{VIAJE}}', '{{INSPECTOR}}', 'llegada', SYSDATETIMEOFFSET());
        INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId) VALUES (NEWID(), '{{INSPECCION}}', '{{CONDUCTOR}}', '{{INSPECTOR}}');
        INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'novedad_proceso', 'alta', '{{INSPECTOR}}', N'T', N'M', 'bandeja', 'previa-1');
        """;

    private static string Espera(string columnas = "", string valores = "", string activada = "DATEADD(MINUTE, -20, SYSDATETIMEOFFSET())") =>
        $"INSERT dbo.tblEsperasInspectorPunto (Id, ViajeId, LugarId, ConductorId, MotivoCodigo, ActivadaAt{columnas}) VALUES (NEWID(), '{VIAJE}', '{LUGAR}', '{CONDUCTOR}', 'inspector_no_llego', {activada}{valores})";

    private static string Notificacion(string clave, string tipo) =>
        $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), '{tipo}', 'alta', '{INSPECTOR}', N'T', N'M', 'bandeja', '{clave}')";

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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "49-espera-del-inspector-del-punto.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_no_hay_tabla_de_espera_ni_marca_en_la_transferencia_ni_el_tipo_de_aviso()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblEsperasInspectorPunto'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name = 'SinInspectorDelPunto'"));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k0", "espera_inspector_punto")));
    }

    [Fact]
    public async Task Migrar_crea_la_tabla_con_sus_restricciones_y_el_resultado_del_script_coincide_con_lo_esperado()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblEsperasInspectorPunto'"));
        Assert.Equal(8, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.tblEsperasInspectorPunto') AND name LIKE 'CK[_]tblEsperasInspectorPunto[_]%'"));
        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.tblEsperasInspectorPunto')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblEsperasInspectorPunto_Abierta' AND object_id = OBJECT_ID('dbo.tblEsperasInspectorPunto') AND is_unique = 1 AND has_filter = 1"));
    }

    [Fact]
    public async Task Cada_estado_exige_sus_datos_y_la_hora_de_activacion_nunca_es_posterior_a_la_de_llegada_al_servidor()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(Espera()));
        // Un estado que no existe, y una activacion posterior a la llegada al servidor.
        Assert.Equal(547, await ErrorDeAsync(Espera(", Estado", ", 'inventado'")));
        Assert.Equal(547, await ErrorDeAsync(Espera(", Estado", ", 'anulada'", activada: "DATEADD(MINUTE, 5, SYSDATETIMEOFFSET())")));
        // «El inspector llego» trae al inspector y la hora; los demas estados no los traen.
        Assert.Equal(547, await ErrorDeAsync(Espera(", Estado", ", 'inspector_llego'")));
        Assert.Equal(547, await ErrorDeAsync(Espera(", Estado, InspectorId, InspectorLlegoAt", $", 'anulada', '{INSPECTOR}', SYSDATETIMEOFFSET()")));
        Assert.Equal(0, await ErrorDeAsync(Espera(", Estado, InspectorId, InspectorLlegoAt", $", 'inspector_llego', '{INSPECTOR}', SYSDATETIMEOFFSET()")));
        // «Cerrada sin inspector del punto» trae la hora y quien cerro; los demas estados no.
        Assert.Equal(547, await ErrorDeAsync(Espera(", Estado", ", 'cerrada_sin_inspector_del_punto'")));
        Assert.Equal(0, await ErrorDeAsync(Espera(", Estado, CerradaAt, CerradaPor", $", 'cerrada_sin_inspector_del_punto', SYSDATETIMEOFFSET(), '{CONDUCTOR}'")));
        Assert.Equal(0, await ErrorDeAsync(Espera(", Estado", ", 'anulada'")));
        // Un motivo que no esta en el catalogo, un GPS fuera de rango y un texto vacio se rechazan.
        Assert.Equal(547, await ErrorDeAsync("INSERT dbo.tblEsperasInspectorPunto (Id, ViajeId, LugarId, ConductorId, MotivoCodigo, ActivadaAt, Estado) " +
            $"VALUES (NEWID(), '{VIAJE}', '{LUGAR}', '{CONDUCTOR}', 'no_existe', SYSDATETIMEOFFSET(), 'anulada')"));
        Assert.Equal(547, await ErrorDeAsync(Espera(", Lat, Lng, Estado", ", 95, 10, 'anulada'")));
        Assert.Equal(547, await ErrorDeAsync(Espera(", MotivoTexto, Estado", ", N'   ', 'anulada'")));
    }

    [Fact]
    public async Task Una_parada_tiene_a_lo_sumo_una_espera_abierta_y_las_que_terminaron_quedan_como_historial()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(Espera()));
        // Una segunda espera abierta en la misma parada choca con el indice unico (2601).
        Assert.Equal(2601, await ErrorDeAsync(Espera()));
        // Al terminar la primera se puede activar otra: la que termino queda como historial.
        await EjecutarAsync($"UPDATE dbo.tblEsperasInspectorPunto SET Estado = 'inspector_llego', InspectorId = '{INSPECTOR}', InspectorLlegoAt = SYSDATETIMEOFFSET() WHERE ViajeId = '{VIAJE}'");
        Assert.Equal(0, await ErrorDeAsync(Espera()));
        Assert.Equal(2, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto WHERE ViajeId = '{VIAJE}' AND LugarId = '{LUGAR}'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblEsperasInspectorPunto WHERE ViajeId = '{VIAJE}' AND Estado = 'esperando'"));
    }

    [Fact]
    public async Task Migrar_deja_sin_marca_la_transferencia_que_ya_existia_y_conserva_lo_demas_y_la_notificacion_admite_el_tipo_nuevo_sin_perder_los_anteriores()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblTransferenciasCustodia') AND name = 'SinInspectorDelPunto' AND is_nullable = 0"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE SinInspectorDelPunto = 0"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k1", "espera_inspector_punto")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k2", "inventado")));
        foreach (var (tipo, i) in new[] { "novedad_conductor", "permiso_sin_inspector", "firma_cargue_pendiente", "cambio_destino_unidad", "carro_rechazado_en_destino", "ubicacion_fijada" }.Select((t, i) => (t, i)))
        {
            Assert.Equal(0, await ErrorDeAsync(Notificacion($"anterior-{i}", tipo)));
        }
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblEsperasInspectorPunto'"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(8, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.tblEsperasInspectorPunto') AND name LIKE 'CK[_]tblEsperasInspectorPunto[_]%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblEsperasInspectorPunto_Abierta' AND object_id = OBJECT_ID('dbo.tblEsperasInspectorPunto')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE SinInspectorDelPunto = 0"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k3", "espera_inspector_punto")));
    }
}
