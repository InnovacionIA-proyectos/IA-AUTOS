using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class AsignacionesYEtapasApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private async Task<(string Token, Guid UsuarioId)> AutenticarAsync(string rol, string email)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol);
        return (await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ClonarAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    [Fact]
    public async Task Admin_asigna_un_vin_a_una_ninera_y_conductor_y_queda_registrado_quien_asigno()
    {
        var (token, adminId) = await AutenticarAsync(clsRol.ADMIN, "admin@ejemplo.com");
        var conductor = await Fabrica.SembrarUsuarioAsync("cond@ejemplo.com", "x", clsRol.CONDUCTOR);
        var (vehiculoId, _) = await Sembrador.VehiculoConRutaAsync("VIN-ASIG");

        var r = await Cliente(token).PostAsJsonAsync("/api/asignaciones", new { vehiculo_id = vehiculoId, ninera = "  NIN-123 ", conductor_id = conductor.Id });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var a = await ClonarAsync(r);
        Assert.Equal(["id", "vehiculo_id", "ninera", "conductor_id", "asignado_por", "created_at", "updated_at"], a.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("NIN-123", a.GetProperty("ninera").GetString());
        Assert.Equal(adminId, a.GetProperty("asignado_por").GetGuid());
        Assert.Equal(conductor.Id, a.GetProperty("conductor_id").GetGuid());
    }

    [Fact]
    public async Task Asignar_sin_conductor_es_valido()
    {
        var (token, _) = await AutenticarAsync(clsRol.SUPER_ADMIN, "super@ejemplo.com");
        var (vehiculoId, _) = await Sembrador.VehiculoConRutaAsync("VIN-ASIG2");

        var r = await Cliente(token).PostAsJsonAsync("/api/asignaciones", new { vehiculo_id = vehiculoId, ninera = "N-1" });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ClonarAsync(r)).GetProperty("conductor_id").ValueKind);
    }

    [Fact]
    public async Task Asignacion_con_referencias_o_datos_invalidos_responde_el_error_correspondiente()
    {
        var (token, _) = await AutenticarAsync(clsRol.ADMIN, "admin@ejemplo.com");
        var inspector = await Fabrica.SembrarUsuarioAsync("insp@ejemplo.com", "x", clsRol.INSPECTOR);
        var (vehiculoId, _) = await Sembrador.VehiculoConRutaAsync("VIN-ASIG3");

        var sinVehiculo = await Cliente(token).PostAsJsonAsync("/api/asignaciones", new { vehiculo_id = Guid.NewGuid(), ninera = "N" });
        var sinConductor = await Cliente(token).PostAsJsonAsync("/api/asignaciones", new { vehiculo_id = vehiculoId, ninera = "N", conductor_id = Guid.NewGuid() });
        var noEsConductor = await Cliente(token).PostAsJsonAsync("/api/asignaciones", new { vehiculo_id = vehiculoId, ninera = "N", conductor_id = inspector.Id });
        var sinNinera = await Cliente(token).PostAsJsonAsync("/api/asignaciones", new { vehiculo_id = vehiculoId, ninera = " " });
        var idMalo = await Cliente(token).PostAsync("/api/asignaciones", Json("{\"vehiculo_id\":\"no-es-guid\",\"ninera\":\"N\"}"));

        Assert.Equal(HttpStatusCode.NotFound, sinVehiculo.StatusCode);
        Assert.Equal("El vehículo indicado no existe", (await ClonarAsync(sinVehiculo)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, sinConductor.StatusCode);
        Assert.Equal("El conductor indicado no existe", (await ClonarAsync(sinConductor)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, noEsConductor.StatusCode);
        Assert.Equal("El usuario indicado no es un conductor", (await ClonarAsync(noEsConductor)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinNinera.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, idMalo.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAsignaciones"));
    }

    [Theory]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    public async Task Asignaciones_y_etapas_son_de_administracion(string rol)
    {
        var (token, _) = await AutenticarAsync(rol, "u@ejemplo.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(token).PostAsync("/api/asignaciones", Json("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(token).PostAsync("/api/etapas", Json("{}"))).StatusCode);
    }

    [Fact]
    public async Task Admin_crea_una_etapa_con_requiere_inspeccion_por_defecto()
    {
        var (token, _) = await AutenticarAsync(clsRol.ADMIN, "admin@ejemplo.com");
        var (vehiculoId, _) = await Sembrador.VehiculoConRutaAsync("VIN-ETAPA", cantidadEtapas: 0);
        var puntoId = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblPuntosCustodia WHERE Nombre = 'concesionario'"))!;

        var r = await Cliente(token).PostAsJsonAsync("/api/etapas", new { vehiculo_id = vehiculoId, punto_custodia_id = puntoId, orden = 1 });
        var soloFirma = await Cliente(token).PostAsJsonAsync("/api/etapas", new { vehiculo_id = vehiculoId, punto_custodia_id = puntoId, orden = 2, requiere_inspeccion = false });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var e = await ClonarAsync(r);
        Assert.Equal(["id", "vehiculo_id", "punto_custodia_id", "orden", "requiere_inspeccion", "created_at", "updated_at"], e.EnumerateObject().Select(p => p.Name).ToList());
        Assert.True(e.GetProperty("requiere_inspeccion").GetBoolean());
        Assert.False((await ClonarAsync(soloFirma)).GetProperty("requiere_inspeccion").GetBoolean());
    }

    [Fact]
    public async Task Etapa_con_orden_repetido_es_conflicto_y_con_referencias_inexistentes_es_404()
    {
        var (token, _) = await AutenticarAsync(clsRol.ADMIN, "admin@ejemplo.com");
        var (vehiculoId, _) = await Sembrador.VehiculoConRutaAsync("VIN-ETAPA2");
        var puntoId = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblPuntosCustodia WHERE Nombre = 'concesionario'"))!;

        var repetido = await Cliente(token).PostAsJsonAsync("/api/etapas", new { vehiculo_id = vehiculoId, punto_custodia_id = puntoId, orden = 1 });
        var sinVehiculo = await Cliente(token).PostAsJsonAsync("/api/etapas", new { vehiculo_id = Guid.NewGuid(), punto_custodia_id = puntoId, orden = 9 });
        var ordenCero = await Cliente(token).PostAsJsonAsync("/api/etapas", new { vehiculo_id = vehiculoId, punto_custodia_id = puntoId, orden = 0 });

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("Ya existe una etapa con ese orden para el vehículo", (await ClonarAsync(repetido)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, sinVehiculo.StatusCode);
        Assert.Equal("El vehículo o el punto de custodia no existen", (await ClonarAsync(sinVehiculo)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, ordenCero.StatusCode);
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
    }
}
