using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Lo que comparten las pruebas de los reportes y de las exportaciones: una sesion por rol y un escenario con dos clientes, viajes, unidades, inspecciones cerradas y hallazgos (con los dias en hora de Colombia).
/// </summary>
public abstract class clsPruebaTableroBase(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    protected clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    protected sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Los dias del escenario: el del despacho (hace tres dias, hora de Colombia) y el de un despacho viejo (hace cuarenta).</summary>
    protected sealed record Dias(DateTimeOffset Despacho, string Dia, string Hoy);

    protected async Task<Sesion> SesionAsync(string rol, string? email = null, Guid? clienteId = null)
    {
        email ??= $"{rol}@ejemplo.com";
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + rol, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    protected static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    protected async Task<JsonElement> ConsultarAsync(Sesion quien, string ruta, string consulta = "")
    {
        var r = await Cliente(quien.Token).GetAsync(ruta + consulta);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{ruta}{consulta} dio {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await ComoJsonAsync(r);
    }

    protected static string DiaDeColombia(DateTimeOffset instante) => instante.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Cliente Sofasa: V1 (Renault, nacional, viaje A con trailer TR-1, placa ABC123, conductor con cedula, origen y destino) con sus seis registros cerrados; V2 (igual viaje) con la salida del Cargue y un
    /// hallazgo V1; V3 (Toyota, importacion, viaje B con TR-2) con la salida y la llegada del Cargue y un hallazgo V3 en la zona izquierdo 7; V5, despachada hace cuarenta dias; V6, programada sin inspecciones.
    /// Cliente Otro: V4 (Hyundai, viaje con TR-9) con la salida del Cargue y un hallazgo V1. Todo cierra el mismo dia de Colombia (hace tres dias).
    /// </summary>
    protected async Task<(Dias Dias, Guid Sofasa, Guid Otro, Guid Conductor, Guid I1, Guid I2, Guid Origen, Guid Destino)> EscenarioAsync()
    {
        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;
        var otro = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otro));
        var i1 = await Fabrica.SembrarUsuarioAsync("inspector1@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR, nombre: "Ines Uno");
        var i2 = await Fabrica.SembrarUsuarioAsync("inspector2@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR, nombre: "Ivan Dos");
        var conductor = await Fabrica.SembrarUsuarioConPinAsync("1032456789", "4821", nombre: "Carlos Conductor");
        await Sembrador.CuadranteAsync(7, "Puerta delantera");
        var zona7 = await Sembrador.ZonaAsync(7, "izquierdo");
        var origen = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1'"))!;
        var destino = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'concesionario'"))!;

        var despacho = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-3).AddHours(15), TimeSpan.Zero);
        var dias = new Dias(despacho, DiaDeColombia(despacho), DiaDeColombia(DateTimeOffset.UtcNow));

        await Sembrador.EjecutarAsync(
            """
            ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
            DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
            DECLARE @transporte uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte');
            DECLARE @descargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue');
            DECLARE @frente uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente');
            DECLARE @ninera uniqueidentifier = (SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera');
            DECLARE @nodriza uniqueidentifier = (SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'nodriza');

            DECLARE @v1 uniqueidentifier = NEWID(), @v2 uniqueidentifier = NEWID(), @v3 uniqueidentifier = NEWID(), @v4 uniqueidentifier = NEWID(), @v5 uniqueidentifier = NEWID(), @v6 uniqueidentifier = NEWID();
            INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES
                (@v1, 'VIN-IND-0001', N'Renault', 'nacional', @sof), (@v2, 'VIN-IND-0002', N'Renault', 'nacional', @sof), (@v3, 'VIN-IND-0003', N'Toyota', 'importacion', @sof),
                (@v4, 'VIN-IND-0004', N'Hyundai', 'nacional', @otro), (@v5, 'VIN-IND-0005', N'Renault', 'nacional', @sof), (@v6, 'VIN-IND-0006', N'Renault', 'nacional', @sof);

            DECLARE @a uniqueidentifier = NEWID(), @b uniqueidentifier = NEWID(), @o uniqueidentifier = NEWID(), @e uniqueidentifier = NEWID(), @f uniqueidentifier = NEWID();
            INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Trailer, TipoUnidadId, ConductorId, OrigenId, DestinoId, Estado, CreatedAt) VALUES
                (@a, 'MTK-IND-A', N'ABC123', N'TR-1', @ninera, @cond, @origen, @destino, 'en_curso', DATEADD(MINUTE, -60, @t0)),
                (@b, 'MTK-IND-B', N'DEF456', N'TR-2', @nodriza, NULL, NULL, NULL, 'en_curso', DATEADD(MINUTE, -60, @t0)),
                (@o, 'MTK-IND-O', N'GHI789', N'TR-9', @ninera, NULL, NULL, NULL, 'en_curso', DATEADD(MINUTE, -60, @t0)),
                (@e, 'MTK-IND-E', N'MNO345', N'TR-5', @ninera, NULL, NULL, NULL, 'finalizado', DATEADD(DAY, -40, @t0)),
                (@f, 'MTK-IND-F', N'JKL012', N'TR-1', @ninera, NULL, NULL, NULL, 'programado', DATEADD(MINUTE, -60, @t0));
            INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES
                (NEWID(), @a, @v1), (NEWID(), @a, @v2), (NEWID(), @b, @v3), (NEWID(), @o, @v4), (NEWID(), @e, @v5), (NEWID(), @f, @v6);

            DECLARE @e1c uniqueidentifier = NEWID(), @e1t uniqueidentifier = NEWID(), @e1d uniqueidentifier = NEWID(), @e2 uniqueidentifier = NEWID(), @e3 uniqueidentifier = NEWID(), @e4 uniqueidentifier = NEWID(), @e5 uniqueidentifier = NEWID();
            INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES
                (@e1c, @v1, @a, @cargue, 1), (@e1t, @v1, @a, @transporte, 2), (@e1d, @v1, @a, @descargue, 3), (@e2, @v2, @a, @cargue, 1),
                (@e3, @v3, @b, @cargue, 1), (@e4, @v4, @o, @cargue, 1), (@e5, @v5, @e, @cargue, 1),
                (NEWID(), @v2, @a, @transporte, 2), (NEWID(), @v3, @b, @transporte, 2);

            -- Id, etapa, viaje, inspector, momento, minutos despues del despacho en que cierra, minutos que duro.
            CREATE TABLE #i (Id uniqueidentifier, EtapaId uniqueidentifier, ViajeId uniqueidentifier, UsuarioId uniqueidentifier, Momento varchar(20), Cierre datetimeoffset(3), Minutos int);
            INSERT #i VALUES
                ('11111111-0000-0000-0000-000000000001', @e1c, @a, @i1, 'salida',  @t0, 20),
                ('11111111-0000-0000-0000-000000000002', @e1c, @a, @i1, 'llegada', DATEADD(MINUTE, 60, @t0), 10),
                ('11111111-0000-0000-0000-000000000003', @e1t, @a, @i1, 'salida',  DATEADD(MINUTE, 120, @t0), 7),
                ('11111111-0000-0000-0000-000000000004', @e1t, @a, @i1, 'llegada', DATEADD(MINUTE, 180, @t0), 7),
                ('11111111-0000-0000-0000-000000000005', @e1d, @a, @i1, 'salida',  DATEADD(MINUTE, 240, @t0), 10),
                ('11111111-0000-0000-0000-000000000006', @e1d, @a, @i1, 'llegada', DATEADD(MINUTE, 300, @t0), 30),
                ('22222222-0000-0000-0000-000000000001', @e2,  @a, @i1, 'salida',  DATEADD(MINUTE, 30, @t0), 40),
                ('33333333-0000-0000-0000-000000000001', @e3,  @b, @i2, 'salida',  DATEADD(MINUTE, 120, @t0), 5),
                ('33333333-0000-0000-0000-000000000002', @e3,  @b, @i2, 'llegada', DATEADD(MINUTE, 180, @t0), 15),
                ('44444444-0000-0000-0000-000000000001', @e4,  @o, @i1, 'salida',  DATEADD(MINUTE, 240, @t0), 10),
                ('55555555-0000-0000-0000-000000000001', @e5,  @e, @i1, 'salida',  DATEADD(DAY, -40, @t0), 10);
            INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, IniciadaAt, Estado, CerradaAt)
            SELECT Id, EtapaId, ViajeId, UsuarioId, Momento, DATEADD(MINUTE, -Minutos, Cierre), DATEADD(MINUTE, -Minutos, Cierre), 'cerrada', Cierre FROM #i;
            DROP TABLE #i;

            INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES
                (NEWID(), '22222222-0000-0000-0000-000000000001', @frente, 'golpe', '5_10cm', 'v1'),
                (NEWID(), '33333333-0000-0000-0000-000000000002', @zona7, 'rotura', '1_5cm', 'v3'),
                (NEWID(), '44444444-0000-0000-0000-000000000001', @frente, 'golpe', '5_10cm', 'v1');
            ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
            -- Con los triggers apagados no se mantuvo Completa: se recalcula con la regla de la vista.
            EXEC dbo.usp_RecalcularCompleta;
            """,
            ("@sof", sofasa), ("@otro", otro), ("@cond", conductor.Id), ("@i1", i1.Id), ("@i2", i2.Id), ("@origen", origen), ("@destino", destino), ("@zona7", zona7), ("@t0", despacho));
        return (dias, sofasa, otro, conductor.Id, i1.Id, i2.Id, origen, destino);
    }
}
