using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El recibo sin entrega: cuando el conductor hizo la salida del Cargue (sin personal de apoyo, o con la excepcion «No hay inspector») nadie de Botero Soto firma la entrega y el conductor del viaje cierra
/// la transferencia solo con su firma. Desbloquea la etapa siguiente, deja la evidencia de los hallazgos incompleta, crea la novedad de proceso solo si hubo excepcion y aparece en el PDF y en el reporte.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class ReciboSinEntregaApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-23T08:30:00-05:00";
    private const string AUTOMATICO = "(segun quien la hace)";

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private sealed record Escenario(Guid Viaje, Guid Vehiculo, Guid[] Etapas, string Vin, Sesion Conductor, Sesion Inspector, Sesion Coordinador, Sesion Admin);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _api = CrearFabrica(servicios: s => s.AddSingleton<IEnviadorPush>(_push));
    }

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await _api.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1", _api), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token, _api);

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync(string vin = "VIN-REC-0001")
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (vehiculo, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), vehiculo, etapas, vin, conductor, inspector, coordinador, admin);
    }

    /// <summary>La salida del cargue: la hace el inspector o, si no hay inspector, el conductor activando la excepcion con su motivo (por defecto «el inspector no llego»).</summary>
    private async Task<Guid> SalidaDelCargueAsync(Sesion quien, Guid etapa, string? motivo = AUTOMATICO)
    {
        var esConductor = (string?)(await Sembrador.EscalarAsync("SELECT Rol FROM dbo.tblUsuarios WHERE Id = @u", ("@u", quien.UsuarioId))) == clsRol.CONDUCTOR;
        motivo = motivo == AUTOMATICO ? (esConductor ? "inspector_no_llego" : null) : motivo;
        var r = await Como(quien).PostAsJsonAsync("/api/inspecciones", new Dictionary<string, object?>
        {
            ["etapa_id"] = etapa, ["momento"] = "salida", ["fecha"] = FECHA, ["lat"] = 4.6, ["lng"] = -74.1, ["motivo_sin_inspector"] = motivo,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await ComoJsonAsync(r)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> SinEntregaAsync(Sesion quien, Guid inspeccion, string? firma = "M10,10 L50,50") =>
        Como(quien).PostAsJsonAsync($"/api/inspecciones/{inspeccion}/transferencia/sin-entrega", new { firma });

    private Task<int> NovedadesAsync(string tipo) => Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE Tipo = @t", ("@t", tipo));

    // ----------------------------------------------------------------------------------------------- el cierre

    [Fact]
    public async Task El_conductor_cierra_la_transferencia_de_su_cargue_por_excepcion_solo_con_su_firma_y_quedan_las_dos_novedades_de_proceso()
    {
        var e = await EscenarioAsync();
        var inspeccion = await SalidaDelCargueAsync(e.Conductor, e.Etapas[0]);

        var r = await SinEntregaAsync(e.Conductor, inspeccion);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var t = await ComoJsonAsync(r);
        Assert.Equal("recibo_sin_entrega", t.GetProperty("tipo_cierre").GetString());
        Assert.Equal(JsonValueKind.Null, t.GetProperty("entrega_usuario_id").ValueKind);
        Assert.Equal(e.Conductor.UsuarioId, t.GetProperty("recibe_usuario_id").GetGuid());
        // Queda registrado que quien entrega no firmo y por que (lo exige el cierre en la base).
        Assert.Equal("entrega", t.GetProperty("no_firma_parte").GetString());
        Assert.Equal("El conductor hizo la salida del cargue y nadie de Botero Soto entregó la unidad", t.GetProperty("motivo_no_firma").GetString());
        Assert.Equal(JsonValueKind.Null, t.GetProperty("entrega_firmada_at").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, t.GetProperty("recibe_firmada_at").ValueKind);
        Assert.False(t.GetProperty("firmada").GetBoolean());
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @i AND TipoCierre = 'recibo_sin_entrega' AND EntregaUsuarioId IS NULL AND FirmaEntrega IS NULL AND FirmaEntregaAt IS NULL AND FirmaEntregaIp IS NULL AND FirmaRecibe = N'M10,10 L50,50' AND FirmaRecibeAt IS NOT NULL AND RecibeUsuarioId = @u",
            ("@i", inspeccion), ("@u", e.Conductor.UsuarioId)));
        // El cargue lo hizo el conductor por la excepcion: queda la novedad del cargue sin inspector y la del recibo sin entrega.
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'transferencia.sin_entrega' AND EntidadId = @t AND UsuarioId = @u AND ViajeId = @v", ("@t", t.GetProperty("id").GetGuid()), ("@u", e.Conductor.UsuarioId), ("@v", e.Viaje)));
    }

    [Fact]
    public async Task El_cierre_es_inmutable_y_el_informe_la_constancia_y_las_notas_lo_reconocen()
    {
        var e = await EscenarioAsync();
        var inspeccion = await SalidaDelCargueAsync(e.Conductor, e.Etapas[0]);
        Assert.Equal(HttpStatusCode.Created, (await SinEntregaAsync(e.Conductor, inspeccion)).StatusCode);

        var repetido = await SinEntregaAsync(e.Conductor, inspeccion);
        var firmada = await Como(e.Conductor).PostAsJsonAsync($"/api/inspecciones/{inspeccion}/transferencia", new { firma = "otra" });
        var informe = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/inspecciones/{inspeccion}"));
        var constancia = await Como(e.Coordinador).GetAsync($"/api/inspecciones/{inspeccion}/transferencia/constancia/pdf");
        var pdfInforme = await Como(e.Coordinador).GetAsync($"/api/inspecciones/{inspeccion}/pdf");
        var nota = await Como(e.Conductor).PostAsJsonAsync($"/api/inspecciones/{inspeccion}/transferencia/notas", new { texto = "Se entregó sin inspector" });

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, firmada.StatusCode);
        Assert.Equal("recibo_sin_entrega", informe.GetProperty("transferencia").GetProperty("tipo_cierre").GetString());
        Assert.Equal(JsonValueKind.Null, informe.GetProperty("transferencia").GetProperty("entrega_usuario_id").ValueKind);
        Assert.Equal(HttpStatusCode.OK, constancia.StatusCode);
        var textoConstancia = Encoding.Latin1.GetString(await constancia.Content.ReadAsByteArrayAsync());
        Assert.Contains("recibo sin entrega", textoConstancia);
        Assert.Contains("Entrega: no hubo entrega", textoConstancia);
        Assert.DoesNotContain("Firmada por ambas partes", textoConstancia);
        Assert.Equal(HttpStatusCode.OK, pdfInforme.StatusCode);
        var textoInforme = Encoding.Latin1.GetString(await pdfInforme.Content.ReadAsByteArrayAsync());
        Assert.Contains("recibo sin entrega", textoInforme);
        Assert.Contains("Entrega: no hubo entrega", textoInforme);
        // Quien recibe es quien firmo: puede aclarar la transferencia aunque este cerrada.
        Assert.Equal(HttpStatusCode.Created, nota.StatusCode);
    }

    [Fact]
    public async Task Una_transferencia_abierta_donde_solo_firmo_quien_recibe_se_cierra_sin_entrega_pero_no_si_quien_entrega_ya_firmo()
    {
        var e = await EscenarioAsync();
        var inspeccion = await SalidaDelCargueAsync(e.Conductor, e.Etapas[0]);
        // El conductor firmo su parte (recibe) con el inspector como quien entrega, que aun no firma: la transferencia sigue abierta.
        var abierta = await Como(e.Conductor).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccion}/transferencia", new { entrega_usuario_id = e.Inspector.UsuarioId, recibe_usuario_id = e.Conductor.UsuarioId, firma = "abierta" });
        Assert.Equal(HttpStatusCode.Created, abierta.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ComoJsonAsync(abierta)).GetProperty("tipo_cierre").ValueKind);

        var cierre = await SinEntregaAsync(e.Conductor, inspeccion, "cerrada sin entrega");

        Assert.Equal(HttpStatusCode.Created, cierre.StatusCode);
        var t = await ComoJsonAsync(cierre);
        Assert.Equal("recibo_sin_entrega", t.GetProperty("tipo_cierre").GetString());
        Assert.Equal(JsonValueKind.Null, t.GetProperty("entrega_usuario_id").ValueKind);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @i AND FirmaRecibe = N'cerrada sin entrega'", ("@i", inspeccion)));
        // La auditoria guarda el antes (abierta, con quien entrega) y el despues.
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'transferencia.sin_entrega' AND JSON_VALUE(Detalle, '$.antes.tipo_cierre') IS NULL AND JSON_VALUE(Detalle, '$.antes.entrega_usuario_id') IS NOT NULL AND JSON_VALUE(Detalle, '$.despues.tipo_cierre') = 'recibo_sin_entrega'"));
    }

    [Fact]
    public async Task Si_quien_entrega_ya_firmo_la_transferencia_no_se_cierra_sin_entrega()
    {
        var e = await EscenarioAsync();
        var inspeccion = await SalidaDelCargueAsync(e.Conductor, e.Etapas[0]);
        Assert.Equal(HttpStatusCode.Created, (await Como(e.Inspector).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccion}/transferencia", new { entrega_usuario_id = e.Inspector.UsuarioId, recibe_usuario_id = e.Conductor.UsuarioId, firma = "entrega" })).StatusCode);

        var r = await SinEntregaAsync(e.Conductor, inspeccion);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre = 'recibo_sin_entrega'"));
    }

    // ----------------------------------------------------------------------------------------------- quien puede

    [Fact]
    public async Task Solo_el_conductor_del_viaje_lo_cierra_y_solo_en_el_cargue_con_la_salida_del_conductor()
    {
        var e = await EscenarioAsync();
        var inspeccion = await SalidaDelCargueAsync(e.Conductor, e.Etapas[0]);
        var otroConductor = await SesionAsync("otro-conductor@ejemplo.com", clsRol.CONDUCTOR);

        var delInspector = await SinEntregaAsync(e.Inspector, inspeccion);
        var delAdmin = await SinEntregaAsync(e.Admin, inspeccion);
        var delCoordinador = await SinEntregaAsync(e.Coordinador, inspeccion);
        var deOtroConductor = await SinEntregaAsync(otroConductor, inspeccion);
        var sinFirma = await SinEntregaAsync(e.Conductor, inspeccion, firma: null);
        var inexistente = await SinEntregaAsync(e.Conductor, Guid.NewGuid());
        var sinToken = await Cliente().PostAsJsonAsync($"/api/inspecciones/{inspeccion}/transferencia/sin-entrega", new { firma = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, delInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delCoordinador.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deOtroConductor.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinFirma.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Si_la_salida_la_hizo_el_inspector_o_la_etapa_no_es_el_cargue_responde_409()
    {
        var e = await EscenarioAsync();
        var delInspector = await SalidaDelCargueAsync(e.Inspector, e.Etapas[0]);
        await Sembrador.InformeAsync(e.Etapas[0], e.Conductor.UsuarioId, cerrada: true, momento: "llegada");
        var transporte = await SalidaDelCargueAsync(e.Conductor, e.Etapas[1], motivo: null);

        var salidaDelInspector = await SinEntregaAsync(e.Conductor, delInspector);
        var deTransporte = await SinEntregaAsync(e.Conductor, transporte);

        Assert.Equal(HttpStatusCode.Conflict, salidaDelInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deTransporte.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    // ----------------------------------------------------------------------------------------------- lo que desbloquea y lo que deja

    [Fact]
    public async Task El_cierre_desbloquea_la_salida_de_la_etapa_siguiente()
    {
        var e = await EscenarioAsync();
        var inspeccion = await SalidaDelCargueAsync(e.Conductor, e.Etapas[0]);
        await Sembrador.InformeAsync(e.Etapas[0], e.Conductor.UsuarioId, cerrada: true, momento: "llegada");
        // La transferencia sigue abierta (quien recibe firmo su parte y quien entrega no): la etapa siguiente espera.
        Assert.Equal(HttpStatusCode.Created, (await Como(e.Conductor).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccion}/transferencia", new { entrega_usuario_id = e.Inspector.UsuarioId, recibe_usuario_id = e.Conductor.UsuarioId, firma = "abierta" })).StatusCode);
        var antes = await Como(e.Conductor).PostAsJsonAsync("/api/inspecciones", new { etapa_id = e.Etapas[1], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1 });

        Assert.Equal(HttpStatusCode.Created, (await SinEntregaAsync(e.Conductor, inspeccion)).StatusCode);
        var despues = await Como(e.Conductor).PostAsJsonAsync("/api/inspecciones", new { etapa_id = e.Etapas[1], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1 });

        Assert.Equal(HttpStatusCode.Conflict, antes.StatusCode);
        var cuerpo = await ComoJsonAsync(antes);
        Assert.Equal("ETAPA_PREVIA_SIN_CIERRE", cuerpo.GetProperty("code").GetString());
        Assert.Equal("transferencia_sin_cierre", cuerpo.GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.Created, despues.StatusCode);
    }

    [Fact]
    public async Task Los_hallazgos_de_esa_inspeccion_quedan_con_la_evidencia_incompleta_por_transferencia_sin_entrega()
    {
        var e = await EscenarioAsync();
        var zona = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!;
        var (inspeccion, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[0], e.Conductor.UsuarioId, zona, conFoto: true);

        var antes = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/hallazgos/{hallazgo}"));
        Assert.Equal(HttpStatusCode.Created, (await SinEntregaAsync(e.Conductor, inspeccion)).StatusCode);
        var despues = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/hallazgos/{hallazgo}"));

        Assert.Equal(["transferencia_sin_cierre"], antes.GetProperty("evidencia_faltante").EnumerateArray().Select(x => x.GetString()).ToList());
        Assert.False(despues.GetProperty("evidencia_completa").GetBoolean());
        Assert.Equal(["transferencia_sin_entrega"], despues.GetProperty("evidencia_faltante").EnumerateArray().Select(x => x.GetString()).ToList());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND EvidenciaCompleta = 1", ("@h", hallazgo)));
    }

    // ----------------------------------------------------------------------------------------------- con la excepcion «No hay inspector»

    [Fact]
    public async Task Con_la_excepcion_queda_ademas_la_novedad_de_proceso_recibo_sin_entrega_sin_nuevo_aviso()
    {
        var e = await EscenarioAsync();
        var inspeccion = await SalidaDelCargueAsync(e.Conductor, e.Etapas[0], motivo: "inspector_no_llego");
        var avisosAntes = await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones");
        var pushAntes = _push.Enviados.Count;

        var r = await SinEntregaAsync(e.Conductor, inspeccion);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(1, await NovedadesAsync("cargue_sin_inspector"));
        Assert.Equal(1, await NovedadesAsync("recibo_sin_entrega"));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE Tipo = 'recibo_sin_entrega' AND InspeccionId = @i AND ViajeId = @v AND VehiculoId = @ve AND EtapaId = @e AND UsuarioId = @u AND MotivoCodigo = 'inspector_no_llego'",
            ("@i", inspeccion), ("@v", e.Viaje), ("@ve", e.Vehiculo), ("@e", e.Etapas[0]), ("@u", e.Conductor.UsuarioId)));
        // Sin nueva notificacion: ni bandeja ni push.
        Assert.Equal(avisosAntes, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones"));
        Assert.Equal(pushAntes, _push.Enviados.Count);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'novedad_proceso.crear'"));
        var informe = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/inspecciones/{inspeccion}"));
        Assert.Equal(["cargue_sin_inspector", "recibo_sin_entrega"], informe.GetProperty("novedades_proceso").EnumerateArray().Select(n => n.GetProperty("tipo").GetString()).Order().ToList());
    }

    // ----------------------------------------------------------------------------------------------- el reporte

    [Fact]
    public async Task El_reporte_cuenta_las_novedades_de_proceso_por_tipo_punto_cliente_y_mes_con_los_filtros_del_tablero()
    {
        var e = await EscenarioAsync();
        var inspeccion = await SalidaDelCargueAsync(e.Conductor, e.Etapas[0], motivo: "inspector_no_llego");
        Assert.Equal(HttpStatusCode.Created, (await SinEntregaAsync(e.Conductor, inspeccion)).StatusCode);
        var hoy = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd");
        var desde = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).AddDays(-10).ToString("yyyy-MM-dd");

        var r = await Como(e.Coordinador).GetAsync($"/api/reportes/novedades-proceso?desde={desde}&hasta={hoy}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var reporte = await ComoJsonAsync(r);
        Assert.Equal(["total", "por_tipo", "por_punto", "por_cliente", "por_mes"], reporte.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(2, reporte.GetProperty("total").GetInt32());
        Assert.Equal(["cargue_sin_inspector=1", "recibo_sin_entrega=1"], reporte.GetProperty("por_tipo").EnumerateArray().Select(x => $"{x.GetProperty("tipo").GetString()}={x.GetProperty("total").GetInt32()}").Order().ToList());
        Assert.Equal(2, reporte.GetProperty("por_cliente").EnumerateArray().Sum(x => x.GetProperty("total").GetInt32()));
        Assert.Equal(2, reporte.GetProperty("por_punto").EnumerateArray().Sum(x => x.GetProperty("total").GetInt32()));
        Assert.Equal(2, reporte.GetProperty("por_mes").EnumerateArray().Sum(x => x.GetProperty("total").GetInt32()));
        // Una novedad de proceso no cuenta como hallazgo: los patrones del tablero siguen en cero.
        Assert.Equal(0, (await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/reportes/patrones?desde={desde}&hasta={hoy}"))).GetProperty("total").GetInt32());

        // Los filtros del tablero aplican: otra operacion o el proceso Transporte no ven nada.
        var otraOperacion = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/reportes/novedades-proceso?desde={desde}&hasta={hoy}&operacion=exportacion"));
        var transporte = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/reportes/novedades-proceso?desde={desde}&hasta={hoy}&proceso=transporte"));
        var lejano = await ComoJsonAsync(await Como(e.Coordinador).GetAsync("/api/reportes/novedades-proceso?desde=2020-01-01&hasta=2020-01-31"));
        Assert.Equal(0, otraOperacion.GetProperty("total").GetInt32());
        Assert.Equal(0, transporte.GetProperty("total").GetInt32());
        Assert.Equal(0, lejano.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task El_reporte_es_del_cliente_de_quien_consulta_y_el_inspector_y_el_conductor_no_lo_ven()
    {
        var e = await EscenarioAsync();
        await SalidaDelCargueAsync(e.Conductor, e.Etapas[0], motivo: "inspector_no_llego");
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var delOtro = await ComoJsonAsync(await Como(coordinadorOtro).GetAsync("/api/reportes/novedades-proceso"));
        var delSuper = await ComoJsonAsync(await Como(superAdmin).GetAsync("/api/reportes/novedades-proceso"));
        var pidiendoElDeOtro = await Como(coordinadorOtro).GetAsync($"/api/reportes/novedades-proceso?cliente_id={e.Vehiculo}");

        Assert.Equal(0, delOtro.GetProperty("total").GetInt32());
        Assert.Equal(1, delSuper.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, pidiendoElDeOtro.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Inspector).GetAsync("/api/reportes/novedades-proceso")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Conductor).GetAsync("/api/reportes/novedades-proceso")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(e.Coordinador).GetAsync("/api/reportes/novedades-proceso?desde=ayer")).StatusCode);
    }
}
