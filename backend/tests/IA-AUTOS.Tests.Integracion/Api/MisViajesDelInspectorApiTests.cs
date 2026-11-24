using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// «Mis viajes» del inspector trae tambien los viajes en los que tiene un reclamo activo de entrega (una sola vez aunque tome varias paradas), cada viaje dice su papel (carga, recibe o carga_y_recibe) y el viaje
/// deja de salir al liberar el reclamo salvo que ademas sea su inspector del viaje. El papel es nulo para el conductor y para los roles de oficina.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class MisViajesDelInspectorApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Tres puntos (Envigado, Cartagena y Buenaventura). Esleider y Andres en Envigado, Paula en Cartagena y Buenaventura, un coordinador y un conductor.</summary>
    private sealed record Escenario(Guid Envigado, Guid Cartagena, Guid Buenaventura, Sesion Esleider, Sesion Andres, Sesion Paula, Sesion Coordinador, Sesion Conductor);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task PuntoAsync(Sesion inspector, Guid lugar) =>
        await Sembrador.EjecutarAsync("INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), @u, @l)", ("@u", inspector.UsuarioId), ("@l", lugar));

    private async Task<Escenario> EscenarioAsync()
    {
        var coordinador = await SesionAsync("luisa@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var esleider = await SesionAsync("esleider@ejemplo.com", clsRol.INSPECTOR);
        var andres = await SesionAsync("andres@ejemplo.com", clsRol.INSPECTOR);
        var paula = await SesionAsync("paula@ejemplo.com", clsRol.INSPECTOR);
        var envigado = await LugarRealAsync(Como(coordinador), "CEDI Envigado", "cedi_1", "Envigado");
        var cartagena = await LugarRealAsync(Como(coordinador), "Puerto Cartagena", "cedi_2", "Cartagena");
        var buenaventura = await LugarRealAsync(Como(coordinador), "Puerto Buenaventura", "destino", "Buenaventura");
        await PuntoAsync(esleider, envigado);
        await PuntoAsync(andres, envigado);
        await PuntoAsync(paula, cartagena);
        await PuntoAsync(paula, buenaventura);
        return new Escenario(envigado, cartagena, buenaventura, esleider, andres, paula, coordinador, conductor);
    }

    /// <summary>Las 06:00 de hoy en Colombia (UTC-5), en ISO 8601 con zona.</summary>
    private static string FechaDeHoy() =>
        new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-5).Date.AddHours(6), DateTimeKind.Unspecified), TimeSpan.FromHours(-5)).ToString("yyyy-MM-ddTHH:mm:sszzz");

    /// <summary>Un cargue de hoy en Envigado con una unidad por cada destino indicado.</summary>
    private async Task<Guid> ViajeAsync(Escenario e, string codigo, params Guid[] destinos)
    {
        var tipo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;
        var unidades = new List<object>();
        for (var i = 0; i < destinos.Length; i++)
        {
            var vin = $"VIN-MIO-{codigo}-{i}";
            await Sembrador.VehiculoAsync(vin);
            unidades.Add(new { vin, lugar_destino_id = destinos[i] });
        }

        var creado = await Como(e.Coordinador).PostAsync("/api/viajes", Cuerpo(new
        {
            codigo_mtrack = codigo,
            tipo_unidad_id = tipo,
            placa = "ABC123",
            conductor_id = e.Conductor.UsuarioId,
            fecha_programada = FechaDeHoy(),
            lugar_cargue_id = e.Envigado,
            unidades,
        }));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await JsonAsync(creado)).GetProperty("id").GetGuid();
        var programado = await Como(e.Coordinador).PatchAsync($"/api/viajes/{id}", Cuerpo(new { fecha_programada = FechaDeHoy(), lugar_cargue_id = e.Envigado }));
        Assert.Equal(HttpStatusCode.OK, programado.StatusCode);
        return id;
    }

    private async Task ReclamarAsync(Sesion quien, Guid viaje, string proceso, Guid? lugar = null)
    {
        var r = await Como(quien).PostAsync($"/api/viajes/{viaje}/reclamos", Cuerpo(lugar is null ? new { proceso } : new { proceso, lugar_id = lugar }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    private async Task LiberarAsync(Sesion quien, Guid viaje, string proceso, Guid? lugar = null)
    {
        var r = await Como(quien).DeleteAsync($"/api/viajes/{viaje}/reclamos/{proceso}" + (lugar is null ? string.Empty : $"?lugar_id={lugar}"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    private async Task<List<JsonElement>> MiosAsync(Sesion quien) =>
        (await JsonAsync(await Como(quien).GetAsync("/api/viajes/mios"))).EnumerateArray().ToList();

    private async Task<JsonElement> DetalleAsync(Sesion quien, Guid viaje)
    {
        var r = await Como(quien).GetAsync($"/api/viajes/{viaje}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonAsync(r);
    }

    private static string? Papel(JsonElement viaje) =>
        viaje.GetProperty("papel").ValueKind == JsonValueKind.Null ? null : viaje.GetProperty("papel").GetString();

    // ------------------------------------------------------------------------------ CA-1 y CA-2: el viaje de la entrega y el papel

    [Fact]
    public async Task Quien_toma_la_entrega_ve_el_viaje_una_sola_vez_con_papel_recibe_y_quien_cargo_lo_ve_con_papel_carga()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-3101", e.Cartagena, e.Buenaventura);
        await ReclamarAsync(e.Esleider, viaje, "cargue");
        Assert.DoesNotContain(await MiosAsync(e.Paula), v => v.GetProperty("id").GetGuid() == viaje);

        await ReclamarAsync(e.Paula, viaje, "entrega", e.Cartagena);
        await ReclamarAsync(e.Paula, viaje, "entrega", e.Buenaventura);

        // Paula no es la inspectora del viaje, pero lo ve, una sola vez aunque tomo dos paradas.
        var deLaEntrega = (await MiosAsync(e.Paula)).Where(v => v.GetProperty("id").GetGuid() == viaje).ToList();
        Assert.Single(deLaEntrega);
        Assert.Equal("recibe", Papel(deLaEntrega[0]));
        Assert.Equal(e.Esleider.UsuarioId, deLaEntrega[0].GetProperty("inspector_id").GetGuid());
        // Quien cargo sigue viendolo como quien carga, y el detalle dice lo mismo que la lista.
        var delCargue = (await MiosAsync(e.Esleider)).Single(v => v.GetProperty("id").GetGuid() == viaje);
        Assert.Equal("carga", Papel(delCargue));
        Assert.Equal("recibe", Papel(await DetalleAsync(e.Paula, viaje)));
        Assert.Equal("carga", Papel(await DetalleAsync(e.Esleider, viaje)));
        // Otro inspector sin reclamo no lo ve en su lista.
        Assert.DoesNotContain(await MiosAsync(e.Andres), v => v.GetProperty("id").GetGuid() == viaje);
    }

    [Fact]
    public async Task El_inspector_del_viaje_que_ademas_toma_la_entrega_de_una_parada_suya_tiene_el_papel_carga_y_recibe()
    {
        var e = await EscenarioAsync();
        await PuntoAsync(e.Esleider, e.Cartagena);
        var viaje = await ViajeAsync(e, "MTK-3102", e.Cartagena);
        await ReclamarAsync(e.Esleider, viaje, "cargue");

        await ReclamarAsync(e.Esleider, viaje, "entrega", e.Cartagena);

        var mio = (await MiosAsync(e.Esleider)).Single(v => v.GetProperty("id").GetGuid() == viaje);
        Assert.Equal("carga_y_recibe", Papel(mio));
        Assert.Equal("carga_y_recibe", Papel(await DetalleAsync(e.Esleider, viaje)));
    }

    [Fact]
    public async Task El_papel_es_nulo_para_el_conductor_y_para_la_oficina_y_el_viaje_sigue_igual()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-3103", e.Cartagena);
        await ReclamarAsync(e.Esleider, viaje, "cargue");

        var delConductor = (await MiosAsync(e.Conductor)).Single(v => v.GetProperty("id").GetGuid() == viaje);

        Assert.Null(Papel(delConductor));
        Assert.Null(Papel(await DetalleAsync(e.Conductor, viaje)));
        Assert.Null(Papel(await DetalleAsync(e.Coordinador, viaje)));
        // Los campos de siempre siguen en el viaje: el papel es un campo nuevo, no cambia ninguno.
        Assert.Equal("MTK-3103", delConductor.GetProperty("codigo_mtrack").GetString());
        Assert.Equal(e.Esleider.UsuarioId, delConductor.GetProperty("inspector_id").GetGuid());
    }

    // ------------------------------------------------------------------------------------------------- CA-3: liberar el reclamo

    [Fact]
    public async Task Al_liberar_la_entrega_el_viaje_sale_de_mis_viajes_y_quien_cargo_lo_sigue_viendo()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-3104", e.Cartagena, e.Buenaventura);
        await ReclamarAsync(e.Esleider, viaje, "cargue");
        await ReclamarAsync(e.Paula, viaje, "entrega", e.Cartagena);
        await ReclamarAsync(e.Paula, viaje, "entrega", e.Buenaventura);

        // Mientras le quede una parada, el viaje sigue en su lista.
        await LiberarAsync(e.Paula, viaje, "entrega", e.Cartagena);
        Assert.Contains(await MiosAsync(e.Paula), v => v.GetProperty("id").GetGuid() == viaje);

        await LiberarAsync(e.Paula, viaje, "entrega", e.Buenaventura);

        Assert.DoesNotContain(await MiosAsync(e.Paula), v => v.GetProperty("id").GetGuid() == viaje);
        Assert.Equal("carga", Papel((await MiosAsync(e.Esleider)).Single(v => v.GetProperty("id").GetGuid() == viaje)));
    }

    [Fact]
    public async Task Al_liberar_la_entrega_quien_ademas_es_inspector_del_viaje_conserva_el_papel_carga()
    {
        var e = await EscenarioAsync();
        await PuntoAsync(e.Esleider, e.Cartagena);
        var viaje = await ViajeAsync(e, "MTK-3105", e.Cartagena);
        await ReclamarAsync(e.Esleider, viaje, "cargue");
        await ReclamarAsync(e.Esleider, viaje, "entrega", e.Cartagena);

        await LiberarAsync(e.Esleider, viaje, "entrega", e.Cartagena);

        Assert.Equal("carga", Papel((await MiosAsync(e.Esleider)).Single(v => v.GetProperty("id").GetGuid() == viaje)));
    }

    [Fact]
    public async Task Un_viaje_cancelado_no_sale_en_mis_viajes_aunque_el_reclamo_siga_activo()
    {
        var e = await EscenarioAsync();
        var viaje = await ViajeAsync(e, "MTK-3106", e.Cartagena);
        await ReclamarAsync(e.Paula, viaje, "entrega", e.Cartagena);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = 'cancelado' WHERE Id = @v", ("@v", viaje));

        Assert.DoesNotContain(await MiosAsync(e.Paula), v => v.GetProperty("id").GetGuid() == viaje);
    }
}
