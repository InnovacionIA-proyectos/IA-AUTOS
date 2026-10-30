using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Ejecuta el script real de migracion (database/cambios-esquema/10-zona-dano-y-hallazgo.sql) contra un SQL Server 2019 con el esquema PREVIO
/// (Esquemas/ddl-previo-a-zona-dano.sql, el que ya esta desplegado), con los 36 cuadrantes y 97 poligonos reales de la ficha
/// (04-cargar-cuadrantes-ficha.sql) y con hallazgos historicos: las zonas salen de los cuadrantes por lado, los poligonos pasan a su panel con el
/// lado que le corresponde, los hallazgos historicos se ubican solo cuando el texto lo permite con seguridad y no se pierde ningun dato.
/// </summary>
public sealed partial class MigracionZonaDanoTests : IAsyncLifetime
{
    private string _cadena = string.Empty;

    public async Task InitializeAsync()
    {
        _cadena = await clsServidorSql.CrearBaseAsync("IAAutosMigracionZona");
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "Infraestructura", "Esquemas", "ddl-previo-a-zona-dano.sql"));
        await EjecutarAsync(_cadena, await LeerScriptAsync("04-cargar-cuadrantes-ficha.sql"));
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

    /// <summary>El error SQL que produce una sentencia (nulo si no falla).</summary>
    private async Task<int?> ErrorDeAsync(string sql)
    {
        try
        {
            await EjecutarAsync(_cadena, sql);
            return null;
        }
        catch (SqlException ex)
        {
            return ex.Number;
        }
    }

    /// <summary>Un script de database/cambios-esquema apuntando al esquema dbo.</summary>
    private static async Task<string> LeerScriptAsync(string nombre) =>
        (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "cambios-esquema", nombre))).Replace("N'db_IA_AUTOS'", "N'dbo'");

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SeparadorGo();

    /// <summary>
    /// Un viaje con un vehiculo y una inspeccion de salida en borrador con seis hallazgos historicos: dos que el texto permite ubicar con seguridad
    /// (parachoque delantero y llanta trasera derecha) y cuatro que no (techo, capot, parabrisas y stop trasero).
    /// </summary>
    private const string DATOS_PREVIOS = """
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes);
        DECLARE @u uniqueidentifier = NEWID(), @v uniqueidentifier = NEWID(), @vj uniqueidentifier = NEWID(), @e uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@u, N'Ines', 'insp@e.com', 'inspector', @cli, 'h');
        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'VINPREVIO26000001', N'Renault', 'nacional', @cli);
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa) VALUES (@vj, 'MTK-27', N'ABC123');
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e, @v, @vj, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1);
        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES ('11111111-1111-1111-1111-111111111111', @e, @vj, @u, 'salida', SYSDATETIMEOFFSET());
        INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano, CoordsDiagrama) VALUES
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'Parachoque delantero', 'golpe', '1_5cm', N'{"x":0.5,"y":0.84}'),
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'Llantas/rines trasero derecho', 'golpe', '1_5cm', N'{"x":0.75,"y":0.43}'),
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'Techo', 'golpe', '1_5cm', N'{"x":0.5,"y":0.53}'),
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'Capot', 'rotura', '5_10cm', N'{"x":0.5,"y":0.66}'),
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'Para brisas delantero', 'golpe', '1_5cm', N'{"x":0.5,"y":0.61}'),
         (NEWID(), '11111111-1111-1111-1111-111111111111', N'Stop trasero izquierdo', 'golpe', '1_5cm', N'{"x":0.42,"y":0.26}');
        """;

    [Fact]
    public async Task La_migracion_crea_las_zonas_por_lado_y_pasa_los_97_poligonos_a_su_panel()
    {
        Assert.Equal(97, await ContarAsync("SELECT COUNT(*) FROM dbo.tblCuadranteVistas"));

        await EjecutarAsync(_cadena, await LeerScriptAsync("10-zona-dano-y-hallazgo.sql"));

        // 36 cuadrantes: 23 con dos lados (del 1 al 25 sin el 4 ni el 12) y 13 sin lado (del 26 al 38), mas las cinco zonas provisionales.
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPanelesDano"));
        Assert.Equal(59, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId IS NOT NULL"));
        Assert.Equal(46, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE Lado IS NOT NULL"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId IS NULL AND Numero IS NULL AND Lado IS NULL AND Codigo IS NOT NULL"));

        // Los 97 poligonos son regiones del panel que corresponde; el lado sale del panel y del 26 en adelante no hay lado.
        Assert.Equal(97, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano"));
        Assert.Equal(13, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano r JOIN dbo.tblPanelesDano p ON p.Id = r.PanelId WHERE p.Codigo = 'superior'"));
        Assert.Equal(19, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano r JOIN dbo.tblPanelesDano p ON p.Id = r.PanelId WHERE p.Codigo = 'costado_izquierdo'"));
        Assert.Equal(19, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano r JOIN dbo.tblPanelesDano p ON p.Id = r.PanelId WHERE p.Codigo = 'costado_derecho'"));
        Assert.Equal(23, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano r JOIN dbo.tblPanelesDano p ON p.Id = r.PanelId WHERE p.Codigo = 'perfil_posterior_izquierdo'"));
        Assert.Equal(23, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano r JOIN dbo.tblPanelesDano p ON p.Id = r.PanelId WHERE p.Codigo = 'perfil_posterior_derecho'"));
        Assert.Equal(0, await ContarAsync(@"SELECT COUNT(*) FROM dbo.tblRegionesDano r JOIN dbo.tblPanelesDano p ON p.Id = r.PanelId JOIN dbo.tblZonasDano z ON z.Id = r.ZonaId
            WHERE (p.Codigo IN ('costado_izquierdo', 'perfil_posterior_izquierdo') AND z.Numero <= 25 AND z.Lado <> 'izquierdo')
               OR (p.Codigo IN ('costado_derecho', 'perfil_posterior_derecho') AND z.Numero <= 25 AND z.Lado <> 'derecho')
               OR (z.Numero > 25 AND z.Lado IS NOT NULL)"));

        // El respaldo no se toca y la geometria de cada region es la del poligono de origen.
        Assert.Equal(97, await ContarAsync("SELECT COUNT(*) FROM dbo.tblCuadranteVistas"));
        Assert.Equal(97, await ContarAsync(@"SELECT COUNT(*) FROM dbo.tblRegionesDano r JOIN dbo.tblZonasDano z ON z.Id = r.ZonaId
            JOIN dbo.tblCuadranteVistas cv ON cv.CuadranteId = z.CuadranteId AND cv.Geometria = r.Geometria"));

        // Repetir el script no cambia nada (ya esta aplicado).
        await EjecutarAsync(_cadena, await LeerScriptAsync("10-zona-dano-y-hallazgo.sql"));
        Assert.Equal(97, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano"));
        Assert.Equal(59, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId IS NOT NULL"));
    }

    [Fact]
    public async Task La_migracion_ubica_los_hallazgos_historicos_solo_cuando_es_seguro_y_conserva_todos_los_datos()
    {
        await EjecutarAsync(_cadena, await LeerScriptAsync("10-zona-dano-y-hallazgo.sql"));

        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos h JOIN dbo.tblZonasDano z ON z.Id = h.ZonaId WHERE h.ParteVehiculo = N'Parachoque delantero' AND z.Codigo = 'frente' AND h.MigracionPendiente = 0"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos h JOIN dbo.tblZonasDano z ON z.Id = h.ZonaId WHERE h.ParteVehiculo = N'Llantas/rines trasero derecho' AND z.Codigo = 'llanta_trasera_derecha' AND h.MigracionPendiente = 0"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE ZonaId IS NULL AND MigracionPendiente = 1"));

        // No se pierde ningun dato: la parte y las coordenadas se conservan y los hallazgos nacen como inspector, pendientes y sin evidencia calculada.
        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE ParteVehiculo IS NOT NULL AND CoordsDiagrama IS NOT NULL AND ClaseDano IS NOT NULL"));
        Assert.Equal(6, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Origen = 'inspector' AND EstadoSeguimiento = 'pendiente' AND EsPreexistente = 0 AND EvidenciaCompleta = 0 AND NivelSeveridad IS NULL"));

        // Las unidades de los viajes quedan activas.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE EstadoUnidad = 'activa' AND NoSaleMotivo IS NULL"));
    }

    [Fact]
    public async Task Despues_de_migrar_un_cuadrante_nuevo_trae_sus_zonas_y_las_reglas_de_la_zona_se_cumplen()
    {
        await EjecutarAsync(_cadena, await LeerScriptAsync("10-zona-dano-y-hallazgo.sql"));

        // Cuadrante 4 (no existe en la ficha): dos zonas, una por lado; el 40 no lleva lado.
        await EjecutarAsync(_cadena, "INSERT dbo.tblCuadrantes (Id, TipoVehiculo, Numero, Nombre) VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'zz', 4, N'Nuevo'), ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'zz', 40, N'Nuevo 40')");
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' AND Lado IN ('izquierdo', 'derecho')"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' AND Lado IS NULL"));

        // La forma de la zona la garantiza la base: un lado donde no va, o su falta, no se guarda; tampoco una zona repetida.
        Assert.Equal(547, await ErrorDeAsync("INSERT dbo.tblZonasDano (Id, CuadranteId, Numero, Lado) VALUES (NEWID(), 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 40, 'izquierdo')"));
        Assert.Equal(547, await ErrorDeAsync("INSERT dbo.tblZonasDano (Id, CuadranteId, Numero, Lado) VALUES (NEWID(), 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 4, NULL)"));
        Assert.Equal(2601, await ErrorDeAsync("INSERT dbo.tblZonasDano (Id, CuadranteId, Numero, Lado) VALUES (NEWID(), 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 4, 'derecho')"));

        // Un hallazgo preexistente exige su origen y una unidad que no sale exige su motivo; la base lo comprueba.
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblHallazgos SET EsPreexistente = 1 WHERE ParteVehiculo = N'Techo'"));
        Assert.Equal(547, await ErrorDeAsync("UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'no_sale'"));

        // Borrar un cuadrante sin hallazgos borra sus zonas.
        await EjecutarAsync(_cadena, "DELETE FROM dbo.tblCuadrantes WHERE Id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'");
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_encima_de_una_base_ya_migrada_sin_cambiar_nada()
    {
        await EjecutarAsync(_cadena, await LeerScriptAsync("10-zona-dano-y-hallazgo.sql"));

        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));

        Assert.Equal(97, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano"));
        Assert.Equal(59, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId IS NOT NULL"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPanelesDano"));
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE MigracionPendiente = 1"));
    }

    [Fact]
    public async Task El_ddl_nuevo_se_puede_ejecutar_sobre_la_base_anterior_y_la_migracion_se_completa_despues()
    {
        // Antes de migrar: el ddl no toca los hallazgos ni las unidades de los viajes ni crea el dibujo (en una base anterior lo crean los guiones 10 y 34).
        await EjecutarScriptConGoAsync(_cadena, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tblHallazgos') AND name = 'ZonaId'"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'tblPanelesDano'"));

        await EjecutarAsync(_cadena, await LeerScriptAsync("10-zona-dano-y-hallazgo.sql"));

        Assert.Equal(97, await ContarAsync("SELECT COUNT(*) FROM dbo.tblRegionesDano"));
        Assert.Equal(59, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId IS NOT NULL"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM dbo.tblPanelesDano"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId IS NULL"));
    }
}
