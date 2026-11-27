using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Alertas;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// «No sale» con un motivo del catalogo, «vuelve a salir» antes de firmar el cargue y «bajar del viaje»: quien puede cada cosa, lo que exige cada motivo, que un carro que no sale no se revisa ni se firma, el conductor
/// sin el nivel, y el estado <c>bajado</c> (libera el puesto, conserva la historia y no cuenta en el viaje).
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class MotivosNoSaleApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private static readonly string[] VINES = ["9FB45RFL8NM401001", "9FB45RFL8NM401002", "9FB45RFL8NM401003"];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>El viaje con tres carros activos, su inspector y su conductor, y el coordinador, un administrador y un super administrador.</summary>
    private sealed record Escenario(Guid Viaje, Guid[] Vehiculos, Guid[][] Etapas, Sesion Inspector, Sesion Conductor, Sesion Coordinador, Sesion Admin, Sesion SuperAdmin);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync(int carros = 3)
    {
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var viaje = await Sembrador.ViajeAsync(conductor.UsuarioId, inspector.UsuarioId, estado: "en_curso", codigoMtrack: "M-MOTIVOS");
        var vehiculos = new List<Guid>();
        var etapas = new List<Guid[]>();
        foreach (var vin in VINES.Take(carros))
        {
            var (vehiculo, ruta) = await Sembrador.VehiculoConRutaAsync(vin, viajeId: viaje);
            vehiculos.Add(vehiculo);
            etapas.Add(ruta);
        }

        return new Escenario(viaje, [.. vehiculos], [.. etapas], inspector, conductor, coordinador, admin, superAdmin);
    }

    private Task<HttpResponseMessage> NoSaleAsync(Sesion quien, Guid viaje, string vin, object cuerpo) =>
        Como(quien).PostAsJsonAsync($"/api/viajes/{viaje}/vehiculos/{vin}/no-sale", cuerpo);

    private Task<HttpResponseMessage> VuelveAsync(Sesion quien, Guid viaje, string vin) =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/vehiculos/{vin}/vuelve-a-salir", null);

    private Task<HttpResponseMessage> BajarAsync(Sesion quien, Guid viaje, string vin, object? cuerpo = null) =>
        Como(quien).PostAsJsonAsync($"/api/viajes/{viaje}/vehiculos/{vin}/bajar", cuerpo ?? new { motivo = "Cambio de pedido" });

    private static JsonElement Unidad(JsonElement viaje, string vin) =>
        viaje.GetProperty("vehiculos").EnumerateArray().Single(v => v.GetProperty("vin").GetString() == vin);

    private static string? Motivo(JsonElement error) => error.GetProperty("details").GetProperty("motivo").GetString();

    private async Task<string?> EstadoAsync(string vin) =>
        (string?)await Sembrador.EscalarAsync("SELECT vv.EstadoUnidad FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId WHERE v.Vin = @v", ("@v", vin));

    /// <summary>La entrega del cargue de un carro cerrada: su salida del cargue y la transferencia firmada.</summary>
    private async Task CerrarEntregaDelCargueAsync(Escenario e, int carro)
    {
        var salida = await Sembrador.InformeAsync(e.Etapas[carro][0], e.Inspector.UsuarioId, cerrada: true, momento: "salida");
        await Sembrador.TransferenciaAsync(salida, e.Inspector.UsuarioId, e.Conductor.UsuarioId, "firmada");
    }

    // ------------------------------------------------------------------------------------------------ el catalogo

    [Fact]
    public async Task El_catalogo_de_motivos_trae_los_seis_con_sus_marcas_y_se_lee_en_el_catalogo_general()
    {
        var e = await EscenarioAsync();

        var r = await Como(e.Inspector).GetAsync("/api/catalogos");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var motivos = (await JsonAsync(r)).GetProperty("catalogos").GetProperty("motivos-no-sale").EnumerateArray().ToList();
        Assert.Equal(["dano_v1_plus", "dano_reportado_conductor", "no_llego_al_patio", "faltan_documentos", "no_esta_listo", "otro"], motivos.Select(m => m.GetProperty("codigo").GetString()).ToList());
        Assert.Equal("Daño V1+", motivos[0].GetProperty("nombre").GetString());
        Assert.Equal("Carro no llegó al patio", motivos[2].GetProperty("nombre").GetString());
        Assert.Equal([true, true, false, false, false, false], motivos.Select(m => m.GetProperty("exige_hallazgo").GetBoolean()).ToList());
        Assert.Equal([false, false, false, false, false, true], motivos.Select(m => m.GetProperty("exige_texto").GetBoolean()).ToList());
        Assert.Equal([false, true, false, false, false, false], motivos.Select(m => m.GetProperty("solo_sin_inspector").GetBoolean()).ToList());
    }

    // ------------------------------------------------------------------------------------------------ no sale con motivo

    [Fact]
    public async Task Sin_ningun_dano_un_carro_no_sale_por_no_llegar_al_patio_y_deja_su_puesto_libre_y_el_conductor_ve_el_motivo()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajeVehiculos SET Posicion = 2 WHERE ViajeId = @v AND VehiculoId = @x", ("@v", e.Viaje), ("@x", e.Vehiculos[0]));

        var r = await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "no_llego_al_patio" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var unidad = Unidad(await JsonAsync(r), VINES[0]);
        Assert.Equal("no_sale", unidad.GetProperty("estado_unidad").GetString());
        var noSale = unidad.GetProperty("no_sale");
        Assert.Equal("no_llego_al_patio", noSale.GetProperty("motivo_codigo").GetString());
        Assert.Equal("Carro no llegó al patio", noSale.GetProperty("motivo").GetString());
        Assert.Equal(JsonValueKind.Null, noSale.GetProperty("motivo_texto").ValueKind);
        Assert.Equal(JsonValueKind.Null, noSale.GetProperty("hallazgo_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, unidad.GetProperty("posicion").ValueKind);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.no_sale'"));
        // El conductor del viaje lo ve en «mis viajes» con el motivo, sin ningun dato de hallazgo.
        var mios = await JsonAsync(await Como(e.Conductor).GetAsync("/api/viajes/mios"));
        var delConductor = Unidad(mios.EnumerateArray().Single(), VINES[0]).GetProperty("no_sale");
        Assert.Equal("Carro no llegó al patio", delConductor.GetProperty("motivo").GetString());
        Assert.Equal(JsonValueKind.Null, delConductor.GetProperty("hallazgo_id").ValueKind);
    }

    [Fact]
    public async Task El_motivo_se_valida_otro_exige_su_texto_y_el_texto_admite_300_caracteres()
    {
        var e = await EscenarioAsync();

        var desconocido = await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "porque_si" });
        var sinMotivo = await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { });
        var otroSinTexto = await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "otro", motivo_texto = "   " });
        var largo = await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "otro", motivo_texto = new string('x', 301) });

        Assert.Equal(HttpStatusCode.BadRequest, desconocido.StatusCode);
        Assert.Equal("Motivo desconocido", (await JsonAsync(desconocido)).GetProperty("details").GetProperty("fieldErrors").GetProperty("motivo_codigo")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otroSinTexto.StatusCode);
        Assert.Equal("Escribe el motivo", (await JsonAsync(otroSinTexto)).GetProperty("details").GetProperty("fieldErrors").GetProperty("motivo_texto")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, largo.StatusCode);
        Assert.Equal("activa", await EstadoAsync(VINES[0]));

        var conTexto = await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "otro", motivo_texto = "Llanta pinchada, sin repuesto" });
        Assert.Equal(HttpStatusCode.OK, conTexto.StatusCode);
        var noSale = Unidad(await JsonAsync(conTexto), VINES[0]).GetProperty("no_sale");
        Assert.Equal("Otro", noSale.GetProperty("motivo").GetString());
        Assert.Equal("Llanta pinchada, sin repuesto", noSale.GetProperty("motivo_texto").GetString());
    }

    [Fact]
    public async Task El_dano_reportado_por_el_conductor_no_aplica_donde_el_lugar_no_admite_cargue_sin_inspector()
    {
        var e = await EscenarioAsync();
        var hallazgo = await Sembrador.HallazgoEnFrenteAsync(await Sembrador.InformeAsync(e.Etapas[0][1], e.Inspector.UsuarioId, cerrada: false), nivel: "v1_plus");

        var r = await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "dano_reportado_conductor", hallazgo_id = hallazgo });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var error = await JsonAsync(r);
        Assert.Equal("motivo_no_aplica", Motivo(error));
        Assert.Equal("Ese motivo no aplica en este lugar de cargue", error.GetProperty("message").GetString());
        Assert.Equal("activa", await EstadoAsync(VINES[0]));
    }

    [Fact]
    public async Task Con_el_dano_v1_plus_el_conductor_lo_ve_como_dano_que_impide_viajar_y_sin_el_hallazgo_y_los_demas_con_su_motivo()
    {
        var e = await EscenarioAsync();
        var zona = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Numero = 50"))!;
        var (_, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[0][0], e.Inspector.UsuarioId, zona, nivel: "v1_plus");
        Assert.Equal(HttpStatusCode.OK, (await NoSaleAsync(e.Inspector, e.Viaje, VINES[0], new { motivo_codigo = "dano_v1_plus", hallazgo_id = hallazgo })).StatusCode);

        var delInspector = Unidad(await JsonAsync(await Como(e.Inspector).GetAsync($"/api/viajes/{e.Viaje}")), VINES[0]).GetProperty("no_sale");
        var delConductor = Unidad(await JsonAsync(await Como(e.Conductor).GetAsync($"/api/viajes/{e.Viaje}")), VINES[0]).GetProperty("no_sale");
        var mios = Unidad((await JsonAsync(await Como(e.Conductor).GetAsync("/api/viajes/mios"))).EnumerateArray().Single(), VINES[0]).GetProperty("no_sale");

        Assert.Equal("Daño V1+", delInspector.GetProperty("motivo").GetString());
        Assert.Equal(hallazgo, delInspector.GetProperty("hallazgo_id").GetGuid());
        foreach (var visto in new[] { delConductor, mios })
        {
            Assert.Equal("Daño que impide viajar", visto.GetProperty("motivo").GetString());
            Assert.Equal(JsonValueKind.Null, visto.GetProperty("hallazgo_id").ValueKind);
        }
    }

    [Fact]
    public async Task Solo_el_inspector_del_viaje_y_el_coordinador_marcan_y_vuelven_a_salir_y_solo_el_coordinador_baja()
    {
        var e = await EscenarioAsync();
        var otroInspector = await SesionAsync("otro-inspector@ejemplo.com", clsRol.INSPECTOR);
        var cuerpo = new { motivo_codigo = "faltan_documentos" };

        foreach (var sin in new[] { e.Admin, e.SuperAdmin, e.Conductor, otroInspector })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await NoSaleAsync(sin, e.Viaje, VINES[0], cuerpo)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await VuelveAsync(sin, e.Viaje, VINES[0])).StatusCode);
        }

        foreach (var sin in new[] { e.Admin, e.SuperAdmin, e.Conductor, e.Inspector })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await BajarAsync(sin, e.Viaje, VINES[0])).StatusCode);
        }

        Assert.Equal("activa", await EstadoAsync(VINES[0]));
        Assert.Equal(HttpStatusCode.OK, (await NoSaleAsync(e.Inspector, e.Viaje, VINES[0], cuerpo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VuelveAsync(e.Coordinador, e.Viaje, VINES[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BajarAsync(e.Coordinador, e.Viaje, VINES[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[1]}/vuelve-a-salir", null)).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------ un carro que no sale no se revisa ni se firma

    [Fact]
    public async Task Un_carro_que_no_sale_no_admite_inspecciones_ni_firmas_hasta_que_vuelve_a_salir()
    {
        var e = await EscenarioAsync();
        var salida = await Sembrador.InformeAsync(e.Etapas[0][0], e.Inspector.UsuarioId, cerrada: false, momento: "llegada");
        Assert.Equal(HttpStatusCode.OK, (await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "no_esta_listo" })).StatusCode);

        var crear = await Como(e.Inspector).PostAsJsonAsync("/api/inspecciones", new { etapa_id = e.Etapas[0][0], momento = "salida", fecha = "2026-11-18T14:09:00-05:00", lat = 4.6, lng = -74.1 });
        var cerrar = await Como(e.Inspector).PostAsync($"/api/inspecciones/{salida}/cerrar", null);
        var firmar = await Como(e.Inspector).PostAsJsonAsync($"/api/inspecciones/{salida}/transferencia", new { entrega_usuario_id = e.Inspector.UsuarioId, recibe_usuario_id = e.Conductor.UsuarioId, firma = "A" });
        var reservas = await Como(e.Conductor).PostAsJsonAsync($"/api/inspecciones/{salida}/transferencia/reservas", new { firma = "B", reservas = "Rayón" });

        foreach (var r in new[] { crear, cerrar, firmar, reservas })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("unidad_no_sale", Motivo(error));
            Assert.Equal("La unidad no sale de este viaje", error.GetProperty("message").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, (await VuelveAsync(e.Coordinador, e.Viaje, VINES[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Como(e.Inspector).PostAsJsonAsync("/api/inspecciones", new { etapa_id = e.Etapas[0][0], momento = "salida", fecha = "2026-11-18T14:09:00-05:00", lat = 4.6, lng = -74.1 })).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------ vuelve a salir

    [Fact]
    public async Task Vuelve_a_salir_deja_el_carro_activo_sin_posicion_borra_la_marca_y_conserva_el_historial_en_la_auditoria()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await NoSaleAsync(e.Inspector, e.Viaje, VINES[0], new { motivo_codigo = "no_esta_listo" })).StatusCode);

        var r = await VuelveAsync(e.Inspector, e.Viaje, VINES[0]);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var unidad = Unidad(await JsonAsync(r), VINES[0]);
        Assert.Equal("activa", unidad.GetProperty("estado_unidad").GetString());
        Assert.Equal(JsonValueKind.Null, unidad.GetProperty("no_sale").ValueKind);
        Assert.Equal(JsonValueKind.Null, unidad.GetProperty("posicion").ValueKind);
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE VehiculoId = @x AND EstadoUnidad = 'activa' AND NoSaleMotivoCodigo IS NULL AND NoSaleMotivo IS NULL AND NoSaleMarcadoPor IS NULL AND NoSaleAt IS NULL", ("@x", e.Vehiculos[0])));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.vuelve_a_salir' AND JSON_VALUE(Detalle, '$.antes.motivo_codigo') = 'no_esta_listo' AND JSON_VALUE(Detalle, '$.antes.estado_unidad') = 'no_sale'"));
        // No es un «no sale»: no se vuelve a salir dos veces.
        Assert.Equal(HttpStatusCode.Conflict, (await VuelveAsync(e.Inspector, e.Viaje, VINES[0])).StatusCode);
    }

    [Fact]
    public async Task Con_la_entrega_del_cargue_cerrada_el_carro_no_vuelve_a_salir()
    {
        var e = await EscenarioAsync(carros: 2);
        Assert.Equal(HttpStatusCode.OK, (await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "no_esta_listo" })).StatusCode);
        // El unico carro activo ya cerro su entrega del cargue: la entrega del viaje esta cerrada.
        await CerrarEntregaDelCargueAsync(e, 1);

        var r = await VuelveAsync(e.Coordinador, e.Viaje, VINES[0]);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var error = await JsonAsync(r);
        Assert.Equal("cargue_firmado", Motivo(error));
        Assert.Equal("La entrega del cargue ya está cerrada: el carro va en otro viaje", error.GetProperty("message").GetString());
        Assert.Equal("no_sale", await EstadoAsync(VINES[0]));
    }

    [Fact]
    public async Task Si_la_ninera_esta_llena_el_carro_no_vuelve_a_salir_y_si_ya_tiene_reemplazo_tampoco()
    {
        var e = await EscenarioAsync(carros: 2);
        Assert.Equal(HttpStatusCode.OK, (await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "no_esta_listo" })).StatusCode);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Capacidad = 1 WHERE Id = @v", ("@v", e.Viaje));

        var llena = await VuelveAsync(e.Coordinador, e.Viaje, VINES[0]);

        Assert.Equal(HttpStatusCode.Conflict, llena.StatusCode);
        var error = await JsonAsync(llena);
        Assert.Equal("sin_cupo", Motivo(error));
        Assert.Equal("La niñera tiene 1 puestos y ya hay 1 unidades", error.GetProperty("message").GetString());
        Assert.Equal("no_sale", await EstadoAsync(VINES[0]));

        // Con reemplazo ya agregado: otro viaje, con la niñera en su capacidad normal.
        var (e2, vinConReemplazo) = await EscenarioConReemplazoAsync();
        var conReemplazo = await VuelveAsync(e2.Coordinador, e2.Viaje, vinConReemplazo);
        Assert.Equal(HttpStatusCode.Conflict, conReemplazo.StatusCode);
        var errorReemplazo = await JsonAsync(conReemplazo);
        Assert.Equal("tiene_reemplazo", Motivo(errorReemplazo));
        Assert.Equal("El carro ya tiene reemplazo: quita primero el reemplazo", errorReemplazo.GetProperty("message").GetString());
    }

    private async Task<(Escenario Escenario, string Vin)> EscenarioConReemplazoAsync()
    {
        // Un segundo viaje con su propio inspector y conductor: el reemplazo es un VIN suelto que entra al viaje al marcar «no sale».
        var inspector = await SesionAsync("inspector-b@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor-b@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord-b@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var viaje = await Sembrador.ViajeAsync(conductor.UsuarioId, inspector.UsuarioId, estado: "en_curso", codigoMtrack: "M-REEMPLAZO");
        var (vehiculo, ruta) = await Sembrador.VehiculoConRutaAsync("9FB45RFL8NM409001", viajeId: viaje);
        await Sembrador.VehiculoAsync("9FB45RFL8NM409002");
        Assert.Equal(HttpStatusCode.OK, (await NoSaleAsync(coordinador, viaje, "9FB45RFL8NM409001", new { motivo_codigo = "no_llego_al_patio", vin_reemplazo = "9FB45RFL8NM409002" })).StatusCode);
        return (new Escenario(viaje, [vehiculo], [ruta], inspector, conductor, coordinador, coordinador, coordinador), "9FB45RFL8NM409001");
    }

    // ------------------------------------------------------------------------------------------------ bajar del viaje

    [Fact]
    public async Task Bajar_un_carro_libera_su_puesto_conserva_su_historia_y_ya_no_cuenta_en_el_viaje()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajeVehiculos SET Posicion = 4 WHERE ViajeId = @v AND VehiculoId = @x", ("@v", e.Viaje), ("@x", e.Vehiculos[0]));
        var revision = await Sembrador.InformeAsync(e.Etapas[0][0], e.Inspector.UsuarioId, cerrada: true, momento: "llegada");

        var r = await BajarAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo = "  Cambio de pedido  " });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var viaje = await JsonAsync(r);
        var unidad = Unidad(viaje, VINES[0]);
        Assert.Equal("bajado", unidad.GetProperty("estado_unidad").GetString());
        Assert.Equal(JsonValueKind.Null, unidad.GetProperty("posicion").ValueKind);
        var bajado = unidad.GetProperty("bajado");
        Assert.Equal("Cambio de pedido", bajado.GetProperty("motivo").GetString());
        Assert.Equal(e.Coordinador.UsuarioId, bajado.GetProperty("bajado_por").GetGuid());
        Assert.Equal("Nombre coord@ejemplo.com", bajado.GetProperty("bajado_por_nombre").GetString());
        // Su historia sigue: etapas, revisiones.
        Assert.Equal(3, unidad.GetProperty("etapas").GetInt32());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Id = @i AND Estado = 'cerrada'", ("@i", revision)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.bajar_vin' AND JSON_VALUE(Detalle, '$.despues.estado_unidad') = 'bajado'"));
        // No cuenta: ni en los totales del viaje, ni en la cobertura, ni en «mis viajes» del conductor y del inspector.
        var lista = (await JsonAsync(await Como(e.Coordinador).GetAsync("/api/viajes"))).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(2, lista.GetProperty("total_vehiculos").GetInt32());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.vwCobertura WHERE VehiculoId = @x", ("@x", e.Vehiculos[0])));
        foreach (var quien in new[] { e.Conductor, e.Inspector })
        {
            var mios = (await JsonAsync(await Como(quien).GetAsync("/api/viajes/mios"))).EnumerateArray().Single();
            Assert.DoesNotContain(mios.GetProperty("vehiculos").EnumerateArray(), v => v.GetProperty("vin").GetString() == VINES[0]);
        }

        // Un carro bajado no se vuelve a bajar y deja de admitir revisiones.
        Assert.Equal(HttpStatusCode.Conflict, (await BajarAsync(e.Coordinador, e.Viaje, VINES[0])).StatusCode);
        var crear = await Como(e.Inspector).PostAsJsonAsync("/api/inspecciones", new { etapa_id = e.Etapas[0][0], momento = "salida", fecha = "2026-11-18T14:09:00-05:00", lat = 4.6, lng = -74.1 });
        Assert.Equal(HttpStatusCode.Conflict, crear.StatusCode);
        Assert.Equal("unidad_bajada", Motivo(await JsonAsync(crear)));
    }

    [Fact]
    public async Task El_vin_bajado_se_agrega_a_otro_viaje_abierto_y_el_motivo_es_obligatorio_y_de_hasta_300_caracteres()
    {
        var e = await EscenarioAsync();
        var sinMotivo = await BajarAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo = "  " });
        var largo = await BajarAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo = new string('m', 301) });
        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, largo.StatusCode);
        Assert.Equal("activa", await EstadoAsync(VINES[0]));

        Assert.Equal(HttpStatusCode.OK, (await BajarAsync(e.Coordinador, e.Viaje, VINES[0])).StatusCode);
        var otro = await Sembrador.ViajeAsync(e.Conductor.UsuarioId, e.Inspector.UsuarioId, codigoMtrack: "M-OTRO-PEDIDO");
        var lugar = await LugarRealAsync(Como(e.Admin));
        var agregar = await Como(e.Coordinador).PostAsJsonAsync($"/api/viajes/{otro}/vehiculos", new { unidades = new[] { new { vin = VINES[0], lugar_destino_id = lugar } } });

        Assert.Equal(HttpStatusCode.OK, agregar.StatusCode);
        Assert.Contains((await JsonAsync(agregar)).GetProperty("vehiculos").EnumerateArray(), v => v.GetProperty("vin").GetString() == VINES[0]);
    }

    [Fact]
    public async Task Con_la_entrega_del_cargue_cerrada_no_se_baja_ningun_carro_y_un_carro_que_no_sale_si_se_puede_bajar()
    {
        var e = await EscenarioAsync(carros: 2);
        Assert.Equal(HttpStatusCode.OK, (await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "faltan_documentos" })).StatusCode);
        // Un «no sale» se baja, con o sin revisiones, mientras el cargue no se cierre; una novedad o un «Llegue» no cuentan.
        Assert.Equal(HttpStatusCode.OK, (await BajarAsync(e.Coordinador, e.Viaje, VINES[0])).StatusCode);
        await Sembrador.InformeAsync(e.Etapas[1][1], e.Conductor.UsuarioId, cerrada: true, momento: "llegada");

        await CerrarEntregaDelCargueAsync(e, 1);
        var r = await BajarAsync(e.Coordinador, e.Viaje, VINES[1]);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var error = await JsonAsync(r);
        Assert.Equal("cargue_firmado", Motivo(error));
        Assert.Equal("La entrega del cargue ya está cerrada: el camión ya salió y no se puede bajar ningún carro", error.GetProperty("message").GetString());
        Assert.Equal("activa", await EstadoAsync(VINES[1]));
    }

    [Fact]
    public async Task Un_carro_no_sale_ni_bajado_no_genera_avisos_y_el_bajado_no_cuenta_para_la_capacidad()
    {
        var e = await EscenarioAsync(carros: 2);
        // El primero no sale, el segundo se baja: la salida del cargue de los dos lleva 40 minutos cerrada y sin firma.
        foreach (var carro in new[] { 0, 1 })
        {
            var salida = await Sembrador.InformeAsync(e.Etapas[carro][0], e.Inspector.UsuarioId, cerrada: true, momento: "salida");
            await Sembrador.EjecutarAsync(
                "ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL; UPDATE dbo.tblInspecciones SET CerradaAt = DATEADD(MINUTE, -40, SYSDATETIMEOFFSET()) WHERE Id = @i; ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL", ("@i", salida));
        }

        Assert.Equal(HttpStatusCode.OK, (await NoSaleAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo_codigo = "no_esta_listo" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BajarAsync(e.Coordinador, e.Viaje, VINES[1])).StatusCode);

        using var alcance = Fabrica.Services.CreateScope();
        Assert.Equal(0, await alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>().EjecutarAvisosAsync());
        // La capacidad solo cuenta las unidades activas: con 1 puesto y el tercer carro como unica unidad activa, se puede dejar la capacidad en 1.
        var tercero = await Sembrador.VehiculoConRutaAsync(VINES[2], viajeId: e.Viaje);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = @v AND EstadoUnidad = 'activa'", ("@v", e.Viaje)));
        var capacidad = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}", new StringContent(JsonSerializer.Serialize(new { capacidad = 1 }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, capacidad.StatusCode);
        Assert.NotEqual(Guid.Empty, tercero.VehiculoId);
    }

    [Fact]
    public async Task El_inventario_de_un_carro_bajado_trae_el_motivo_quien_lo_bajo_y_cuando_igual_que_el_viaje_y_los_demas_no_lo_traen()
    {
        var e = await EscenarioAsync();
        var bajar = await BajarAsync(e.Coordinador, e.Viaje, VINES[0], new { motivo = "Cambio de pedido" });
        Assert.Equal(HttpStatusCode.OK, bajar.StatusCode);
        var delViaje = Unidad(await JsonAsync(bajar), VINES[0]).GetProperty("bajado");

        var inventario = await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/vehiculos/{VINES[0]}/inventario"));

        var etapas = inventario.GetProperty("etapas").EnumerateArray().ToList();
        Assert.NotEmpty(etapas);
        Assert.All(etapas, etapa =>
        {
            Assert.Equal("bajado", etapa.GetProperty("estado_unidad").GetString());
            var bajado = etapa.GetProperty("bajado");
            Assert.Equal("Cambio de pedido", bajado.GetProperty("motivo").GetString());
            Assert.Equal(e.Coordinador.UsuarioId, bajado.GetProperty("bajado_por").GetGuid());
            Assert.Equal("Nombre coord@ejemplo.com", bajado.GetProperty("bajado_por_nombre").GetString());
            Assert.Equal(["motivo", "bajado_por", "bajado_por_nombre", "fecha"], bajado.EnumerateObject().Select(p => p.Name).ToList());
            Assert.Equal(delViaje.GetProperty("fecha").GetDateTimeOffset(), bajado.GetProperty("fecha").GetDateTimeOffset());
        });

        // Un carro que sigue en el viaje lleva el dato vacio, y el de «no sale» conserva el suyo.
        var activo = await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/vehiculos/{VINES[1]}/inventario"));
        Assert.All(activo.GetProperty("etapas").EnumerateArray(), etapa => Assert.Equal(JsonValueKind.Null, etapa.GetProperty("bajado").ValueKind));
    }
}
