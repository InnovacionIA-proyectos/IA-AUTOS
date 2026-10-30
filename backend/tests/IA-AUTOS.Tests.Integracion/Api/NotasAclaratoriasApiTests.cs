using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Notas aclaratorias: una aclaracion de una inspeccion, un hallazgo o una transferencia es un registro nuevo (tambien con la inspeccion cerrada) que agrega su autor, el administrador o el
/// coordinador de calidad; nadie la edita ni la borra y queda en la auditoria.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed class NotasAclaratoriasApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Una inspeccion de salida cerrada del inspector con un hallazgo y su transferencia firmada por el inspector (entrega) y el conductor (recibe).</summary>
    private sealed record Escenario(Guid Inspeccion, Guid Hallazgo, Guid Viaje, string Vin, Sesion Inspector, Sesion Conductor, Sesion Coordinador, Sesion Admin);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync(bool conTransferencia = true)
    {
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-NOTA-0001", conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        var zona = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!;
        var (inspeccion, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], inspector.UsuarioId, zona);
        if (conTransferencia)
        {
            await Sembrador.TransferenciaAsync(inspeccion, inspector.UsuarioId, conductor.UsuarioId, "firmada");
        }

        return new Escenario(inspeccion, hallazgo, await Sembrador.ViajeDeEtapaAsync(etapas[0]), "VIN-NOTA-0001", inspector, conductor, coordinador, admin);
    }

    private Task<HttpResponseMessage> PostAsync(Sesion quien, string ruta, object cuerpo) => Cliente(quien.Token).PostAsJsonAsync(ruta, cuerpo);

    private static string RutaInspeccion(Escenario e) => $"/api/inspecciones/{e.Inspeccion}/notas";

    private static string RutaHallazgo(Escenario e) => $"/api/hallazgos/{e.Hallazgo}/notas";

    private static string RutaTransferencia(Escenario e) => $"/api/inspecciones/{e.Inspeccion}/transferencia/notas";

    // ------------------------------------------------------------------------------------ agregar

    [Fact]
    public async Task El_autor_agrega_una_nota_a_su_inspeccion_cerrada_y_el_original_no_cambia()
    {
        var e = await EscenarioAsync();
        var antes = await Sembrador.EscalarAsync("SELECT CONCAT(Estado, '|', UpdatedAt, '|', Version) FROM dbo.tblInspecciones WHERE Id = @i", ("@i", e.Inspeccion));

        var r = await PostAsync(e.Inspector, RutaInspeccion(e), new { texto = "  Se aclara que el golpe ya estaba al recibir  " });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var nota = await ComoJsonAsync(r);
        Assert.Equal(["id", "entidad", "entidad_id", "autor_id", "autor_nombre", "autor_rol", "texto", "created_at"], nota.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("inspeccion", nota.GetProperty("entidad").GetString());
        Assert.Equal(e.Inspeccion, nota.GetProperty("entidad_id").GetGuid());
        Assert.Equal(e.Inspector.UsuarioId, nota.GetProperty("autor_id").GetGuid());
        Assert.Equal("Nombre inspector@ejemplo.com", nota.GetProperty("autor_nombre").GetString());
        Assert.Equal("inspector", nota.GetProperty("autor_rol").GetString());
        Assert.Equal("Se aclara que el golpe ya estaba al recibir", nota.GetProperty("texto").GetString());
        Assert.Equal(antes, await Sembrador.EscalarAsync("SELECT CONCAT(Estado, '|', UpdatedAt, '|', Version) FROM dbo.tblInspecciones WHERE Id = @i", ("@i", e.Inspeccion)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotasAclaratorias WHERE Entidad = 'inspeccion' AND EntidadId = @i", ("@i", e.Inspeccion)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'nota.crear'"));
    }

    [Fact]
    public async Task El_autor_de_la_inspeccion_agrega_notas_a_su_hallazgo_y_los_firmantes_a_la_transferencia()
    {
        var e = await EscenarioAsync();

        var aHallazgo = await PostAsync(e.Inspector, RutaHallazgo(e), new { texto = "Aclaracion del hallazgo" });
        var deInspector = await PostAsync(e.Inspector, RutaTransferencia(e), new { texto = "Firmo el inspector" });
        var deConductor = await PostAsync(e.Conductor, RutaTransferencia(e), new { texto = "Firmo el conductor" });

        Assert.Equal(HttpStatusCode.Created, aHallazgo.StatusCode);
        Assert.Equal("hallazgo", (await ComoJsonAsync(aHallazgo)).GetProperty("entidad").GetString());
        Assert.Equal(HttpStatusCode.Created, deInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Created, deConductor.StatusCode);
        var transferencia = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @i", ("@i", e.Inspeccion)))!;
        var guardada = await ComoJsonAsync(deConductor);
        Assert.Equal("transferencia", guardada.GetProperty("entidad").GetString());
        Assert.Equal(transferencia, guardada.GetProperty("entidad_id").GetGuid());
    }

    [Theory]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.SUPER_ADMIN)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task El_administrador_y_el_coordinador_agregan_notas_a_cualquier_registro_de_su_cliente(string rol)
    {
        var e = await EscenarioAsync();
        var sesion = await SesionAsync($"{rol}-nota@ejemplo.com", rol);

        var rutas = new[] { RutaInspeccion(e), RutaHallazgo(e), RutaTransferencia(e) };

        foreach (var ruta in rutas)
        {
            var r = await PostAsync(sesion, ruta, new { texto = "Aclaracion de gestion" });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            Assert.Equal(rol, (await ComoJsonAsync(r)).GetProperty("autor_rol").GetString());
        }
    }

    [Fact]
    public async Task Un_tercero_que_no_es_autor_ni_administrador_ni_coordinador_recibe_403_y_la_nota_no_se_guarda()
    {
        var e = await EscenarioAsync();
        var otroInspector = await SesionAsync("otro-inspector@ejemplo.com", clsRol.INSPECTOR);
        var otroConductor = await SesionAsync("otro-conductor@ejemplo.com", clsRol.CONDUCTOR);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL);

        foreach (var tercero in new[] { otroInspector, otroConductor, comercial })
        {
            foreach (var ruta in new[] { RutaInspeccion(e), RutaHallazgo(e), RutaTransferencia(e) })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(tercero, ruta, new { texto = "No me corresponde" })).StatusCode);
            }
        }

        // El conductor de la transferencia firmo, pero no es autor de la inspeccion ni del hallazgo del inspector.
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(e.Conductor, RutaInspeccion(e), new { texto = "No es mi inspeccion" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(e.Conductor, RutaHallazgo(e), new { texto = "No es mi hallazgo" })).StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotasAclaratorias"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsJsonAsync(RutaInspeccion(e), new { texto = "x" })).StatusCode);
    }

    [Fact]
    public async Task Quien_firma_una_transferencia_que_solo_firmo_la_otra_parte_no_la_aclara()
    {
        var e = await EscenarioAsync(conTransferencia: false);
        // Solo firmo quien entrega (el inspector): quien recibe aun no firmo.
        await Sembrador.TransferenciaAsync(e.Inspeccion, e.Inspector.UsuarioId, e.Conductor.UsuarioId, tipoCierre: null);

        var deQuienFirmo = await PostAsync(e.Inspector, RutaTransferencia(e), new { texto = "Firme yo" });
        var deQuienNoFirmo = await PostAsync(e.Conductor, RutaTransferencia(e), new { texto = "Todavia no firmo" });

        Assert.Equal(HttpStatusCode.Created, deQuienFirmo.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deQuienNoFirmo.StatusCode);
    }

    [Fact]
    public async Task El_administrador_o_el_coordinador_de_otro_cliente_reciben_403()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var adminOtro = await SesionAsync("admin-otro@ejemplo.com", clsRol.ADMIN, otroCliente);
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);

        foreach (var sesion in new[] { adminOtro, coordinadorOtro })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(sesion, RutaInspeccion(e), new { texto = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(sesion.Token).GetAsync(RutaInspeccion(e))).StatusCode);
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotasAclaratorias"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"texto\":\"   \"}")]
    [InlineData("{\"texto\":null}")]
    public async Task Una_nota_sin_texto_responde_400(string cuerpo)
    {
        var e = await EscenarioAsync();

        var r = await Cliente(e.Inspector.Token).PostAsync(RutaInspeccion(e), Json(cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await ComoJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("texto", out _));
    }

    [Fact]
    public async Task El_texto_admite_mil_caracteres_y_no_mil_uno()
    {
        var e = await EscenarioAsync();

        var larga = await PostAsync(e.Inspector, RutaInspeccion(e), new { texto = new string('t', 1001) });
        var justa = await PostAsync(e.Inspector, RutaInspeccion(e), new { texto = new string('t', 1000) });

        Assert.Equal(HttpStatusCode.BadRequest, larga.StatusCode);
        Assert.Equal(HttpStatusCode.Created, justa.StatusCode);
    }

    [Fact]
    public async Task Un_registro_que_no_existe_o_una_inspeccion_sin_transferencia_responden_404()
    {
        var e = await EscenarioAsync(conTransferencia: false);

        var inspeccion = await PostAsync(e.Admin, $"/api/inspecciones/{Guid.NewGuid()}/notas", new { texto = "x" });
        var hallazgo = await PostAsync(e.Admin, $"/api/hallazgos/{Guid.NewGuid()}/notas", new { texto = "x" });
        var sinTransferencia = await PostAsync(e.Admin, RutaTransferencia(e), new { texto = "x" });

        Assert.Equal(HttpStatusCode.NotFound, inspeccion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hallazgo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sinTransferencia.StatusCode);
    }

    // ----------------------------------------------------------------------------------- consultar

    [Fact]
    public async Task Cada_ruta_lista_sus_notas_de_la_mas_antigua_a_la_mas_reciente_y_el_informe_el_inventario_y_el_hallazgo_las_traen()
    {
        var e = await EscenarioAsync();
        await PostAsync(e.Inspector, RutaInspeccion(e), new { texto = "Primera" });
        await PostAsync(e.Admin, RutaInspeccion(e), new { texto = "Segunda" });
        await PostAsync(e.Inspector, RutaHallazgo(e), new { texto = "Del hallazgo" });
        await PostAsync(e.Conductor, RutaTransferencia(e), new { texto = "De la transferencia" });

        var deInspeccion = await ComoJsonAsync(await Cliente(e.Coordinador.Token).GetAsync(RutaInspeccion(e)));
        var deHallazgo = await ComoJsonAsync(await Cliente(e.Coordinador.Token).GetAsync(RutaHallazgo(e)));
        var deTransferencia = await ComoJsonAsync(await Cliente(e.Coordinador.Token).GetAsync(RutaTransferencia(e)));
        var informe = await ComoJsonAsync(await Cliente(e.Coordinador.Token).GetAsync($"/api/inspecciones/{e.Inspeccion}"));
        var inventario = await ComoJsonAsync(await Cliente(e.Coordinador.Token).GetAsync($"/api/vehiculos/{e.Vin}/inventario"));
        var detalleHallazgo = await ComoJsonAsync(await Cliente(e.Coordinador.Token).GetAsync($"/api/hallazgos/{e.Hallazgo}"));

        Assert.Equal(["Primera", "Segunda"], deInspeccion.EnumerateArray().Select(n => n.GetProperty("texto").GetString()).ToList());
        Assert.Equal("Del hallazgo", Assert.Single(deHallazgo.EnumerateArray()).GetProperty("texto").GetString());
        Assert.Equal("De la transferencia", Assert.Single(deTransferencia.EnumerateArray()).GetProperty("texto").GetString());
        Assert.Equal(["Primera", "Segunda"], informe.GetProperty("notas_aclaratorias").EnumerateArray().Select(n => n.GetProperty("texto").GetString()).ToList());
        Assert.Equal("De la transferencia", Assert.Single(informe.GetProperty("transferencia").GetProperty("notas_aclaratorias").EnumerateArray()).GetProperty("texto").GetString());
        var deInventario = inventario.GetProperty("etapas").EnumerateArray().SelectMany(et => et.GetProperty("inspecciones").EnumerateArray()).Single(i => i.GetProperty("id").GetGuid() == e.Inspeccion);
        Assert.Equal(2, deInventario.GetProperty("notas_aclaratorias").GetArrayLength());
        Assert.Equal("Del hallazgo", Assert.Single(detalleHallazgo.GetProperty("notas_aclaratorias").EnumerateArray()).GetProperty("texto").GetString());
        // El hallazgo original no cambia: sigue igual en el informe.
        Assert.Equal(e.Hallazgo, Assert.Single(informe.GetProperty("hallazgos").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Sin_notas_los_arreglos_van_vacios()
    {
        var e = await EscenarioAsync();

        var informe = await ComoJsonAsync(await Cliente(e.Coordinador.Token).GetAsync($"/api/inspecciones/{e.Inspeccion}"));
        var detalleHallazgo = await ComoJsonAsync(await Cliente(e.Coordinador.Token).GetAsync($"/api/hallazgos/{e.Hallazgo}"));

        Assert.Equal(0, informe.GetProperty("notas_aclaratorias").GetArrayLength());
        Assert.Equal(0, informe.GetProperty("transferencia").GetProperty("notas_aclaratorias").GetArrayLength());
        Assert.Equal(0, detalleHallazgo.GetProperty("notas_aclaratorias").GetArrayLength());
    }

    [Fact]
    public async Task Solo_ve_las_notas_quien_ve_el_registro()
    {
        var e = await EscenarioAsync();
        await PostAsync(e.Inspector, RutaHallazgo(e), new { texto = "Del hallazgo" });
        var inspectorAjeno = await SesionAsync("ajeno@ejemplo.com", clsRol.INSPECTOR);

        // Un inspector sin el VIN en sus viajes no ve nada; el conductor tampoco ve el hallazgo del inspector pero si la inspeccion de su viaje.
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(inspectorAjeno.Token).GetAsync(RutaInspeccion(e))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(e.Conductor.Token).GetAsync(RutaHallazgo(e))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(e.Conductor.Token).GetAsync(RutaInspeccion(e))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(e.Inspector.Token).GetAsync(RutaHallazgo(e))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Cliente(e.Admin.Token).GetAsync($"/api/inspecciones/{Guid.NewGuid()}/notas")).StatusCode);
    }

    // ------------------------------------------------------------------------- nadie edita ni borra

    [Fact]
    public async Task No_hay_forma_de_editar_ni_de_borrar_una_nota_ni_por_la_api_ni_por_la_base()
    {
        var e = await EscenarioAsync();
        var nota = (await ComoJsonAsync(await PostAsync(e.Inspector, RutaInspeccion(e), new { texto = "Definitiva" }))).GetProperty("id").GetGuid();

        var patch = await Cliente(e.Admin.Token).PatchAsync($"{RutaInspeccion(e)}/{nota}", JsonContent.Create(new { texto = "Otra" }));
        var delete = await Cliente(e.Admin.Token).DeleteAsync($"{RutaInspeccion(e)}/{nota}");
        var put = await Cliente(e.Admin.Token).PutAsJsonAsync($"{RutaInspeccion(e)}/{nota}", new { texto = "Otra" });

        Assert.All(new[] { patch, delete, put }, r => Assert.True(r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed));
        var errorUpdate = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => Sembrador.EjecutarAsync("UPDATE dbo.tblNotasAclaratorias SET Texto = N'Otra'"));
        var errorDelete = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => Sembrador.EjecutarAsync("DELETE FROM dbo.tblNotasAclaratorias"));
        Assert.Equal(51005, errorUpdate.Number);
        Assert.Equal(51003, errorDelete.Number);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotasAclaratorias WHERE Texto = N'Definitiva'"));
    }
}
