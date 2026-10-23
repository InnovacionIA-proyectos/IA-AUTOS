using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Visibilidad por cliente en lectura: el coordinador, el comercial y el administrador de un cliente ven solo las unidades de su cliente (vehiculos, inventario,
/// informes, viajes, ordenes Sofasa y reportes) y un recurso de otro cliente responde 403; el super administrador ve todos y es el unico que pide un cliente
/// distinto del suyo.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class VisibilidadClienteApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Dos clientes, cada uno con una unidad con su ruta y un informe de salida cerrado: <c>VIN-SOF</c> (Sofasa) y <c>VIN-OTRO</c> (el otro).</summary>
    private sealed record Escenario(
        Guid Sofasa,
        Guid Otro,
        Guid InformeSofasa,
        Guid InformeOtro,
        Guid ViajeSofasa,
        Guid ViajeOtro,
        Guid[] EtapasSofasa,
        Guid[] EtapasOtro);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync()
    {
        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;
        var otro = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otro));
        var inspector = await Fabrica.SembrarUsuarioAsync("inspector-base@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);

        var (_, etapasSofasa) = await Sembrador.VehiculoConRutaAsync("VIN-SOF", operacion: "nacional");
        var (_, etapasOtro) = await Sembrador.VehiculoConRutaAsync("VIN-OTRO", operacion: "nacional");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET ClienteId = @c WHERE Vin = 'VIN-OTRO'", ("@c", otro));
        var informeSofasa = await Sembrador.InformeAsync(etapasSofasa[0], inspector.Id, cerrada: true);
        var informeOtro = await Sembrador.InformeAsync(etapasOtro[0], inspector.Id, cerrada: true);
        return new Escenario(
            sofasa, otro, informeSofasa, informeOtro,
            await Sembrador.ViajeDeEtapaAsync(etapasSofasa[0]), await Sembrador.ViajeDeEtapaAsync(etapasOtro[0]),
            etapasSofasa, etapasOtro);
    }

    private static List<string?> Vines(JsonElement filas) => filas.EnumerateArray().Select(f => f.GetProperty("vin").GetString()).Order().ToList();

    // ------------------------------------------------------------------------------------------- vehiculos

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.ADMIN)]
    public async Task La_busqueda_y_el_listado_de_vehiculos_traen_solo_los_del_cliente_de_quien_consulta(string rol)
    {
        var e = await EscenarioAsync();
        var delCliente = await SesionAsync("cliente@ejemplo.com", rol, e.Sofasa);
        var delOtro = await SesionAsync("otro@ejemplo.com", rol, e.Otro);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var busquedaSofasa = await ComoJsonAsync(await Cliente(delCliente.Token).GetAsync("/api/vehiculos?vin=VIN-"));
        var busquedaOtro = await ComoJsonAsync(await Cliente(delOtro.Token).GetAsync("/api/vehiculos?vin=VIN-"));
        var busquedaSuper = await ComoJsonAsync(await Cliente(superAdmin.Token).GetAsync("/api/vehiculos?vin=VIN-"));
        var listadoSofasa = await ComoJsonAsync(await Cliente(delCliente.Token).GetAsync("/api/vehiculos/listado?tipo=pendientes"));
        var listadoOtro = await ComoJsonAsync(await Cliente(delOtro.Token).GetAsync("/api/vehiculos/listado?tipo=pendientes"));
        var listadoSuper = await ComoJsonAsync(await Cliente(superAdmin.Token).GetAsync("/api/vehiculos/listado?tipo=pendientes"));

        Assert.Equal(["VIN-SOF"], Vines(busquedaSofasa));
        Assert.Equal(["VIN-OTRO"], Vines(busquedaOtro));
        Assert.Equal(["VIN-OTRO", "VIN-SOF"], Vines(busquedaSuper));
        Assert.Equal(["VIN-SOF"], Vines(listadoSofasa.GetProperty("items")));
        Assert.Equal(1, listadoSofasa.GetProperty("total").GetInt32());
        Assert.Equal(["VIN-OTRO"], Vines(listadoOtro.GetProperty("items")));
        Assert.Equal(["VIN-OTRO", "VIN-SOF"], Vines(listadoSuper.GetProperty("items")));
    }

    [Fact]
    public async Task El_inventario_de_un_vin_de_otro_cliente_responde_403_en_json_y_en_pdf_y_el_super_administrador_lo_ve()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, e.Sofasa);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var propio = await Cliente(coordinador.Token).GetAsync("/api/vehiculos/VIN-SOF/inventario");
        var propioPdf = await Cliente(coordinador.Token).GetAsync("/api/vehiculos/VIN-SOF/inventario/pdf");
        var ajeno = await Cliente(coordinador.Token).GetAsync("/api/vehiculos/VIN-OTRO/inventario");
        var ajenoPdf = await Cliente(coordinador.Token).GetAsync("/api/vehiculos/VIN-OTRO/inventario/pdf");
        var inexistente = await Cliente(coordinador.Token).GetAsync("/api/vehiculos/NO-EXISTE/inventario");
        var delSuper = await Cliente(superAdmin.Token).GetAsync("/api/vehiculos/VIN-OTRO/inventario");

        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.OK, propioPdf.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal("El VIN no es de su cliente", (await ComoJsonAsync(ajeno)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, ajenoPdf.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delSuper.StatusCode);
    }

    // ------------------------------------------------------------------------------------------ inspecciones

    [Fact]
    public async Task Los_informes_se_listan_y_se_abren_solo_los_del_cliente_y_uno_de_otro_cliente_es_403()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, e.Sofasa);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL, e.Otro);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var listado = Items(await ComoJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones")));
        var listadoOtro = Items(await ComoJsonAsync(await Cliente(comercial.Token).GetAsync("/api/inspecciones")));
        var listadoSuper = Items(await ComoJsonAsync(await Cliente(superAdmin.Token).GetAsync("/api/inspecciones")));
        var propio = await Cliente(coordinador.Token).GetAsync($"/api/inspecciones/{e.InformeSofasa}");
        var ajeno = await Cliente(coordinador.Token).GetAsync($"/api/inspecciones/{e.InformeOtro}");
        var delSuper = await Cliente(superAdmin.Token).GetAsync($"/api/inspecciones/{e.InformeOtro}");
        var inexistente = await Cliente(coordinador.Token).GetAsync($"/api/inspecciones/{Guid.NewGuid()}");

        Assert.Equal([e.InformeSofasa], listado.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList());
        Assert.Equal([e.InformeOtro], listadoOtro.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList());
        Assert.Equal(2, listadoSuper.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal("La inspección es de un VIN de otro cliente", (await ComoJsonAsync(ajeno)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, delSuper.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Fact]
    public async Task La_constancia_en_pdf_y_los_hallazgos_previos_de_un_informe_de_otro_cliente_responden_403()
    {
        var e = await EscenarioAsync();
        var administrador = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN, e.Sofasa);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var constanciaAjena = await Cliente(administrador.Token).GetAsync($"/api/inspecciones/{e.InformeOtro}/transferencia/constancia/pdf");
        var previosAjenos = await Cliente(administrador.Token).GetAsync($"/api/inspecciones/{e.InformeOtro}/hallazgos-previos");
        var previosPropios = await Cliente(administrador.Token).GetAsync($"/api/inspecciones/{e.InformeSofasa}/hallazgos-previos");
        var previosDelSuper = await Cliente(superAdmin.Token).GetAsync($"/api/inspecciones/{e.InformeOtro}/hallazgos-previos");

        Assert.Equal(HttpStatusCode.Forbidden, constanciaAjena.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, previosAjenos.StatusCode);
        Assert.Equal(HttpStatusCode.OK, previosPropios.StatusCode);
        Assert.Equal(HttpStatusCode.OK, previosDelSuper.StatusCode);
    }

    // ----------------------------------------------------------------------------------------------- viajes

    [Fact]
    public async Task Un_viaje_solo_de_otro_cliente_es_403_y_pedir_otro_cliente_en_el_filtro_tambien_salvo_al_super_administrador()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, e.Sofasa);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var propio = await Cliente(coordinador.Token).GetAsync($"/api/viajes/{e.ViajeSofasa}");
        var ajeno = await Cliente(coordinador.Token).GetAsync($"/api/viajes/{e.ViajeOtro}");
        var listado = Items(await ComoJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes")));
        var conSuCliente = await Cliente(coordinador.Token).GetAsync($"/api/viajes?cliente_id={e.Sofasa}");
        var conOtroCliente = await Cliente(coordinador.Token).GetAsync($"/api/viajes?cliente_id={e.Otro}");
        var delSuper = Items(await ComoJsonAsync(await Cliente(superAdmin.Token).GetAsync($"/api/viajes?cliente_id={e.Otro}")));

        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal([e.ViajeSofasa], listado.EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToList());
        Assert.Equal(HttpStatusCode.OK, conSuCliente.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, conOtroCliente.StatusCode);
        Assert.Equal([e.ViajeOtro], delSuper.EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToList());
    }

    [Fact]
    public async Task Pedir_el_listado_de_usuarios_de_otro_cliente_es_403_para_quien_no_es_super_administrador()
    {
        var e = await EscenarioAsync();
        var administrador = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN, e.Sofasa);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        await SesionAsync("usuario-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, e.Otro);

        var propio = await Cliente(administrador.Token).GetAsync($"/api/usuarios?cliente_id={e.Sofasa}");
        var ajeno = await Cliente(administrador.Token).GetAsync($"/api/usuarios?cliente_id={e.Otro}");
        var delSuper = await ComoJsonAsync(await Cliente(superAdmin.Token).GetAsync($"/api/usuarios?cliente_id={e.Otro}"));

        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.All(delSuper.GetProperty("items").EnumerateArray(), u => Assert.Equal(e.Otro, u.GetProperty("cliente_id").GetGuid()));
    }

    // ------------------------------------------------------------------------------------- ordenes sofasa

    [Fact]
    public async Task Las_ordenes_de_sofasa_se_limitan_a_los_vin_del_cliente_de_quien_consulta()
    {
        var e = await EscenarioAsync();
        var etl = Fabrica.CreateClient();
        etl.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clsFabricaApi.TOKEN_ETL);
        Dictionary<string, object?> Fila(long id, string vin) => new()
        {
            ["source_id"] = id, ["status_code"] = "EN_RUTA", ["datetime_event"] = "2026-07-17T08:30:15.123Z", ["orden_number"] = "OC-" + id, ["vin"] = vin, ["make"] = "Renault",
        };
        await etl.PostAsJsonAsync("/api/etl/sofasa/ordenes", new { ordenes = new[] { Fila(1, "VIN-SOF"), Fila(2, "VIN-OTRO"), Fila(3, "VIN-OTRO") } });
        var delSofasa = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, e.Sofasa);
        var delOtro = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL, e.Otro);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var deSofasa = Items(await ComoJsonAsync(await Cliente(delSofasa.Token).GetAsync("/api/sofasa/ordenes")));
        var deOtro = Items(await ComoJsonAsync(await Cliente(delOtro.Token).GetAsync("/api/sofasa/ordenes")));
        var deTodos = Items(await ComoJsonAsync(await Cliente(superAdmin.Token).GetAsync("/api/sofasa/ordenes")));

        Assert.Equal(["VIN-SOF"], deSofasa.EnumerateArray().Select(o => o.GetProperty("vin").GetString()).ToList());
        Assert.Equal(["VIN-OTRO", "VIN-OTRO"], deOtro.EnumerateArray().Select(o => o.GetProperty("vin").GetString()).ToList());
        Assert.Equal(3, deTodos.GetArrayLength());
    }

    // ---------------------------------------------------------------------------------------------- reportes

    [Fact]
    public async Task Los_reportes_cuentan_solo_las_unidades_del_cliente_y_el_super_administrador_cuenta_todas()
    {
        var e = await EscenarioAsync();
        var inspector = await Fabrica.SembrarUsuarioAsync("inspector-reportes@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        // Las dos unidades se movilizan (informe cerrado en sus tres etapas); la de Sofasa con 2 hallazgos y la del otro cliente con 1.
        foreach (var (etapas, hallazgos) in new[] { (e.EtapasSofasa, 2), (e.EtapasOtro, 1) })
        {
            for (var i = 1; i < etapas.Length; i++)
            {
                await Sembrador.InformeConHallazgosAsync(etapas[i], inspector.Id, cerrada: true, Enumerable.Repeat(("Capot", "golpe", "1_5cm"), i == 1 ? hallazgos : 0).ToArray());
            }
        }

        var delSofasa = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, e.Sofasa);
        var delOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, e.Otro);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var patronesSofasa = await ComoJsonAsync(await Cliente(delSofasa.Token).GetAsync("/api/patrones"));
        var patronesOtro = await ComoJsonAsync(await Cliente(delOtro.Token).GetAsync("/api/patrones"));
        var patronesTodos = await ComoJsonAsync(await Cliente(superAdmin.Token).GetAsync("/api/patrones"));
        var rechazoSofasa = await ComoJsonAsync(await Cliente(delSofasa.Token).GetAsync("/api/metricas/rechazo"));
        var rechazoTodos = await ComoJsonAsync(await Cliente(superAdmin.Token).GetAsync("/api/metricas/rechazo"));

        Assert.Equal(2, patronesSofasa.GetProperty("total").GetInt32());
        Assert.Equal(1, patronesOtro.GetProperty("total").GetInt32());
        Assert.Equal(3, patronesTodos.GetProperty("total").GetInt32());
        Assert.Equal(1, rechazoSofasa.GetProperty("totalMovilizadas").GetInt32());
        Assert.Equal(2, rechazoTodos.GetProperty("totalMovilizadas").GetInt32());
    }
}
