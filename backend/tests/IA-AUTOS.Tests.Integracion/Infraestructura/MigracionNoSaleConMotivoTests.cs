using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/43-no-sale-con-motivo-y-bajar-del-viaje.sql) contra un SQL Server 2019 con el esquema PREVIO (Esquemas/ddl-previo-a-no-sale-con-motivo.sql, el que ya esta
/// desplegado) y con una unidad que ya no salia: nace el catalogo de seis motivos, el «no sale» anterior queda con el motivo dano_v1_plus y su texto de siempre, un «no sale» ya no exige hallazgo ni texto, nace el estado
/// «bajado» (con su cuando, quien y motivo, y sin posicion), las vistas dejan de contar a la unidad bajada y el script y el ddl nuevo se pueden repetir.
/// </summary>
public sealed partial class MigracionNoSaleConMotivoTests : IAsyncLifetime
{
    private const string UNIDAD = "VINMOTIVO0000001";

    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionNoSaleMotivo");
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-no-sale-con-motivo.sql"));
        await EjecutarAsync(DATOS_PREVIOS);
    }

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(_cadena);

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>Una unidad con una sola etapa y un informe cerrado con un hallazgo V1+, marcada «no sale» con el texto «Golpe en el frente»: lo que ya estaba guardado antes de migrar.</summary>
    private const string DATOS_PREVIOS = $$"""
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @u uniqueidentifier = NEWID(), @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID(), @e uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@u, N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, '{{UNIDAD}}', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES (@vj, 'MTK-43', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('11111111-1111-1111-1111-111111111111', @e, @vj, @u, 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad)
        VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', (SELECT TOP 1 Id FROM dbo.tblZonasDano WHERE Numero = 50), 'golpe', '5_10cm', 'v1_plus');
        UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = '11111111-1111-1111-1111-111111111111';
        UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'no_sale', NoSaleMotivo = N'Golpe en el frente', NoSaleHallazgoId = '22222222-2222-2222-2222-222222222222', NoSaleMarcadoPor = @u, NoSaleAt = SYSDATETIMEOFFSET();
        """;

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
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", "43-no-sale-con-motivo-y-bajar-del-viaje.sql"))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    private const string ESTADO_DE_LA_UNIDAD = "SELECT CONCAT(EstadoUnidad, '|', NoSaleMotivoCodigo, '|', NoSaleMotivo) FROM dbo.tblViajeVehiculos";

    [Fact]
    public async Task Antes_de_migrar_no_hay_catalogo_de_motivos_ni_estado_bajado()
    {
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblMotivosNoSale'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajeVehiculos') AND name IN ('NoSaleMotivoCodigo', 'BajadoAt')"));
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'bajado'"));
    }

    [Fact]
    public async Task Migrar_crea_el_catalogo_y_las_columnas_y_el_no_sale_anterior_queda_con_el_motivo_dano_v1_plus_y_su_texto()
    {
        await EjecutarAsync(await ScriptMigracionAsync());

        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosNoSale"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosNoSale WHERE Codigo = 'dano_v1_plus' AND ExigeHallazgo = 1 AND ExigeTexto = 0 AND SoloSinInspector = 0"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosNoSale WHERE Codigo = 'dano_reportado_conductor' AND ExigeHallazgo = 1 AND SoloSinInspector = 1"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosNoSale WHERE Codigo = 'otro' AND ExigeTexto = 1 AND ExigeHallazgo = 0"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblViajeVehiculos') AND is_nullable = 1 AND name IN ('NoSaleMotivoCodigo', 'BajadoAt', 'BajadoPor', 'BajadoMotivo')"));
        // El «no sale» de antes se lee con el motivo daño V1+ y su texto de siempre; el hallazgo y quien lo marco no cambian.
        Assert.Equal("no_sale|dano_v1_plus|Golpe en el frente", await TextoAsync(ESTADO_DE_LA_UNIDAD));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE NoSaleHallazgoId = '22222222-2222-2222-2222-222222222222' AND NoSaleMarcadoPor IS NOT NULL"));
    }

    [Fact]
    public async Task Un_no_sale_ya_no_exige_hallazgo_ni_texto_pero_si_su_motivo_y_el_bajado_exige_cuando_quien_y_por_que()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        var usuario = await TextoAsync("SELECT TOP 1 Id FROM dbo.tblUsuarios");
        const string ACTIVA = "UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'activa', NoSaleMotivoCodigo = NULL, NoSaleMotivo = NULL, NoSaleHallazgoId = NULL, NoSaleMarcadoPor = NULL, NoSaleAt = NULL, BajadoAt = NULL, BajadoPor = NULL, BajadoMotivo = NULL, Posicion = NULL";
        await EjecutarAsync(ACTIVA);

        // Sin hallazgo ni texto, con motivo: se admite; sin motivo, no.
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'no_sale', NoSaleMarcadoPor = '{usuario}', NoSaleAt = SYSDATETIMEOFFSET()"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'no_sale', NoSaleMotivoCodigo = 'inventado', NoSaleMarcadoPor = '{usuario}', NoSaleAt = SYSDATETIMEOFFSET()"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'no_sale', NoSaleMotivoCodigo = 'no_llego_al_patio', NoSaleMarcadoPor = '{usuario}', NoSaleAt = SYSDATETIMEOFFSET()"));
        await EjecutarAsync(ACTIVA);

        // Bajado: con cuando, quien y por que, y sin posicion.
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'bajado'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'bajado', BajadoAt = SYSDATETIMEOFFSET(), BajadoPor = '{usuario}'"));
        Assert.Equal(547, await ErrorDeAsync($"UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'bajado', BajadoAt = SYSDATETIMEOFFSET(), BajadoPor = '{usuario}', BajadoMotivo = N'Cambio de pedido', Posicion = 3"));
        Assert.Equal(0, await ErrorDeAsync($"UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'bajado', BajadoAt = SYSDATETIMEOFFSET(), BajadoPor = '{usuario}', BajadoMotivo = N'Cambio de pedido'"));
        // Una unidad activa no lleva nada de eso.
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'activa'"));
    }

    [Fact]
    public async Task La_unidad_bajada_deja_de_contar_en_la_vista_de_completos_y_en_la_de_cobertura()
    {
        await EjecutarAsync(await ScriptMigracionAsync());
        var usuario = await TextoAsync("SELECT TOP 1 Id FROM dbo.tblUsuarios");
        await EjecutarAsync("UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'activa', NoSaleMotivoCodigo = NULL, NoSaleMotivo = NULL, NoSaleHallazgoId = NULL, NoSaleMarcadoPor = NULL, NoSaleAt = NULL");
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos"));

        await EjecutarAsync($"UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'bajado', BajadoAt = SYSDATETIMEOFFSET(), BajadoPor = '{usuario}', BajadoMotivo = N'Cambio de pedido'");

        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE Completa = 1"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.vwCobertura"));
    }

    [Fact]
    public async Task El_script_se_puede_repetir_y_el_ddl_nuevo_se_puede_ejecutar_antes_y_despues_de_migrar_y_deja_lo_mismo()
    {
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosNoSale"));
        Assert.Equal("no_sale|dano_v1_plus|Golpe en el frente", await TextoAsync(ESTADO_DE_LA_UNIDAD));

        // Un motivo que el negocio ya ajusto no se pisa al repetir.
        await EjecutarAsync("UPDATE dbo.tblMotivosNoSale SET Nombre = N'Daño V1+ (ajustado)' WHERE Codigo = 'dano_v1_plus'");
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarAsync(await ScriptMigracionAsync());
        await EjecutarScriptConGoAsync(Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosNoSale"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblMotivosNoSale WHERE Nombre LIKE N'%ajustado%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblViajeVehiculos_NoSale' AND definition LIKE '%bajado%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_tblViajeVehiculos_EstadoUnidad' AND definition LIKE '%bajado%'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN ('FK_tblViajeVehiculos_NoSaleMotivo')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
    }
}
