using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>El detalle del viaje trae el cargue de la niñera completo (firmas, reservas por carro y constancia de no firma) sin la dirección de red de las firmas ni ninguna cédula.</summary>
public sealed partial class CargueNineraApiTests
{
    private async Task FirmarLasDosPartesAsync(Escenario e, params string[] vinesConReservas)
    {
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);
        var reservas = vinesConReservas.Select(v => Reserva(v, "Rayón en " + v)).ToList();
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Conductor, e.Viaje, "recibe", reservas.Count == 0 ? null : reservas)).StatusCode);
        if (reservas.Count > 0)
        {
            Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega", fecha: "2026-11-18T14:20:00-05:00")).StatusCode);
        }
    }

    [Fact]
    public async Task El_detalle_del_viaje_con_el_cargue_firmado_trae_los_recibidos_las_reservas_de_esos_carros_y_las_dos_firmas_sin_ip()
    {
        var e = await EscenarioAsync();
        await FirmarLasDosPartesAsync(e, VINES[0]);

        var viaje = await ViajeAsync(e.Coordinador, e.Viaje);

        var ninera = viaje.GetProperty("cargue_ninera");
        Assert.Equal(2, ninera.GetProperty("recibidos").GetInt32());
        var reserva = Assert.Single(ninera.GetProperty("reservas").EnumerateArray());
        Assert.Equal(VINES[0], reserva.GetProperty("vin").GetString());
        Assert.Equal("Rayón en " + VINES[0], reserva.GetProperty("observaciones").GetString());
        var entrega = ninera.GetProperty("entrega");
        var recibe = ninera.GetProperty("recibe");
        Assert.Equal("Nombre inspector@ejemplo.com", entrega.GetProperty("nombre").GetString());
        Assert.Equal("Nombre conductor@ejemplo.com", recibe.GetProperty("nombre").GetString());
        Assert.Equal("M0 0 L10 10", entrega.GetProperty("firma").GetString());
        Assert.Equal("M0 0 L10 10", recibe.GetProperty("firma").GetString());
        Assert.NotEqual(JsonValueKind.Null, entrega.GetProperty("firmada_at").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, recibe.GetProperty("firmada_at").ValueKind);
        Assert.Equal(["nombre", "firmada_at", "firma"], entrega.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(["nombre", "firmada_at", "firma"], recibe.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(JsonValueKind.Null, ninera.GetProperty("constancia").ValueKind);
        Assert.Equal(0, ninera.GetProperty("carros_sin_firma").GetInt32());

        // Ni la direccion de red ni ninguna cedula viajan en el bloque.
        var texto = ninera.GetRawText();
        Assert.DoesNotContain("\"ip\"", texto);
        Assert.DoesNotContain("cedula", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_viaje_de_cuatro_carros_con_reservas_en_dos_trae_solo_las_de_esos_dos()
    {
        var e = await EscenarioAsync();
        var otros = new[] { "9FB45RFL8NM301003", "9FB45RFL8NM301004" };
        foreach (var vin in otros)
        {
            var (vehiculo, ruta) = await Sembrador.VehiculoConRutaAsync(vin, viajeId: e.Viaje);
            await Sembrador.InformeAsync(ruta[0], e.Inspector.UsuarioId, cerrada: true, momento: "llegada");
            await Sembrador.InformeAsync(ruta[0], e.Inspector.UsuarioId, cerrada: true, momento: "salida");
            Assert.NotEqual(Guid.Empty, vehiculo);
        }

        await FirmarLasDosPartesAsync(e, VINES[1], otros[1]);

        var ninera = (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("cargue_ninera");

        Assert.Equal(4, ninera.GetProperty("recibidos").GetInt32());
        Assert.Equal([VINES[1], otros[1]], ninera.GetProperty("reservas").EnumerateArray().Select(r => r.GetProperty("vin").GetString()).Order().ToList());
    }

    [Fact]
    public async Task El_detalle_con_la_constancia_de_no_firma_trae_la_parte_el_motivo_el_testigo_la_foto_con_enlace_el_momento_y_los_carros_sin_firma()
    {
        var e = await EscenarioAsync();
        var testigo = await SesionAsync("testigo@ejemplo.com", clsRol.INSPECTOR);
        Assert.Equal(HttpStatusCode.OK, (await NoFirmaAsync(e.Inspector, e.Viaje, "recibe", testigo.UsuarioId, "El conductor se niega a firmar el cargue")).StatusCode);

        var ninera = (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("cargue_ninera");

        var constancia = ninera.GetProperty("constancia");
        Assert.Equal("recibe", constancia.GetProperty("parte").GetString());
        Assert.Equal("El conductor se niega a firmar el cargue", constancia.GetProperty("motivo").GetString());
        Assert.Equal("Nombre testigo@ejemplo.com", constancia.GetProperty("testigo_nombre").GetString());
        Assert.Equal(DateTimeOffset.Parse(MOMENTO), constancia.GetProperty("momento").GetDateTimeOffset());
        Assert.Equal(6.2, constancia.GetProperty("lat").GetDouble());
        Assert.Equal(-75.5, constancia.GetProperty("lng").GetDouble());
        var foto = constancia.GetProperty("foto");
        Assert.StartsWith("http", foto.GetProperty("url").GetString());
        Assert.NotEqual(JsonValueKind.Null, foto.GetProperty("url_expira_at").ValueKind);
        Assert.Equal(6.2, foto.GetProperty("sello_lat").GetDouble());
        Assert.Equal(-75.5, foto.GetProperty("sello_lng").GetDouble());
        Assert.Equal(4.5, foto.GetProperty("precision").GetDouble());
        Assert.Equal("tablet-1", foto.GetProperty("dispositivo_id").GetString());
        Assert.False(foto.GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal(64, foto.GetProperty("hash_sha256").GetString()!.Length);
        Assert.Equal(2, ninera.GetProperty("carros_sin_firma").GetInt32());
        Assert.Equal(0, ninera.GetProperty("recibidos").GetInt32());
        Assert.Equal(JsonValueKind.Null, ninera.GetProperty("recibe").ValueKind);
        Assert.DoesNotContain("cedula", ninera.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"ip\"", ninera.GetRawText());
    }

    [Fact]
    public async Task La_foto_de_la_constancia_tomada_sin_gps_llega_con_la_latitud_y_la_longitud_vacias()
    {
        var e = await EscenarioAsync();
        var testigo = await SesionAsync("testigo@ejemplo.com", clsRol.INSPECTOR);
        var (ruta, hash) = await SubirAsync(e.Inspector);
        var r = await Como(e.Inspector).PostAsJsonAsync($"/api/viajes/{e.Viaje}/cargue/no-firma", new Dictionary<string, object?>
        {
            ["parte"] = "recibe", ["motivo"] = "Se niega a firmar el cargue", ["testigo_usuario_id"] = testigo.UsuarioId,
            ["evidencia"] = new { storage_path = ruta, sello_fecha = MOMENTO, hash_sha256 = hash },
            ["fecha_momento"] = MOMENTO, ["lat"] = 6.2, ["lng"] = -75.5,
        });
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());

        var constancia = (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("cargue_ninera").GetProperty("constancia");

        var foto = constancia.GetProperty("foto");
        Assert.True(foto.GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal(JsonValueKind.Null, foto.GetProperty("sello_lat").ValueKind);
        Assert.Equal(JsonValueKind.Null, foto.GetProperty("sello_lng").ValueKind);
        Assert.StartsWith("http", foto.GetProperty("url").GetString());
        // El momento de quien deja la constancia siempre trae su GPS: solo la foto puede haberse tomado sin senal.
        Assert.Equal(6.2, constancia.GetProperty("lat").GetDouble());
        Assert.Equal(-75.5, constancia.GetProperty("lng").GetDouble());
    }

    [Fact]
    public async Task Un_viaje_con_el_cargue_sin_firmar_y_sin_constancia_no_trae_el_cargue_de_la_ninera()
    {
        var e = await EscenarioAsync();

        Assert.Equal(JsonValueKind.Null, (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("cargue_ninera").ValueKind);

        // Con una sola firma el cargue sigue sin cerrarse: tampoco lo trae.
        Assert.Equal(HttpStatusCode.OK, (await FirmarAsync(e.Inspector, e.Viaje, "entrega")).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ViajeAsync(e.Coordinador, e.Viaje)).GetProperty("cargue_ninera").ValueKind);
    }

    [Fact]
    public async Task El_cargue_de_la_ninera_viaja_solo_en_el_detalle_y_no_en_mis_viajes_ni_en_las_respuestas_de_las_operaciones()
    {
        var e = await EscenarioAsync();
        await FirmarLasDosPartesAsync(e);

        var mios = (await JsonAsync(await Como(e.Conductor).GetAsync("/api/viajes/mios"))).EnumerateArray().Single();
        var cambio = await Como(e.Coordinador).PatchAsync($"/api/viajes/{e.Viaje}", new StringContent("{\"placa\":\"XYZ987\"}", System.Text.Encoding.UTF8, "application/json"));
        var lista = (await JsonAsync(await Como(e.Coordinador).GetAsync("/api/viajes"))).GetProperty("items").EnumerateArray().Single();

        Assert.Equal(JsonValueKind.Null, mios.GetProperty("cargue_ninera").ValueKind);
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(cambio)).GetProperty("cargue_ninera").ValueKind);
        Assert.False(lista.TryGetProperty("cargue_ninera", out _));
        Assert.NotEqual(JsonValueKind.Null, (await ViajeAsync(e.Conductor, e.Viaje)).GetProperty("cargue_ninera").ValueKind);
    }
}
