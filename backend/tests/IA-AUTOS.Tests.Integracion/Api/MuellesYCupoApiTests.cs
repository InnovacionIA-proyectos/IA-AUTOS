using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El viaje refleja el patio: el catalogo de muelles por lugar y el muelle del viaje (obligatorio para cerrar la acomodacion donde el lugar tiene muelles), el muelle y el trailer que cambia el inspector durante el cargue con su
/// aviso, que no haya mas carros que puestos en ninguna puerta, el aviso y la marca del cambio de destino de una unidad, y si el viaje se puede eliminar.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class MuellesYCupoApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private static readonly string[] VINES = ["9FB45RFL8NM501001", "9FB45RFL8NM501002", "9FB45RFL8NM501003"];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private sealed record Escenario(Guid Viaje, Guid[] Vehiculos, Guid[][] Etapas, Guid LugarCargue, Guid OtroLugar, Sesion Inspector, Sesion Conductor, Sesion Coordinador, Sesion Admin, Sesion SuperAdmin);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private async Task<Guid> LugarAsync(Sesion admin, string nombre, string tipo = "cedi_1") => await LugarRealAsync(Como(admin), nombre, tipo, "Envigado");

    private async Task<Guid> MuelleAsync(Sesion quien, Guid lugar, string codigo, string nombre)
    {
        var r = await Como(quien).PostAsync("/api/catalogos/muelles", Cuerpo(new { lugar_id = lugar, codigo, nombre }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonAsync(r)).GetProperty("id").GetGuid();
    }

    /// <summary>El viaje en curso con dos carros, cargando en «CEDI Envigado» (con muelles 1 a 3 si <paramref name="conMuelles"/>), con su inspector, su conductor y el coordinador.</summary>
    private async Task<(Escenario Escenario, Guid[] Muelles)> EscenarioAsync(bool conMuelles = true, int carros = 2)
    {
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var lugar = await LugarAsync(admin, "CEDI Envigado");
        var otro = await LugarAsync(admin, "Patio de Prueba");
        var muelles = conMuelles ? new[] { await MuelleAsync(admin, lugar, "m1", "Muelle 1"), await MuelleAsync(admin, lugar, "m2", "Muelle 2"), await MuelleAsync(admin, lugar, "m3", "Muelle 3") } : [];
        var viaje = await Sembrador.ViajeAsync(conductor.UsuarioId, inspector.UsuarioId, estado: "en_curso", codigoMtrack: "M-MUELLE");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET LugarCargueId = @l, Trailer = N'TRX-101' WHERE Id = @v", ("@l", lugar), ("@v", viaje));
        var vehiculos = new List<Guid>();
        var etapas = new List<Guid[]>();
        foreach (var vin in VINES.Take(carros))
        {
            var (vehiculo, ruta) = await Sembrador.VehiculoConRutaAsync(vin, viajeId: viaje);
            vehiculos.Add(vehiculo);
            etapas.Add(ruta);
        }

        return (new Escenario(viaje, [.. vehiculos], [.. etapas], lugar, otro, inspector, conductor, coordinador, admin, superAdmin), muelles);
    }

    private Task<HttpResponseMessage> CargueAsync(Sesion quien, Guid viaje, object cuerpo) => Como(quien).PatchAsync($"/api/viajes/{viaje}/cargue", Cuerpo(cuerpo));

    private async Task<JsonElement> ViajeAsync(Sesion quien, Guid viaje) => await JsonAsync(await Como(quien).GetAsync($"/api/viajes/{viaje}"));

    private Task<int> AvisosAsync(string tipo, Guid usuario, string canal = "bandeja") =>
        Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = @t AND Canal = @c AND UsuarioId = @u", ("@t", tipo), ("@c", canal), ("@u", usuario));

    // ------------------------------------------------------------------------------------------------ el catalogo de muelles

    [Fact]
    public async Task Los_muelles_son_hijos_de_un_lugar_con_codigo_unico_dentro_del_lugar_y_se_leen_en_el_catalogo_general()
    {
        var (e, muelles) = await EscenarioAsync();

        var lista = await Como(e.Inspector).GetAsync("/api/catalogos");

        var catalogo = (await JsonAsync(lista)).GetProperty("catalogos").GetProperty("muelles").EnumerateArray().ToList();
        Assert.Equal(["Muelle 1", "Muelle 2", "Muelle 3"], catalogo.Select(m => m.GetProperty("nombre").GetString()).ToList());
        Assert.All(catalogo, m => Assert.Equal(e.LugarCargue, m.GetProperty("lugar_id").GetGuid()));
        Assert.Equal(["id", "codigo", "nombre", "orden", "activo", "lugar_id"], catalogo[0].EnumerateObject().Select(p => p.Name).ToList());
        // El mismo codigo en otro lugar si; en el mismo lugar no.
        Assert.Equal(HttpStatusCode.Created, (await Como(e.Admin).PostAsync("/api/catalogos/muelles", Cuerpo(new { lugar_id = e.OtroLugar, codigo = "m1", nombre = "Muelle 1" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Como(e.Admin).PostAsync("/api/catalogos/muelles", Cuerpo(new { lugar_id = e.LugarCargue, codigo = "m1", nombre = "Otro nombre" }))).StatusCode);
        // El lugar es obligatorio y debe existir; no se cambia despues.
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(e.Admin).PostAsync("/api/catalogos/muelles", Cuerpo(new { codigo = "m9", nombre = "Muelle 9" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(e.Admin).PostAsync("/api/catalogos/muelles", Cuerpo(new { lugar_id = Guid.NewGuid(), codigo = "m9", nombre = "Muelle 9" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(e.Admin).PatchAsync($"/api/catalogos/muelles/{muelles[0]}", Cuerpo(new { lugar_id = e.OtroLugar }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Admin).PatchAsync($"/api/catalogos/muelles/{muelles[0]}", Cuerpo(new { nombre = "Muelle uno" }))).StatusCode);
        // Los roles de los lugares administran los muelles; el inspector solo los lee.
        Assert.Equal(HttpStatusCode.Created, (await Como(e.Coordinador).PostAsync("/api/catalogos/muelles", Cuerpo(new { lugar_id = e.LugarCargue, codigo = "m4", nombre = "Muelle 4" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Como(e.SuperAdmin).PostAsync("/api/catalogos/muelles", Cuerpo(new { lugar_id = e.LugarCargue, codigo = "m5", nombre = "Muelle 5" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Inspector).PostAsync("/api/catalogos/muelles", Cuerpo(new { lugar_id = e.LugarCargue, codigo = "m6", nombre = "Muelle 6" }))).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------ el muelle y el trailer durante el cargue

    [Fact]
    public async Task El_inspector_asigna_el_muelle_y_cambia_el_trailer_y_el_coordinador_recibe_un_aviso_de_cada_cosa_solo_en_la_bandeja()
    {
        var (e, muelles) = await EscenarioAsync();

        var muelle = await CargueAsync(e.Inspector, e.Viaje, new { muelle_id = muelles[2] });
        var trailer = await CargueAsync(e.Inspector, e.Viaje, new { trailer = "TRX-202" });

        Assert.Equal(HttpStatusCode.OK, muelle.StatusCode);
        Assert.Equal(HttpStatusCode.OK, trailer.StatusCode);
        var viaje = await ViajeAsync(e.Coordinador, e.Viaje);
        Assert.Equal(muelles[2], viaje.GetProperty("muelle_id").GetGuid());
        Assert.Equal("Muelle 3", viaje.GetProperty("muelle").GetString());
        Assert.Equal("TRX-202", viaje.GetProperty("trailer").GetString());
        Assert.Equal(2, await AvisosAsync("cambio_cargue_inspector", e.Coordinador.UsuarioId));
        Assert.Equal(0, await AvisosAsync("cambio_cargue_inspector", e.Coordinador.UsuarioId, "push"));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'cambio_cargue_inspector' AND Urgencia = 'media' AND Mensaje LIKE N'%asignó el muelle 3 al viaje M-MUELLE' AND JSON_VALUE(Datos, '$.campo') = 'muelle' AND JSON_VALUE(Datos, '$.despues') = 'Muelle 3'"));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'cambio_cargue_inspector' AND Mensaje LIKE N'%cambió el tráiler del viaje M-MUELLE de TRX-101 a TRX-202' AND JSON_VALUE(Datos, '$.antes') = 'TRX-101' AND JSON_VALUE(Datos, '$.usuario_nombre') = N'Nombre inspector@ejemplo.com'"));

        // Repetir lo mismo no cambia nada ni avisa otra vez.
        Assert.Equal(HttpStatusCode.OK, (await CargueAsync(e.Inspector, e.Viaje, new { muelle_id = muelles[2], trailer = "TRX-202" })).StatusCode);
        Assert.Equal(2, await AvisosAsync("cambio_cargue_inspector", e.Coordinador.UsuarioId));
    }

    [Fact]
    public async Task Los_cambios_del_coordinador_no_avisan_y_conductor_admin_y_super_admin_no_tienen_la_ruta()
    {
        var (e, muelles) = await EscenarioAsync();

        Assert.Equal(HttpStatusCode.OK, (await CargueAsync(e.Coordinador, e.Viaje, new { muelle_id = muelles[0], trailer = "TRX-300" })).StatusCode);
        Assert.Equal(0, await AvisosAsync("cambio_cargue_inspector", e.Coordinador.UsuarioId));
        foreach (var sin in new[] { e.Conductor, e.Admin, e.SuperAdmin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await CargueAsync(sin, e.Viaje, new { trailer = "X" })).StatusCode);
        }

        var ajeno = await SesionAsync("ajeno@ejemplo.com", clsRol.INSPECTOR);
        Assert.Equal(HttpStatusCode.Forbidden, (await CargueAsync(ajeno, e.Viaje, new { trailer = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PatchAsync($"/api/viajes/{e.Viaje}/cargue", Cuerpo(new { trailer = "X" }))).StatusCode);
        // TI conserva el PATCH del viaje de siempre.
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Admin).PatchAsync($"/api/viajes/{e.Viaje}", Cuerpo(new { trailer = "TRX-TI" }))).StatusCode);
    }

    [Fact]
    public async Task El_muelle_debe_ser_activo_y_del_lugar_de_cargue_del_viaje_y_se_puede_quitar()
    {
        var (e, muelles) = await EscenarioAsync();
        var deOtroLugar = await MuelleAsync(e.Admin, e.OtroLugar, "p1", "Muelle del patio");
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Admin).PatchAsync($"/api/catalogos/muelles/{muelles[1]}", Cuerpo(new { activo = false }))).StatusCode);

        var ajeno = await CargueAsync(e.Inspector, e.Viaje, new { muelle_id = deOtroLugar });
        var inactivo = await CargueAsync(e.Inspector, e.Viaje, new { muelle_id = muelles[1] });
        var inexistente = await CargueAsync(e.Inspector, e.Viaje, new { muelle_id = Guid.NewGuid() });
        var vacio = await CargueAsync(e.Inspector, e.Viaje, new { });
        var desconocido = await CargueAsync(e.Inspector, e.Viaje, new { placa = "ABC123" });

        Assert.Equal(HttpStatusCode.BadRequest, ajeno.StatusCode);
        Assert.Equal("El muelle no pertenece al lugar de cargue del viaje", (await JsonAsync(ajeno)).GetProperty("details").GetProperty("fieldErrors").GetProperty("muelle_id")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, inactivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, desconocido.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CargueAsync(e.Inspector, e.Viaje, new { muelle_id = muelles[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CargueAsync(e.Inspector, e.Viaje, new { muelle_id = (Guid?)null })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("muelle_id").ValueKind);
    }

    [Fact]
    public async Task Con_la_entrega_del_cargue_cerrada_el_inspector_ya_no_cambia_el_trailer_ni_el_muelle_pero_el_coordinador_si()
    {
        var (e, muelles) = await EscenarioAsync(carros: 1);
        var salida = await Sembrador.InformeAsync(e.Etapas[0][0], e.Inspector.UsuarioId, cerrada: true, momento: "salida");
        await Sembrador.TransferenciaAsync(salida, e.Inspector.UsuarioId, e.Conductor.UsuarioId, "firmada");

        var inspector = await CargueAsync(e.Inspector, e.Viaje, new { trailer = "TRX-202" });
        var coordinador = await CargueAsync(e.Coordinador, e.Viaje, new { muelle_id = muelles[0], trailer = "TRX-202" });

        Assert.Equal(HttpStatusCode.Conflict, inspector.StatusCode);
        var error = await JsonAsync(inspector);
        Assert.Equal("cargue_firmado", error.GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal("El tráiler y el muelle solo los cambia el inspector durante el cargue", error.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, coordinador.StatusCode);
        Assert.Equal("TRX-202", (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("trailer").GetString());
    }

    // ------------------------------------------------------------------------------------------------ el muelle y el cierre de la acomodacion

    private async Task<Guid> SalidaDelCargueAsync(Escenario e)
    {
        var r = await Como(e.Inspector).PostAsJsonAsync("/api/inspecciones", new { etapa_id = e.Etapas[0][0], momento = "salida", fecha = "2026-11-18T14:09:00-05:00", lat = 6.2, lng = -75.5 });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonAsync(r)).GetProperty("id").GetGuid();
    }

    private static List<string?> Detalles(JsonElement cuerpo) => cuerpo.GetProperty("details").EnumerateArray().Select(d => d.GetString()).ToList();

    [Fact]
    public async Task Donde_el_lugar_tiene_muelles_la_acomodacion_no_cierra_sin_el_muelle_del_viaje_y_donde_no_los_tiene_no_se_pide()
    {
        var (e, muelles) = await EscenarioAsync();
        var inspeccion = await SalidaDelCargueAsync(e);

        var sinMuelle = await Como(e.Inspector).PostAsync($"/api/inspecciones/{inspeccion}/cerrar", null);

        Assert.Equal(HttpStatusCode.BadRequest, sinMuelle.StatusCode);
        Assert.Contains("Falta el muelle del viaje", Detalles(await JsonAsync(sinMuelle)));
        Assert.Equal(HttpStatusCode.OK, (await CargueAsync(e.Inspector, e.Viaje, new { muelle_id = muelles[2] })).StatusCode);
        var conMuelle = await Como(e.Inspector).PostAsync($"/api/inspecciones/{inspeccion}/cerrar", null);
        Assert.DoesNotContain("Falta el muelle del viaje", Detalles(await JsonAsync(conMuelle)));

        // En un lugar sin muelles no se exige.
        await Sembrador.EjecutarAsync("DELETE FROM dbo.tblInspecciones; UPDATE dbo.tblViajes SET MuelleId = NULL, LugarCargueId = @otro WHERE Id = @v", ("@otro", e.OtroLugar), ("@v", e.Viaje));
        var enOtroLugar = await SalidaDelCargueAsync(e);
        var sinMuelles = await Como(e.Inspector).PostAsync($"/api/inspecciones/{enOtroLugar}/cerrar", null);
        Assert.DoesNotContain("Falta el muelle del viaje", Detalles(await JsonAsync(sinMuelles)));
    }

    // ------------------------------------------------------------------------------------------------ no hay mas carros que puestos

    [Fact]
    public async Task El_carro_de_mas_se_rechaza_con_sin_cupo_y_uno_que_no_sale_o_bajado_no_cuenta()
    {
        var (e, _) = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Capacidad = 2 WHERE Id = @v", ("@v", e.Viaje));
        await Sembrador.VehiculoAsync(VINES[2]);
        await Sembrador.VehiculoAsync("9FB45RFL8NM501004");
        var lugar = e.OtroLugar;

        var lleno = await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/vehiculos", Cuerpo(new { unidades = new[] { new { vin = VINES[2], lugar_destino_id = lugar } } }));

        Assert.Equal(HttpStatusCode.Conflict, lleno.StatusCode);
        var error = await JsonAsync(lleno);
        Assert.Equal("sin_cupo", error.GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal($"La niñera tiene 2 puestos y ya hay 2 unidades: no cabe el VIN {VINES[2]}", error.GetProperty("message").GetString());
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = @v", ("@v", e.Viaje)));

        // Una unidad que no sale libera su cupo: entra uno, no dos.
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[0]}/no-sale", Cuerpo(new { motivo_codigo = "no_esta_listo" }))).StatusCode);
        var dos = await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/vehiculos", Cuerpo(new { unidades = new[] { new { vin = VINES[2], lugar_destino_id = lugar }, new { vin = "9FB45RFL8NM501004", lugar_destino_id = lugar } } }));
        Assert.Equal(HttpStatusCode.Conflict, dos.StatusCode);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = @v", ("@v", e.Viaje)));
        var uno = await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/vehiculos", Cuerpo(new { unidades = new[] { new { vin = VINES[2], lugar_destino_id = lugar } } }));
        Assert.Equal(HttpStatusCode.OK, uno.StatusCode);
        // Y uno bajado tampoco cuenta.
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[1]}/bajar", Cuerpo(new { motivo = "Cambio de pedido" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/vehiculos", Cuerpo(new { unidades = new[] { new { vin = "9FB45RFL8NM501004", lugar_destino_id = lugar } } }))).StatusCode);
    }

    [Fact]
    public async Task Crear_un_viaje_con_mas_unidades_que_puestos_se_rechaza_y_no_deja_nada_y_el_reemplazo_cabe_porque_la_unidad_que_sale_no_cuenta()
    {
        var (e, _) = await EscenarioAsync();
        await Sembrador.VehiculoAsync("9FB45RFL8NM502001");
        await Sembrador.VehiculoAsync("9FB45RFL8NM502002");
        var tipo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;
        var conductor = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblUsuarios WHERE Email = 'conductor@ejemplo.com'"))!;

        var creado = await Como(e.Coordinador).PostAsync("/api/viajes", Cuerpo(new
        {
            codigo_mtrack = "M-CUPO", tipo_unidad_id = tipo, placa = "CUP123", conductor_id = conductor, fecha_programada = "2026-11-25T06:00:00-05:00", lugar_cargue_id = e.LugarCargue, capacidad = 1,
            unidades = new[] { new { vin = "9FB45RFL8NM502001", lugar_destino_id = e.OtroLugar }, new { vin = "9FB45RFL8NM502002", lugar_destino_id = e.OtroLugar } },
        }));

        Assert.Equal(HttpStatusCode.Conflict, creado.StatusCode);
        Assert.Equal("sin_cupo", (await JsonAsync(creado)).GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes WHERE CodigoMtrack = 'M-CUPO'"));

        // El reemplazo de un «no sale» ocupa el lugar de la unidad que sale.
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Capacidad = 2 WHERE Id = @v", ("@v", e.Viaje));
        var reemplazo = await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[0]}/no-sale", Cuerpo(new { motivo_codigo = "no_llego_al_patio", vin_reemplazo = "9FB45RFL8NM502001" }));
        Assert.Equal(HttpStatusCode.OK, reemplazo.StatusCode);
    }

    // ------------------------------------------------------------------------------------------------ el destino cambia

    [Fact]
    public async Task Cambiar_el_destino_marca_la_unidad_y_avisa_al_conductor_a_los_inspectores_del_lugar_nuevo_y_a_coordinacion_y_administracion_menos_a_quien_lo_cambio()
    {
        var (e, _) = await EscenarioAsync();
        var inspectorDelDestino = await SesionAsync("inspector-destino@ejemplo.com", clsRol.INSPECTOR);
        var otroCoordinador = await SesionAsync("coord2@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorAjeno = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var destinoAnterior = await LugarAsync(e.Admin, "Concesionario Norte", "concesionario");
        var destinoNuevo = await LugarAsync(e.Admin, "Puerto de Cartagena", "destino");
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PostAsync($"/api/usuarios/{inspectorDelDestino.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { destinoNuevo } }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[0]}", Cuerpo(new { lugar_destino_id = destinoAnterior }))).StatusCode);
        await Sembrador.EjecutarAsync("ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL; DELETE FROM dbo.tblNotificaciones; ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL");
        var antes = (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("vehiculos").EnumerateArray().Single(v => v.GetProperty("vin").GetString() == VINES[0]);
        Assert.NotEqual(JsonValueKind.Null, antes.GetProperty("lugar_destino_cambiado_at").ValueKind);
        var sinCambio = (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("vehiculos").EnumerateArray().Single(v => v.GetProperty("vin").GetString() == VINES[1]);
        Assert.Equal(JsonValueKind.Null, sinCambio.GetProperty("lugar_destino_cambiado_at").ValueKind);

        var r = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[0]}", Cuerpo(new { lugar_destino_id = destinoNuevo }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1, await AvisosAsync("cambio_destino_unidad", e.Conductor.UsuarioId));
        Assert.Equal(1, await AvisosAsync("cambio_destino_unidad", inspectorDelDestino.UsuarioId));
        Assert.Equal(0, await AvisosAsync("cambio_destino_unidad", e.Inspector.UsuarioId));
        // Coordinacion y administracion del cliente lo reciben una sola vez; quien hizo el cambio no recibe el suyo y otro cliente no recibe ninguno.
        Assert.Equal(1, await AvisosAsync("cambio_destino_unidad", otroCoordinador.UsuarioId));
        Assert.Equal(1, await AvisosAsync("cambio_destino_unidad", e.Admin.UsuarioId));
        Assert.Equal(0, await AvisosAsync("cambio_destino_unidad", e.Coordinador.UsuarioId));
        Assert.Equal(0, await AvisosAsync("cambio_destino_unidad", coordinadorAjeno.UsuarioId));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'cambio_destino_unidad' AND Urgencia = 'media' AND Canal = 'bandeja' AND UsuarioId = @u AND Mensaje = @m AND JSON_VALUE(Datos, '$.lugar_anterior') = N'Concesionario Norte' AND JSON_VALUE(Datos, '$.lugar_nuevo') = N'Puerto de Cartagena'",
            ("@u", e.Conductor.UsuarioId), ("@m", $"Cambió el destino del VIN {VINES[0]}: ahora va a Puerto de Cartagena")));
        // El conductor lo lee en su bandeja y ve la marca del cambio en «mis viajes».
        var bandeja = await JsonAsync(await Como(e.Conductor).GetAsync("/api/notificaciones?tipo=cambio_destino_unidad"));
        Assert.Single(bandeja.GetProperty("items").EnumerateArray());
        var mios = (await JsonAsync(await Como(e.Conductor).GetAsync("/api/viajes/mios"))).EnumerateArray().Single().GetProperty("vehiculos").EnumerateArray().Single(v => v.GetProperty("vin").GetString() == VINES[0]);
        Assert.NotEqual(JsonValueKind.Null, mios.GetProperty("lugar_destino_cambiado_at").ValueKind);
    }

    // ------------------------------------------------------------------------------------------------ eliminable

    [Fact]
    public async Task El_viaje_dice_si_es_eliminable_solo_si_no_tiene_ninguna_inspeccion_en_borrador_o_cerrada()
    {
        var (e, _) = await EscenarioAsync();

        var sinInspecciones = await ViajeAsync(e.Coordinador, e.Viaje);
        var lista = (await JsonAsync(await Como(e.Coordinador).GetAsync("/api/viajes"))).GetProperty("items").EnumerateArray().Single();
        Assert.True(sinInspecciones.GetProperty("eliminable").GetBoolean());
        Assert.True(lista.GetProperty("eliminable").GetBoolean());

        await Sembrador.InformeAsync(e.Etapas[0][0], e.Inspector.UsuarioId, cerrada: false, momento: "llegada");

        Assert.False((await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("eliminable").GetBoolean());
        Assert.False((await JsonAsync(await Como(e.Coordinador).GetAsync("/api/viajes"))).GetProperty("items").EnumerateArray().Single().GetProperty("eliminable").GetBoolean());
        // El eliminar de siempre no cambia: con una inspeccion responde su 409.
        Assert.Equal(HttpStatusCode.Conflict, (await Como(e.Coordinador).DeleteAsync($"/api/viajes/{e.Viaje}")).StatusCode);
    }
}
