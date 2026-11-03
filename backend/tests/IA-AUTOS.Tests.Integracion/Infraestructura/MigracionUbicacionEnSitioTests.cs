using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/32-ubicacion-con-rango-y-confirmacion-en-sitio.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-ubicacion-en-sitio.sql, el
/// que ya esta desplegado) con un lugar que ya tiene coordenadas, otro que no y una inspeccion con su foto: siembra los parametros sin pisar los ajustados, deja confirmada la ubicacion de los lugares que ya la tenian,
/// la base impone los estados de la ubicacion, el resultado de la confirmacion, los tipos de notificacion y las propuestas de autocalibracion, y el script se puede repetir.
/// </summary>
public sealed partial class MigracionUbicacionEnSitioTests : IAsyncLifetime
{
    private const string LUGAR_CON_COORDENADAS = "55555555-5555-5555-5555-555555555551";
    private const string LUGAR_SIN_COORDENADAS = "55555555-5555-5555-5555-555555555552";
    private const string INSPECCION = "55555555-5555-5555-5555-555555555561";
    private const string FOTO = "55555555-5555-5555-5555-555555555571";
    private const string USUARIO = "55555555-5555-5555-5555-555555555581";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionUbicacion");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-ubicacion-en-sitio.sql"));
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "32-ubicacion-con-rango-y-confirmacion-en-sitio.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un lugar con coordenadas y otro sin ellas, un usuario y una inspeccion en borrador con una foto: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @tipo uniqueidentifier = (SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera');
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        DECLARE @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID(), @e uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES ('{{USUARIO}}', N'Marcos Inspector', N'marcos@ejemplo.com', 'inspector');
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad, Latitud, Longitud, RadioMetros)
        SELECT '{{LUGAR_CON_COORDENADAS}}', N'CEDI Envigado', Id, N'Envigado', 6.17, -75.58, 800 FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1';
        INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad)
        SELECT '{{LUGAR_SIN_COORDENADAS}}', N'Concesionario Caminos Cali', Id, N'Cali' FROM dbo.tblPuntosCustodia WHERE Codigo = 'concesionario';
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINUBICACION00001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, TipoUnidadId, Placa, InspectorId) VALUES (@vj, 'M-4501', @tipo, N'ABC123', '{{USUARIO}}');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e, @v, @vj, @cargue, 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION}}', @e, @vj, '{{USUARIO}}', 'llegada', SYSDATETIMEOFFSET());
        INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, Precision) VALUES ('{{FOTO}}', '{{INSPECCION}}', N'foto.jpg', SYSDATETIMEOFFSET(), 6.17, -75.58, 25);
        UPDATE dbo.tblParametros SET Valor = N'45' WHERE Clave = 'transferencia_pendiente_minutos';
        """;

    private static string Propuesta(string lugar, string columnas = "", string valores = "", int radio = 300) =>
        $"INSERT dbo.tblPropuestasUbicacion (Id, LugarId, CentroLat, CentroLng, RadioMetros, Fotos, Visitas{columnas}) VALUES (NEWID(), '{lugar}', 6.25, -75.56, {radio}, 12, 5{valores})";

    [Fact]
    public async Task La_migracion_siembra_los_parametros_y_marca_la_ubicacion_de_los_lugares_que_ya_la_tenian_sin_cambiar_lo_demas_y_se_puede_repetir()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblLugares') AND name = 'EstadoUbicacion'"));
        var parametrosAntes = await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros");

        // Dos veces seguidas, sin error.
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Los cuatro parametros, y los demas (incluida la precision minima del GPS y un valor ya ajustado) sin cambios.
        Assert.Equal(parametrosAntes + 4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Tipo = 'numero' AND ((Clave = 'radio_lugar_cargue_m' AND Valor = N'1000') OR (Clave = 'radio_destino_m' AND Valor = N'500') OR (Clave = 'valvula_precision_m' AND Valor = N'500') OR (Clave = 'cargue_sin_reclamar_minutos' AND Valor = N'30'))"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'gps_precision_minima_m' AND Valor = N'30'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'transferencia_pendiente_minutos' AND Valor = N'45'"));
        // El lugar que ya tenia coordenadas queda confirmado y el otro sin ubicacion; lo demas del lugar no cambia.
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblLugares WHERE Id = '{LUGAR_CON_COORDENADAS}' AND EstadoUbicacion = 'confirmada' AND UbicacionFijadaPor IS NULL AND UbicacionFijadaAt IS NULL AND Latitud = 6.17 AND RadioMetros = 800"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblLugares WHERE Id = '{LUGAR_SIN_COORDENADAS}' AND EstadoUbicacion = 'sin_ubicacion' AND Latitud IS NULL"));
        // La inspeccion y su foto siguen igual, sin resultado de confirmacion; la tabla de propuestas nace vacia.
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Id = '{INSPECCION}' AND Estado = 'borrador' AND ConfirmacionSitio IS NULL AND ConfirmacionDistanciaM IS NULL AND ConfirmacionEvidenciaId IS NULL"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblEvidencias WHERE Id = '{FOTO}' AND Precision = 25"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPropuestasUbicacion"));
        // Un lugar con ubicacion cambiada despues de migrar no se vuelve a marcar al repetir el script.
        await EjecutarAsync(_cadena, $"UPDATE dbo.tblLugares SET EstadoUbicacion = 'pendiente_confirmar' WHERE Id = '{LUGAR_CON_COORDENADAS}'");
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblLugares WHERE Id = '{LUGAR_CON_COORDENADAS}' AND EstadoUbicacion = 'pendiente_confirmar'"));
    }

    [Fact]
    public async Task La_migracion_no_pisa_un_parametro_que_el_negocio_ya_ajusto()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, "UPDATE dbo.tblParametros SET Valor = N'1500' WHERE Clave = 'radio_lugar_cargue_m'");

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'radio_lugar_cargue_m' AND Valor = N'1500'"));
    }

    [Fact]
    public async Task La_base_impone_los_estados_de_la_ubicacion_el_resultado_de_la_confirmacion_y_los_tipos_de_notificacion()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        // Estados de la ubicacion del lugar: solo los tres; quien la fijo debe existir.
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblLugares SET EstadoUbicacion = 'inventado' WHERE Id = '{LUGAR_SIN_COORDENADAS}'"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblLugares SET EstadoUbicacion = 'pendiente_confirmar', Latitud = 3.4, Longitud = -76.5, UbicacionFijadaPor = '{USUARIO}', UbicacionFijadaAt = SYSDATETIMEOFFSET() WHERE Id = '{LUGAR_SIN_COORDENADAS}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblLugares SET UbicacionFijadaPor = NEWID() WHERE Id = '{LUGAR_SIN_COORDENADAS}'"));
        // Resultado de la confirmacion: tres valores, la distancia y la foto solo con un resultado, la distancia no negativa y la foto debe existir.
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET ConfirmacionSitio = 'inventado' WHERE Id = '{INSPECCION}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET ConfirmacionDistanciaM = 400 WHERE Id = '{INSPECCION}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET ConfirmacionSitio = 'confirmado_en_sitio', ConfirmacionDistanciaM = -1 WHERE Id = '{INSPECCION}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET ConfirmacionSitio = 'confirmado_en_sitio', ConfirmacionDistanciaM = 400, ConfirmacionEvidenciaId = NEWID() WHERE Id = '{INSPECCION}'"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET ConfirmacionSitio = 'confirmado_en_sitio', ConfirmacionDistanciaM = 400, ConfirmacionEvidenciaId = '{FOTO}' WHERE Id = '{INSPECCION}'"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET ConfirmacionSitio = 'fuera_de_rango', ConfirmacionDistanciaM = 2500 WHERE Id = '{INSPECCION}'"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblInspecciones SET ConfirmacionSitio = 'sin_confirmacion_posible', ConfirmacionDistanciaM = NULL, ConfirmacionEvidenciaId = NULL WHERE Id = '{INSPECCION}'"));
        // Tipos de notificacion: los dos nuevos y los anteriores; uno inventado no.
        string Notificacion(string tipo, string clave) =>
            $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), '{tipo}', 'alta', '{USUARIO}', N'T', N'M', 'bandeja', '{clave}')";
        Assert.Equal(0, await ErrorDeAsync(Notificacion("fuera_de_rango", "k1")));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("cargue_sin_reclamar", "k2")));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("novedad_conductor", "k3")));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("reclamo_sin_actividad", "k4")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("inventado", "k5")));
    }

    [Fact]
    public async Task Las_propuestas_de_autocalibracion_tienen_una_pendiente_por_lugar_y_una_decidida_no_se_modifica_ni_se_borra()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(Propuesta(LUGAR_CON_COORDENADAS)));
        Assert.Equal(2601, await ErrorDeAsync(Propuesta(LUGAR_CON_COORDENADAS, radio: 350)));
        Assert.Equal(547, await ErrorDeAsync(Propuesta(LUGAR_SIN_COORDENADAS, radio: 0)));
        Assert.Equal(547, await ErrorDeAsync(Propuesta(LUGAR_SIN_COORDENADAS, ", Estado", ", 'inventada'")));
        Assert.Equal(547, await ErrorDeAsync(Propuesta(LUGAR_SIN_COORDENADAS, ", Estado", ", 'confirmada'")));
        Assert.Equal(547, await ErrorDeAsync(Propuesta(Guid.NewGuid().ToString())));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblPropuestasUbicacion SET CentroLat = 91 WHERE LugarId = '{LUGAR_CON_COORDENADAS}'"));
        // Decidir: con autor y fecha; despues no se modifica ni se borra, y el lugar admite una pendiente nueva.
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblPropuestasUbicacion SET Estado = 'confirmada', DecididaPor = '{USUARIO}', DecididaAt = SYSDATETIMEOFFSET() WHERE LugarId = '{LUGAR_CON_COORDENADAS}'"));
        Assert.Equal(51010, await ErrorDeAsync($"UPDATE dbo.tblPropuestasUbicacion SET RadioMetros = 999 WHERE LugarId = '{LUGAR_CON_COORDENADAS}'"));
        Assert.Equal(51010, await ErrorDeAsync($"DELETE FROM dbo.tblPropuestasUbicacion WHERE LugarId = '{LUGAR_CON_COORDENADAS}'"));
        Assert.Equal(0, await ErrorDeAsync(Propuesta(LUGAR_CON_COORDENADAS, radio: 400)));
        // Una pendiente se puede borrar; un lugar con propuestas no se borra.
        Assert.Equal(547, await ErrorDeAsync($"DELETE FROM dbo.tblLugares WHERE Id = '{LUGAR_CON_COORDENADAS}'"));
        Assert.Equal(0, await ErrorDeAsync($"DELETE FROM dbo.tblPropuestasUbicacion WHERE LugarId = '{LUGAR_CON_COORDENADAS}' AND Estado = 'pendiente'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblLugares WHERE Id = '{LUGAR_CON_COORDENADAS}' AND EstadoUbicacion = 'confirmada'"));
        Assert.Equal(1, await ContarAsync($"SELECT COUNT(*) FROM dbo.tblLugares WHERE Id = '{LUGAR_SIN_COORDENADAS}' AND EstadoUbicacion = 'sin_ubicacion'"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave IN ('radio_lugar_cargue_m', 'radio_destino_m', 'valvula_precision_m', 'cargue_sin_reclamar_minutos')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblInspecciones_ConfirmacionSitio'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo' AND definition LIKE '%cargue_sin_reclamar%'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares"));
    }
}
