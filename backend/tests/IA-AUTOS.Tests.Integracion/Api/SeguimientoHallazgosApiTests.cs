using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Seguimiento del hallazgo: el coordinador (y comercial, admin y super admin) lo lleva a validado, rechazado, recuperado o derogado; un V2 se valida con la foto del sello de
/// quien valida; la respuesta dice si la evidencia esta completa y que falta; todo queda en un historial que no se modifica.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class SeguimientoHallazgosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-07-17T12:30:15.123Z";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private Task<HttpResponseMessage> CambiarAsync(Sesion quien, Guid hallazgo, object cuerpo) =>
        Cliente(quien.Token).PostAsJsonAsync($"/api/hallazgos/{hallazgo}/seguimiento", cuerpo);

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var bytes = FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var r = await Cliente(quien.Token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await ComoJsonAsync(r);
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    /// <summary>Un VIN con su viaje (con el inspector y el conductor) y un hallazgo en la zona «frente» de una inspeccion de salida.</summary>
    private sealed record Escenario(Guid Viaje, Guid[] Etapas, Guid Inspeccion, Guid Hallazgo, Sesion Inspector, Sesion Conductor, Guid Zona);

    private async Task<Escenario> HallazgoAsync(
        string? nivel = "v1", bool cerrada = true, bool conFoto = true, string origen = "inspector", string vin = "VIN-SEG-0001", string email = "insp@ejemplo.com", string emailConductor = "cond@ejemplo.com")
    {
        var inspector = await SesionAsync(email, clsRol.INSPECTOR);
        var conductor = await SesionAsync(emailConductor, clsRol.CONDUCTOR);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        var zona = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!;
        var (inspeccion, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], inspector.UsuarioId, zona, nivel: nivel, conFoto: conFoto, origen: origen, cerrada: cerrada);
        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), etapas, inspeccion, hallazgo, inspector, conductor, zona);
    }

    private async Task<string?> EstadoGuardadoAsync(Guid hallazgo) =>
        (string?)await Sembrador.EscalarAsync("SELECT EstadoSeguimiento FROM dbo.tblHallazgos WHERE Id = @h", ("@h", hallazgo));

    // -------------------------------------------------------------------------------------------- quien puede

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.SUPER_ADMIN)]
    public async Task Los_roles_de_gestion_cambian_el_seguimiento_y_el_cambio_queda_en_el_historial(string rol)
    {
        var e = await HallazgoAsync(nivel: "v1");
        var quien = await SesionAsync("gestor@ejemplo.com", rol);

        var r = await CambiarAsync(quien, e.Hallazgo, new { estado = "rechazado", nota = "  Se rechaza el vehículo  " });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var h = await ComoJsonAsync(r);
        Assert.Equal(e.Hallazgo, h.GetProperty("id").GetGuid());
        Assert.Equal("rechazado", h.GetProperty("estado_seguimiento").GetString());
        Assert.Equal("Se rechaza el vehículo", h.GetProperty("nivel_accion").GetString());
        Assert.Equal("rechazado", await EstadoGuardadoAsync(e.Hallazgo));

        var historial = await ComoJsonAsync(await Cliente(quien.Token).GetAsync($"/api/hallazgos/{e.Hallazgo}/seguimiento"));
        var fila = Assert.Single(historial.EnumerateArray());
        Assert.Equal(
            ["id", "accion", "estado_anterior", "estado_nuevo", "usuario_id", "usuario_nombre", "nota", "evidencia_sello", "detalle", "created_at"],
            fila.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("seguimiento", fila.GetProperty("accion").GetString());
        Assert.Equal("pendiente", fila.GetProperty("estado_anterior").GetString());
        Assert.Equal("rechazado", fila.GetProperty("estado_nuevo").GetString());
        Assert.Equal(quien.UsuarioId, fila.GetProperty("usuario_id").GetGuid());
        Assert.Equal("Nombre gestor@ejemplo.com", fila.GetProperty("usuario_nombre").GetString());
        Assert.Equal("Se rechaza el vehículo", fila.GetProperty("nota").GetString());
        Assert.Equal(JsonValueKind.Null, fila.GetProperty("evidencia_sello").ValueKind);
        Assert.Equal(JsonValueKind.Null, fila.GetProperty("detalle").ValueKind);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'hallazgo.seguimiento'"));
    }

    [Fact]
    public async Task Un_inspector_un_conductor_o_quien_no_inicio_sesion_no_cambian_ni_leen_el_seguimiento()
    {
        var e = await HallazgoAsync(nivel: "v1");

        var inspector = await CambiarAsync(e.Inspector, e.Hallazgo, new { estado = "rechazado" });
        var conductor = await CambiarAsync(e.Conductor, e.Hallazgo, new { estado = "rechazado" });
        var historialInspector = await Cliente(e.Inspector.Token).GetAsync($"/api/hallazgos/{e.Hallazgo}/seguimiento");
        var anonimo = await Cliente().PostAsJsonAsync($"/api/hallazgos/{e.Hallazgo}/seguimiento", new { estado = "rechazado" });

        Assert.Equal(HttpStatusCode.Forbidden, inspector.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, conductor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, historialInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal("pendiente", await EstadoGuardadoAsync(e.Hallazgo));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgoSeguimiento"));
    }

    [Fact]
    public async Task Las_capacidades_del_perfil_dicen_quien_gestiona_el_seguimiento()
    {
        foreach (var (rol, tiene) in new[]
        {
            (clsRol.COORDINADOR_CALIDAD, true), (clsRol.COMERCIAL, true), (clsRol.ADMIN, true), (clsRol.SUPER_ADMIN, true), (clsRol.INSPECTOR, false), (clsRol.CONDUCTOR, false),
        })
        {
            var sesion = await SesionAsync($"{rol}@ejemplo.com", rol);

            var me = await ComoJsonAsync(await Cliente(sesion.Token).GetAsync("/api/auth/me"));

            var capacidades = me.GetProperty("capacidades").EnumerateArray().Select(c => c.GetString()).ToList();
            Assert.Equal(tiene, capacidades.Contains("gestionar_seguimiento"));
        }
    }

    [Fact]
    public async Task Quien_gestiona_el_seguimiento_tambien_sube_la_foto_del_sello()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL);

        var (ruta, _) = await SubirAsync(coordinador);
        var (otra, _) = await SubirAsync(comercial);

        Assert.NotEqual(ruta, otra);
    }

    // ---------------------------------------------------------------------------------------------- estados

    [Theory]
    [InlineData("v1_plus", "recuperado")]
    [InlineData("v1", "rechazado")]
    [InlineData("v3", "derogado")]
    [InlineData("v1", "validado")]
    public async Task Cada_nivel_acepta_su_estado_de_llegada_y_el_servidor_no_impide_otro(string nivel, string estado)
    {
        var e = await HallazgoAsync(nivel: nivel);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var r = await CambiarAsync(coordinador, e.Hallazgo, new { estado });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(estado, (await ComoJsonAsync(r)).GetProperty("estado_seguimiento").GetString());
        Assert.Equal(estado, await EstadoGuardadoAsync(e.Hallazgo));
    }

    [Fact]
    public async Task Los_cambios_sucesivos_se_acumulan_en_el_historial_con_el_estado_anterior()
    {
        var e = await HallazgoAsync(nivel: "v1_plus");
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        await CambiarAsync(coordinador, e.Hallazgo, new { estado = "rechazado" });
        await CambiarAsync(coordinador, e.Hallazgo, new { estado = "recuperado", nota = "Se recupero antes del despacho" });

        var historial = (await ComoJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/hallazgos/{e.Hallazgo}/seguimiento"))).EnumerateArray().ToList();
        Assert.Equal(["pendiente>rechazado", "rechazado>recuperado"], historial.Select(f => $"{f.GetProperty("estado_anterior").GetString()}>{f.GetProperty("estado_nuevo").GetString()}").ToList());
        Assert.Equal("recuperado", await EstadoGuardadoAsync(e.Hallazgo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pendiente")]
    [InlineData("archivado")]
    [InlineData("")]
    public async Task Un_estado_que_no_es_validado_rechazado_recuperado_o_derogado_es_400(string? estado)
    {
        var e = await HallazgoAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var r = await CambiarAsync(coordinador, e.Hallazgo, new { estado });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await ComoJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("estado", out _));
        Assert.Equal("pendiente", await EstadoGuardadoAsync(e.Hallazgo));
    }

    [Fact]
    public async Task La_nota_admite_hasta_1000_caracteres()
    {
        var e = await HallazgoAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var larga = await CambiarAsync(coordinador, e.Hallazgo, new { estado = "rechazado", nota = new string('n', 1001) });
        var justa = await CambiarAsync(coordinador, e.Hallazgo, new { estado = "rechazado", nota = new string('n', 1000) });

        Assert.Equal(HttpStatusCode.BadRequest, larga.StatusCode);
        Assert.True((await ComoJsonAsync(larga)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("nota", out _));
        Assert.Equal(HttpStatusCode.OK, justa.StatusCode);
    }

    [Fact]
    public async Task Un_hallazgo_inexistente_es_404_y_uno_de_una_inspeccion_en_borrador_o_de_una_novedad_sin_revisar_es_409()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var borrador = await HallazgoAsync(cerrada: false, vin: "VIN-SEG-BORRADOR", email: "insp1@ejemplo.com", emailConductor: "cond1@ejemplo.com");
        var novedad = await HallazgoAsync(nivel: null, origen: "conductor", vin: "VIN-SEG-NOVEDAD", email: "insp2@ejemplo.com", emailConductor: "cond2@ejemplo.com");

        var inexistente = await CambiarAsync(coordinador, Guid.NewGuid(), new { estado = "rechazado" });
        var enBorrador = await CambiarAsync(coordinador, borrador.Hallazgo, new { estado = "rechazado" });
        var sinRevisar = await CambiarAsync(coordinador, novedad.Hallazgo, new { estado = "rechazado" });

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, enBorrador.StatusCode);
        Assert.Contains("borrador", (await ComoJsonAsync(enBorrador)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, sinRevisar.StatusCode);
        Assert.Contains("sin revisar", (await ComoJsonAsync(sinRevisar)).GetProperty("message").GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgoSeguimiento"));
    }

    // ---------------------------------------------------------------------------------------- sello de V2

    [Fact]
    public async Task Validar_un_hallazgo_v2_exige_la_foto_del_sello_y_esta_queda_como_evidencia_de_la_inspeccion_cerrada()
    {
        var e = await HallazgoAsync(nivel: "v2");
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var (ruta, hash) = await SubirAsync(coordinador);

        var sinSello = await CambiarAsync(coordinador, e.Hallazgo, new { estado = "validado" });
        var conSello = await CambiarAsync(coordinador, e.Hallazgo, new
        {
            estado = "validado",
            nota = "Validado por Sofasa",
            evidencia_sello = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 6.2, sello_lng = -75.5, precision = 5.0, dispositivo_id = "tablet-9", hash_sha256 = hash },
        });

        Assert.Equal(HttpStatusCode.BadRequest, sinSello.StatusCode);
        Assert.True((await ComoJsonAsync(sinSello)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("evidencia_sello", out _));
        Assert.Equal(HttpStatusCode.OK, conSello.StatusCode);
        Assert.Equal("validado", (await ComoJsonAsync(conSello)).GetProperty("estado_seguimiento").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsSello = 1 AND InspeccionId = @i AND HallazgoId IS NULL AND VistaId IS NULL", ("@i", e.Inspeccion)));

        var historial = (await ComoJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/hallazgos/{e.Hallazgo}/seguimiento"))).EnumerateArray().ToList();
        var sello = Assert.Single(historial).GetProperty("evidencia_sello");
        Assert.True(sello.GetProperty("es_sello").GetBoolean());
        Assert.StartsWith($"http://localhost/api/uploads/{ruta}?exp=", sello.GetProperty("url").GetString());
        Assert.Equal(hash, sello.GetProperty("hash_sha256").GetString());
        Assert.Equal(e.Inspeccion, sello.GetProperty("inspeccion_id").GetGuid());
        Assert.Equal(JsonValueKind.Null, sello.GetProperty("hallazgo_id").ValueKind);
    }

    [Fact]
    public async Task Validar_un_hallazgo_que_no_es_v2_no_exige_el_sello_y_uno_con_foto_invalida_es_400()
    {
        var e = await HallazgoAsync(nivel: "v1");
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var fotoNoSubida = await CambiarAsync(coordinador, e.Hallazgo, new
        {
            estado = "validado",
            evidencia_sello = new { storage_path = "3f2b6c1e-0000-4000-8000-123456789abc.png", sello_fecha = FECHA, sello_lat = 6.2, sello_lng = -75.5 },
        });
        var sinSello = await CambiarAsync(coordinador, e.Hallazgo, new { estado = "validado" });

        Assert.Equal(HttpStatusCode.BadRequest, fotoNoSubida.StatusCode);
        Assert.True((await ComoJsonAsync(fotoNoSubida)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("storage_path", out _));
        Assert.Equal(HttpStatusCode.OK, sinSello.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsSello = 1"));
    }

    // ------------------------------------------------------------------------------------- evidencia completa

    [Fact]
    public async Task Rechazar_nunca_se_bloquea_por_la_evidencia_pero_la_respuesta_dice_lo_que_falta()
    {
        var sinFotoNiCierre = await HallazgoAsync(nivel: "v1", conFoto: false, vin: "VIN-SEG-A", email: "insp-a@ejemplo.com", emailConductor: "cond-a@ejemplo.com");
        var conFotoSinCierre = await HallazgoAsync(nivel: "v1", conFoto: true, vin: "VIN-SEG-B", email: "insp-b@ejemplo.com", emailConductor: "cond-b@ejemplo.com");
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var a = await ComoJsonAsync(await CambiarAsync(coordinador, sinFotoNiCierre.Hallazgo, new { estado = "rechazado" }));
        var b = await ComoJsonAsync(await CambiarAsync(coordinador, conFotoSinCierre.Hallazgo, new { estado = "rechazado" }));

        Assert.Equal("rechazado", a.GetProperty("estado_seguimiento").GetString());
        Assert.False(a.GetProperty("evidencia_completa").GetBoolean());
        Assert.Equal(["foto_hallazgo", "transferencia_sin_cierre"], a.GetProperty("evidencia_faltante").EnumerateArray().Select(x => x.GetString()).ToList());
        Assert.False(b.GetProperty("evidencia_completa").GetBoolean());
        Assert.Equal(["transferencia_sin_cierre"], b.GetProperty("evidencia_faltante").EnumerateArray().Select(x => x.GetString()).ToList());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE EvidenciaCompleta = 1"));
    }

    [Theory]
    [InlineData("firmada", true, new string[0])]
    [InlineData("con_reservas", true, new string[0])]
    [InlineData("no_firma", false, new[] { "transferencia_no_firma" })]
    [InlineData(null, false, new[] { "transferencia_sin_cierre" })]
    public async Task La_evidencia_esta_completa_con_foto_y_transferencia_firmada_o_con_reservas_y_no_firma_no_cuenta(string? cierre, bool completa, string[] faltante)
    {
        var e = await HallazgoAsync(nivel: "v1");
        var testigo = await SesionAsync("testigo@ejemplo.com", clsRol.INSPECTOR);
        await Sembrador.TransferenciaAsync(e.Inspeccion, e.Inspector.UsuarioId, e.Conductor.UsuarioId, cierre, cierre == "no_firma" ? testigo.UsuarioId : null);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var h = await ComoJsonAsync(await CambiarAsync(coordinador, e.Hallazgo, new { estado = "rechazado" }));

        Assert.Equal(completa, h.GetProperty("evidencia_completa").GetBoolean());
        Assert.Equal(faltante, h.GetProperty("evidencia_faltante").EnumerateArray().Select(x => x.GetString()).ToList());
        Assert.Equal(completa ? 1 : 0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND EvidenciaCompleta = 1", ("@h", e.Hallazgo)));
    }

    [Fact]
    public async Task La_foto_del_sello_no_cuenta_como_la_foto_propia_del_hallazgo()
    {
        var e = await HallazgoAsync(nivel: "v2", conFoto: false);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var (ruta, hash) = await SubirAsync(coordinador);

        var h = await ComoJsonAsync(await CambiarAsync(coordinador, e.Hallazgo, new
        {
            estado = "validado",
            evidencia_sello = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 6.2, sello_lng = -75.5, hash_sha256 = hash },
        }));

        Assert.Contains("foto_hallazgo", h.GetProperty("evidencia_faltante").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Al_cerrarse_la_transferencia_se_recalcula_la_evidencia_de_los_hallazgos_de_la_inspeccion()
    {
        var e = await HallazgoAsync(nivel: "v1");
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND EvidenciaCompleta = 1", ("@h", e.Hallazgo)));

        var primera = await Cliente(e.Inspector.Token).PostAsJsonAsync(
            $"/api/inspecciones/{e.Inspeccion}/transferencia",
            new { entrega_usuario_id = e.Inspector.UsuarioId, recibe_usuario_id = e.Conductor.UsuarioId, firma = "M0 0 L10 10" });
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND EvidenciaCompleta = 1", ("@h", e.Hallazgo)));
        var segunda = await Cliente(e.Conductor.Token).PostAsJsonAsync($"/api/inspecciones/{e.Inspeccion}/transferencia", new { firma = "M5 5 L20 20" });

        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND EvidenciaCompleta = 1", ("@h", e.Hallazgo)));
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var h = await ComoJsonAsync(await CambiarAsync(coordinador, e.Hallazgo, new { estado = "rechazado" }));
        Assert.True(h.GetProperty("evidencia_completa").GetBoolean());
        Assert.Equal(0, h.GetProperty("evidencia_faltante").GetArrayLength());
    }

    [Fact]
    public async Task El_informe_muestra_lo_que_falta_a_la_evidencia_de_cada_hallazgo()
    {
        var e = await HallazgoAsync(nivel: "v1", conFoto: true);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var informe = await ComoJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/inspecciones/{e.Inspeccion}"));

        var hallazgo = informe.GetProperty("hallazgos")[0];
        Assert.False(hallazgo.GetProperty("evidencia_completa").GetBoolean());
        Assert.Equal(["transferencia_sin_cierre"], hallazgo.GetProperty("evidencia_faltante").EnumerateArray().Select(x => x.GetString()).ToList());
    }

    // --------------------------------------------------------------------------------- cliente del usuario

    [Fact]
    public async Task Un_hallazgo_de_otro_cliente_es_403_para_quien_se_limita_a_su_cliente_y_el_super_administrador_lo_ve()
    {
        var e = await HallazgoAsync(nivel: "v1");
        var otro = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otro));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET ClienteId = @c WHERE Vin = 'VIN-SEG-0001'", ("@c", otro));
        var delSofasa = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var delOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otro);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var ajeno = await CambiarAsync(delSofasa, e.Hallazgo, new { estado = "rechazado" });
        var historialAjeno = await Cliente(delSofasa.Token).GetAsync($"/api/hallazgos/{e.Hallazgo}/seguimiento");
        var propio = await CambiarAsync(delOtro, e.Hallazgo, new { estado = "rechazado" });
        var delSuper = await CambiarAsync(superAdmin, e.Hallazgo, new { estado = "recuperado" });

        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, historialAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delSuper.StatusCode);
    }
}
