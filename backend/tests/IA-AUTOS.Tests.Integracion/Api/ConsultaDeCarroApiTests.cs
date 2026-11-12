using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La consulta de un carro desde la app: el coordinador de calidad (y los demas roles de consulta de un cliente) busca y abre los carros de SU cliente sin tener ningun viaje asignado, un carro de otro cliente se le
/// rechaza con 403, y el inspector y el conductor siguen viendo solo los carros de sus viajes.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class ConsultaDeCarroApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static List<string?> Vines(JsonElement filas) => filas.EnumerateArray().Select(f => f.GetProperty("vin").GetString()).Order().ToList();

    /// <summary>
    /// Dos clientes. De Sofasa: <c>VIN-SOF-VIAJE</c> (en un viaje, con un informe cerrado con un dano) y <c>VIN-SOF-LIBRE</c> (sin ningun viaje). Del otro cliente: <c>VIN-OTRO</c>. El viaje de <c>VIN-SOF-VIAJE</c>
    /// es de un inspector y un conductor; <c>VIN-OTRO-DE-ELLOS</c> es un carro de Sofasa de un viaje ajeno a ellos.
    /// </summary>
    private sealed record Escenario(Guid Sofasa, Guid Otro, Sesion Inspector, Sesion Conductor);

    private async Task<Escenario> EscenarioAsync()
    {
        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;
        var otro = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otro));
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);

        var (_, etapasViaje) = await Sembrador.VehiculoConRutaAsync("VIN-SOF-VIAJE", operacion: "nacional");
        await Sembrador.InformeConHallazgosAsync(etapasViaje[0], inspector.UsuarioId, cerrada: true, ("capó", "golpe", "5_10cm"));
        var viaje = await Sembrador.ViajeDeEtapaAsync(etapasViaje[0]);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET InspectorId = @i, ConductorId = @c WHERE Id = @v", ("@i", inspector.UsuarioId), ("@c", conductor.UsuarioId), ("@v", viaje));

        await Sembrador.VehiculoAsync("VIN-SOF-LIBRE");
        await Sembrador.VehiculoConRutaAsync("VIN-SOF-AJENO", operacion: "nacional");
        await Sembrador.VehiculoConRutaAsync("VIN-OTRO", operacion: "nacional");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET ClienteId = @c WHERE Vin = 'VIN-OTRO'", ("@c", otro));
        return new Escenario(sofasa, otro, inspector, conductor);
    }

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    [InlineData(clsRol.COMERCIAL)]
    public async Task El_coordinador_sin_viajes_busca_y_abre_los_carros_de_su_cliente_aunque_no_esten_en_ningun_viaje(string rol)
    {
        var e = await EscenarioAsync();
        var consulta = await SesionAsync("consulta@ejemplo.com", rol, e.Sofasa);

        var busqueda = await JsonAsync(await Cliente(consulta.Token).GetAsync("/api/vehiculos?vin=VIN-SOF-LIBRE"));
        var busquedaTodos = await JsonAsync(await Cliente(consulta.Token).GetAsync("/api/vehiculos?vin=VIN-"));
        var libre = await Cliente(consulta.Token).GetAsync("/api/vehiculos/VIN-SOF-LIBRE/inventario");
        var enViaje = await Cliente(consulta.Token).GetAsync("/api/vehiculos/VIN-SOF-VIAJE/inventario");
        var pdf = await Cliente(consulta.Token).GetAsync("/api/vehiculos/VIN-SOF-VIAJE/inventario/pdf");

        Assert.Equal(["VIN-SOF-LIBRE"], Vines(busqueda));
        Assert.Equal(["VIN-SOF-AJENO", "VIN-SOF-LIBRE", "VIN-SOF-VIAJE"], Vines(busquedaTodos));
        Assert.Equal(HttpStatusCode.OK, libre.StatusCode);
        Assert.Equal("VIN-SOF-LIBRE", (await JsonAsync(libre)).GetProperty("vin").GetString());
        Assert.Equal(HttpStatusCode.OK, enViaje.StatusCode);
        var inventario = await JsonAsync(enViaje);
        Assert.Equal("VIN-SOF-VIAJE", inventario.GetProperty("vin").GetString());
        Assert.True(inventario.GetProperty("etapas").GetArrayLength() > 0);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        // Sus inspecciones: el informe del carro del viaje se lista y se abre, sin ser de ese viaje.
        var informes = await JsonAsync(await Cliente(consulta.Token).GetAsync("/api/inspecciones"));
        var items = informes.TryGetProperty("items", out var paginados) ? paginados : informes;
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await Cliente(consulta.Token).GetAsync($"/api/inspecciones/{items[0].GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes WHERE InspectorId = @u OR ConductorId = @u", ("@u", consulta.UsuarioId)));
    }

    [Fact]
    public async Task Un_carro_de_otro_cliente_se_le_rechaza_al_coordinador_con_403_y_no_aparece_en_su_busqueda()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, e.Sofasa);

        var ajeno = await Cliente(coordinador.Token).GetAsync("/api/vehiculos/VIN-OTRO/inventario");
        var ajenoPdf = await Cliente(coordinador.Token).GetAsync("/api/vehiculos/VIN-OTRO/inventario/pdf");
        var busqueda = await JsonAsync(await Cliente(coordinador.Token).GetAsync("/api/vehiculos?vin=VIN-OTRO"));

        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal("El VIN no es de su cliente", (await JsonAsync(ajeno)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, ajenoPdf.StatusCode);
        Assert.Empty(Vines(busqueda));
    }

    [Fact]
    public async Task El_inspector_y_el_conductor_siguen_viendo_solo_los_carros_de_sus_viajes_aunque_el_carro_sea_de_su_cliente()
    {
        var e = await EscenarioAsync();

        foreach (var sesion in new[] { e.Inspector, e.Conductor })
        {
            var propio = await Cliente(sesion.Token).GetAsync("/api/vehiculos/VIN-SOF-VIAJE/inventario");
            var delMismoClienteSinViaje = await Cliente(sesion.Token).GetAsync("/api/vehiculos/VIN-SOF-LIBRE/inventario");
            var deOtroViaje = await Cliente(sesion.Token).GetAsync("/api/vehiculos/VIN-SOF-AJENO/inventario");
            var busqueda = await JsonAsync(await Cliente(sesion.Token).GetAsync("/api/vehiculos?vin=VIN-"));

            Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, delMismoClienteSinViaje.StatusCode);
            Assert.Equal("El VIN no pertenece a ninguno de sus viajes", (await JsonAsync(delMismoClienteSinViaje)).GetProperty("message").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, deOtroViaje.StatusCode);
            Assert.Equal(["VIN-SOF-VIAJE"], Vines(busqueda));
        }
    }
}
