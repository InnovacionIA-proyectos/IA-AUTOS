using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// PDF del informe de una inspeccion (GET /api/inspecciones/{id}/pdf) y del inventario del VIN (GET /api/vehiculos/{vin}/inventario/pdf): con las fotos incrustadas desde el almacen, el panel de danos
/// con las zonas marcadas, las firmas, las notas aclaratorias y el historial de seguimiento; quien los ve y el maximo de fotos por documento.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed partial class PdfsApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    // La inspeccion de llegada del Cargue de V3 (Toyota, importacion), con su hallazgo en la zona izquierdo 7.
    private const string INSPECCION_V3 = "33333333-0000-0000-0000-000000000002";

    // La salida del Cargue de V2, con un hallazgo V1 que registro el inspector 1.
    private const string INSPECCION_V2 = "22222222-0000-0000-0000-000000000001";

    private static readonly Regex IMAGENES = ImagenXObject();

    [GeneratedRegex(@"/Subtype /Image")]
    private static partial Regex ImagenXObject();

    /// <summary>PNG RGB de 8 bits sin entrelazado, con datos comprimidos de verdad (lo que el generador de PDF sabe incrustar).</summary>
    private static byte[] Png(int ancho = 4, int alto = 4, byte relleno = 0)
    {
        static byte[] Trozo(string nombre, byte[] cuerpo)
        {
            var longitud = new[] { (byte)(cuerpo.Length >> 24), (byte)(cuerpo.Length >> 16), (byte)(cuerpo.Length >> 8), (byte)cuerpo.Length };
            return [.. longitud, .. Encoding.ASCII.GetBytes(nombre), .. cuerpo, 0, 0, 0, 0];
        }

        var cabecera = new byte[13];
        cabecera[3] = (byte)ancho;
        cabecera[7] = (byte)alto;
        cabecera[8] = 8;
        cabecera[9] = 2;
        var crudo = new byte[alto * (1 + (ancho * 3))];
        Array.Fill(crudo, relleno);
        using var comprimido = new MemoryStream();
        using (var zlib = new ZLibStream(comprimido, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(crudo);
        }

        var partes = new List<byte[]> { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, Trozo("IHDR", cabecera), Trozo("IDAT", comprimido.ToArray()), Trozo("IEND", []) };
        return [.. partes.SelectMany(p => p)];
    }

    private async Task<string> SubirAsync(Sesion quien, byte relleno = 0)
    {
        var r = await Cliente(quien.Token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(Png(relleno: relleno)), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await ComoJsonAsync(r)).GetProperty("storage_path").GetString()!;
    }

    private static string Texto(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static int Imagenes(string pdf) => IMAGENES.Count(pdf);

    /// <summary>El texto del PDF en una sola linea (las lineas que se parten por el ancho se vuelven a unir).</summary>
    private static string Plano(string pdf) =>
        string.Join(' ', TextoDeLinea().Matches(pdf).Select(m => m.Groups[1].Value.Replace(@"\(", "(").Replace(@"\)", ")").Replace(@"\\", @"\")));

    [GeneratedRegex(@"\(((?:\\.|[^\\)])*)\) Tj")]
    private static partial Regex TextoDeLinea();

    private async Task<(HttpResponseMessage Respuesta, byte[] Pdf)> PdfAsync(Sesion quien, string ruta)
    {
        var r = await Cliente(quien.Token).GetAsync(ruta);
        return (r, await r.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// A la inspeccion de V3: una foto de la vista frontal y la foto de su hallazgo (con su sello), una transferencia firmada con trazos, una nota aclaratoria y la region de la zona izquierdo 7 en el panel
    /// costado izquierdo (con la imagen del panel).
    /// </summary>
    private async Task SembrarDetalleAsync(Sesion quien, Guid coordinador, Guid inspector, Guid conductor)
    {
        var foto = await SubirAsync(quien, 10);
        var fotoHallazgo = await SubirAsync(quien, 80);
        var imagenPanel = await SubirAsync(quien, 160);
        await Sembrador.EjecutarAsync(
            """
            ALTER TABLE dbo.tblEvidencias DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblTransferenciasCustodia DISABLE TRIGGER ALL;
            DECLARE @vista uniqueidentifier = (SELECT Id FROM dbo.tblVistasFoto WHERE Codigo = 'frontal');
            DECLARE @hallazgo uniqueidentifier = (SELECT Id FROM dbo.tblHallazgos WHERE InspeccionId = @insp);
            INSERT dbo.tblEvidencias (Id, InspeccionId, VistaId, StoragePath, SelloFecha, SelloLat, SelloLng, HashSha256, Precision)
            VALUES (NEWID(), @insp, @vista, @foto, '2026-10-20T14:05:00Z', 4.60971, -74.08175, 'aaaabbbbccccddddeeeeffff0000111122223333444455556666777788889999', 8.5);
            INSERT dbo.tblEvidencias (Id, InspeccionId, HallazgoId, StoragePath, SelloFecha, SelloLat, SelloLng, HashSha256)
            VALUES (NEWID(), @insp, @hallazgo, @fotoH, '2026-10-20T14:06:00Z', 4.6098, -74.0818, '9999888877776666555544443333222211110000ffffeeeeddddccccbbbbaaaa');
            INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaEntregaIp, FirmaRecibeAt, FirmaRecibeIp, FirmadaAt, TipoCierre)
            VALUES (NEWID(), @insp, @inspector, @conductor, 'M0 0 L40 20 L80 0', 'M0 10 L30 0 L60 10', '2026-10-20T14:10:00Z', '203.0.113.7', '2026-10-20T14:11:00Z', '198.51.100.9', '2026-10-20T14:11:00Z', 'firmada');
            INSERT dbo.tblNotasAclaratorias (Id, Entidad, EntidadId, AutorId, Texto, CreatedAt)
            VALUES (NEWID(), 'inspeccion', @insp, @coordinador, N'Aclaro que la unidad llego lloviendo', '2026-10-20T15:00:00Z');
            ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL;
            """,
            ("@insp", Guid.Parse(INSPECCION_V3)), ("@foto", foto), ("@fotoH", fotoHallazgo), ("@inspector", inspector), ("@conductor", conductor), ("@coordinador", coordinador));

        // La region de la zona izquierdo 7 en el panel y la imagen del panel, por la API del catalogo (renueva el cache de catalogos).
        var cuadrante = (Guid)(await Sembrador.EscalarAsync("SELECT TOP 1 CuadranteId FROM dbo.tblZonasDano WHERE Numero = 7 AND Lado = 'izquierdo'"))!;
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN, "super-pdf@ejemplo.com");
        var region = await Cliente(superAdmin.Token).PostAsJsonAsync(
            "/api/catalogos/cuadrante-regiones", new { panel = "costado_izquierdo", cuadrante_id = cuadrante, lado = "izquierdo", geometria = new { puntos = new[] { new[] { 0.2, 0.2 }, new[] { 0.6, 0.2 }, new[] { 0.6, 0.5 }, new[] { 0.2, 0.5 } } } });
        Assert.Equal(HttpStatusCode.Created, region.StatusCode);
        var imagen = await Cliente(superAdmin.Token).PutAsJsonAsync("/api/catalogos/paneles-dano/costado_izquierdo/imagen", new { archivo = imagenPanel });
        Assert.Equal(HttpStatusCode.NoContent, imagen.StatusCode);
    }

    // ------------------------------------------------------------------------------------ informe

    [Fact]
    public async Task El_informe_en_pdf_trae_la_unidad_las_fotos_con_su_sello_los_hallazgos_el_panel_las_firmas_y_las_notas()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        await SembrarDetalleAsync(coordinador, coordinador.UsuarioId, e.I2, e.Conductor);

        var (r, bytes) = await PdfAsync(coordinador, $"/api/inspecciones/{INSPECCION_V3}/pdf");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("informe-VIN-IND-0003-cargue-llegada-v1.pdf", r.Content.Headers.ContentDisposition!.FileName);
        var pdf = Texto(bytes);
        var plano = Plano(pdf);
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
        // La unidad, el viaje y la etapa.
        foreach (var texto in new[] { "VIN-IND-0003", "Toyota", "importacion", "Sofasa", "MTK-IND-B", "DEF456", "TR-2", "cargue" })
        {
            Assert.Contains(texto, plano);
        }

        // Las fotos con su sello: fecha y hora, ubicacion, sello del servidor y hash.
        Assert.Contains("Vista: Frontal", plano);
        Assert.Contains("Fecha y hora: 2026-10-20 14:05:00 UTC", plano);
        Assert.Contains("4.60971, -74.08175", plano);
        Assert.Contains("SHA-256: aaaabbbbccccddddeeeeffff00001111", plano);
        Assert.Contains("22223333444455556666777788889999", plano);
        Assert.Contains("Sello del servidor:", plano);
        // El hallazgo con su zona, clase, tamano y nivel; el panel con la zona marcada.
        Assert.Contains("Hallazgo 1", plano);
        Assert.Contains("Izquierdo 7", plano);
        Assert.Contains("Clase: rotura", plano);
        Assert.Contains("1_5cm", plano);
        Assert.Contains("(1) Tj", pdf);
        Assert.Contains("Costado izquierdo", plano);
        // Dos fotos y la imagen del panel; las firmas son trazos.
        Assert.Equal(3, Imagenes(pdf));
        Assert.Contains("Cierre: firmada", plano);
        Assert.Contains("IP 203.0.113.7", plano);
        Assert.Contains("IP 198.51.100.9", plano);
        Assert.Contains("Aclaro que la unidad llego lloviendo", plano);
    }

    [Fact]
    public async Task El_historial_de_seguimiento_del_hallazgo_sale_en_el_informe()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var hallazgo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblHallazgos WHERE InspeccionId = @i", ("@i", Guid.Parse(INSPECCION_V3))))!;
        var cambio = await Cliente(coordinador.Token).PostAsJsonAsync($"/api/hallazgos/{hallazgo}/seguimiento", new { estado = "validado", nota = "Se valida con el cliente" });
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);

        var (r, bytes) = await PdfAsync(coordinador, $"/api/inspecciones/{INSPECCION_V3}/pdf");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var plano = Plano(Texto(bytes));
        Assert.Contains("Historial de seguimiento", plano);
        Assert.Contains("Se valida con el cliente", plano);
        Assert.Contains("pendiente", plano);
        Assert.Contains("validado", plano);
    }

    [Fact]
    public async Task Un_informe_sin_fotos_ni_hallazgos_ni_transferencia_tambien_se_genera()
    {
        await EscenarioAsync();
        var admin = await SesionAsync(clsRol.ADMIN);

        var (r, bytes) = await PdfAsync(admin, "/api/inspecciones/55555555-0000-0000-0000-000000000001/pdf");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var plano = Plano(Texto(bytes));
        Assert.Contains("Sin fotos de vistas.", plano);
        Assert.Contains("Sin hallazgos.", plano);
        Assert.Contains("no tiene transferencia", plano);
        Assert.Equal(0, Imagenes(Texto(bytes)));
    }

    [Fact]
    public async Task Una_foto_cuyo_archivo_no_esta_en_el_almacen_se_indica_en_lugar_de_romper_el_pdf()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        await Sembrador.EjecutarAsync(
            """
            ALTER TABLE dbo.tblEvidencias DISABLE TRIGGER ALL;
            INSERT dbo.tblEvidencias (Id, InspeccionId, VistaId, StoragePath, SelloFecha, SelloLat, SelloLng)
            SELECT NEWID(), @insp, Id, 'no-existe.png', '2026-10-20T14:05:00Z', 4.6, -74.1 FROM dbo.tblVistasFoto WHERE Codigo = 'frontal';
            ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
            """,
            ("@insp", Guid.Parse(INSPECCION_V3)));

        var (r, bytes) = await PdfAsync(coordinador, $"/api/inspecciones/{INSPECCION_V3}/pdf");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var plano = Plano(Texto(bytes));
        Assert.Contains("Archivo no disponible en el almac", plano);
        Assert.Equal(0, Imagenes(Texto(bytes)));
    }

    [Fact]
    public async Task Un_pdf_no_lleva_mas_del_maximo_de_fotos_y_avisa_cuantas_dejo_fuera()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var foto = await SubirAsync(coordinador);
        await Sembrador.EjecutarAsync(
            """
            ALTER TABLE dbo.tblEvidencias DISABLE TRIGGER ALL;
            DECLARE @hallazgo uniqueidentifier = (SELECT Id FROM dbo.tblHallazgos WHERE InspeccionId = @insp);
            INSERT dbo.tblEvidencias (Id, InspeccionId, HallazgoId, StoragePath, SelloFecha, SelloLat, SelloLng)
            SELECT TOP (65) NEWID(), @insp, @hallazgo, @foto, SYSDATETIMEOFFSET(), 4.6, -74.1 FROM sys.all_objects;
            ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
            """,
            ("@insp", Guid.Parse(INSPECCION_V3)), ("@foto", foto));

        var (r, bytes) = await PdfAsync(coordinador, $"/api/inspecciones/{INSPECCION_V3}/pdf");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var pdf = Texto(bytes);
        Assert.Equal(60, Imagenes(pdf));
        Assert.Contains("no incluye 5 foto(s)", Plano(pdf));
    }

    // ------------------------------------------------------------------------------------ quien lo ve

    [Fact]
    public async Task El_inspector_del_viaje_el_coordinador_y_el_administrador_lo_ven_y_los_demas_reciben_403()
    {
        var e = await EscenarioAsync();
        // El inspector 1 (del escenario) es el inspector del viaje A.
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET InspectorId = @i WHERE CodigoMtrack = 'MTK-IND-A'", ("@i", e.I1));
        var inspectorDelViaje = new Sesion(await LoginAsync("inspector1@ejemplo.com", "Clave-Segura-1"), e.I1);
        var otroInspector = await SesionAsync(clsRol.INSPECTOR, "otro-inspector@ejemplo.com");
        var delOtroCliente = await SesionAsync(clsRol.COORDINADOR_CALIDAD, "coord-otro@ejemplo.com", e.Otro);
        var comercial = await SesionAsync(clsRol.COMERCIAL);
        var ruta = $"/api/inspecciones/{INSPECCION_V2}/pdf";

        Assert.Equal(HttpStatusCode.OK, (await PdfAsync(comercial, ruta)).Respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PdfAsync(inspectorDelViaje, ruta)).Respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PdfAsync(otroInspector, ruta)).Respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PdfAsync(delOtroCliente, ruta)).Respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PdfAsync(comercial, $"/api/inspecciones/{Guid.NewGuid()}/pdf")).Respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync(ruta)).StatusCode);
    }

    [Fact]
    public async Task El_conductor_del_viaje_ve_el_pdf_con_los_hallazgos_en_lectura_sin_nivel_seguimiento_ni_historial()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var login = await Cliente().PostAsJsonAsync("/api/auth/login-conductor", new { cedula = "1032456789", pin = "4821" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var conductor = new Sesion((await ComoJsonAsync(login)).GetProperty("token").GetString()!, e.Conductor);

        var (rc, completo) = await PdfAsync(coordinador, $"/api/inspecciones/{INSPECCION_V2}/pdf");
        var (rd, delConductor) = await PdfAsync(conductor, $"/api/inspecciones/{INSPECCION_V2}/pdf");

        Assert.Equal(HttpStatusCode.OK, rc.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rd.StatusCode);
        Assert.Contains("Hallazgo 1", Plano(Texto(completo)));
        Assert.Contains("Seguimiento:", Plano(Texto(completo)));
        // El conductor lee el hallazgo (su zona, clase y tamano) pero no su nivel, su seguimiento ni su historial.
        var plano = Plano(Texto(delConductor));
        Assert.Contains("Hallazgo 1", plano);
        Assert.Contains("Clase:", plano);
        Assert.DoesNotContain("Nivel:", plano);
        Assert.DoesNotContain("Seguimiento:", plano);
        Assert.DoesNotContain("Sin hallazgos.", plano);
    }

    // ------------------------------------------------------------------------------------ inventario

    [Fact]
    public async Task El_inventario_en_pdf_incluye_las_fotos_y_los_paneles_con_la_cantidad_de_hallazgos_de_cada_zona()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        await SembrarDetalleAsync(coordinador, coordinador.UsuarioId, e.I2, e.Conductor);

        var (r, bytes) = await PdfAsync(coordinador, "/api/vehiculos/VIN-IND-0003/inventario/pdf");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inventario-VIN-IND-0003.pdf", r.Content.Headers.ContentDisposition!.FileName);
        var pdf = Texto(bytes);
        var plano = Plano(pdf);
        Assert.Contains("VIN: VIN-IND-0003", plano);
        Assert.Contains("Proceso: cargue", plano);
        Assert.Contains("Izquierdo 7", plano);
        Assert.Contains("Vista: Frontal", plano);
        Assert.Contains("SHA-256: aaaabbbbccccddddeeeeffff00001111", plano);
        // Las dos fotos y la imagen del panel, con el hallazgo de la zona contado.
        Assert.Equal(3, Imagenes(pdf));
        Assert.Contains("Costado izquierdo", plano);
        Assert.Contains("(1) Tj", pdf);
    }

    [Fact]
    public async Task El_inventario_de_un_vehiculo_dado_de_baja_lo_dice_y_otro_cliente_recibe_403()
    {
        var e = await EscenarioAsync();
        var admin = await SesionAsync(clsRol.ADMIN);
        var delOtro = await SesionAsync(clsRol.COORDINADOR_CALIDAD, "coord-otro@ejemplo.com", e.Otro);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(admin.Token).DeleteAsync("/api/vehiculos/VIN-IND-0001")).StatusCode);

        var (r, bytes) = await PdfAsync(admin, "/api/vehiculos/VIN-IND-0001/inventario/pdf");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("dado de baja", Plano(Texto(bytes)));
        Assert.Equal(HttpStatusCode.Forbidden, (await PdfAsync(delOtro, "/api/vehiculos/VIN-IND-0001/inventario/pdf")).Respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PdfAsync(admin, "/api/vehiculos/NO-EXISTE/inventario/pdf")).Respuesta.StatusCode);
    }
}
