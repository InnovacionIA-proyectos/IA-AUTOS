using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>Una prueba de medicion: solo corre con la variable de entorno <c>MEDIR_RENDIMIENTO=1</c> (siembra miles de filas y tarda), para no alargar la corrida normal.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactMedicionAttribute : FactAttribute
{
    public FactMedicionAttribute()
    {
        if (Environment.GetEnvironmentVariable("MEDIR_RENDIMIENTO") != "1")
        {
            Skip = "Medicion de rendimiento: defina MEDIR_RENDIMIENTO=1 para correrla (ver docs/rendimiento.md).";
        }
    }
}

/// <summary>
/// Mide los listados, el inventario y el informe con una semilla de 17 000 VIN (el tamano de la operacion), sobre un SQL Server 2019 real en un contenedor. Cada ruta se llama una vez para calentar y despues
/// cinco veces; el resultado (mediana y maximo, en milisegundos) se escribe en la consola y, si se define <c>RENDIMIENTO_SALIDA</c>, en ese archivo en formato de tabla de Markdown. Las cotas son
/// holgadas: la prueba avisa de una regresion grande, no de una variacion normal de la maquina.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_1)]
public sealed class MedicionRendimientoApiTests(clsBaseDatosFixture bd, ITestOutputHelper salida) : clsPruebaApiBase(bd)
{
    private const int VEHICULOS = 17_000;
    private const int COTA_MS = 5_000;

