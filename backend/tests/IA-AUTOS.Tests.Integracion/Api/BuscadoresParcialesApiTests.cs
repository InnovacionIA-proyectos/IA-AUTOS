using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Los filtros de marca (vehiculos, informes, exportacion de informes y reportes del tablero), de trailer (tablero) y de estado de Sofasa buscan por parte del texto, sin distinguir mayusculas y recortando los espacios de
/// los bordes; un filtro vacio no filtra y sin coincidencias responde 200 con 0 elementos. El VIN de 17 caracteres sigue siendo exacto. Y las listas de marcas y de trailers del cliente.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class BuscadoresParcialesApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    private static readonly string[] VARIANTES = ["rena", "RENA", "%20rena%20", "%20%20ReNa", "Renault"];

    private static int FilasDelArchivo(byte[] contenido)
    {
        using var zip = new ZipArchive(new MemoryStream(contenido), ZipArchiveMode.Read);
        using var flujo = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return XDocument.Load(flujo).Descendants(ns + "row").Count() - 1;
    }

    [Fact]
    public async Task La_marca_busca_por_contenido_sin_distinguir_mayusculas_ni_espacios_en_los_cuatro_listados()
    {
        _ = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        foreach (var variante in VARIANTES)
        {
            // Reportes del tablero: las dos Renault de Sofasa, escriba como se escriba.
            Assert.Equal(2, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", $"?marca={variante}")).GetProperty("despachadas").GetInt32());
        }

        var vehiculos = (await ConsultarAsync(coordinador, "/api/vehiculos/listado", "?tipo=pendientes&marca=Renault")).GetProperty("total").GetInt32();
        var informes = (await ConsultarAsync(coordinador, "/api/inspecciones", "?marca=Renault")).GetProperty("total").GetInt32();
        var archivo = FilasDelArchivo(await (await Cliente(coordinador.Token).GetAsync("/api/exportaciones/inspecciones.xlsx?marca=Renault")).Content.ReadAsByteArrayAsync());
        Assert.True(informes > 0 && archivo > 0);
        foreach (var variante in VARIANTES)
        {
            Assert.Equal(vehiculos, (await ConsultarAsync(coordinador, "/api/vehiculos/listado", $"?tipo=pendientes&marca={variante}")).GetProperty("total").GetInt32());
            Assert.Equal(informes, (await ConsultarAsync(coordinador, "/api/inspecciones", $"?marca={variante}")).GetProperty("total").GetInt32());
            Assert.Equal(archivo, FilasDelArchivo(await (await Cliente(coordinador.Token).GetAsync($"/api/exportaciones/inspecciones.xlsx?marca={variante}")).Content.ReadAsByteArrayAsync()));
        }

        // Una parte del texto de otra marca no trae las Renault y el filtro vacio o solo con espacios no filtra.
        Assert.Equal(1, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?marca=OYOT")).GetProperty("despachadas").GetInt32());
        var sinFiltro = (await ConsultarAsync(coordinador, "/api/inspecciones")).GetProperty("total").GetInt32();
        Assert.True(sinFiltro > informes);
        Assert.Equal(sinFiltro, (await ConsultarAsync(coordinador, "/api/inspecciones", "?marca=")).GetProperty("total").GetInt32());
        Assert.Equal(sinFiltro, (await ConsultarAsync(coordinador, "/api/inspecciones", "?marca=%20%20")).GetProperty("total").GetInt32());
        Assert.Equal(3, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?marca=%20")).GetProperty("despachadas").GetInt32());
    }

    [Fact]
    public async Task Cero_coincidencias_responde_200_con_cero_elementos_y_el_vin_de_17_caracteres_sigue_siendo_exacto()
    {
        _ = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var vehiculos = await Cliente(coordinador.Token).GetAsync("/api/vehiculos/listado?tipo=pendientes&marca=zzzz");
        var informes = await Cliente(coordinador.Token).GetAsync("/api/inspecciones?marca=zzzz");
        var tablero = await Cliente(coordinador.Token).GetAsync("/api/reportes/cobertura?marca=zzzz&trailer=zzzz");

        Assert.Equal(HttpStatusCode.OK, vehiculos.StatusCode);
        Assert.Equal(0, (await ComoJsonAsync(vehiculos)).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, informes.StatusCode);
        Assert.Equal(0, (await ComoJsonAsync(informes)).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, tablero.StatusCode);
        Assert.Equal(0, (await ComoJsonAsync(tablero)).GetProperty("despachadas").GetInt32());
        // Un VIN de 17 caracteres es exacto: con un caracter de menos busca por contenido y con uno cambiado no encuentra nada.
        const string vin = "9FB45RFL8NM600001";
        await Sembrador.VehiculoAsync(vin);
        var cambiado = vin[..16] + (vin[16] == 'X' ? 'Y' : 'X');
        var encontrados = 0;
        foreach (var tipo in new[] { "movilizados", "pendientes", "novedad" })
        {
            encontrados += (await ConsultarAsync(coordinador, "/api/vehiculos/listado", $"?tipo={tipo}&vin={vin}")).GetProperty("total").GetInt32();
            Assert.Equal(0, (await ConsultarAsync(coordinador, "/api/vehiculos/listado", $"?tipo={tipo}&vin={cambiado}")).GetProperty("total").GetInt32());
        }

        Assert.True(encontrados >= 1);
    }

    [Fact]
    public async Task El_trailer_del_tablero_busca_por_contenido_y_tr_12_no_trae_tr_145()
    {
        _ = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        Assert.Equal(1, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?trailer=TR-2")).GetProperty("despachadas").GetInt32());
        Assert.Equal(1, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?trailer=tr-2")).GetProperty("despachadas").GetInt32());
        Assert.Equal(1, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?trailer=%20Tr-2%20")).GetProperty("despachadas").GetInt32());
        // «TR-» esta en TR-1 y en TR-2: las tres despachadas de Sofasa.
        Assert.Equal(3, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?trailer=tr-")).GetProperty("despachadas").GetInt32());
        Assert.Equal(0, (await ConsultarAsync(coordinador, "/api/reportes/cobertura", "?trailer=TR-9")).GetProperty("despachadas").GetInt32());
    }

    [Fact]
    public async Task El_estado_de_sofasa_busca_por_contenido_sobre_el_codigo()
    {
        var admin = await SesionAsync(clsRol.SUPER_ADMIN);
        await Sembrador.EjecutarAsync("INSERT dbo.tblOrdenesSofasa (Id, SourceId, StatusCode) VALUES (NEWID(), 901, 'ENTREGADA'), (NEWID(), 902, 'ENTREGA PARCIAL'), (NEWID(), 903, 'EN RUTA')");

        foreach (var variante in new[] { "entreg", "ENTREG", "%20Entreg%20" })
        {
            var r = await ConsultarAsync(admin, "/api/sofasa/ordenes", $"?estado={variante}");
            Assert.Equal(["ENTREGA PARCIAL", "ENTREGADA"], r.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("status_code").GetString()).Order().ToList());
        }

        Assert.Equal(1, (await ConsultarAsync(admin, "/api/sofasa/ordenes", "?estado=ENTREGADA")).GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await ConsultarAsync(admin, "/api/sofasa/ordenes", "?estado=zzzz")).GetProperty("items").GetArrayLength());
        Assert.Equal(3, (await ConsultarAsync(admin, "/api/sofasa/ordenes", "?estado=%20")).GetProperty("items").GetArrayLength());
    }

    // ------------------------------------------------------------------------------------------------ las listas de marcas y de trailers

    private static List<string> Textos(JsonElement lista) => lista.EnumerateArray().Select(x => x.GetString()!).ToList();

    [Fact]
    public async Task La_lista_de_marcas_es_la_del_cliente_sin_repetir_y_por_orden_con_las_reglas_de_cliente()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var delOtro = await SesionAsync(clsRol.COORDINADOR_CALIDAD, "coord-otro@ejemplo.com", e.Otro);
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);
        var sinVehiculos = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'vacio', N'Cliente sin carros', 3)", ("@id", sinVehiculos));

        var suyas = Textos(await ConsultarAsync(coordinador, "/api/vehiculos/marcas"));
        var deOtro = Textos(await ConsultarAsync(delOtro, "/api/vehiculos/marcas"));

        Assert.Contains("Renault", suyas);
        Assert.Contains("Toyota", suyas);
        Assert.DoesNotContain("Hyundai", suyas);
        Assert.Equal(suyas.Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList(), suyas);
        Assert.Equal(["Hyundai"], deOtro);
        Assert.Equal(["Hyundai"], Textos(await ConsultarAsync(superAdmin, "/api/vehiculos/marcas", $"?cliente_id={e.Otro}")));
        Assert.Contains("Hyundai", Textos(await ConsultarAsync(superAdmin, "/api/vehiculos/marcas")));
        Assert.Empty(Textos(await ConsultarAsync(superAdmin, "/api/vehiculos/marcas", $"?cliente_id={sinVehiculos}")));
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(coordinador.Token).GetAsync($"/api/vehiculos/marcas?cliente_id={e.Otro}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Cliente(superAdmin.Token).GetAsync("/api/vehiculos/marcas?cliente_id=no-es-uuid")).StatusCode);
        // Los roles que ven los listados: el inspector, el conductor y quien no inicio sesion, no.
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente((await SesionAsync(clsRol.INSPECTOR, "insp-x@ejemplo.com")).Token).GetAsync("/api/vehiculos/marcas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync("/api/vehiculos/marcas")).StatusCode);
    }

    [Fact]
    public async Task La_lista_de_trailers_es_la_del_cliente_sin_nulos_ni_vacios_y_con_las_mismas_reglas_de_cliente()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var delOtro = await SesionAsync(clsRol.COORDINADOR_CALIDAD, "coord-otro@ejemplo.com", e.Otro);
        var superAdmin = await SesionAsync(clsRol.SUPER_ADMIN);
        await Sembrador.EjecutarAsync("UPDATE TOP (1) dbo.tblViajes SET Trailer = N'' WHERE Trailer IS NOT NULL AND Trailer <> N'TR-9'");

        var suyos = Textos(await ConsultarAsync(coordinador, "/api/viajes/trailers"));

        Assert.DoesNotContain(suyos, string.IsNullOrEmpty);
        Assert.DoesNotContain("TR-9", suyos);
        Assert.Equal(suyos.Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList(), suyos);
        Assert.Equal(["TR-9"], Textos(await ConsultarAsync(delOtro, "/api/viajes/trailers")));
        Assert.Equal(["TR-9"], Textos(await ConsultarAsync(superAdmin, "/api/viajes/trailers", $"?cliente_id={e.Otro}")));
        Assert.Contains("TR-9", Textos(await ConsultarAsync(superAdmin, "/api/viajes/trailers")));
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(coordinador.Token).GetAsync($"/api/viajes/trailers?cliente_id={e.Otro}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente((await SesionAsync(clsRol.CONDUCTOR, "cond-x@ejemplo.com")).Token).GetAsync("/api/viajes/trailers")).StatusCode);
    }
}
