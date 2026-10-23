using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class ReportesApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private static async Task<JsonElement> ClonarAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<(string Token, Guid UsuarioId)> AutenticarAsync()
    {
        var usuario = await Fabrica.SembrarUsuarioAsync("analista@ejemplo.com", "Clave-Segura-1", clsRol.COORDINADOR_CALIDAD);
        return (await LoginAsync("analista@ejemplo.com", "Clave-Segura-1"), usuario.Id);
    }

    /// <summary>
    /// V1 importacion movilizado (3 hallazgos), V2 importacion movilizado (0), V3 nacional movilizado (1),
    /// V4 exportacion pendiente con 5 hallazgos (solo cuenta en "con novedad"), V5 nacional pendiente sin hallazgos.
    /// </summary>
    private async Task SembrarEscenarioAsync(Guid usuarioId)
    {
        async Task Movilizar(string vin, string operacion, params (string, string, string)[][] hallazgosPorEtapa)
        {
            var (_, etapas) = await Sembrador.VehiculoConRutaAsync(vin, operacion: operacion);
            for (var i = 0; i < etapas.Length; i++)
            {
                var h = i < hallazgosPorEtapa.Length ? hallazgosPorEtapa[i] : [];
                await Sembrador.InformeConHallazgosAsync(etapas[i], usuarioId, cerrada: true, h);
            }
        }

        await Movilizar(
            "V1",
            "importacion",
            [("Capot", "golpe", "1_5cm"), ("Techo", "mancha", "requiere_reemplazo")],
            [("Capot", "rotura", "5_10cm")]);
        await Movilizar("V2", "importacion");
        await Movilizar("V3", "nacional", [], [], [("Puertas", "golpe", "1_5cm")]);

        var (_, etapas4) = await Sembrador.VehiculoConRutaAsync("V4", operacion: "exportacion");
        await Sembrador.InformeConHallazgosAsync(etapas4[0], usuarioId, cerrada: true,
            [("Capot", "golpe", "1_5cm"), ("Capot", "golpe", "1_5cm"), ("Capot", "golpe", "1_5cm"), ("Capot", "golpe", "1_5cm"), ("Capot", "golpe", "1_5cm")]);
        await Sembrador.VehiculoConRutaAsync("V5", operacion: "nacional");
    }

    [Fact]
    public async Task La_tasa_de_rechazo_se_calcula_sobre_movilizadas_y_se_compara_con_la_linea_base()
    {
        var (token, usuarioId) = await AutenticarAsync();
        await SembrarEscenarioAsync(usuarioId);

        var r = await Cliente(token).GetAsync("/api/metricas/rechazo");

        var m = await ClonarAsync(r);
        Assert.Equal(["tasaGlobal", "totalMovilizadas", "totalPendientes", "totalConNovedad", "totalNovedad", "porOperacion"], m.EnumerateObject().Select(x => x.Name).ToList());
        Assert.Equal(66.67m, m.GetProperty("tasaGlobal").GetDecimal());
        Assert.Equal(3, m.GetProperty("totalMovilizadas").GetInt32());
        Assert.Equal(2, m.GetProperty("totalPendientes").GetInt32());
        Assert.Equal(3, m.GetProperty("totalConNovedad").GetInt32()); // V1, V3 y V4 (V4 pendiente, igual cuenta aqui)
        Assert.Equal(2, m.GetProperty("totalNovedad").GetInt32());

        var ops = m.GetProperty("porOperacion");
        Assert.Equal(2, ops.GetArrayLength());
        Assert.Equal(["operacion", "total", "conNovedad", "tasaActual", "lineaBase", "variacion"], ops[0].EnumerateObject().Select(x => x.Name).ToList());
        Assert.Equal("importacion", ops[0].GetProperty("operacion").GetString());
        Assert.Equal(2, ops[0].GetProperty("total").GetInt32());
        Assert.Equal(1, ops[0].GetProperty("conNovedad").GetInt32());
        Assert.Equal(50m, ops[0].GetProperty("tasaActual").GetDecimal());
        Assert.Equal(8.81m, ops[0].GetProperty("lineaBase").GetDecimal());
        Assert.Equal(41.19m, ops[0].GetProperty("variacion").GetDecimal());
        Assert.Equal("nacional", ops[1].GetProperty("operacion").GetString());
        Assert.Equal(100m, ops[1].GetProperty("tasaActual").GetDecimal());
        Assert.Equal(1.95m, ops[1].GetProperty("lineaBase").GetDecimal());
        Assert.Equal(98.05m, ops[1].GetProperty("variacion").GetDecimal());
    }

    [Fact]
    public async Task Sin_datos_la_tasa_de_rechazo_responde_en_cero()
    {
        var (token, _) = await AutenticarAsync();

        var rechazo = await ClonarAsync(await Cliente(token).GetAsync("/api/metricas/rechazo"));

        Assert.Equal(0m, rechazo.GetProperty("tasaGlobal").GetDecimal());
        Assert.Equal(0, rechazo.GetProperty("totalMovilizadas").GetInt32());
        Assert.Equal(0, rechazo.GetProperty("porOperacion").GetArrayLength());
    }

    [Fact]
    public async Task La_tasa_de_rechazo_exige_autenticacion()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync("/api/metricas/rechazo")).StatusCode);
    }

    [Fact]
    public async Task El_inventario_se_descarga_en_pdf_con_el_contenido_del_vin()
    {
        var (token, usuarioId) = await AutenticarAsync();
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("PDF-VIN-0001", "Renault");
        await Sembrador.InformeConHallazgosAsync(etapas[0], usuarioId, cerrada: true, ("Guardafango (izq.)", "golpe", "5_10cm"));

        var r = await Cliente(token).GetAsync("/api/vehiculos/PDF-VIN-0001/inventario/pdf");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", r.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("inventario-PDF-VIN-0001.pdf", r.Content.Headers.ContentDisposition.ToString());
        var bytes = await r.Content.ReadAsByteArrayAsync();
        var texto = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", texto);
        Assert.EndsWith("%%EOF\n", texto);
        Assert.Contains("VIN: PDF-VIN-0001", texto);
        Assert.Contains("Proceso: cargue \\(orden 1\\)", texto);
        Assert.Contains("Guardafango \\(izq.\\): golpe \\(5_10cm\\)", texto);
    }

    [Fact]
    public async Task El_pdf_de_un_vin_inexistente_responde_404_y_exige_autenticacion()
    {
        var (token, _) = await AutenticarAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Cliente(token).GetAsync("/api/vehiculos/NO-EXISTE/inventario/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync("/api/vehiculos/X/inventario/pdf")).StatusCode);
    }
}
