using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Las mejoras menores del contrato (S7-HU-10.1): el motivo fijo de los rechazos de viajes, usuarios y constancia, la baja de un vehiculo que esta en un viaje activo y su reactivacion, y el usuario por su identificador
/// con la visibilidad por cliente del listado. (Los nombres de quien fijo una ubicacion y de quien decidio una propuesta estan en <c>UbicacionDeLugaresApiTests</c> y el aviso unico del super administrador en
/// <c>NotificacionesApiTests</c>.)
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class MejorasMenoresApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static string Motivo(JsonElement error) => error.GetProperty("details").GetProperty("motivo").GetString()!;

    private async Task<Guid> ClienteAsync(string codigo) => (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = @c", ("@c", codigo)))!;

    private async Task<Guid> OtroClienteAsync()
    {
        var otro = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otro));
        return otro;
    }

    // ------------------------------------------------------------------------------------------------ motivo fijo

    [Theory]
    [InlineData("finalizado", "viaje_finalizado")]
    [InlineData("cancelado", "viaje_cancelado")]
    public async Task Corregir_un_viaje_que_ya_no_admite_cambios_se_rechaza_con_su_motivo_fijo(string estado, string motivo)
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, await ClienteAsync("sofasa"));
        var viaje = await Sembrador.ViajeAsync(estado: estado);

        var r = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viaje}", Cuerpo(new { placa = "NUEVA123" }));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var error = await JsonAsync(r);
        Assert.Equal("CONFLICT", error.GetProperty("code").GetString());
        Assert.Equal(motivo, Motivo(error));
        Assert.Contains("no admite cambios", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Un_viaje_que_no_existe_y_un_codigo_repetido_traen_su_motivo_fijo()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, await ClienteAsync("sofasa"));
        await Sembrador.ViajeAsync(codigoMtrack: "MTK-REPETIDO");
        var otro = await Sembrador.ViajeAsync();

        var inexistente = await Cliente(coordinador.Token).GetAsync($"/api/viajes/{Guid.NewGuid()}");
        var repetido = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{otro}", Cuerpo(new { codigo_mtrack = "MTK-REPETIDO" }));

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("viaje_no_existe", Motivo(await JsonAsync(inexistente)));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("codigo_mtrack_duplicado", Motivo(await JsonAsync(repetido)));
    }

    [Fact]
    public async Task Los_rechazos_de_usuarios_traen_su_motivo_fijo()
    {
        var sofasa = await ClienteAsync("sofasa");
        var otro = await OtroClienteAsync();
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN, sofasa);
        var deOtro = await SesionAsync("deotro@ejemplo.com", clsRol.INSPECTOR, otro);

        var propio = await Cliente(admin.Token).DeleteAsync($"/api/usuarios/{admin.UsuarioId}");
        var ajeno = await Cliente(admin.Token).PatchAsync($"/api/usuarios/{deOtro.UsuarioId}", Cuerpo(new { nombre = "Otro nombre" }));
        var inexistente = await Cliente(admin.Token).DeleteAsync($"/api/usuarios/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Conflict, propio.StatusCode);
        Assert.Equal("usuario_propio", Motivo(await JsonAsync(propio)));
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Equal("usuario_no_existe", Motivo(await JsonAsync(ajeno)));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("usuario_no_existe", Motivo(await JsonAsync(inexistente)));
    }

    [Fact]
    public async Task La_descarga_de_la_constancia_se_rechaza_con_su_motivo_fijo()
    {
        var sofasa = await ClienteAsync("sofasa");
        var otro = await OtroClienteAsync();
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, sofasa);
        var (_, etapasSofasa) = await Sembrador.VehiculoConRutaAsync("VIN-CON-SOF", operacion: "nacional");
        var (_, etapasOtro) = await Sembrador.VehiculoConRutaAsync("VIN-CON-OTRO", operacion: "nacional");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET ClienteId = @c WHERE Vin = 'VIN-CON-OTRO'", ("@c", otro));
        var informeSofasa = await Sembrador.InformeAsync(etapasSofasa[0], inspector.UsuarioId, cerrada: true);
        var informeOtro = await Sembrador.InformeAsync(etapasOtro[0], inspector.UsuarioId, cerrada: true);

        var sinTransferencia = await Cliente(coordinador.Token).GetAsync($"/api/inspecciones/{informeSofasa}/transferencia/constancia/pdf");
        var deOtroCliente = await Cliente(coordinador.Token).GetAsync($"/api/inspecciones/{informeOtro}/transferencia/constancia/pdf");
        var inexistente = await Cliente(coordinador.Token).GetAsync($"/api/inspecciones/{Guid.NewGuid()}/transferencia/constancia/pdf");

        Assert.Equal(HttpStatusCode.NotFound, sinTransferencia.StatusCode);
        Assert.Equal("inspeccion_sin_transferencia", Motivo(await JsonAsync(sinTransferencia)));
        Assert.Equal(HttpStatusCode.Forbidden, deOtroCliente.StatusCode);
        Assert.Equal("inspeccion_de_otro_cliente", Motivo(await JsonAsync(deOtroCliente)));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("inspeccion_no_existe", Motivo(await JsonAsync(inexistente)));
    }

    // ------------------------------------------------------------------------------------------------ baja y reactivacion

    [Fact]
    public async Task Un_carro_en_un_viaje_programado_o_en_curso_no_se_da_de_baja_y_el_rechazo_nombra_el_viaje()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, await ClienteAsync("sofasa"));
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-BAJA-VIAJE", operacion: "nacional");
        var viaje = await Sembrador.ViajeDeEtapaAsync(etapas[0]);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET CodigoMtrack = 'MT-2026-0417' WHERE Id = @v", ("@v", viaje));

        foreach (var estado in new[] { "programado", "en_curso" })
        {
            await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = @e WHERE Id = @v", ("@e", estado), ("@v", viaje));

            var r = await Cliente(coordinador.Token).DeleteAsync("/api/vehiculos/VIN-BAJA-VIAJE");

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("vehiculo_en_viaje_activo", Motivo(error));
            Assert.Equal("MT-2026-0417", error.GetProperty("details").GetProperty("codigo_mtrack").GetString());
            Assert.Equal(viaje, error.GetProperty("details").GetProperty("viaje_id").GetGuid());
            Assert.Contains("MT-2026-0417", error.GetProperty("message").GetString());
            Assert.Equal(true, await Sembrador.EscalarAsync("SELECT Activo FROM dbo.tblVehiculos WHERE Vin = 'VIN-BAJA-VIAJE'"));
        }

        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = 'finalizado' WHERE Id = @v", ("@v", viaje));
        Assert.Equal(HttpStatusCode.OK, (await Cliente(coordinador.Token).DeleteAsync("/api/vehiculos/VIN-BAJA-VIAJE")).StatusCode);
        Assert.Equal(false, await Sembrador.EscalarAsync("SELECT Activo FROM dbo.tblVehiculos WHERE Vin = 'VIN-BAJA-VIAJE'"));
    }

    [Fact]
    public async Task Un_carro_dado_de_baja_se_reactiva_con_su_permiso_y_queda_en_la_auditoria()
    {
        var sofasa = await ClienteAsync("sofasa");
        var otro = await OtroClienteAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, sofasa);
        var deOtro = await SesionAsync("coordotro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otro);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        await Sembrador.VehiculoAsync("VIN-REACTIVAR");
        Assert.Equal(HttpStatusCode.OK, (await Cliente(coordinador.Token).DeleteAsync("/api/vehiculos/VIN-REACTIVAR")).StatusCode);

        var sinPermiso = await Cliente(inspector.Token).PostAsync("/api/vehiculos/VIN-REACTIVAR/reactivar", null);
        var deOtroCliente = await Cliente(deOtro.Token).PostAsync("/api/vehiculos/VIN-REACTIVAR/reactivar", null);
        var inexistente = await Cliente(coordinador.Token).PostAsync("/api/vehiculos/NO-EXISTE/reactivar", null);
        var reactivado = await Cliente(coordinador.Token).PostAsync("/api/vehiculos/VIN-REACTIVAR/reactivar", null);
        var otraVez = await Cliente(coordinador.Token).PostAsync("/api/vehiculos/VIN-REACTIVAR/reactivar", null);

        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deOtroCliente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reactivado.StatusCode);
        Assert.Equal("reactivado", (await JsonAsync(reactivado)).GetProperty("modo").GetString());
        Assert.Equal(HttpStatusCode.OK, otraVez.StatusCode);
        Assert.Equal(true, await Sembrador.EscalarAsync("SELECT Activo FROM dbo.tblVehiculos WHERE Vin = 'VIN-REACTIVAR'"));
        // Quien lo reactivo y cuando queda registrado una sola vez; el carro vuelve a buscarse.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'vehiculo.reactivar' AND UsuarioId = @u", ("@u", coordinador.UsuarioId)));
        var busqueda = await JsonAsync(await Cliente(coordinador.Token).GetAsync("/api/vehiculos?vin=VIN-REACTIVAR"));
        Assert.Equal(1, busqueda.GetArrayLength());
    }

    // ------------------------------------------------------------------------------------------------ usuario por id

    [Fact]
    public async Task Se_pide_un_usuario_por_su_id_con_la_visibilidad_por_cliente_del_listado()
    {
        var sofasa = await ClienteAsync("sofasa");
        var otro = await OtroClienteAsync();
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN, sofasa);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR, sofasa);
        var deOtro = await SesionAsync("deotro@ejemplo.com", clsRol.INSPECTOR, otro);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var inactivo = await Fabrica.SembrarUsuarioAsync("inactivo@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR, nombre: "Luis Mejía", clienteId: sofasa);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblUsuarios SET Activo = 0 WHERE Id = @u", ("@u", inactivo.Id));

        var completo = await Cliente(admin.Token).GetAsync($"/api/usuarios/{inspector.UsuarioId}");
        var otroClienteParaAdmin = await Cliente(admin.Token).GetAsync($"/api/usuarios/{deOtro.UsuarioId}");
        var inexistente = await Cliente(admin.Token).GetAsync($"/api/usuarios/{Guid.NewGuid()}");
        var delSuper = await Cliente(superAdmin.Token).GetAsync($"/api/usuarios/{deOtro.UsuarioId}");
        var reducido = await Cliente(inspector.Token).GetAsync($"/api/usuarios/{admin.UsuarioId}");
        var reducidoDeOtro = await Cliente(inspector.Token).GetAsync($"/api/usuarios/{deOtro.UsuarioId}");
        var reducidoInactivo = await Cliente(inspector.Token).GetAsync($"/api/usuarios/{inactivo.Id}");
        var anonimo = await Cliente().GetAsync($"/api/usuarios/{inspector.UsuarioId}");

        Assert.Equal(HttpStatusCode.OK, completo.StatusCode);
        var usuario = await JsonAsync(completo);
        Assert.Equal(inspector.UsuarioId, usuario.GetProperty("id").GetGuid());
        Assert.Equal("inspector@ejemplo.com", usuario.GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.NotFound, otroClienteParaAdmin.StatusCode);
        Assert.Equal("usuario_no_existe", Motivo(await JsonAsync(otroClienteParaAdmin)));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delSuper.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reducido.StatusCode);
        var basico = await JsonAsync(reducido);
        Assert.Equal(["id", "nombre", "rol"], basico.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("admin", basico.GetProperty("rol").GetString());
        Assert.Equal(HttpStatusCode.NotFound, reducidoDeOtro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reducidoInactivo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }
}
