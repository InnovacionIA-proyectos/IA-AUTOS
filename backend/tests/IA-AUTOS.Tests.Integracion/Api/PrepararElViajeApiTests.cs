using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Preparar el viaje no lo arranca: asignar o cambiar el muelle y el trailer, «No sale», «Vuelve a salir» y «Bajar del viaje» dejan el viaje programado; la primera inspeccion y el cambio de muelle de un viaje en curso siguen igual;
/// y cambiar el lugar de cargue de un viaje programado quita el muelle (con el mismo lugar se conserva).
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class PrepararElViajeApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private static readonly string[] VINES = ["9FB45RFL8NM601001", "9FB45RFL8NM601002", "9FB45RFL8NM601003", "9FB45RFL8NM601004"];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private sealed record Escenario(Guid Viaje, Guid LugarCargue, Guid OtroLugar, Guid[] Muelles, Sesion Inspector, Sesion Coordinador, Sesion Admin);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    /// <summary>Un viaje programado de cuatro carros que carga en «CEDI Envigado» (con tres muelles), con su inspector y el coordinador de calidad.</summary>
    private async Task<Escenario> EscenarioAsync(string estado = "programado")
    {
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var lugar = await LugarRealAsync(Como(admin), "CEDI Envigado", "cedi_1", "Envigado");
        var otro = await LugarRealAsync(Como(admin), "Puerto de Cartagena", "cedi_1", "Cartagena");
        var muelles = new List<Guid>();
        foreach (var n in new[] { "1", "2", "3" })
        {
            var r = await Como(admin).PostAsync("/api/catalogos/muelles", Cuerpo(new { lugar_id = lugar, codigo = "m" + n, nombre = "Muelle " + n }));
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            muelles.Add((await JsonAsync(r)).GetProperty("id").GetGuid());
        }

        var viaje = await Sembrador.ViajeAsync(conductor.UsuarioId, inspector.UsuarioId, estado: estado, codigoMtrack: "M-PREPARAR");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET LugarCargueId = @l, Trailer = N'TRX-101' WHERE Id = @v", ("@l", lugar), ("@v", viaje));
        foreach (var vin in VINES)
        {
            await Sembrador.VehiculoConRutaAsync(vin, viajeId: viaje);
        }

        return new Escenario(viaje, lugar, otro, [.. muelles], inspector, coordinador, admin);
    }

    private async Task<string> EstadoDelViajeAsync(Guid viaje) => (string)(await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", viaje)))!;

    [Fact]
    public async Task Asignar_el_muelle_o_cambiar_el_trailer_deja_el_viaje_programado_y_su_fecha_y_su_lugar_se_corrigen_despues()
    {
        var e = await EscenarioAsync();

        var muelle = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/cargue", Cuerpo(new { muelle_id = e.Muelles[2] }));
        var trailer = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/cargue", Cuerpo(new { trailer = "TRX-202" }));

        Assert.Equal(HttpStatusCode.OK, muelle.StatusCode);
        Assert.Equal(HttpStatusCode.OK, trailer.StatusCode);
        var viaje = await JsonAsync(trailer);
        Assert.Equal("programado", viaje.GetProperty("estado").GetString());
        Assert.Equal(e.Muelles[2], viaje.GetProperty("muelle_id").GetGuid());
        Assert.Equal("programado", await EstadoDelViajeAsync(e.Viaje));
        // Su fecha y su lugar de cargue se pueden cambiar despues: sigue programado.
        var reprogramado = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}", Cuerpo(new { fecha_programada = "2026-11-27T07:30:00-05:00", lugar_cargue_id = e.OtroLugar }));
        Assert.Equal(HttpStatusCode.OK, reprogramado.StatusCode);
    }

    [Fact]
    public async Task El_inspector_del_viaje_tampoco_arranca_el_viaje_al_cambiar_el_muelle()
    {
        var e = await EscenarioAsync();

        var r = await Como(e.Inspector).PatchAsync($"/api/viajes/{e.Viaje}/cargue", Cuerpo(new { muelle_id = e.Muelles[0] }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("programado", await EstadoDelViajeAsync(e.Viaje));
    }

    [Fact]
    public async Task No_sale_vuelve_a_salir_y_bajar_del_viaje_dejan_el_viaje_programado_despues_de_cada_accion()
    {
        var e = await EscenarioAsync();

        var noSale = await Como(e.Coordinador).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[1]}/no-sale", new { motivo_codigo = "otro", motivo_texto = "Llanta pinchada, sin repuesto" });
        Assert.Equal(HttpStatusCode.OK, noSale.StatusCode);
        Assert.Equal("programado", await EstadoDelViajeAsync(e.Viaje));

        var vuelve = await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[1]}/vuelve-a-salir", null);
        Assert.Equal(HttpStatusCode.OK, vuelve.StatusCode);
        Assert.Equal("programado", await EstadoDelViajeAsync(e.Viaje));

        var bajar = await Como(e.Coordinador).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[2]}/bajar", new { motivo = "Cambio de pedido" });
        Assert.Equal(HttpStatusCode.OK, bajar.StatusCode);
        Assert.Equal("programado", await EstadoDelViajeAsync(e.Viaje));
    }

    [Fact]
    public async Task El_cambio_de_muelle_de_un_viaje_en_curso_sigue_funcionando_y_no_cambia_su_estado()
    {
        var e = await EscenarioAsync(estado: "en_curso");

        var r = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/cargue", Cuerpo(new { muelle_id = e.Muelles[1] }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("en_curso", await EstadoDelViajeAsync(e.Viaje));
    }

    [Fact]
    public async Task Cambiar_el_lugar_de_cargue_de_un_viaje_programado_quita_el_muelle_y_con_el_mismo_lugar_lo_conserva()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/cargue", Cuerpo(new { muelle_id = e.Muelles[2] }))).StatusCode);

        // Con el mismo lugar el muelle se conserva.
        var mismo = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}", Cuerpo(new { lugar_cargue_id = e.LugarCargue, fecha_programada = "2026-11-27T07:30:00-05:00" }));
        Assert.Equal(HttpStatusCode.OK, mismo.StatusCode);
        Assert.Equal(e.Muelles[2], (await JsonAsync(mismo)).GetProperty("muelle_id").GetGuid());

        // Con otro lugar el viaje queda con el lugar nuevo y sin muelle.
        var otro = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}", Cuerpo(new { lugar_cargue_id = e.OtroLugar }));

        Assert.Equal(HttpStatusCode.OK, otro.StatusCode);
        var viaje = await JsonAsync(otro);
        Assert.Equal("Puerto de Cartagena", viaje.GetProperty("lugar_cargue").GetString());
        Assert.Equal(JsonValueKind.Null, viaje.GetProperty("muelle_id").ValueKind);
        Assert.Equal("programado", viaje.GetProperty("estado").GetString());
        // El muelle del lugar viejo ya no vale en el lugar nuevo (400), y uno del lugar nuevo se puede asignar.
        var viejo = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/cargue", Cuerpo(new { muelle_id = e.Muelles[2] }));
        Assert.Equal(HttpStatusCode.BadRequest, viejo.StatusCode);
        var nuevo = await Como(e.Admin).PostAsync("/api/catalogos/muelles", Cuerpo(new { lugar_id = e.OtroLugar, codigo = "p1", nombre = "Puerta 1" }));
        var asignado = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}/cargue", Cuerpo(new { muelle_id = (await JsonAsync(nuevo)).GetProperty("id").GetGuid() }));
        Assert.Equal(HttpStatusCode.OK, asignado.StatusCode);
    }
}
