using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/51-cobertura-con-cinco-registros-y-aviso-de-viaje-sin-movimiento.sql) contra un SQL Server 2019 con el esquema PREVIO
/// (Esquemas/ddl-previo-a-cobertura-de-cinco.sql, el que ya esta desplegado) y con datos: la cobertura cuenta cinco registros (el Descargue ya no tiene salida), un carro que ya tenia sus seis registros sigue completo,
/// una salida del Descargue cerrada no completa a un carro al que le falta la llegada, la notificacion admite el tipo viaje_sin_movimiento y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionCoberturaDeCincoTests : IAsyncLifetime
{
    private const string USUARIO = "11111111-1111-1111-1111-111111111111";
    private const string VIAJE = "55555555-5555-5555-5555-555555555555";
    private const string CON_SEIS = "VINCOBERTURA00001";
    private const string SIN_LLEGADA_DEL_DESCARGUE = "VINCOBERTURA00002";
    private const string CON_CINCO = "VINCOBERTURA00003";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionCoberturaDeCinco");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-cobertura-de-cinco.sql"));
        await EjecutarAsync(DatosPrevios());
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Tres unidades despachadas del mismo viaje. La primera tiene los seis registros de antes (la salida y la llegada de las tres etapas); la segunda tiene cinco cerrados pero sin la llegada del Descargue (si la salida del
    /// Descargue contara, estaria completa); la tercera tiene los cinco de ahora (sin la salida del Descargue).
    /// </summary>
    private static string DatosPrevios()
    {
        var sql = new System.Text.StringBuilder($$"""
            DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
            INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES ('{{USUARIO}}', N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
            INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Estado) VALUES ('{{VIAJE}}', 'MTK-50', N'ABC123', 'en_curso');
            INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), 'novedad_proceso', 'alta', '{{USUARIO}}', N'T', N'M', 'bandeja', 'previa-1');

            """);
        AgregarUnidad(sql, CON_SEIS, 1, ("cargue", "llegada"), ("cargue", "salida"), ("transporte", "llegada"), ("transporte", "salida"), ("descargue", "llegada"), ("descargue", "salida"));
        AgregarUnidad(sql, SIN_LLEGADA_DEL_DESCARGUE, 2, ("cargue", "llegada"), ("cargue", "salida"), ("transporte", "llegada"), ("transporte", "salida"), ("descargue", "salida"));
        AgregarUnidad(sql, CON_CINCO, 3, ("cargue", "llegada"), ("cargue", "salida"), ("transporte", "llegada"), ("transporte", "salida"), ("descargue", "llegada"));
        return sql.ToString();
    }

    private static void AgregarUnidad(System.Text.StringBuilder sql, string vin, int numero, params (string Etapa, string Momento)[] cerradas)
    {
        var vehiculo = $"6666666{numero}-6666-6666-6666-666666666666";
        sql.AppendLine($"INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES ('{vehiculo}', '{vin}', N'Renault', 'nacional', @cli);");
        sql.AppendLine($"INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), '{VIAJE}', '{vehiculo}');");
        var orden = 1;
        foreach (var etapa in new[] { "cargue", "transporte", "descargue" })
        {
            var id = $"7777777{numero}-{orden:D4}-7777-7777-777777777777";
            sql.AppendLine($"INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES ('{id}', '{vehiculo}', '{VIAJE}', (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = '{etapa}'), {orden});");
            foreach (var momento in cerradas.Where(c => c.Etapa == etapa).Select(c => c.Momento))
            {
                sql.AppendLine(
                    $"INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, IniciadaAt, Estado, CerradaAt) VALUES (NEWID(), '{id}', '{VIAJE}', '{USUARIO}', '{momento}', SYSDATETIMEOFFSET(), DATEADD(MINUTE, -10, SYSDATETIMEOFFSET()), 'cerrada', SYSDATETIMEOFFSET());");
            }

            orden++;
        }
    }

    private static string Notificacion(string clave, string tipo) =>
        $"INSERT dbo.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, Titulo, Mensaje, Canal, ClaveDedupe) VALUES (NEWID(), '{tipo}', 'alta', '{USUARIO}', N'T', N'M', 'bandeja', '{clave}')";

    private static string Cobertura(string vin) => $"SELECT CAST(RegistrosCerrados AS int) * 10 + CAST(Completa AS int) FROM dbo.vwCobertura WHERE Vin = '{vin}'";

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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "51-cobertura-con-cinco-registros-y-aviso-de-viaje-sin-movimiento.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [Fact]
    public async Task Antes_de_migrar_la_cobertura_cuenta_la_salida_del_descargue_y_no_hay_tipo_de_aviso()
    {
        Assert.Equal(61, await ContarAsync(Cobertura(CON_SEIS)));
        Assert.Equal(50, await ContarAsync(Cobertura(SIN_LLEGADA_DEL_DESCARGUE)));
        Assert.Equal(50, await ContarAsync(Cobertura(CON_CINCO)));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k0", "viaje_sin_movimiento")));
    }

    [Fact]
    public async Task Migrar_cuenta_cinco_registros_conserva_completo_al_carro_de_seis_y_no_completa_con_la_salida_del_descargue()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        // Registros cerrados * 10 + completa: el carro que tenia seis queda con cinco y completo; el que no tenia la llegada del Descargue queda con cuatro e incompleto; el de cinco queda completo.
        Assert.Equal(51, await ContarAsync(Cobertura(CON_SEIS)));
        Assert.Equal(40, await ContarAsync(Cobertura(SIN_LLEGADA_DEL_DESCARGUE)));
        Assert.Equal(51, await ContarAsync(Cobertura(CON_CINCO)));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM dbo.vwCobertura"));
        // Ningun dato cambia: las inspecciones de las tres unidades siguen todas ahi, incluida la salida del Descargue.
        Assert.Equal(16, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones i JOIN dbo.tblEtapas e ON e.Id = i.EtapaId JOIN dbo.tblEtapasCatalogo ec ON ec.Id = e.EtapaCatalogoId WHERE ec.Codigo = 'descargue' AND i.Momento = 'salida'"));
    }

    [Fact]
    public async Task Migrar_hace_que_la_notificacion_admita_viaje_sin_movimiento_sin_perder_los_tipos_anteriores()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(0, await ErrorDeAsync(Notificacion("k1", "viaje_sin_movimiento")));
        Assert.Equal(547, await ErrorDeAsync(Notificacion("k2", "inventado")));
        foreach (var (tipo, i) in new[] { "novedad_conductor", "ubicacion_fijada", "carro_rechazado_en_destino", "espera_inspector_punto", "firma_cargue_pendiente", "cambio_destino_unidad", "propuesta_ubicacion" }.Select((t, i) => (t, i)))
        {
            Assert.Equal(0, await ErrorDeAsync(Notificacion($"anterior-{i}", tipo)));
        }

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe = 'previa-1'"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(51, await ContarAsync(Cobertura(CON_SEIS)));
        Assert.Equal(40, await ContarAsync(Cobertura(SIN_LLEGADA_DEL_DESCARGUE)));

        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(51, await ContarAsync(Cobertura(CON_CINCO)));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblNotificaciones_Tipo' AND definition LIKE '%viaje_sin_movimiento%'"));
        Assert.Equal(0, await ErrorDeAsync(Notificacion("k3", "viaje_sin_movimiento")));
        Assert.Equal(16, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }
}
