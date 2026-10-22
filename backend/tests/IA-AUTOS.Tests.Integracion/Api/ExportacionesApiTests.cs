using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Exportaciones a Excel (GET /api/exportaciones/*.xlsx): quien las descarga (coordinador, comercial y administradores, con los datos de su cliente), el nombre del archivo, las columnas de cada
/// uno (datos de la unidad, el viaje y la etapa; las firmas en las inspecciones; la zona y la foto en los hallazgos) y los filtros de los listados.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed partial class ExportacionesApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    private static readonly string[] RUTAS = ["/api/exportaciones/viajes.xlsx", "/api/exportaciones/inspecciones.xlsx", "/api/exportaciones/hallazgos.xlsx"];

    private static readonly XNamespace NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Un archivo leido: los titulos y las filas como texto (las fechas y los numeros, tal como los guarda el archivo).</summary>
    private sealed record Hoja(IReadOnlyList<string> Titulos, IReadOnlyList<IReadOnlyDictionary<string, string>> Filas)
    {
        public IReadOnlyList<IReadOnlyDictionary<string, string>> De(string titulo, string valor) => Filas.Where(f => f.GetValueOrDefault(titulo) == valor).ToList();
    }

    private async Task<(HttpResponseMessage Respuesta, Hoja Hoja)> DescargarAsync(Sesion quien, string ruta, string consulta = "")
    {
        var r = await Cliente(quien.Token).GetAsync(ruta + consulta);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{ruta}{consulta} dio {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (r, Leer(await r.Content.ReadAsByteArrayAsync()));
    }

    /// <summary>Lee la hoja de un .xlsx: valida que sea un paquete con sus partes y que cada fila tenga celdas bien referenciadas.</summary>
    private static Hoja Leer(byte[] contenido)
    {
        using var zip = new ZipArchive(new MemoryStream(contenido), ZipArchiveMode.Read);
        foreach (var parte in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml" })
        {
            Assert.NotNull(zip.GetEntry(parte));
        }

        using var flujo = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var filas = XDocument.Load(flujo).Descendants(NS + "row").Select(fila =>
            fila.Elements(NS + "c").ToDictionary(c => ColumnaDe((string)c.Attribute("r")!), c => (string?)c.Element(NS + "is")?.Element(NS + "t") ?? (string?)c.Element(NS + "v") ?? string.Empty)).ToList();
        var titulos = filas[0].OrderBy(c => c.Key).Select(c => c.Value).ToList();
        var datos = filas.Skip(1).Select(f => (IReadOnlyDictionary<string, string>)titulos
            .Select((titulo, i) => (titulo, valor: f.GetValueOrDefault(i)))
            .Where(x => x.valor is not null)
            .ToDictionary(x => x.titulo, x => x.valor!)).ToList();
        return new Hoja(titulos, datos);
    }

    private static int ColumnaDe(string referencia) =>
        LetrasDeColumna().Match(referencia).Value.Aggregate(0, (acumulado, c) => (acumulado * 26) + (c - 'A' + 1)) - 1;

    [GeneratedRegex("^[A-Z]+")]
    private static partial Regex LetrasDeColumna();

    private async Task SembrarFirmasYFotosAsync(Guid inspector, Guid conductor, DateTimeOffset fecha)
    {
        // La transferencia firmada de la salida del Cargue de V1 (con la fecha y la IP de cada firma) y la foto del hallazgo de V3 con su ubicacion.
        await Sembrador.EjecutarAsync(
            """
            ALTER TABLE dbo.tblTransferenciasCustodia DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblEvidencias DISABLE TRIGGER ALL;
            INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaEntregaIp, FirmaRecibeAt, FirmaRecibeIp, FirmadaAt, TipoCierre)
            VALUES (NEWID(), '11111111-0000-0000-0000-000000000001', @i, @c, 'A', 'B', @f, '203.0.113.7', DATEADD(MINUTE, 5, @f), '198.51.100.9', DATEADD(MINUTE, 5, @f), 'firmada');
            INSERT dbo.tblEvidencias (Id, InspeccionId, HallazgoId, StoragePath, SelloFecha, SelloLat, SelloLng)
            SELECT NEWID(), InspeccionId, Id, 'foto-hallazgo.png', @f, 4.60971, -74.08175 FROM dbo.tblHallazgos WHERE ClaseDano = 'rotura';
            ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
            """,
            ("@i", inspector), ("@c", conductor), ("@f", fecha));
    }

    // ------------------------------------------------------------------------------------ quien las descarga

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.SUPER_ADMIN)]
    public async Task El_coordinador_el_comercial_y_los_administradores_descargan_las_tres_exportaciones_con_su_nombre_y_fecha(string rol)
    {
        var sesion = await SesionAsync(rol);
        var hoy = DiaDeColombia(DateTimeOffset.UtcNow);

        foreach (var ruta in RUTAS)
        {
            var (r, hoja) = await DescargarAsync(sesion, ruta);

            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", r.Content.Headers.ContentType!.MediaType);
            Assert.Equal("attachment", r.Content.Headers.ContentDisposition!.DispositionType);
            Assert.Equal($"{Path.GetFileName(ruta)[..^5]}-{hoy}.xlsx", r.Content.Headers.ContentDisposition!.FileName);
            Assert.NotEmpty(hoja.Titulos);
            Assert.Empty(hoja.Filas);
        }
    }

    [Theory]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    public async Task El_inspector_y_el_conductor_reciben_403_en_las_tres_exportaciones(string rol)
    {
        var sesion = await SesionAsync(rol);

        foreach (var ruta in RUTAS)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(sesion.Token).GetAsync(ruta)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync(RUTAS[0])).StatusCode);
    }

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD, true)]
    [InlineData(clsRol.COMERCIAL, true)]
    [InlineData(clsRol.ADMIN, true)]
    [InlineData(clsRol.SUPER_ADMIN, true)]
    [InlineData(clsRol.INSPECTOR, false)]
    [InlineData(clsRol.CONDUCTOR, false)]
    public async Task La_capacidad_exportar_soportes_solo_la_tienen_quienes_descargan(string rol, bool esperado)
    {
        var sesion = await SesionAsync(rol);

        var me = await ComoJsonAsync(await Cliente(sesion.Token).GetAsync("/api/auth/me"));

        Assert.Equal(esperado, me.GetProperty("capacidades").EnumerateArray().Select(c => c.GetString()).Contains("exportar_soportes"));
    }

    // ------------------------------------------------------------------------------------ viajes.xlsx

    [Fact]
    public async Task Los_viajes_traen_una_fila_por_unidad_etapa_y_momento_con_los_datos_del_viaje_y_el_estado_de_cada_registro()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var (_, hoja) = await DescargarAsync(coordinador, RUTAS[0]);

        Assert.Equal(
            [
                "VIN", "Marca", "Tipo de vehículo", "Operación", "Cliente", "Código MTrack", "Placa", "Tráiler", "Unidad", "Conductor", "Cédula del conductor", "Etapa", "Lugar",
                "Estado del viaje", "Estado de la unidad", "Origen", "Destino", "Momento", "Versión", "Estado", "Inicio", "Cierre",
            ],
            hoja.Titulos);
        // V1: tres etapas (6 filas); V2: dos (4); V3: dos (4); V5: una (2). V6 no tiene etapas y V4 es de otro cliente.
        Assert.Equal(16, hoja.Filas.Count);
        Assert.Empty(hoja.De("VIN", "VIN-IND-0004"));
        var v1 = hoja.De("VIN", "VIN-IND-0001");
        Assert.Equal(6, v1.Count);
        Assert.All(v1, f =>
        {
            Assert.Equal("Renault", f["Marca"]);
            Assert.Equal("nacional", f["Operación"]);
            Assert.Equal("Sofasa", f["Cliente"]);
            Assert.Equal("MTK-IND-A", f["Código MTrack"]);
            Assert.Equal("ABC123", f["Placa"]);
            Assert.Equal("TR-1", f["Tráiler"]);
            Assert.Equal("Carlos Conductor", f["Conductor"]);
            Assert.Equal("1032456789", f["Cédula del conductor"]);
            Assert.Equal("en_curso", f["Estado del viaje"]);
            Assert.Equal("activa", f["Estado de la unidad"]);
            Assert.Equal("cerrada", f["Estado"]);
            Assert.Equal("1", f["Versión"]);
            Assert.True(f.ContainsKey("Inicio") && f.ContainsKey("Cierre"));
        });
        Assert.Equal(["Cargue", "Cargue", "Transporte", "Transporte", "Descargue", "Descargue"], v1.Select(f => f["Etapa"]).ToList());
        Assert.Equal(["salida", "llegada", "salida", "llegada", "salida", "llegada"], v1.Select(f => f["Momento"]).ToList());
        // V2 solo tiene cerrada la salida del Cargue: los demas registros salen como sin inspeccion y sin fechas.
        var v2 = hoja.De("VIN", "VIN-IND-0002");
        Assert.Equal(["cerrada", "sin_inspeccion", "sin_inspeccion", "sin_inspeccion"], v2.Select(f => f["Estado"]).ToList());
        Assert.All(v2.Skip(1), f => Assert.False(f.ContainsKey("Versión") || f.ContainsKey("Inicio") || f.ContainsKey("Cierre")));
        Assert.Equal(e.Dias.Dia, DiaDeSerie(v1[0]["Cierre"]));
    }

    /// <summary>El dia (hora de Colombia) de un numero de serie de fecha de Excel.</summary>
    private static string DiaDeSerie(string serie) =>
        new DateTime(1899, 12, 30).AddDays(double.Parse(serie, CultureInfo.InvariantCulture)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Los_viajes_aceptan_los_filtros_del_listado()
    {
        await EscenarioAsync();
        // El filtro de VIN sigue la regla unica: los VIN del escenario pasan a ser VIN ISO validos.
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET Vin = REPLACE(Vin, 'VIN-IND-', '9FBHSR4E8PM00')");

        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        Assert.Equal(10, (await DescargarAsync(coordinador, RUTAS[0], "?codigo_mtrack=MTK-IND-A")).Hoja.Filas.Count);
        Assert.Equal(4, (await DescargarAsync(coordinador, RUTAS[0], "?vin=PM000003")).Hoja.Filas.Count);
        Assert.Empty((await DescargarAsync(coordinador, RUTAS[0], "?estado=programado")).Hoja.Filas);
        Assert.Equal(2, (await DescargarAsync(coordinador, RUTAS[0], "?estado=finalizado")).Hoja.Filas.Count);
        var conductor = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblUsuarios WHERE Cedula = '1032456789'"))!;
        Assert.Equal(10, (await DescargarAsync(coordinador, RUTAS[0], $"?conductor_id={conductor}")).Hoja.Filas.Count);
        var reciente = DateTimeOffset.UtcNow.AddDays(-10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Assert.Equal(14, (await DescargarAsync(coordinador, RUTAS[0], $"?desde={reciente}")).Hoja.Filas.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente(coordinador.Token).GetAsync(RUTAS[0] + "?estado=volando")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente(coordinador.Token).GetAsync(RUTAS[0] + "?desde=ayer")).StatusCode);
    }

    // ------------------------------------------------------------------------------------ inspecciones.xlsx

    [Fact]
    public async Task Las_inspecciones_traen_una_fila_por_version_con_el_inspector_y_las_firmas_de_la_transferencia()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var firma = DateTimeOffset.UtcNow.AddDays(-3);
        await SembrarFirmasYFotosAsync(e.I1, e.Conductor, firma);

        var (_, hoja) = await DescargarAsync(coordinador, RUTAS[1]);

        Assert.Equal(
            [
                "VIN", "Marca", "Tipo de vehículo", "Operación", "Cliente", "Código MTrack", "Placa", "Tráiler", "Unidad", "Conductor", "Cédula del conductor", "Etapa", "Lugar",
                "Momento", "Versión", "Estado", "Inicio", "Cierre", "Inspector", "Tipo de cierre",
                "Entrega: quién", "Entrega: fecha", "Entrega: IP", "Recibe: quién", "Recibe: fecha", "Recibe: IP",
            ],
            hoja.Titulos);
        // V1: 6, V2: 1, V3: 2 y V5: 1 (V4 es de otro cliente).
        Assert.Equal(10, hoja.Filas.Count);
        var conFirmas = Assert.Single(hoja.Filas, f => f.ContainsKey("Tipo de cierre"));
        Assert.Equal("VIN-IND-0001", conFirmas["VIN"]);
        Assert.Equal("firmada", conFirmas["Tipo de cierre"]);
        Assert.Equal("Ines Uno", conFirmas["Entrega: quién"]);
        Assert.Equal("Carlos Conductor", conFirmas["Recibe: quién"]);
        Assert.Equal("203.0.113.7", conFirmas["Entrega: IP"]);
        Assert.Equal("198.51.100.9", conFirmas["Recibe: IP"]);
        Assert.Equal(DiaDeColombia(firma), DiaDeSerie(conFirmas["Entrega: fecha"]));
        Assert.Equal("Ines Uno", conFirmas["Inspector"]);
        Assert.Equal("Cargue", conFirmas["Etapa"]);
        Assert.Equal("salida", conFirmas["Momento"]);
        Assert.Equal("cerrada", conFirmas["Estado"]);
        Assert.Equal(9, hoja.Filas.Count(f => !f.ContainsKey("Tipo de cierre")));
    }

    [Fact]
    public async Task Las_inspecciones_aceptan_los_filtros_del_listado_y_el_cliente_solo_lo_elige_el_super_administrador()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);
        var delOtro = await SesionAsync(clsRol.COORDINADOR_CALIDAD, "coord-otro@ejemplo.com", e.Otro);

        Assert.Equal(4, (await DescargarAsync(coordinador, RUTAS[1], "?momento=llegada")).Hoja.Filas.Count);
        var viajeA = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = 'MTK-IND-A'"))!;
        Assert.Equal(7, (await DescargarAsync(coordinador, RUTAS[1], $"?viaje_id={viajeA}")).Hoja.Filas.Count);
        Assert.Equal(2, (await DescargarAsync(coordinador, RUTAS[1], "?proceso=descargue")).Hoja.Filas.Count);
        Assert.Equal(5, (await DescargarAsync(coordinador, RUTAS[1], $"?usuario_id={e.I1}&estado=cerrada&momento=salida")).Hoja.Filas.Count);
        Assert.Equal(11, (await DescargarAsync(superAdmin, RUTAS[1])).Hoja.Filas.Count);
        Assert.Single((await DescargarAsync(superAdmin, RUTAS[1], $"?cliente_id={e.Otro}")).Hoja.Filas);
        Assert.Equal("Otro cliente", (await DescargarAsync(delOtro, RUTAS[1])).Hoja.Filas.Single()["Cliente"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(coordinador.Token).GetAsync($"{RUTAS[1]}?cliente_id={e.Otro}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente(coordinador.Token).GetAsync(RUTAS[1] + "?momento=ayer")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente(coordinador.Token).GetAsync(RUTAS[1] + "?cliente_id=x")).StatusCode);
    }

    // ------------------------------------------------------------------------------------ hallazgos.xlsx

    [Fact]
    public async Task Los_hallazgos_traen_la_zona_la_clase_el_tamano_el_nivel_el_seguimiento_y_la_foto_con_su_ubicacion()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var foto = DateTimeOffset.UtcNow.AddDays(-3);
        await SembrarFirmasYFotosAsync(e.I1, e.Conductor, foto);

        var (_, hoja) = await DescargarAsync(coordinador, RUTAS[2]);

        Assert.Equal(
            [
                "VIN", "Marca", "Tipo de vehículo", "Operación", "Cliente", "Código MTrack", "Placa", "Tráiler", "Unidad", "Conductor", "Cédula del conductor", "Etapa", "Lugar",
                "Momento", "Versión", "Estado de la inspección", "Inicio", "Cierre",
                "Lado", "Número", "Zona", "Clase de daño", "Tamaño", "Nivel", "Preexistente", "Origen", "Estado de seguimiento", "Foto: fecha y hora", "Foto: latitud", "Foto: longitud",
            ],
            hoja.Titulos);
        Assert.Equal(2, hoja.Filas.Count);
        var v3 = hoja.De("VIN", "VIN-IND-0003").Single();
        Assert.Equal("izquierdo", v3["Lado"]);
        Assert.Equal("7", v3["Número"]);
        Assert.Equal("Puerta delantera", v3["Zona"]);
        Assert.Equal("rotura", v3["Clase de daño"]);
        Assert.Equal("1_5cm", v3["Tamaño"]);
        Assert.Equal("v3", v3["Nivel"]);
        Assert.Equal("No", v3["Preexistente"]);
        Assert.Equal("inspector", v3["Origen"]);
        Assert.Equal("pendiente", v3["Estado de seguimiento"]);
        Assert.Equal("Toyota", v3["Marca"]);
        Assert.Equal("importacion", v3["Operación"]);
        Assert.Equal("TR-2", v3["Tráiler"]);
        Assert.Equal("llegada", v3["Momento"]);
        Assert.Equal("4.60971", v3["Foto: latitud"]);
        Assert.Equal("-74.08175", v3["Foto: longitud"]);
        Assert.Equal(DiaDeColombia(foto), DiaDeSerie(v3["Foto: fecha y hora"]));
        // La zona provisional no tiene lado ni numero: va solo su nombre.
        var v2 = hoja.De("VIN", "VIN-IND-0002").Single();
        Assert.False(v2.ContainsKey("Lado") || v2.ContainsKey("Número"));
        Assert.False(string.IsNullOrWhiteSpace(v2["Zona"]));
        Assert.Equal("v1", v2["Nivel"]);
        Assert.False(v2.ContainsKey("Foto: fecha y hora"));
    }

    [Fact]
    public async Task Los_hallazgos_aceptan_los_filtros_del_listado_y_respetan_el_cliente()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);
        var delOtro = await SesionAsync(clsRol.COORDINADOR_CALIDAD, "coord-otro@ejemplo.com", e.Otro);

        Assert.Single((await DescargarAsync(coordinador, RUTAS[2], "?nivel=v1")).Hoja.Filas);
        Assert.Single((await DescargarAsync(coordinador, RUTAS[2], "?lado=izquierdo")).Hoja.Filas);
        Assert.Single((await DescargarAsync(coordinador, RUTAS[2], "?cuadrante_numero=7")).Hoja.Filas);
        Assert.Single((await DescargarAsync(coordinador, RUTAS[2], "?vin=VIN-IND-0002")).Hoja.Filas);
        Assert.Equal(2, (await DescargarAsync(coordinador, RUTAS[2], "?proceso=cargue")).Hoja.Filas.Count);
        Assert.Empty((await DescargarAsync(coordinador, RUTAS[2], "?proceso=descargue")).Hoja.Filas);
        Assert.Equal(2, (await DescargarAsync(coordinador, RUTAS[2], "?estado_seguimiento=pendiente&origen=inspector")).Hoja.Filas.Count);
        Assert.Equal(3, (await DescargarAsync(superAdmin, RUTAS[2])).Hoja.Filas.Count);
        Assert.Single((await DescargarAsync(superAdmin, RUTAS[2], $"?cliente_id={e.Otro}")).Hoja.Filas);
        Assert.Equal("VIN-IND-0004", (await DescargarAsync(delOtro, RUTAS[2])).Hoja.Filas.Single()["VIN"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(coordinador.Token).GetAsync($"{RUTAS[2]}?cliente_id={e.Otro}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente(coordinador.Token).GetAsync(RUTAS[2] + "?nivel=v9")).StatusCode);
    }

    [Fact]
    public async Task Los_textos_con_caracteres_especiales_se_escriben_sin_romper_el_archivo()
    {
        await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET Vin = REPLACE(Vin, 'VIN-IND-', '9FBHSR4E8PM00')");

        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Placa = N'A&B<C>\"D''' WHERE CodigoMtrack = 'MTK-IND-B'");
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var (_, hoja) = await DescargarAsync(coordinador, RUTAS[0], "?vin=PM000003");

        Assert.All(hoja.Filas, f => Assert.Equal("A&B<C>\"D'", f["Placa"]));
    }
}
