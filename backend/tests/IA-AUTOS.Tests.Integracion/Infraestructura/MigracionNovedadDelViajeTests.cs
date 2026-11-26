using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/52-novedad-del-viaje-y-reenvio-sin-duplicar.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-novedad-del-viaje.sql, el que ya esta
/// desplegado) y con datos: crea el catalogo de tipos con sus diez valores y la tabla de la novedad del viaje con sus restricciones, su envio unico por viaje y sus triggers de solo agregar, el hallazgo gana el identificador del
/// envio (los que ya existian quedan sin el), la notificacion admite el tipo novedad_viaje sin perder los anteriores, ningun dato existente cambia y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionNovedadDelViajeTests : IAsyncLifetime
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
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionNovedadViaje");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-novedad-del-viaje.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Lo que ya estaba guardado antes de migrar: un conductor, un inspector, un lugar, un viaje con un carro, su revision con un hallazgo y un aviso.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa');
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{CONDUCTOR}}', N'Luis', 'luis@e.com', 'conductor', @cli, 'h'), ('{{INSPECTOR}}', N'Pedro', 'pedro@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES ('{{LUGAR}}', N'Puerto de Cartagena', (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia), N'Cartagena');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{{VEHICULO}}', 'VINNOVEDAD000001', N'R', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado, ConductorId, LugarCargueId) VALUES ('{{VIAJE}}', 'MTK-53', N'ABC123', 'en_curso', '{{CONDUCTOR}}', '{{LUGAR}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId, LugarDestinoId) VALUES (NEWID(), '{{VIAJE}}', '{{VEHICULO}}', '{{LUGAR}}');
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{{ETAPA}}', '{{VEHICULO}}', '{{VIAJE}}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue'), 3);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION}}', '{{ETAPA}}', '{{VIAJE}}', '{{INSPECTOR}}', 'llegada', SYSDATETIMEOFFSET());
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (NEWID(), '{{INSPECCION}}', N'Capot', 'golpe', '1_5cm');
        INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'novedad_proceso', 'alta', '{{INSPECTOR}}', N'T', N'M', 'bandeja', 'previa-1');
        """;

    /// <summary>Una novedad del conductor valida; las columnas y valores extra se suman (o se repiten para probar un rechazo).</summary>
    private static string Novedad(string columnas = "", string valores = "", string tipo = "falla_camion", string usuario = CONDUCTOR, string rol = "conductor") =>
        $"INSERT dbo.tblNovedadesViaje (Id, ViajeId, TipoCodigo, UsuarioId, Rol, Texto, Fecha{columnas}) VALUES (NEWID(), '{VIAJE}', '{tipo}', '{usuario}', '{rol}', N'Se reventó una llanta', SYSDATETIMEOFFSET(){valores})";

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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "52-novedad-del-viaje-y-reenvio-sin-duplicar.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_no_hay_catalogo_ni_tabla_ni_identificador_de_envio_ni_el_tipo_de_aviso()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblTiposNovedadViaje', 'tblNovedadesViaje')"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblHallazgos') AND name = 'IdEnvio'"));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k0", "novedad_viaje")));
    }

    [Fact]
    public async Task Migrar_crea_el_catalogo_con_sus_diez_tipos_y_la_tabla_con_sus_restricciones_y_sus_indices()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(10, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposNovedadViaje"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposNovedadViaje WHERE Rol = 'conductor'"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposNovedadViaje WHERE Rol = 'inspector'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposNovedadViaje WHERE Rol = 'todos' AND Codigo = 'otro'"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposNovedadViaje WHERE Urgencia = 'alta'"));
        Assert.Equal(7, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposNovedadViaje WHERE Urgencia = 'media'"));
        Assert.Equal(7, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.tblNovedadesViaje') AND name LIKE 'CK[_]tblNovedadesViaje[_]%'"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('dbo.tblNovedadesViaje')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblNovedadesViaje_Envio' AND object_id = OBJECT_ID('dbo.tblNovedadesViaje') AND is_unique = 1 AND has_filter = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_tblHallazgos_Envio' AND object_id = OBJECT_ID('dbo.tblHallazgos') AND is_unique = 1 AND has_filter = 1"));
    }

    [Fact]
    public async Task La_base_exige_el_tipo_del_catalogo_el_rol_el_proceso_con_su_parada_el_texto_y_el_gps_completo()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Una novedad del conductor sin GPS ni foto es valida.
        Assert.Equal(0, await ErrorDeAsync(Novedad()));
        // Un tipo que no esta en el catalogo, un rol que no existe y un texto en blanco se rechazan.
        Assert.Equal(547, await ErrorDeAsync(Novedad(tipo: "inventado")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(rol: "admin")));
        Assert.Equal(547, await ErrorDeAsync("INSERT dbo.tblNovedadesViaje (Id, ViajeId, TipoCodigo, UsuarioId, Rol, Texto, Fecha) " +
            $"VALUES (NEWID(), '{VIAJE}', 'falla_camion', '{CONDUCTOR}', 'conductor', N'   ', SYSDATETIMEOFFSET())"));
        // El conductor no indica proceso ni parada.
        Assert.Equal(547, await ErrorDeAsync(Novedad(", Proceso", ", 'cargue'")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(", LugarId", $", '{LUGAR}'")));
        // El inspector indica el proceso: en el cargue sin parada y en la entrega con ella.
        Assert.Equal(547, await ErrorDeAsync(Novedad(tipo: "faltan_documentos", usuario: INSPECTOR, rol: "inspector")));
        Assert.Equal(0, await ErrorDeAsync(Novedad(", Proceso", ", 'cargue'", "faltan_documentos", INSPECTOR, "inspector")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(", Proceso, LugarId", $", 'cargue', '{LUGAR}'", "faltan_documentos", INSPECTOR, "inspector")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(", Proceso", ", 'entrega'", "faltan_documentos", INSPECTOR, "inspector")));
        Assert.Equal(0, await ErrorDeAsync(Novedad(", Proceso, LugarId", $", 'entrega', '{LUGAR}'", "faltan_documentos", INSPECTOR, "inspector")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(", Proceso", ", 'otro'", "faltan_documentos", INSPECTOR, "inspector")));
        // El GPS va completo o no va, y dentro de rango.
        Assert.Equal(0, await ErrorDeAsync(Novedad(", Lat, Lng", ", 6.2, -75.5")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(", Lat", ", 6.2")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(", Lat, Lng", ", 95, -75.5")));
    }

    [Fact]
    public async Task La_foto_va_completa_con_su_sello_y_su_hash_o_no_va()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        const string FOTO = ", FotoStoragePath, FotoSelloFecha, FotoSelloLat, FotoSelloLng, FotoHashSha256";
        var hash = new string('a', 64);

        Assert.Equal(0, await ErrorDeAsync(Novedad(FOTO, $", 'llanta.jpg', SYSDATETIMEOFFSET(), 6.2, -75.5, '{hash}'")));
        // Una foto sin hash, o con datos sueltos sin la foto, se rechaza.
        Assert.Equal(547, await ErrorDeAsync(Novedad(", FotoStoragePath, FotoSelloFecha, FotoSelloLat, FotoSelloLng", ", 'llanta.jpg', SYSDATETIMEOFFSET(), 6.2, -75.5")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(", FotoPrecision", ", 5")));
        // El hash es hexadecimal en minuscula, el sello esta en rango y la precision no es negativa.
        Assert.Equal(547, await ErrorDeAsync(Novedad(FOTO, $", 'llanta.jpg', SYSDATETIMEOFFSET(), 6.2, -75.5, '{new string('Z', 64)}'")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(FOTO, $", 'llanta.jpg', SYSDATETIMEOFFSET(), 95, -75.5, '{hash}'")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(FOTO + ", FotoPrecision", $", 'llanta.jpg', SYSDATETIMEOFFSET(), 6.2, -75.5, '{hash}', -1")));
    }

    [Fact]
    public async Task Un_identificador_de_envio_se_repite_solo_si_no_existe_y_las_que_no_lo_traen_conviven()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        var envio = Guid.NewGuid();

        Assert.Equal(0, await ErrorDeAsync(Novedad(", IdEnvio", $", '{envio}'")));
        // El mismo envio en el mismo viaje choca con el indice unico (2601); otro envio, y las que no traen identificador, no.
        Assert.Equal(2601, await ErrorDeAsync(Novedad(", IdEnvio", $", '{envio}'")));
        Assert.Equal(0, await ErrorDeAsync(Novedad(", IdEnvio", $", '{Guid.NewGuid()}'")));
        Assert.Equal(0, await ErrorDeAsync(Novedad()));
        Assert.Equal(0, await ErrorDeAsync(Novedad()));

        // Lo mismo en la novedad de un carro: el envio es unico por inspeccion y los hallazgos sin envio conviven.
        string Hallazgo(Guid? id) =>
            $"INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano{(id is null ? string.Empty : ", IdEnvio")}) VALUES (NEWID(), '{INSPECCION}', N'Capot', 'golpe', '1_5cm'{(id is null ? string.Empty : $", '{id}'")})";
        var envioDelCarro = Guid.NewGuid();
        Assert.Equal(0, await ErrorDeAsync(Hallazgo(envioDelCarro)));
        Assert.Equal(2601, await ErrorDeAsync(Hallazgo(envioDelCarro)));
        Assert.Equal(0, await ErrorDeAsync(Hallazgo(null)));
        Assert.Equal(0, await ErrorDeAsync(Hallazgo(null)));
    }

    [Fact]
    public async Task La_novedad_del_viaje_no_se_modifica_ni_se_borra()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(Novedad());

        Assert.Equal(51005, await ErrorDeAsync("UPDATE dbo.tblNovedadesViaje SET Texto = N'Otro texto'"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblNovedadesViaje"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesViaje"));
    }

    [Fact]
    public async Task Migrar_no_toca_lo_que_ya_existia_deja_sin_envio_el_hallazgo_anterior_y_la_notificacion_admite_el_tipo_nuevo_sin_perder_los_anteriores()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE InspeccionId = '" + INSPECCION + "' AND IdEnvio IS NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes WHERE Id = '" + VIAJE + "'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k1", "novedad_viaje")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k2", "inventado")));
        foreach (var (tipo, i) in new[] { "novedad_conductor", "novedad_proceso", "permiso_sin_inspector", "cambio_destino_unidad", "carro_rechazado_en_destino", "ubicacion_fijada", "espera_inspector_punto" }.Select((t, i) => (t, i)))
        {
            Assert.Equal(0, await ErrorDeAsync(Notificacion($"anterior-{i}", tipo)));
        }
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblNovedadesViaje'"));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(10, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposNovedadViaje"));
        Assert.Equal(7, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('dbo.tblNovedadesViaje') AND name LIKE 'CK[_]tblNovedadesViaje[_]%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.tblNovedadesViaje')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k3", "novedad_viaje")));
    }
}
