using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/15-notificaciones-y-dispositivos.sql) contra un SQL Server 2019 con el esquema PREVIO
/// (Esquemas/ddl-previo-a-notificaciones.sql, el que ya esta desplegado) y con datos: se crean las notificaciones (append-only salvo lectura y respuesta) y los dispositivos,
/// y la novedad del conductor queda sin zona, clase, nivel ni texto hasta que se revisa y solo en la salida de Transporte. No cambia ningun dato.
/// </summary>
public sealed partial class MigracionNotificacionesTests : IAsyncLifetime
{
    private const string INSPECCION_CARGUE = "11111111-1111-1111-1111-111111111111";
    private const string INSPECCION_TRANSPORTE = "22222222-2222-2222-2222-222222222222";
    private const string USUARIO = "33333333-3333-3333-3333-333333333333";

    private readonly MsSqlContainer _contenedor = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2019-latest").Build();
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();
        var maestra = _contenedor.GetConnectionString();
        await EjecutarAsync(maestra, "CREATE DATABASE IAAutosMigracionNotificaciones");
        _cadena = new SqlConnectionStringBuilder(maestra) { InitialCatalog = "IAAutosMigracionNotificaciones" }.ConnectionString;
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-notificaciones.sql"));
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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "15-notificaciones-y-dispositivos.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Un usuario y una unidad con su salida de Cargue y su salida de Transporte (inspecciones en borrador), con un hallazgo del inspector.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID(), @ec uniqueidentifier = NEWID(), @et uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINNOTIF28000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES (@vj, 'MTK-31', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@ec, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@et, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte'), 2);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_CARGUE}}', @ec, @vj, '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('{{INSPECCION_TRANSPORTE}}', @et, @vj, '{{USUARIO}}', 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad)
        VALUES (NEWID(), '{{INSPECCION_CARGUE}}', (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'), 'golpe', '5_10cm', 'v1_plus');
        """;

    private static string Notificacion(string tipo = "novedad_conductor", string canal = "bandeja", string? usuario = USUARIO, string clave = "k1", string extra = "") =>
        $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), '{tipo}', 'alta', {(usuario is null ? "NULL" : $"'{usuario}'")}, N'T', N'M', '{canal}', '{clave}'){extra}";

    private static string Novedad(string inspeccion, string columnasExtra = "", string valoresExtra = "") =>
        $"INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad{columnasExtra}) VALUES (NEWID(), '{inspeccion}', '5_10cm', 'conductor', 'golpe'{valoresExtra})";

    // ------------------------------------------------------------------------------------------------ migracion

    [Fact]
    public async Task La_migracion_crea_las_tablas_las_restricciones_y_los_triggers_sin_cambiar_ningun_dato()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblNotificaciones', 'tblDispositivos')"));

        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblNotificaciones', 'tblDispositivos')"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name LIKE 'CK_tblHallazgos_Novedad%' OR name = 'CK_tblHallazgos_NivelDelConductor'"));
        Assert.Equal(3, await ContarAsync(
            "SELECT COUNT(*) FROM sys.triggers WHERE name IN ('TR_tblNotificaciones_SoloLecturaYRespuesta', 'TR_tblNotificaciones_NoBorrar', 'TR_tblHallazgos_NovedadDeTransporte')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones"));

        // Repetir el script no cambia nada (ya esta aplicado).
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblNotificaciones', 'tblDispositivos')"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name IN ('tblNotificaciones', 'tblDispositivos')"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name LIKE 'CK_tblHallazgos_Novedad%' OR name = 'CK_tblHallazgos_NivelDelConductor'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE name = 'TR_tblHallazgos_NovedadDeTransporte'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    // ------------------------------------------------------------------------------------------ notificaciones

    [Fact]
    public async Task Una_notificacion_se_lee_y_se_responde_pero_ninguna_otra_columna_cambia_ni_se_borra()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, Notificacion());

        Assert.Equal(0, await ErrorDeAsync("UPDATE dbo.tblNotificaciones SET LeidaAt = SYSDATETIMEOFFSET()"));
        Assert.Equal(0, await ErrorDeAsync(
            $"UPDATE dbo.tblNotificaciones SET RespuestaTexto = N'Voy en camino', RespuestaMedio = 'llamada', RespondidaPor = '{USUARIO}', RespondidaAt = SYSDATETIMEOFFSET()"));
        Assert.Equal(51006, await ErrorDeAsync("UPDATE dbo.tblNotificaciones SET Titulo = N'Otro'"));
        Assert.Equal(51006, await ErrorDeAsync("UPDATE dbo.tblNotificaciones SET Urgencia = 'media'"));
        Assert.Equal(51006, await ErrorDeAsync("UPDATE dbo.tblNotificaciones SET EnviadaAt = SYSDATETIMEOFFSET()"));
        Assert.Equal(51006, await ErrorDeAsync("UPDATE dbo.tblNotificaciones SET ClaveDedupe = 'otra'"));
        Assert.Equal(51003, await ErrorDeAsync("DELETE FROM dbo.tblNotificaciones"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Titulo = N'T' AND LeidaAt IS NOT NULL AND RespuestaMedio = 'llamada'"));
    }

    [Fact]
    public async Task Las_reglas_de_cada_notificacion_las_hace_cumplir_la_base()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(547, await ErrorDeAsync(Notificacion(tipo: "inventado", clave: "a1")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion(canal: "sms", clave: "a2")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion(canal: "correo", clave: "a3")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion(tipo: "hallazgo_critico", usuario: null, clave: "a4")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion().Replace("'alta'", "'baja'").Replace("'k1'", "'a5'")));
        // Solo el reporte diario no tiene destinatario y se envia por correo.
        Assert.Equal(0, await ErrorDeAsync(Notificacion(tipo: "reporte_diario", canal: "correo", usuario: null, clave: "reporte-1")));
        // La clave de dedupe no se repite.
        Assert.Equal(0, await ErrorDeAsync(Notificacion(clave: "dup")));
        Assert.Equal(2627, await ErrorDeAsync(Notificacion(clave: "dup")));
        // La respuesta es completa y solo es de una novedad del conductor.
        Assert.Equal(547, await ErrorDeAsync(Notificacion(clave: "r1").Replace("ClaveDedupe)", "ClaveDedupe, RespuestaTexto)").Replace("'r1')", "'r1', N'Solo texto')")));
        Assert.Equal(547, await ErrorDeAsync(
            $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe, RespuestaTexto, RespuestaMedio, RespondidaPor, RespondidaAt) "
            + $"VALUES (NEWID(), 'hallazgo_critico', 'alta', '{USUARIO}', N'T', N'M', 'bandeja', 'r2', N'x', 'app', '{USUARIO}', SYSDATETIMEOFFSET())"));
        Assert.Equal(547, await ErrorDeAsync(
            $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe, RespuestaTexto, RespuestaMedio, RespondidaPor, RespondidaAt) "
            + $"VALUES (NEWID(), 'novedad_conductor', 'alta', '{USUARIO}', N'T', N'M', 'bandeja', 'r3', N'x', 'sms', '{USUARIO}', SYSDATETIMEOFFSET())"));
        // Una notificacion sin usuario inexistente: la relacion existe.
        Assert.Equal(547, await ErrorDeAsync(Notificacion(usuario: "99999999-9999-9999-9999-999999999999", clave: "u1")));
    }

    [Fact]
    public async Task Un_dispositivo_tiene_un_token_unico_y_una_plataforma_valida()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        string Dispositivo(string token, string plataforma = "android") =>
            $"INSERT dbo.tblDispositivos (Id, UsuarioId, TokenFcm, Plataforma, Modelo) VALUES (NEWID(), '{USUARIO}', '{token}', '{plataforma}', N'Pixel 8')";

        Assert.Equal(0, await ErrorDeAsync(Dispositivo("token-1")));
        Assert.Equal(2627, await ErrorDeAsync(Dispositivo("token-1")));
        Assert.Equal(547, await ErrorDeAsync(Dispositivo("token-2", "symbian")));
        Assert.Equal(547, await ErrorDeAsync(Dispositivo("")));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblDispositivos WHERE Activo = 1 AND UltimoUsoAt IS NOT NULL"));
    }

    // ----------------------------------------------------------------------------------- novedad del conductor

    [Fact]
    public async Task La_novedad_del_conductor_en_la_salida_de_transporte_no_tiene_zona_clase_nivel_ni_texto_hasta_revisarla()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(Novedad(INSPECCION_TRANSPORTE)));
        Assert.Equal(547, await ErrorDeAsync(Novedad(INSPECCION_TRANSPORTE, ", ClaseDano", ", 'golpe'")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(INSPECCION_TRANSPORTE, ", ZonaId", ", (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente')")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(INSPECCION_TRANSPORTE, ", NivelSeveridad", ", 'v2'")));
        Assert.Equal(547, await ErrorDeAsync(Novedad(INSPECCION_TRANSPORTE, ", Nota", ", N'texto libre'")));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Origen = 'conductor'"));
    }

    [Fact]
    public async Task Al_revisarla_la_novedad_se_completa_con_su_zona_clase_y_nivel()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());
        await EjecutarAsync(_cadena, Novedad(INSPECCION_TRANSPORTE));
        var completar = $"UPDATE dbo.tblHallazgos SET RevisadoAt = SYSDATETIMEOFFSET(), RevisadoPor = '{USUARIO}' WHERE Origen = 'conductor'";

        // Revisada sin completar: falla; completa: se guarda.
        Assert.Equal(547, await ErrorDeAsync(completar));
        Assert.Equal(0, await ErrorDeAsync(
            completar.Replace("WHERE", ", ClaseDano = 'golpe', NivelSeveridad = 'v2', ZonaId = (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente') WHERE")));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Origen = 'conductor' AND RevisadoAt IS NOT NULL AND NivelSeveridad = 'v2'"));
    }

    [Fact]
    public async Task Una_novedad_solo_puede_ser_de_la_salida_de_transporte_y_el_conductor_nunca_trae_nivel()
    {
        await EjecutarAsync(_cadena, await ScriptMigracionAsync());

        Assert.Equal(51007, await ErrorDeAsync(Novedad(INSPECCION_CARGUE)));
        // Un hallazgo del conductor (por ejemplo en un Cargue sin personal de apoyo) puede no ser una novedad, pero no trae nivel hasta revisarse.
        var delConductor = $"INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, Origen{{0}}) VALUES (NEWID(), '{INSPECCION_CARGUE}', "
            + "(SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'), 'golpe', '5_10cm', 'conductor'{1})";
        Assert.Equal(0, await ErrorDeAsync(string.Format(delConductor, "", "")));
        Assert.Equal(547, await ErrorDeAsync(string.Format(delConductor, ", NivelSeveridad", ", 'v1'")));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Origen = 'inspector'"));
    }
}