    /// <summary>
    /// 17 000 VIN en 4 250 viajes de cuatro unidades, con las tres etapas de cada una (51 000). Seis de cada diez unidades tienen sus seis informes cerrados (movilizadas), dos de cada diez solo el Cargue
    /// y dos no tienen informes; una de cada veinte trae un hallazgo (850), y 17 000 ordenes Sofasa. Los triggers que mantienen <c>Completa</c> se apagan mientras se siembra y se recalcula al final.
    /// </summary>
    private const string SEMILLA = """
        SET NOCOUNT ON;
        DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes WHERE Codigo = 'sofasa');
        DECLARE @cargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
        DECLARE @transporte uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte');
        DECLARE @descargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue');
        DECLARE @zona uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente');
        DECLARE @insp uniqueidentifier = NEWID(), @cond uniqueidentifier = NEWID();
        INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, ClienteId, PasswordHash) VALUES (@insp, N'Ines Inspectora', 'medicion-insp@ejemplo.com', 'inspector', @cli, 'h');
        INSERT dbo.tblUsuarios (Id, Nombre, Rol, ClienteId, Cedula, TipoCredencial, PinHash) VALUES (@cond, N'Carlos Conductor', 'conductor', @cli, '1032456789', 'pin', 'h');

        ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER TR_tblInspecciones_Completa;
        ALTER TABLE dbo.tblEtapas DISABLE TRIGGER TR_tblEtapas_Completa;
        ALTER TABLE dbo.tblViajeVehiculos DISABLE TRIGGER TR_tblViajeVehiculos_Completa;

        CREATE TABLE #v (i int PRIMARY KEY, Id uniqueidentifier NOT NULL, ViajeId uniqueidentifier NULL);
        ;WITH n AS (SELECT TOP (17000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
        INSERT #v (i, Id) SELECT i, NEWID() FROM n;
        CREATE TABLE #j (g int PRIMARY KEY, Id uniqueidentifier NOT NULL);
        INSERT #j SELECT g, NEWID() FROM (SELECT DISTINCT (i - 1) / 4 + 1 AS g FROM #v) x;
        UPDATE v SET ViajeId = j.Id FROM #v v JOIN #j j ON j.g = (v.i - 1) / 4 + 1;

        INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId)
        SELECT Id, CONCAT('9FBHSR4E8PM', RIGHT(CONCAT('000000', i), 6)), CHOOSE(i % 4 + 1, N'Renault', N'Toyota', N'Hyundai', N'Nissan'), CHOOSE(i % 3 + 1, 'nacional', 'importacion', 'exportacion'), @cli FROM #v;
        INSERT dbo.tblViajes (Id, CodigoMtrack, Placa, Trailer, ConductorId, InspectorId, Estado, CreatedAt)
        SELECT Id, CONCAT('MTK-', g), CONCAT(N'PL', RIGHT(CONCAT('0000', g), 4)), CONCAT(N'TR-', g % 50), @cond, @insp, 'en_curso', DATEADD(MINUTE, -g, SYSDATETIMEOFFSET()) FROM #j;
        INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) SELECT NEWID(), ViajeId, Id FROM #v;
        INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden)
        SELECT NEWID(), v.Id, v.ViajeId, e.Id, e.Orden FROM #v v CROSS JOIN (VALUES (@cargue, 1), (@transporte, 2), (@descargue, 3)) e (Id, Orden);

        INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, IniciadaAt)
        SELECT NEWID(), e.Id, e.ViajeId, @insp, m.Momento, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
        FROM dbo.tblEtapas e JOIN #v v ON v.Id = e.VehiculoId CROSS JOIN (VALUES ('salida'), ('llegada')) m (Momento)
        WHERE v.i % 10 < 6 OR (v.i % 10 < 8 AND e.Orden = 1);

        INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad)
        SELECT NEWID(), x.InspeccionId, @zona, 'golpe', '5_10cm', 'v1'
        FROM (SELECT MIN(i.Id) AS InspeccionId FROM dbo.tblInspecciones i JOIN dbo.tblEtapas e ON e.Id = i.EtapaId JOIN #v v ON v.Id = e.VehiculoId WHERE v.i % 20 = 0 AND i.Momento = 'salida' GROUP BY v.i) x;

        UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Estado = 'borrador';

        INSERT dbo.tblOrdenesSofasa (Id, SourceId, StatusCode, DatetimeEvent, OrdenNumber, Vin, Make, DestinationCity, VehiculoId)
        SELECT NEWID(), v.i, CHOOSE(v.i % 5 + 1, N'ENTREGADA', N'EN_RUTA', N'PROGRAMADA', N'CARGADA', N'CANCELADA'), DATEADD(MINUTE, -v.i, SYSDATETIMEOFFSET()), CONCAT(N'OC-', v.i),
               CONCAT('9FBHSR4E8PM', RIGHT(CONCAT('000000', v.i), 6)), N'Renault', CHOOSE(v.i % 4 + 1, N'Bogota', N'Medellin', N'Cali', N'Barranquilla'), v.Id
        FROM #v v;

        ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER TR_tblInspecciones_Completa;
        ALTER TABLE dbo.tblEtapas ENABLE TRIGGER TR_tblEtapas_Completa;
        ALTER TABLE dbo.tblViajeVehiculos ENABLE TRIGGER TR_tblViajeVehiculos_Completa;
        EXEC dbo.usp_RecalcularCompleta;
        DROP TABLE #v;
        DROP TABLE #j;
        """;

