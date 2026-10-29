using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El destino por unidad y la entrega por punto: cada unidad entra a un viaje con su lugar de destino (ESC-04), su Descargue se crea en el tipo de ese lugar y se mueve solo con el de esa unidad (ESC-01), en cada punto
/// se firma una vez entre quien entrega y quien recibe y la firma queda en la transferencia de cada unidad (ESC-02, ESC-05), una sola firma no bloquea y avisa al coordinador (ESC-03), y el viaje se finaliza cuando
/// todas sus unidades tienen el Descargue cerrado.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class EntregasPorPuntoApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Un viaje en curso con sus unidades: el viaje, los dos lugares de destino y los usuarios.</summary>
    private sealed record Escenario(Guid Viaje, Guid Autonorte, Guid CaliSur, Sesion Conductor, Sesion Inspector, Sesion Coordinador, Dictionary<string, Guid> Llegadas);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _api = CrearFabrica(servicios: s => s.AddSingleton<IEnviadorPush>(_push));
    }

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await _api.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1", _api), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token, _api);

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Guid> UnidadAsync() => (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;

    private static object[] Unidades(Guid lugar, params string[] vines) => vines.Select(v => (object)new { vin = v, lugar_destino_id = lugar }).ToArray();

    private async Task<Guid> EtapaAsync(Guid viaje, string vin, string codigo) =>
        (Guid)(await Sembrador.EscalarAsync(
            "SELECT e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId WHERE e.ViajeId = @v AND v.Vin = @vin AND c.Codigo = @c",
            ("@v", viaje), ("@vin", vin), ("@c", codigo)))!;

    private async Task<HttpResponseMessage> CrearViajeAsync(Sesion coordinador, string codigo, object[] unidades, Sesion? conductor = null, Sesion? inspector = null) =>
        await Como(coordinador).PostAsync("/api/viajes", Cuerpo(new
        {
            codigo_mtrack = codigo,
            tipo_unidad_id = await UnidadAsync(),
            placa = "ABC123",
            conductor_id = conductor?.UsuarioId,
            inspector_id = inspector?.UsuarioId,
            unidades,
        }));

    /// <summary>
    /// El viaje MT-4512: tres unidades al Concesionario Autonorte (Bogota) y dos al Concesionario Cali Sur (Cali), ya en curso, cada una con la inspeccion de llegada del Descargue cerrada (ahi se firma la entrega).
    /// </summary>
    private async Task<Escenario> EscenarioAsync(bool conLlegadas = true)
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var autonorte = await LugarRealAsync(Como(coordinador), "Concesionario Autonorte", "concesionario", "Bogota");
        var caliSur = await LugarRealAsync(Como(coordinador), "Concesionario Cali Sur", "cedi_2", "Cali");
        string[] vinesBogota = ["VIN-ENT-B1", "VIN-ENT-B2", "VIN-ENT-B3"];
        string[] vinesCali = ["VIN-ENT-C1", "VIN-ENT-C2"];
        foreach (var vin in vinesBogota.Concat(vinesCali))
        {
            await Sembrador.VehiculoAsync(vin);
        }

        var creado = await CrearViajeAsync(coordinador, "MT-4512", [.. Unidades(autonorte, vinesBogota), .. Unidades(caliSur, vinesCali)], conductor, inspector);
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var viaje = (await JsonAsync(creado)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Como(coordinador).PatchAsync($"/api/viajes/{viaje}/estado", Cuerpo(new { estado = "en_curso" }))).StatusCode);

        var llegadas = new Dictionary<string, Guid>();
        if (conLlegadas)
        {
            foreach (var vin in vinesBogota.Concat(vinesCali))
            {
                llegadas[vin] = await Sembrador.InformeAsync(await EtapaAsync(viaje, vin, "descargue"), inspector.UsuarioId, cerrada: true, momento: "llegada");
            }
        }

        return new Escenario(viaje, autonorte, caliSur, conductor, inspector, coordinador, llegadas);
    }

    private Task<HttpResponseMessage> FirmarAsync(Sesion quien, Guid viaje, Guid lugar, string firma = "M10,10 L50,50") =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/entregas/{lugar}/firma", Cuerpo(new { firma }));

    private Task<HttpResponseMessage> UnaFirmaAsync(Sesion quien, Guid viaje, Guid lugar, string? motivo, string? firma = "M10,10 L50,50") =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/entregas/{lugar}/una-firma", Cuerpo(new { firma, motivo }));

    private Task<object?> TransferenciaAsync(Guid inspeccion, string columna) =>
        Sembrador.EscalarAsync($"SELECT {columna} FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @i", ("@i", inspeccion));

    // --------------------------------------------------------------------------------------- ESC-04: una unidad no viaja sin destino

    [Fact]
    public async Task Crear_un_viaje_o_agregar_una_unidad_sin_su_destino_responde_400_y_no_crea_nada()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var lugar = await LugarRealAsync(Como(coordinador), "Concesionario Autonorte", "concesionario", "Bogota");
        await Sembrador.VehiculoAsync("VIN-SIN-D1");
        await Sembrador.VehiculoAsync("VIN-SIN-D2");
        var viaje = (await JsonAsync(await CrearViajeAsync(coordinador, "MT-4513", Unidades(lugar, "VIN-SIN-D1")))).GetProperty("id").GetGuid();

        var crearSinDestino = await CrearViajeAsync(coordinador, "MT-4514", [new { vin = "VIN-SIN-D2" }]);
        var crearDestinoVacio = await CrearViajeAsync(coordinador, "MT-4515", [new { vin = "VIN-SIN-D2", lugar_destino_id = Guid.Empty }]);
        var agregarSinDestino = await Como(coordinador).PostAsync($"/api/viajes/{viaje}/vehiculos", Cuerpo(new { unidades = new[] { new { vin = "VIN-SIN-D2" } } }));
        var crearInexistente = await CrearViajeAsync(coordinador, "MT-4516", Unidades(Guid.NewGuid(), "VIN-SIN-D2"));
        var agregarDosDestinos = await Como(coordinador).PostAsync($"/api/viajes/{viaje}/vehiculos", Cuerpo(new { unidades = new[] { new { vin = "VIN-SIN-D2", lugar_destino_id = lugar }, new { vin = "vin-sin-d2", lugar_destino_id = Guid.NewGuid() } } }));

        foreach (var r in new[] { crearSinDestino, crearDestinoVacio, agregarSinDestino })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("La unidad requiere un destino", (await JsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").GetProperty("unidades")[0].GetString());
        }

        Assert.Equal(HttpStatusCode.BadRequest, crearInexistente.StatusCode);
        Assert.Contains("VIN-SIN-D2", (await JsonAsync(crearInexistente)).GetProperty("details").GetProperty("fieldErrors").GetProperty("unidades")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, agregarDosDestinos.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
    }

    [Fact]
    public async Task El_cuerpo_anterior_con_destino_unico_o_con_vines_ya_no_se_acepta()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var lugar = await LugarRealAsync(Como(coordinador), "Concesionario Autonorte", "concesionario", "Bogota");
        await Sembrador.VehiculoAsync("VIN-VIEJO-1");
        var viaje = (await JsonAsync(await CrearViajeAsync(coordinador, "MT-4517", []))).GetProperty("id").GetGuid();
        var unidad = await UnidadAsync();

        var conDestino = await Como(coordinador).PostAsync("/api/viajes", Cuerpo(new { codigo_mtrack = "MT-4518", tipo_unidad_id = unidad, placa = "ABC123", destino_id = Guid.NewGuid() }));
        var conVines = await Como(coordinador).PostAsync("/api/viajes", Cuerpo(new { codigo_mtrack = "MT-4519", tipo_unidad_id = unidad, placa = "ABC123", vines = new[] { "VIN-VIEJO-1" } }));
        var agregarVines = await Como(coordinador).PostAsync($"/api/viajes/{viaje}/vehiculos", Cuerpo(new { vines = new[] { "VIN-VIEJO-1" } }));

        Assert.Equal(HttpStatusCode.BadRequest, conDestino.StatusCode);
        Assert.Equal("Campo no admitido", (await JsonAsync(conDestino)).GetProperty("details").GetProperty("fieldErrors").GetProperty("destino_id")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, conVines.StatusCode);
        Assert.Equal("Campo no admitido", (await JsonAsync(conVines)).GetProperty("details").GetProperty("fieldErrors").GetProperty("vines")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, agregarVines.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
        Assert.Equal(lugar, (await LugarRealAsync(Como(coordinador), "Concesionario Autonorte")));
    }

    // ------------------------------------------------------------------- ESC-01 y ESC-05: cada unidad se entrega en su punto

    [Fact]
    public async Task El_descargue_de_cada_unidad_queda_en_el_punto_de_su_destino_y_cambiar_el_destino_de_una_solo_mueve_la_suya()
    {
        var e = await EscenarioAsync(conLlegadas: false);
        var concesionario = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'concesionario'"))!;
        var cedi2 = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_2'"))!;
        async Task<object?> PuntoDelDescargueAsync(string vin) =>
            await Sembrador.EscalarAsync("SELECT PuntoCustodiaId FROM dbo.tblEtapas WHERE Id = @e", ("@e", await EtapaAsync(e.Viaje, vin, "descargue")));

        // CA-2: el Descargue de las de Bogota esta en el tipo Concesionario y el de las de Cali en CEDI 2.
        Assert.Equal(concesionario, await PuntoDelDescargueAsync("VIN-ENT-B1"));
        Assert.Equal(concesionario, await PuntoDelDescargueAsync("VIN-ENT-B3"));
        Assert.Equal(cedi2, await PuntoDelDescargueAsync("VIN-ENT-C1"));
        var entregas = await JsonAsync(await Como(e.Conductor).GetAsync($"/api/viajes/{e.Viaje}/entregas"));
        Assert.Equal(["Concesionario Autonorte", "Concesionario Cali Sur"], entregas.GetProperty("puntos").EnumerateArray().Select(p => p.GetProperty("lugar").GetString()).ToList());
        Assert.Equal([3, 2], entregas.GetProperty("puntos").EnumerateArray().Select(p => p.GetProperty("unidades_total").GetInt32()).ToList());
        Assert.Equal(0, entregas.GetProperty("unidades_sin_destino").GetInt32());

        // ESC-01: «Aqui entregas estas 3 unidades»: solo las del punto; las otras no aparecen.
        var punto = await JsonAsync(await Como(e.Conductor).GetAsync($"/api/viajes/{e.Viaje}/entregas/{e.Autonorte}"));
        Assert.Equal(3, punto.GetProperty("unidades_pendientes").GetInt32());
        Assert.Equal(["VIN-ENT-B1", "VIN-ENT-B2", "VIN-ENT-B3"], punto.GetProperty("unidades").EnumerateArray().Select(u => u.GetProperty("vin").GetString()).ToList());
        Assert.All(punto.GetProperty("unidades").EnumerateArray(), u => Assert.Equal("sin_inspeccion", u.GetProperty("estado").GetString()));

        // Cambiar el destino de VIN-ENT-B2 a Cali: solo se mueve su Descargue.
        var cambio = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-ENT-B2", Cuerpo(new { lugar_destino_id = e.CaliSur }));

        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        Assert.Equal(cedi2, await PuntoDelDescargueAsync("VIN-ENT-B2"));
        Assert.Equal(concesionario, await PuntoDelDescargueAsync("VIN-ENT-B1"));
        Assert.Equal(concesionario, await PuntoDelDescargueAsync("VIN-ENT-B3"));
        Assert.Equal(cedi2, await PuntoDelDescargueAsync("VIN-ENT-C1"));
        var unidadB2 = (await JsonAsync(cambio)).GetProperty("vehiculos").EnumerateArray().Single(u => u.GetProperty("vin").GetString() == "VIN-ENT-B2");
        Assert.Equal(e.CaliSur, unidadB2.GetProperty("lugar_destino_id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.cambiar_destino_unidad'"));
        var tras = await JsonAsync(await Como(e.Conductor).GetAsync($"/api/viajes/{e.Viaje}/entregas"));
        Assert.Equal([2, 3], tras.GetProperty("puntos").EnumerateArray().Select(p => p.GetProperty("unidades_total").GetInt32()).ToList());
    }

    [Fact]
    public async Task Cambiar_el_destino_de_una_unidad_valida_el_lugar_el_rol_y_que_el_descargue_no_tenga_informe_cerrado()
    {
        var e = await EscenarioAsync();
        var inspeccionC1 = e.Llegadas["VIN-ENT-C1"];

        var sinLugar = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-ENT-B1", Cuerpo(new { }));
        var inexistente = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-ENT-B1", Cuerpo(new { lugar_destino_id = Guid.NewGuid() }));
        var ajena = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-NO-ESTA", Cuerpo(new { lugar_destino_id = e.CaliSur }));
        var comoInspector = await Como(e.Inspector).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-ENT-B1", Cuerpo(new { lugar_destino_id = e.CaliSur }));
        // El Descargue de VIN-ENT-C1 ya tiene la llegada cerrada: su destino no se cambia.
        var cerrado = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-ENT-C1", Cuerpo(new { lugar_destino_id = e.Autonorte }));

        Assert.Equal(HttpStatusCode.BadRequest, sinLugar.StatusCode);
        Assert.Equal("La unidad requiere un destino", (await JsonAsync(sinLugar)).GetProperty("details").GetProperty("fieldErrors").GetProperty("lugar_destino_id")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comoInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cerrado.StatusCode);
        Assert.NotEqual(Guid.Empty, inspeccionC1);
    }

    [Fact]
    public async Task Un_viaje_con_un_solo_destino_se_entrega_en_una_sola_agrupacion()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var cedi = await LugarRealAsync(Como(coordinador), "CEDI Envigado", "cedi_1", "Envigado");
        string[] vines = ["VIN-UNO-1", "VIN-UNO-2", "VIN-UNO-3", "VIN-UNO-4", "VIN-UNO-5"];
        foreach (var vin in vines)
        {
            await Sembrador.VehiculoAsync(vin);
        }

        var viaje = (await JsonAsync(await CrearViajeAsync(coordinador, "MT-4513", Unidades(cedi, vines), conductor))).GetProperty("id").GetGuid();

        var entregas = await JsonAsync(await Como(conductor).GetAsync($"/api/viajes/{viaje}/entregas"));
        var puntos = entregas.GetProperty("puntos");
        Assert.Equal(1, puntos.GetArrayLength());
        Assert.Equal(5, puntos[0].GetProperty("unidades_total").GetInt32());
        Assert.Equal("CEDI Envigado", puntos[0].GetProperty("lugar").GetString());
        Assert.Equal("Envigado", puntos[0].GetProperty("ciudad").GetString());
        Assert.False(puntos[0].GetProperty("entregada").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await Como(conductor).GetAsync($"/api/viajes/{viaje}/entregas/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Las_entregas_de_un_viaje_solo_las_ve_su_conductor_o_su_inspector()
    {
        var e = await EscenarioAsync();
        var otro = await SesionAsync("otro@ejemplo.com", clsRol.CONDUCTOR);

        var ajeno = await Como(otro).GetAsync($"/api/viajes/{e.Viaje}/entregas");
        var ajenoPunto = await Como(otro).GetAsync($"/api/viajes/{e.Viaje}/entregas/{e.Autonorte}");
        var suyo = await Como(e.Inspector).GetAsync($"/api/viajes/{e.Viaje}/entregas");
        var coordinador = await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}/entregas");

        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajenoPunto.StatusCode);
        Assert.Equal(HttpStatusCode.OK, suyo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, coordinador.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Como(e.Coordinador).GetAsync($"/api/viajes/{Guid.NewGuid()}/entregas")).StatusCode);
    }

    // ------------------------------------------------------------------------- ESC-02: la firma de un punto cierra cada unidad

    [Fact]
    public async Task La_firma_de_cada_parte_del_punto_cierra_la_transferencia_de_cada_unidad_con_las_mismas_firmas_fecha_e_ip_y_la_del_ultimo_punto_finaliza_el_viaje()
    {
        var e = await EscenarioAsync();

        // El conductor (quien entrega) firma el punto de Bogota: cada unidad guarda su propia transferencia con esa firma.
        var firmaConductor = await FirmarAsync(e.Conductor, e.Viaje, e.Autonorte, "firma-del-conductor");
        Assert.Equal(HttpStatusCode.OK, firmaConductor.StatusCode);
        var abierta = await JsonAsync(firmaConductor);
        Assert.Equal(3, abierta.GetProperty("unidades_pendientes").GetInt32());
        Assert.False(abierta.GetProperty("entregada").GetBoolean());
        Assert.All(abierta.GetProperty("unidades").EnumerateArray(), u =>
        {
            Assert.Equal("pendiente", u.GetProperty("estado").GetString());
            Assert.NotEqual(JsonValueKind.Null, u.GetProperty("entrega_firmada_at").ValueKind);
            Assert.Equal(JsonValueKind.Null, u.GetProperty("recibe_firmada_at").ValueKind);
        });
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre IS NULL AND FirmaEntrega = N'firma-del-conductor'"));

        // El inspector (quien recibe) firma el punto: las tres transferencias quedan firmadas.
        var firmaInspector = await FirmarAsync(e.Inspector, e.Viaje, e.Autonorte, "firma-del-inspector");
        Assert.Equal(HttpStatusCode.OK, firmaInspector.StatusCode);
        var bogota = await JsonAsync(firmaInspector);
        Assert.True(bogota.GetProperty("entregada").GetBoolean());
        Assert.Equal(0, bogota.GetProperty("unidades_pendientes").GetInt32());
        Assert.All(bogota.GetProperty("unidades").EnumerateArray(), u => Assert.Equal("firmada", u.GetProperty("tipo_cierre").GetString()));

        // CA-3: cada unidad tiene su transferencia propia con las mismas firmas, fecha e IP.
        string[] vinesBogota = ["VIN-ENT-B1", "VIN-ENT-B2", "VIN-ENT-B3"];
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(DISTINCT Id) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada' AND FirmaEntrega = N'firma-del-conductor' AND FirmaRecibe = N'firma-del-inspector'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(DISTINCT FirmaEntregaAt) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(DISTINCT FirmaRecibeAt) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(DISTINCT ISNULL(FirmaEntregaIp, '')) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada'"));
        foreach (var vin in vinesBogota)
        {
            Assert.Equal("firmada", await TransferenciaAsync(e.Llegadas[vin], "TipoCierre"));
            Assert.Equal(e.Conductor.UsuarioId, await TransferenciaAsync(e.Llegadas[vin], "EntregaUsuarioId"));
            Assert.Equal(e.Inspector.UsuarioId, await TransferenciaAsync(e.Llegadas[vin], "RecibeUsuarioId"));
        }

        // Las de Cali siguen pendientes: el viaje sigue en curso (CA-6).
        Assert.Equal("en_curso", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia t JOIN dbo.tblInspecciones i ON i.Id = t.InspeccionId JOIN dbo.tblEtapas et ON et.Id = i.EtapaId JOIN dbo.tblVehiculos v ON v.Id = et.VehiculoId WHERE v.Vin LIKE 'VIN-ENT-C%'"));

        // ESC-02: la entrega del ultimo punto completa el viaje y queda finalizado.
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, e.CaliSur)).StatusCode);
        var ultimo = await FirmarAsync(e.Conductor, e.Viaje, e.CaliSur);

        Assert.Equal(HttpStatusCode.OK, ultimo.StatusCode);
        Assert.Equal("finalizado", (await JsonAsync(ultimo)).GetProperty("viaje_estado").GetString());
        Assert.Equal("finalizado", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
        Assert.Equal(5, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'firmada'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.estado.finalizado'"));
    }

    [Fact]
    public async Task Firmar_un_punto_exige_ser_del_viaje_la_inspeccion_de_llegada_y_unidades_pendientes()
    {
        var e = await EscenarioAsync(conLlegadas: false);
        var otro = await SesionAsync("otro@ejemplo.com", clsRol.INSPECTOR);

        var sinFirma = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Autonorte}/firma", Cuerpo(new { }));
        var ajeno = await FirmarAsync(otro, e.Viaje, e.Autonorte);
        var coordinador = await FirmarAsync(e.Coordinador, e.Viaje, e.Autonorte);
        var sinLlegada = await FirmarAsync(e.Conductor, e.Viaje, e.Autonorte);
        var puntoInexistente = await FirmarAsync(e.Conductor, e.Viaje, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, sinFirma.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, coordinador.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sinLlegada.StatusCode);
        Assert.Contains("VIN-ENT-B1", (await JsonAsync(sinLlegada)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, puntoInexistente.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Un_punto_ya_entregado_no_se_firma_de_nuevo_y_un_viaje_que_no_esta_activo_no_admite_firmas()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, e.Autonorte)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, e.Autonorte)).StatusCode);

        var otraVez = await FirmarAsync(e.Conductor, e.Viaje, e.Autonorte);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/estado", Cuerpo(new { estado = "cancelado" }))).StatusCode);
        var cancelado = await FirmarAsync(e.Conductor, e.Viaje, e.CaliSur);

        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelado.StatusCode);
    }

    // ---------------------------------------------------------------- ESC-03: una sola firma no bloquea y el coordinador se entera

    [Fact]
    public async Task Cerrar_la_entrega_con_una_sola_firma_deja_cada_unidad_como_recibo_sin_entrega_con_su_novedad_y_un_solo_aviso_al_coordinador()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, e.CaliSur, "firma-del-conductor")).StatusCode);

        var r = await UnaFirmaAsync(e.Conductor, e.Viaje, e.CaliSur, "No hay inspector en el punto", firma: null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var punto = await JsonAsync(r);
        Assert.True(punto.GetProperty("entregada").GetBoolean());
        Assert.All(punto.GetProperty("unidades").EnumerateArray(), u =>
        {
            Assert.Equal("cerrada", u.GetProperty("estado").GetString());
            Assert.Equal("recibo_sin_entrega", u.GetProperty("tipo_cierre").GetString());
            Assert.Equal("recibe", u.GetProperty("no_firma_parte").GetString());
        });
        foreach (var vin in new[] { "VIN-ENT-C1", "VIN-ENT-C2" })
        {
            Assert.Equal("recibo_sin_entrega", await TransferenciaAsync(e.Llegadas[vin], "TipoCierre"));
            Assert.Equal("recibe", await TransferenciaAsync(e.Llegadas[vin], "NoFirmaParte"));
            Assert.Equal("No hay inspector en el punto", await TransferenciaAsync(e.Llegadas[vin], "MotivoNoFirma"));
            Assert.Equal("firma-del-conductor", await TransferenciaAsync(e.Llegadas[vin], "FirmaEntrega"));
            Assert.Null(await TransferenciaAsync(e.Llegadas[vin], "FirmaRecibe"));
        }

        // La novedad de proceso queda por unidad y el aviso es uno solo, con su dedupe por entrega y punto, para el coordinador de calidad.
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE Tipo = 'recibo_sin_entrega' AND MotivoTexto = N'No hay inspector en el punto' AND ViajeId = @v", ("@v", e.Viaje)));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_proceso' AND Canal = 'bandeja' AND UsuarioId = @u AND ViajeId = @v AND ClaveDedupe LIKE @k AND Titulo LIKE N'%Concesionario Cali Sur%'",
            ("@u", e.Coordinador.UsuarioId), ("@v", e.Viaje), ("@k", $"novedad_proceso:entrega:{e.Viaje}:{e.CaliSur}%")));
        Assert.Contains("No hay inspector en el punto", (string)(await Sembrador.EscalarAsync("SELECT TOP 1 Mensaje FROM dbo.tblNotificaciones WHERE UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)))!);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE UsuarioId IN (@c, @i)", ("@c", e.Conductor.UsuarioId), ("@i", e.Inspector.UsuarioId)));
        // Cerrarla de nuevo no repite nada.
        Assert.Equal(HttpStatusCode.Conflict, (await UnaFirmaAsync(e.Conductor, e.Viaje, e.CaliSur, "otra vez")).StatusCode);
    }

    [Fact]
    public async Task Quien_recibe_tambien_puede_cerrar_con_su_sola_firma_y_el_cierre_exige_motivo_y_que_la_otra_parte_no_haya_firmado()
    {
        var e = await EscenarioAsync();

        var sinMotivo = await UnaFirmaAsync(e.Inspector, e.Viaje, e.Autonorte, null);
        var motivoLargo = await UnaFirmaAsync(e.Inspector, e.Viaje, e.Autonorte, new string('x', 301));
        var sinFirmaNiPrevia = await UnaFirmaAsync(e.Inspector, e.Viaje, e.Autonorte, "El conductor no esta", firma: null);
        var ajeno = await UnaFirmaAsync(e.Coordinador, e.Viaje, e.Autonorte, "El conductor no esta");
        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, motivoLargo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinFirmaNiPrevia.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));

        // Firma solo quien recibe (nadie entrego): la parte que no firmo es quien entrega y no queda usuario de entrega.
        var r = await UnaFirmaAsync(e.Inspector, e.Viaje, e.Autonorte, "El conductor no esta en el punto", "firma-del-inspector");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        foreach (var vin in new[] { "VIN-ENT-B1", "VIN-ENT-B2", "VIN-ENT-B3" })
        {
            Assert.Equal("recibo_sin_entrega", await TransferenciaAsync(e.Llegadas[vin], "TipoCierre"));
            Assert.Equal("entrega", await TransferenciaAsync(e.Llegadas[vin], "NoFirmaParte"));
            Assert.Null(await TransferenciaAsync(e.Llegadas[vin], "EntregaUsuarioId"));
            Assert.Equal("firma-del-inspector", await TransferenciaAsync(e.Llegadas[vin], "FirmaRecibe"));
        }

        // Si la otra parte ya firmo, no se puede cerrar con una sola firma (409).
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, e.CaliSur)).StatusCode);
        var yaFirmo = await UnaFirmaAsync(e.Conductor, e.Viaje, e.CaliSur, "El inspector no firma");
        Assert.Equal(HttpStatusCode.Conflict, yaFirmo.StatusCode);
    }

    [Fact]
    public async Task La_entrega_cerrada_con_una_sola_firma_en_el_ultimo_punto_tambien_finaliza_el_viaje()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, e.Autonorte)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, e.Autonorte)).StatusCode);
        Assert.Equal("en_curso", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));

        var r = await UnaFirmaAsync(e.Conductor, e.Viaje, e.CaliSur, "No hay inspector en el punto");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("finalizado", (await JsonAsync(r)).GetProperty("viaje_estado").GetString());
        Assert.Equal("finalizado", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
    }

    [Fact]
    public async Task Un_viaje_con_una_unidad_sin_destino_de_antes_no_se_finaliza_hasta_que_se_cierre_su_descargue()
    {
        var e = await EscenarioAsync();
        var viajeEtapas = await Sembrador.VehiculoConRutaAsync("VIN-ENT-ANTES", viajeId: e.Viaje);
        Assert.Equal(3, viajeEtapas.EtapaIds.Length);

        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, e.Autonorte)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, e.Autonorte)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, e.CaliSur)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, e.CaliSur)).StatusCode);

        // La unidad de un viaje anterior al destino por unidad no tiene destino ni descargue cerrado: el viaje sigue en curso.
        Assert.Equal("en_curso", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
        var entregas = await JsonAsync(await Como(e.Conductor).GetAsync($"/api/viajes/{e.Viaje}/entregas"));
        Assert.Equal(1, entregas.GetProperty("unidades_sin_destino").GetInt32());
    }
}
