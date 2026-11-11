using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// La semilla de desarrollo (database/semilla-desarrollo, S5-HU-31.5, S6-HU-09.18 y S6-HU-01.12): las tres partes se cargan una sobre otra sin romper ninguna regla de la base (restricciones y triggers), cubren lo que pide
/// cada Issue, no se pueden cargar dos veces y la limpieza por prefijos borra todo lo sembrado, deja los triggers activos y NO toca los datos que no son de la semilla. Los scripts usan el esquema de la base compartida
/// (db_IA_AUTOS.); aqui, en una base creada con ddl.sql, se ejecutan con dbo.
/// </summary>
public sealed partial class SemillaDeDesarrolloTests : IAsyncLifetime
{
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosSemilla");
        await clsServidorSql.EjecutarScriptAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        // Las zonas del dibujo las siembra el script 34 (el ddl no las trae): la semilla las necesita.
        await EjecutarTextoAsync((await File.ReadAllTextAsync(Ruta("cambios-esquema", "34-resembrar-dibujo-v6.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'"));
        // Datos que NO son de la semilla (otro prefijo): la limpieza no debe tocarlos.
        await EjecutarTextoAsync(
            """
            DECLARE @c uniqueidentifier = (SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa');
            INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (NEWID(), 'REALVIN0000000001', N'Renault', 'nacional', @c);
            INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, PasswordHash, ClienteId) VALUES (NEWID(), N'Persona Real', 'real@empresa.com', 'admin', 'x', @c);
            INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES (NEWID(), N'Lugar real', (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia), N'Bogota');
            """);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    private static string Ruta(string carpeta, string archivo) => Path.Combine(AppContext.BaseDirectory, "database", carpeta, archivo);

    private async Task EjecutarTextoAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using (var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion))
        {
            await preparar.ExecuteNonQueryAsync();
        }

        foreach (var lote in SeparadorGo().Split(sql).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            await using var comando = new SqlCommand(lote, conexion) { CommandTimeout = 120 };
            await comando.ExecuteNonQueryAsync();
        }
    }

    private async Task EjecutarSemillaAsync(string archivo) =>
        await EjecutarTextoAsync((await File.ReadAllTextAsync(Ruta("semilla-desarrollo", archivo))).Replace("db_IA_AUTOS.", "dbo."));

    private async Task<int> ContarAsync(string sql)
    {
        await using var conexion = new SqlConnection(_cadena);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        return Convert.ToInt32(await comando.ExecuteScalarAsync());
    }

    private async Task SembrarTodoAsync()
    {
        await EjecutarSemillaAsync("01-semilla-base.sql");
        await EjecutarSemillaAsync("02-semilla-ampliacion.sql");
        await EjecutarSemillaAsync("03-semilla-hallazgos.sql");
    }

