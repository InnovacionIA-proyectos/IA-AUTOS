using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La app sabe si el punto tiene inspector (S7-HU-22): la entrega por punto dice si su punto de destino tiene inspector (<c>lugar_tiene_inspector</c>) y el viaje, si lo tiene su punto de cargue
/// (<c>lugar_cargue_tiene_inspector</c>), con la MISMA regla con la que el servidor decide quien firma, sin nombres ni identificadores de inspectores, siguiendo lo que asigna el coordinador y sin contar a un
/// inspector inactivo; los rechazos de siempre no cambian.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class PuntoConInspectorApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Un viaje con una unidad al Puerto de Cartagena (con o sin inspector segun el caso), otra al Concesionario Norte (nunca con inspector) y su punto de cargue, el CEDI Envigado.</summary>
    private sealed record Escenario(Guid Viaje, Guid Cartagena, Guid Norte, Guid Cedi, Sesion Conductor, Sesion Inspector, Sesion Coordinador);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync()
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var cartagena = await LugarRealAsync(Cliente(coordinador.Token), "Puerto de Cartagena", "destino", "Cartagena");
        var norte = await LugarRealAsync(Cliente(coordinador.Token), "Concesionario Norte", "concesionario", "Bogota");
        var cedi = await LugarRealAsync(Cliente(coordinador.Token), "CEDI Envigado", "cedi_1", "Envigado");
        await Sembrador.VehiculoAsync("VIN-PTO-01");
        await Sembrador.VehiculoAsync("VIN-PTO-02");
        var tipo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;

        var creado = await Cliente(coordinador.Token).PostAsync("/api/viajes", Cuerpo(new
        {
            codigo_mtrack = "MT-2026-0417",
            tipo_unidad_id = tipo,
            placa = "ABC123",
            conductor_id = conductor.UsuarioId,
            inspector_id = inspector.UsuarioId,
            fecha_programada = "2026-11-18T06:00:00-05:00",
            lugar_cargue_id = cedi,
            unidades = new object[] { new { vin = "VIN-PTO-01", lugar_destino_id = cartagena }, new { vin = "VIN-PTO-02", lugar_destino_id = norte } },
        }));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        return new Escenario((await JsonAsync(creado)).GetProperty("id").GetGuid(), cartagena, norte, cedi, conductor, inspector, coordinador);
    }

    private Task<HttpResponseMessage> AsignarAsync(Escenario e, Guid lugar) =>
        Cliente(e.Coordinador.Token).PostAsync($"/api/usuarios/{e.Inspector.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { lugar } }));

    private Task<HttpResponseMessage> QuitarAsync(Escenario e, Guid lugar) => Cliente(e.Coordinador.Token).DeleteAsync($"/api/usuarios/{e.Inspector.UsuarioId}/puntos/{lugar}");

    private static bool Entrega(JsonElement punto) => punto.GetProperty("lugar_tiene_inspector").GetBoolean();

    private async Task<JsonElement> PuntoAsync(Escenario e, Sesion quien, Guid lugar)
    {
        var r = await Cliente(quien.Token).GetAsync($"/api/viajes/{e.Viaje}/entregas/{lugar}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonAsync(r);
    }

    private async Task<bool> CargueDelViajeAsync(Escenario e, Sesion quien)
    {
        var r = await Cliente(quien.Token).GetAsync($"/api/viajes/{e.Viaje}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await JsonAsync(r)).GetProperty("lugar_cargue_tiene_inspector").GetBoolean();
    }

    // ------------------------------------------------------------------------------------------ la entrega

    [Fact]
    public async Task La_entrega_dice_si_su_punto_tiene_inspector_en_el_listado_y_en_el_punto_y_no_trae_nombres_ni_ids_de_inspectores()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await AsignarAsync(e, e.Cartagena)).StatusCode);
        // Un segundo inspector en el mismo punto: sigue siendo un solo dato.
        var otro = await SesionAsync("inspector2@ejemplo.com", clsRol.INSPECTOR);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(e.Coordinador.Token).PostAsync($"/api/usuarios/{otro.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { e.Cartagena } }))).StatusCode);

        var listado = await Cliente(e.Conductor.Token).GetAsync($"/api/viajes/{e.Viaje}/entregas");

        Assert.Equal(HttpStatusCode.OK, listado.StatusCode);
        var texto = await listado.Content.ReadAsStringAsync();
        var puntos = JsonDocument.Parse(texto).RootElement.GetProperty("puntos").EnumerateArray().ToDictionary(p => p.GetProperty("lugar_id").GetGuid());
        Assert.True(Entrega(puntos[e.Cartagena]));
        Assert.False(Entrega(puntos[e.Norte]));
        Assert.True(Entrega(await PuntoAsync(e, e.Conductor, e.Cartagena)));
        Assert.False(Entrega(await PuntoAsync(e, e.Conductor, e.Norte)));
        // Ni el nombre ni el identificador de ningun inspector viaja en la entrega.
        foreach (var inspector in new[] { e.Inspector, otro })
        {
            Assert.DoesNotContain(inspector.UsuarioId.ToString(), texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"Nombre {(inspector == e.Inspector ? "inspector@ejemplo.com" : "inspector2@ejemplo.com")}", texto);
        }
    }

    [Fact]
    public async Task Quitar_al_unico_inspector_o_darlo_de_baja_cambia_el_dato_en_la_siguiente_lectura()
    {
        var e = await EscenarioAsync();
        Assert.False(Entrega(await PuntoAsync(e, e.Conductor, e.Cartagena)));

        // CA-4: asignar cambia el dato en la siguiente lectura, y quitar tambien (ESC-05).
        Assert.Equal(HttpStatusCode.OK, (await AsignarAsync(e, e.Cartagena)).StatusCode);
        Assert.True(Entrega(await PuntoAsync(e, e.Conductor, e.Cartagena)));
        Assert.Equal(HttpStatusCode.OK, (await QuitarAsync(e, e.Cartagena)).StatusCode);
        Assert.False(Entrega(await PuntoAsync(e, e.Conductor, e.Cartagena)));

        // CA-5: un inspector inactivo no cuenta (ESC-06) y al reactivarlo vuelve a contar.
        Assert.Equal(HttpStatusCode.OK, (await AsignarAsync(e, e.Cartagena)).StatusCode);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblUsuarios SET Activo = 0 WHERE Id = @u", ("@u", e.Inspector.UsuarioId));
        Assert.False(Entrega(await PuntoAsync(e, e.Conductor, e.Cartagena)));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblUsuarios SET Activo = 1 WHERE Id = @u", ("@u", e.Inspector.UsuarioId));
        Assert.True(Entrega(await PuntoAsync(e, e.Conductor, e.Cartagena)));
    }

    // ------------------------------------------------------------------------------------------ el viaje

    [Fact]
    public async Task El_viaje_dice_si_su_punto_de_cargue_tiene_inspector_en_el_detalle_y_en_el_listado_y_sigue_lo_que_asigna_el_coordinador()
    {
        var e = await EscenarioAsync();
        var sinInspector = await Cliente(e.Coordinador.Token).GetAsync("/api/viajes");
        Assert.Equal(HttpStatusCode.OK, sinInspector.StatusCode);
        Assert.False(await CargueDelViajeAsync(e, e.Conductor));
        Assert.False((await JsonAsync(sinInspector)).GetProperty("items").EnumerateArray().Single().GetProperty("lugar_cargue_tiene_inspector").GetBoolean());

        // ESC-03: el punto de cargue con inspector asignado.
        Assert.Equal(HttpStatusCode.OK, (await AsignarAsync(e, e.Cedi)).StatusCode);
        Assert.True(await CargueDelViajeAsync(e, e.Conductor));
        Assert.True(await CargueDelViajeAsync(e, e.Coordinador));
        var conInspector = await JsonAsync(await Cliente(e.Coordinador.Token).GetAsync("/api/viajes"));
        Assert.True(conInspector.GetProperty("items").EnumerateArray().Single().GetProperty("lugar_cargue_tiene_inspector").GetBoolean());
        // El dato es el del punto de CARGUE: un inspector en un punto de destino no lo cambia.
        Assert.Equal(HttpStatusCode.OK, (await QuitarAsync(e, e.Cedi)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AsignarAsync(e, e.Cartagena)).StatusCode);
        Assert.False(await CargueDelViajeAsync(e, e.Conductor));

        // CA-5: inactivo no cuenta.
        Assert.Equal(HttpStatusCode.OK, (await AsignarAsync(e, e.Cedi)).StatusCode);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblUsuarios SET Activo = 0 WHERE Id = @u", ("@u", e.Inspector.UsuarioId));
        Assert.False(await CargueDelViajeAsync(e, e.Conductor));
    }

    [Fact]
    public async Task Un_viaje_sin_punto_de_cargue_dice_que_no_tiene_inspector_como_decide_el_servidor()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET LugarCargueId = NULL WHERE Id = @v", ("@v", e.Viaje));
        Assert.Equal(HttpStatusCode.OK, (await AsignarAsync(e, e.Cedi)).StatusCode);

        Assert.False(await CargueDelViajeAsync(e, e.Coordinador));
    }

    // ------------------------------------------------------------------------------------------ misma regla, misma visibilidad, mismos rechazos

    [Fact]
    public async Task El_dato_coincide_siempre_con_lo_que_decide_el_servidor_y_los_rechazos_de_la_entrega_no_cambian()
    {
        var e = await EscenarioAsync();
        var externo = new { firma = "firma-conductor", recibe_nombre = "Ana Restrepo", recibe_cedula = "1020304050", firma_recibe = "firma-ana" };

        foreach (var estado in new[] { "sin inspector", "con inspector", "quitado", "inactivo", "reactivado" })
        {
            switch (estado)
            {
                case "con inspector":
                    await AsignarAsync(e, e.Cartagena);
                    break;
                case "quitado":
                    await QuitarAsync(e, e.Cartagena);
                    break;
                case "inactivo":
                    await AsignarAsync(e, e.Cartagena);
                    await Sembrador.EjecutarAsync("UPDATE dbo.tblUsuarios SET Activo = 0 WHERE Id = @u", ("@u", e.Inspector.UsuarioId));
                    break;
                case "reactivado":
                    await Sembrador.EjecutarAsync("UPDATE dbo.tblUsuarios SET Activo = 1 WHERE Id = @u", ("@u", e.Inspector.UsuarioId));
                    break;
            }

            var dato = Entrega(await PuntoAsync(e, e.Conductor, e.Cartagena));
            var intento = await Cliente(e.Conductor.Token).PostAsync($"/api/viajes/{e.Viaje}/entregas/{e.Cartagena}/firma", Cuerpo(externo));
            var rechazadoPorInspector = intento.StatusCode == HttpStatusCode.BadRequest
                && (await intento.Content.ReadAsStringAsync()).Contains("El punto de entrega tiene inspector asignado", StringComparison.Ordinal);

            // El servidor rechaza a la persona externa EXACTAMENTE cuando el dato dice que el punto tiene inspector (ESC-07): una sola regla.
            Assert.True(dato == rechazadoPorInspector, $"{estado}: dato={dato}, rechazo por inspector={rechazadoPorInspector}");
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Quien_no_podia_leer_la_entrega_o_el_viaje_sigue_sin_poder_y_las_respuestas_anteriores_se_leen_igual()
    {
        var e = await EscenarioAsync();
        var ajeno = await SesionAsync("ajeno@ejemplo.com", clsRol.INSPECTOR);

        var entregaAjena = await Cliente(ajeno.Token).GetAsync($"/api/viajes/{e.Viaje}/entregas/{e.Cartagena}");
        var viajeAjeno = await Cliente(ajeno.Token).GetAsync($"/api/viajes/{e.Viaje}");

        Assert.Equal(HttpStatusCode.Forbidden, entregaAjena.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viajeAjeno.StatusCode);
        Assert.DoesNotContain("lugar_tiene_inspector", await entregaAjena.Content.ReadAsStringAsync());
        // CA-8: los campos de siempre siguen donde estaban; el dato nuevo solo se suma.
        var punto = await PuntoAsync(e, e.Conductor, e.Cartagena);
        foreach (var campo in new[] { "viaje_id", "viaje_estado", "lugar_id", "lugar", "ciudad", "unidades_total", "unidades_pendientes", "entregada", "unidades" })
        {
            Assert.True(punto.TryGetProperty(campo, out _), campo);
        }

        var viaje = await JsonAsync(await Cliente(e.Conductor.Token).GetAsync($"/api/viajes/{e.Viaje}"));
        foreach (var campo in new[] { "id", "codigo_mtrack", "estado", "vehiculos", "clientes", "lugar_cargue_id", "lugar_cargue", "capacidad", "disposicion", "fecha_programada" })
        {
            Assert.True(viaje.TryGetProperty(campo, out _), campo);
        }
    }
}