    private async Task SembrarAsync()
    {
        await using var conexion = new SqlConnection(BaseDatos.CadenaConexion);
        await conexion.OpenAsync();
        await using (var preparar = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", conexion))
        {
            await preparar.ExecuteNonQueryAsync();
        }

        await using var comando = new SqlCommand(SEMILLA, conexion) { CommandTimeout = 600 };
        await comando.ExecuteNonQueryAsync();
    }

    private async Task<(long Mediana, long Maximo, long Filas)> MedirAsync(string token, string ruta)
    {
        long filas = 0;
        var tiempos = new List<long>();
        for (var i = 0; i < 6; i++)
        {
            var reloj = Stopwatch.StartNew();
            var r = await Cliente(token).GetAsync(ruta);
            var cuerpo = await r.Content.ReadAsStringAsync();
            reloj.Stop();
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{ruta} dio {(int)r.StatusCode}: {cuerpo[..Math.Min(300, cuerpo.Length)]}");
            filas = cuerpo.Length;
            if (i > 0)
            {
                tiempos.Add(reloj.ElapsedMilliseconds);
            }
        }

        tiempos.Sort();
        return (tiempos[tiempos.Count / 2], tiempos[^1], filas);
    }

    [FactMedicion]
    public async Task Los_listados_el_inventario_y_el_informe_responden_con_17000_vin_dentro_de_la_cota()
    {
        var reloj = Stopwatch.StartNew();
        await SembrarAsync();
        salida.WriteLine($"Semilla: {VEHICULOS} VIN en {reloj.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(VEHICULOS, await new clsSembrador(BaseDatos.CadenaConexion).ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos"));
        var movilizadas = await new clsSembrador(BaseDatos.CadenaConexion).ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE Completa = 1");
        salida.WriteLine($"Unidades completas (movilizadas): {movilizadas}");

        await Fabrica.SembrarUsuarioAsync("medicion@ejemplo.com", "Clave-Segura-1", clsRol.SUPER_ADMIN);
        var token = await LoginAsync("medicion@ejemplo.com", "Clave-Segura-1");
        var vin = "9FBHSR4E8PM000120";
        var informe = await new clsSembrador(BaseDatos.CadenaConexion).EscalarAsync(
            "SELECT TOP 1 i.Id FROM dbo.tblInspecciones i JOIN dbo.tblEtapas e ON e.Id = i.EtapaId JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = '9FBHSR4E8PM000120' AND i.Momento = 'salida' ORDER BY e.Orden");

        var rutas = new (string Nombre, string Ruta)[]
        {
            ("Vehiculos: movilizados, pagina 1 de 50", "/api/vehiculos/listado"),
            ("Vehiculos: movilizados, pagina 1 de 200", "/api/vehiculos/listado?tamano=200"),
            ("Vehiculos: movilizados, ultima pagina de 200", "/api/vehiculos/listado?tamano=200&pagina=51"),
            ("Vehiculos: pendientes", "/api/vehiculos/listado?tipo=pendientes"),
            ("Vehiculos: con novedad", "/api/vehiculos/listado?tipo=novedad"),
            ("Vehiculos: por marca y operacion, orden por marca", "/api/vehiculos/listado?tipo=pendientes&marca=Renault&operacion=nacional&orden=marca:desc"),
            ("Vehiculos: por chasis", "/api/vehiculos/listado?tipo=movilizados&vin=PM000120"),
            ("Vehiculos: busqueda por fragmento", "/api/vehiculos?vin=PM0001"),
            ("Inspecciones: pagina 1 de 50", "/api/inspecciones"),
            ("Inspecciones: por VIN", $"/api/inspecciones?vin={vin}"),
            ("Inspecciones: cerradas de una operacion", "/api/inspecciones?estado=cerrada&operacion=nacional&tamano=200"),
            ("Viajes: pagina 1 de 50", "/api/viajes"),
            ("Viajes: pagina 1 de 200 por codigo", "/api/viajes?tamano=200&orden=codigo_mtrack:asc"),
            ("Sofasa: pagina 1 de 50", "/api/sofasa/ordenes"),
            ("Sofasa: por estado y ciudad", "/api/sofasa/ordenes?estado=ENTREGADA&ciudad=Cali"),
            ("Usuarios: pagina 1 de 50", "/api/usuarios"),
            ("Inventario de un VIN", $"/api/vehiculos/{vin}/inventario"),
            ("Informe de una inspeccion", $"/api/inspecciones/{informe}"),
        };

        var tabla = new StringBuilder();
        tabla.AppendLine("| Consulta | Mediana (ms) | Maximo (ms) | Respuesta (bytes) |");
        tabla.AppendLine("|---|---:|---:|---:|");
        var excedidas = new List<string>();
        foreach (var (nombre, ruta) in rutas)
        {
            var (mediana, maximo, bytes) = await MedirAsync(token, ruta);
            tabla.AppendLine(CultureInfo.InvariantCulture, $"| {nombre} | {mediana} | {maximo} | {bytes} |");
            salida.WriteLine($"{nombre}: mediana {mediana} ms, maximo {maximo} ms");
            if (mediana > COTA_MS)
            {
                excedidas.Add($"{nombre} ({mediana} ms)");
            }
        }

        if (Environment.GetEnvironmentVariable("RENDIMIENTO_SALIDA") is { Length: > 0 } archivo)
        {
            await File.WriteAllTextAsync(archivo, tabla.ToString());
        }

        Assert.True(excedidas.Count == 0, $"Superan {COTA_MS} ms: {string.Join("; ", excedidas)}");
    }
}