    [Fact]
    public async Task Las_tres_partes_se_cargan_y_cubren_lo_que_piden_las_tres_issues()
    {
        await SembrarTodoAsync();

        // #123: viaje finalizado con 18 inspecciones cerradas con fotos y las tres formas de cierre de la transferencia.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0001' AND Estado = 'finalizado'"));
        Assert.Equal(18, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Estado = 'cerrada' AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0001')"));
        foreach (var cierre in new[] { "firmada", "con_reservas", "no_firma" })
        {
            Assert.True(await ContarAsync($"SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = '{cierre}'") >= 1, cierre);
        }

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'no_firma' AND TestigoUsuarioId IS NOT NULL AND EvidenciaConstanciaId IS NOT NULL"));
        // #174: dos clientes con sus 5 roles, viajes en los 4 estados, un viaje con VIN de los dos clientes, cargue sin apoyo, inspeccion corregida, transferencia abierta, PIN pendiente y bloqueado, ordenes por MTrack.
        Assert.Equal(2, await ContarAsync("SELECT COUNT(DISTINCT ClienteId) FROM dbo.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test' AND Rol IN ('admin', 'coordinador_calidad', 'comercial', 'inspector', 'conductor')"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(DISTINCT Estado) FROM dbo.tblViajes WHERE CodigoMtrack LIKE 'SEED-%'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(DISTINCT v.ClienteId) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId WHERE vv.ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0002')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE MotivoSinInspector IS NOT NULL AND UsuarioId = (SELECT Id FROM dbo.tblUsuarios WHERE Email = 'seed.sof_conductor@seed.iaautos.test')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Version = 2 AND Estado = 'cerrada' AND CorrigeInspeccionId IS NOT NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre IS NULL AND FirmaEntrega IS NOT NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test' AND DebeCambiarPin = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test' AND BloqueadoHasta > SYSDATETIMEOFFSET()"));
        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.tblOrdenesSofasa WHERE CodigoMtrack LIKE 'SEED-MTK-%' AND ViajeId IS NOT NULL"));
        // #194: los 4 niveles, los 5 estados de seguimiento, preexistente confirmado, no sale con reemplazo y reingreso, novedades, avisos, notas y auditoria.
        Assert.Equal(4, await ContarAsync("SELECT COUNT(DISTINCT NivelSeveridad) FROM dbo.tblHallazgos WHERE NivelSeveridad IS NOT NULL"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(DISTINCT EstadoSeguimiento) FROM dbo.tblHallazgos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE EsPreexistente = 1 AND HallazgoOrigenId IS NOT NULL AND ConfirmadoPor IS NOT NULL"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE EstadoUnidad = 'no_sale' AND ReemplazoVehiculoId IS NOT NULL"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(DISTINCT vv.ViajeId) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId WHERE v.Vin = 'SEEDS000000000010'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Origen = 'conductor' AND RevisadoAt IS NULL"));
        Assert.Equal(13, await ContarAsync("SELECT COUNT(DISTINCT Tipo) FROM dbo.tblNotificaciones WHERE ClaveDedupe LIKE 'seed:%'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(DISTINCT Urgencia) FROM dbo.tblNotificaciones WHERE ClaveDedupe LIKE 'seed:%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE ClaveDedupe LIKE 'seed:%' AND RespondidaAt IS NOT NULL"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(DISTINCT Entidad) FROM dbo.tblNotasAclaratorias"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE is_disabled = 1"));
    }

    [Fact]
    public async Task Cargar_dos_veces_una_parte_se_detiene_y_cada_parte_exige_la_anterior()
    {
        var sinLaBase = await Assert.ThrowsAsync<SqlException>(() => EjecutarSemillaAsync("02-semilla-ampliacion.sql"));
        Assert.Equal(50013, sinLaBase.Number);
        await EjecutarSemillaAsync("01-semilla-base.sql");

        var otraVez = await Assert.ThrowsAsync<SqlException>(() => EjecutarSemillaAsync("01-semilla-base.sql"));

        Assert.Equal(50010, otraVez.Number);
        Assert.Equal(10, await ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Vin LIKE 'SEED%'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE is_disabled = 1"));
    }

    [Fact]
    public async Task La_limpieza_borra_todo_lo_sembrado_deja_los_triggers_activos_y_no_toca_lo_que_no_es_de_la_semilla()
    {
        await SembrarTodoAsync();

        await EjecutarSemillaAsync("00-limpiar-semilla.sql");
        await EjecutarSemillaAsync("00-limpiar-semilla.sql");   // repetirla no cambia nada

        foreach (var tabla in new[]
                 {
                     "tblInspecciones", "tblEvidencias", "tblHallazgos", "tblHallazgoSeguimiento", "tblNotasAclaratorias", "tblTransferenciasCustodia", "tblNotificaciones", "tblAuditLog", "tblEtapas",
                     "tblViajeVehiculos", "tblViajes", "tblOrdenesSofasa", "tblNovedadesProceso",
                 })
        {
            Assert.Equal(0, await ContarAsync($"SELECT COUNT(*) FROM dbo.{tabla}"));
        }

        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Vin = 'REALVIN0000000001'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblUsuarios WHERE Email = 'real@empresa.com'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblUsuarios"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE Nombre = N'Lugar real'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblClientes"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.triggers WHERE is_disabled = 1"));
        // Y se puede volver a sembrar.
        await SembrarTodoAsync();
        Assert.Equal(14, await ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Vin LIKE 'SEED%'"));
    }
}
