using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La excepcion «No hay inspector»: en un Cargue con personal de apoyo el conductor del viaje crea la salida cuando trae el motivo (del catalogo <c>motivos-sin-inspector</c>); queda el motivo en la
/// inspeccion, una novedad de proceso, el aviso al coordinador de calidad y al administrador del cliente y su registro en la auditoria. Fuera de ese caso el motivo no se admite.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class ExcepcionSinInspectorApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-23T08:30:00-05:00";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Un viaje con su conductor y su inspector, y el Cargue con personal de apoyo; el coordinador y el administrador son del cliente de la unidad.</summary>
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

    private static JsonElement Errores(JsonElement cuerpo) => cuerpo.GetProperty("details").GetProperty("fieldErrors");

    private async Task<Escenario> EscenarioAsync(bool conApoyo = true, string vin = "VIN-SIN-0001")
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (vehiculo, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        if (conApoyo)
        {
            await Sembrador.EjecutarAsync("UPDATE dbo.tblEtapas SET ConPersonalApoyo = 1 WHERE Id = @e", ("@e", etapas[0]));
        }

        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), vehiculo, etapas, vin, conductor, inspector, coordinador, admin);
    }

    private Task<HttpResponseMessage> CrearAsync(Sesion quien, Guid etapa, string momento = "salida", Action<Dictionary<string, object?>>? ajustar = null)
    {
        var cuerpo = new Dictionary<string, object?> { ["etapa_id"] = etapa, ["momento"] = momento, ["fecha"] = FECHA, ["lat"] = 4.6, ["lng"] = -74.1 };
        ajustar?.Invoke(cuerpo);
        return Como(quien).PostAsJsonAsync("/api/inspecciones", cuerpo);
    }

    private Task<HttpResponseMessage> ConMotivoAsync(Sesion quien, Guid etapa, string? motivo = "inspector_no_llego", string? texto = null, string momento = "salida") =>
        CrearAsync(quien, etapa, momento, c =>
        {
            if (motivo is not null)
            {
                c["motivo_sin_inspector"] = motivo;
            }

            if (texto is not null)
            {
                c["motivo_sin_inspector_texto"] = texto;
            }
        });

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var r = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" });
        var cuerpo = await ComoJsonAsync(r);
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), System.Text.Encoding.UTF8, "application/json");

    private Task<int> NovedadesAsync() => Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso");

    // ----------------------------------------------------------------------------------------------- la excepcion

    [Fact]
    public async Task El_conductor_crea_la_salida_del_cargue_con_apoyo_con_un_motivo_y_queda_la_novedad_de_proceso()
    {
        var e = await EscenarioAsync();

        var r = await ConMotivoAsync(e.Conductor, e.Etapas[0]);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var inspeccion = await ComoJsonAsync(r);
        Assert.Equal("borrador", inspeccion.GetProperty("estado").GetString());
        Assert.Equal(e.Conductor.UsuarioId, inspeccion.GetProperty("usuario_id").GetGuid());
        var motivo = inspeccion.GetProperty("motivo_sin_inspector");
        Assert.Equal(["codigo", "nombre", "texto"], motivo.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("inspector_no_llego", motivo.GetProperty("codigo").GetString());
        Assert.Equal("El inspector no llegó", motivo.GetProperty("nombre").GetString());
        Assert.Equal(JsonValueKind.Null, motivo.GetProperty("texto").ValueKind);

        var novedades = inspeccion.GetProperty("novedades_proceso");
        Assert.Equal(1, novedades.GetArrayLength());
        Assert.Equal(["tipo", "motivo_codigo", "motivo", "texto", "usuario_nombre", "fecha"], novedades[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("cargue_sin_inspector", novedades[0].GetProperty("tipo").GetString());
        Assert.Equal("inspector_no_llego", novedades[0].GetProperty("motivo_codigo").GetString());
        Assert.Equal("El inspector no llegó", novedades[0].GetProperty("motivo").GetString());
        Assert.Equal("Nombre conductor@ejemplo.com", novedades[0].GetProperty("usuario_nombre").GetString());
        Assert.Equal(DateTimeOffset.Parse(FECHA), novedades[0].GetProperty("fecha").GetDateTimeOffset());

        // La etapa conserva su personal de apoyo, el motivo queda en la inspeccion y la novedad, con la fecha, lat y lng del cuerpo.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas WHERE Id = @e AND ConPersonalApoyo = 1", ("@e", e.Etapas[0])));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Id = @i AND MotivoSinInspector = 'inspector_no_llego' AND MotivoSinInspectorTexto IS NULL", ("@i", inspeccion.GetProperty("id").GetGuid())));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE Tipo = 'cargue_sin_inspector' AND InspeccionId = @i AND ViajeId = @v AND VehiculoId = @ve AND EtapaId = @e AND UsuarioId = @u AND MotivoCodigo = 'inspector_no_llego' AND MotivoTexto IS NULL AND Lat = 4.6 AND Lng = -74.1",
            ("@i", inspeccion.GetProperty("id").GetGuid()), ("@v", e.Viaje), ("@ve", e.Vehiculo), ("@e", e.Etapas[0]), ("@u", e.Conductor.UsuarioId)));
    }

    [Fact]
    public async Task El_motivo_otro_exige_su_texto_y_un_motivo_de_la_lista_no_lo_admite()
    {
        var e = await EscenarioAsync();

        var sinTexto = await ConMotivoAsync(e.Conductor, e.Etapas[0], "otro");
        var enBlanco = await ConMotivoAsync(e.Conductor, e.Etapas[0], "otro", "   ");
        var largo = await ConMotivoAsync(e.Conductor, e.Etapas[0], "otro", new string('x', 301));
        var textoDeMas = await ConMotivoAsync(e.Conductor, e.Etapas[0], "inspector_no_disponible", "no deberia llevar texto");

        foreach (var r in new[] { sinTexto, enBlanco, largo, textoDeMas })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        Assert.Equal("El motivo elegido exige escribir cuál es", Errores(await ComoJsonAsync(sinTexto)).GetProperty("motivo_sin_inspector_texto")[0].GetString());
        Assert.True(Errores(await ComoJsonAsync(enBlanco)).TryGetProperty("motivo_sin_inspector_texto", out _));
        Assert.True(Errores(await ComoJsonAsync(largo)).TryGetProperty("motivo_sin_inspector_texto", out _));
        Assert.Equal("El motivo elegido no admite texto", Errores(await ComoJsonAsync(textoDeMas)).GetProperty("motivo_sin_inspector_texto")[0].GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(0, await NovedadesAsync());

        var conTexto = await ConMotivoAsync(e.Conductor, e.Etapas[0], "otro", "  El inspector se enfermó  ");

        Assert.Equal(HttpStatusCode.Created, conTexto.StatusCode);
        var cuerpo = await ComoJsonAsync(conTexto);
        Assert.Equal("El inspector se enfermó", cuerpo.GetProperty("motivo_sin_inspector").GetProperty("texto").GetString());
        Assert.Equal("El inspector se enfermó", cuerpo.GetProperty("novedades_proceso")[0].GetProperty("texto").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE MotivoCodigo = 'otro' AND MotivoTexto = N'El inspector se enfermó'"));
    }

    [Fact]
    public async Task Sin_motivo_el_conductor_sigue_recibiendo_403_y_un_motivo_inexistente_o_inactivo_es_400()
    {
        var e = await EscenarioAsync();
        // Se inactiva por la API (no por SQL) para que el catalogo en memoria se entere.
        var motivos = (await ComoJsonAsync(await Como(e.Admin).GetAsync("/api/catalogos/motivos-sin-inspector"))).GetProperty("items").EnumerateArray();
        var idDisponible = motivos.Single(m => m.GetProperty("codigo").GetString() == "inspector_no_disponible").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Admin).PatchAsync($"/api/catalogos/motivos-sin-inspector/{idDisponible}", Cuerpo(new { activo = false }))).StatusCode);

        var sinMotivo = await ConMotivoAsync(e.Conductor, e.Etapas[0], motivo: null);
        var soloTexto = await ConMotivoAsync(e.Conductor, e.Etapas[0], motivo: null, texto: "texto sin motivo");
        var inexistente = await ConMotivoAsync(e.Conductor, e.Etapas[0], "no_existe");
        var inactivo = await ConMotivoAsync(e.Conductor, e.Etapas[0], "inspector_no_disponible");
        var enBlanco = await ConMotivoAsync(e.Conductor, e.Etapas[0], "  ");

        Assert.Equal(HttpStatusCode.Forbidden, sinMotivo.StatusCode);
        Assert.Equal("Este cargue tiene personal de apoyo: el conductor solo firma el recibo", (await ComoJsonAsync(sinMotivo)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, soloTexto.StatusCode);
        foreach (var r in new[] { inexistente, inactivo, enBlanco })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True(Errores(await ComoJsonAsync(r)).TryGetProperty("motivo_sin_inspector", out _));
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(0, await NovedadesAsync());
    }

    [Fact]
    public async Task El_motivo_no_se_admite_para_el_inspector_ni_en_un_cargue_sin_apoyo_ni_en_otra_salida_o_llegada()
    {
        var e = await EscenarioAsync();
        var delInspector = await ConMotivoAsync(e.Inspector, e.Etapas[0]);
        var enLlegada = await ConMotivoAsync(e.Conductor, e.Etapas[0], momento: "llegada");
        var soloTexto = await ConMotivoAsync(e.Inspector, e.Etapas[0], motivo: null, texto: "texto");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblEtapas SET ConPersonalApoyo = 0 WHERE Id = @e", ("@e", e.Etapas[0]));
        var sinApoyo = await ConMotivoAsync(e.Conductor, e.Etapas[0]);
        await Sembrador.InformeAsync(e.Etapas[0], e.Conductor.UsuarioId, cerrada: true, momento: "llegada");
        var deTransporte = await ConMotivoAsync(e.Conductor, e.Etapas[1]);

        foreach (var r in new[] { delInspector, enLlegada, sinApoyo, deTransporte })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("Campo no admitido", Errores(await ComoJsonAsync(r)).GetProperty("motivo_sin_inspector")[0].GetString());
        }

        Assert.Equal(HttpStatusCode.BadRequest, soloTexto.StatusCode);
        Assert.Equal("Campo no admitido", Errores(await ComoJsonAsync(soloTexto)).GetProperty("motivo_sin_inspector_texto")[0].GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE MotivoSinInspector IS NOT NULL"));
        Assert.Equal(0, await NovedadesAsync());
    }

    [Fact]
    public async Task Otro_conductor_recibe_403_y_si_el_inspector_ya_creo_la_salida_es_409()
    {
        var e = await EscenarioAsync();
        var otroConductor = await SesionAsync("otro-conductor@ejemplo.com", clsRol.CONDUCTOR);

        var deOtro = await ConMotivoAsync(otroConductor, e.Etapas[0]);
        var delInspector = await CrearAsync(e.Inspector, e.Etapas[0]);
        var yaExistente = await ConMotivoAsync(e.Conductor, e.Etapas[0]);

        Assert.Equal(HttpStatusCode.Forbidden, deOtro.StatusCode);
        Assert.Equal(HttpStatusCode.Created, delInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, yaExistente.StatusCode);
        // El rechazo no deja nada a medias: ni novedad ni aviso.
        Assert.Equal(0, await NovedadesAsync());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_proceso'"));
    }

    // ----------------------------------------------------------------------------------------------- aviso y auditoria

    [Fact]
    public async Task El_aviso_llega_por_bandeja_y_push_solo_a_coordinadores_y_administradores_del_cliente_de_la_unidad()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var segundoAdmin = await SesionAsync("admin2@ejemplo.com", clsRol.ADMIN);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL);
        foreach (var (token, sesion) in new[] { ("tk-coord", e.Coordinador), ("tk-admin", e.Admin), ("tk-admin2", segundoAdmin), ("tk-otro", coordinadorOtro) })
        {
            Assert.Equal(HttpStatusCode.OK, (await Como(sesion).PostAsJsonAsync("/api/dispositivos", new { token_fcm = token, plataforma = "android", modelo = "Pixel" })).StatusCode);
        }

        var r = await ConMotivoAsync(e.Conductor, e.Etapas[0], "otro", "El inspector no pudo viajar");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var inspeccion = (await ComoJsonAsync(r)).GetProperty("id").GetGuid();
        foreach (var destinatario in new[] { e.Coordinador, e.Admin, segundoAdmin })
        {
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_proceso' AND Urgencia = 'alta' AND Canal = 'bandeja' AND UsuarioId = @u AND InspeccionId = @i AND ViajeId = @v AND VehiculoId = @ve AND EnviadaAt IS NOT NULL",
                ("@u", destinatario.UsuarioId), ("@i", inspeccion), ("@v", e.Viaje), ("@ve", e.Vehiculo)));
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_proceso' AND Canal = 'push' AND UsuarioId = @u AND EnviadaAt IS NOT NULL AND ErrorEnvio IS NULL", ("@u", destinatario.UsuarioId)));
        }

        // Ni otro cliente, ni el comercial, el inspector o el conductor reciben el aviso.
        foreach (var ajeno in new[] { coordinadorOtro.UsuarioId, comercial.UsuarioId, e.Inspector.UsuarioId, e.Conductor.UsuarioId })
        {
            Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE UsuarioId = @u", ("@u", ajeno)));
        }

        Assert.Equal(["tk-admin", "tk-admin2", "tk-coord"], _push.Enviados.Select(x => x.Token).Order().ToList());
        var enviado = _push.Enviados.First().Mensaje;
        Assert.Equal("novedad_proceso", enviado.Datos["tipo"]);
        Assert.Equal(e.Viaje.ToString(), enviado.Datos["viaje_id"]);
        Assert.Contains(e.Vin, enviado.Titulo);
        Assert.Contains("El inspector no pudo viajar", enviado.Mensaje);

        // El coordinador la ve en su bandeja con el tipo nuevo.
        var bandeja = await ComoJsonAsync(await Como(e.Coordinador).GetAsync("/api/notificaciones"));
        Assert.Equal("novedad_proceso", bandeja.GetProperty("items")[0].GetProperty("tipo").GetString());
    }

    [Fact]
    public async Task Si_el_push_falla_la_inspeccion_y_la_novedad_se_guardan_igual()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-roto", plataforma = "android" })).StatusCode);
        _push.Lanza = true;

        var r = await ConMotivoAsync(e.Conductor, e.Etapas[0]);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(1, await NovedadesAsync());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Canal = 'push' AND UsuarioId = @u AND EnviadaAt IS NULL AND ErrorEnvio IS NOT NULL", ("@u", e.Coordinador.UsuarioId)));
    }

    [Fact]
    public async Task Todo_queda_en_la_auditoria_con_el_viaje_y_el_cliente()
    {
        var e = await EscenarioAsync();

        var r = await ConMotivoAsync(e.Conductor, e.Etapas[0], "otro", "El inspector se enfermó");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var inspeccion = (await ComoJsonAsync(r)).GetProperty("id").GetGuid();
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.crear' AND EntidadId = @i AND ViajeId = @v AND UsuarioId = @u AND JSON_VALUE(Detalle, '$.despues.motivo_sin_inspector') = 'otro'",
            ("@i", inspeccion), ("@v", e.Viaje), ("@u", e.Conductor.UsuarioId)));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'novedad_proceso.crear' AND Entidad = 'novedades_proceso' AND ViajeId = @v AND ClienteId IS NOT NULL AND UsuarioId = @u AND JSON_VALUE(Detalle, '$.despues.tipo') = 'cargue_sin_inspector' AND JSON_VALUE(Detalle, '$.despues.motivo_texto') = N'El inspector se enfermó' AND Detalle IS NOT NULL",
            ("@v", e.Viaje), ("@u", e.Conductor.UsuarioId)));
    }

    // ----------------------------------------------------------------------------------------------- lo que ve cada lectura

    [Fact]
    public async Task El_informe_y_el_inventario_traen_el_motivo_y_las_novedades_de_proceso_y_el_resto_de_inspecciones_las_traen_vacias()
    {
        var e = await EscenarioAsync();
        var inspeccion = (await ComoJsonAsync(await ConMotivoAsync(e.Conductor, e.Etapas[0], "otro", "El inspector se enfermó"))).GetProperty("id").GetGuid();
        await Sembrador.InformeAsync(e.Etapas[1], e.Inspector.UsuarioId, cerrada: false);

        var informe = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/inspecciones/{inspeccion}"));
        var inventario = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/vehiculos/{e.Vin}/inventario"));

        Assert.Equal("otro", informe.GetProperty("motivo_sin_inspector").GetProperty("codigo").GetString());
        Assert.Equal("El inspector se enfermó", informe.GetProperty("motivo_sin_inspector").GetProperty("texto").GetString());
        Assert.Equal("cargue_sin_inspector", informe.GetProperty("novedades_proceso")[0].GetProperty("tipo").GetString());
        var cargue = inventario.GetProperty("etapas").EnumerateArray().Single(et => et.GetProperty("orden").GetInt32() == 1).GetProperty("inspecciones")[0];
        Assert.Equal("Otro", cargue.GetProperty("motivo_sin_inspector").GetProperty("nombre").GetString());
        Assert.Equal(1, cargue.GetProperty("novedades_proceso").GetArrayLength());
        var transporte = inventario.GetProperty("etapas").EnumerateArray().Single(et => et.GetProperty("orden").GetInt32() == 2).GetProperty("inspecciones")[0];
        Assert.Equal(JsonValueKind.Null, transporte.GetProperty("motivo_sin_inspector").ValueKind);
        Assert.Equal(0, transporte.GetProperty("novedades_proceso").GetArrayLength());
    }

    [Fact]
    public async Task Una_inspeccion_normal_trae_el_motivo_nulo_y_sin_novedades_de_proceso()
    {
        var e = await EscenarioAsync(conApoyo: false);

        var r = await CrearAsync(e.Inspector, e.Etapas[0]);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await ComoJsonAsync(r);
        Assert.Equal(JsonValueKind.Null, cuerpo.GetProperty("motivo_sin_inspector").ValueKind);
        Assert.Equal(0, cuerpo.GetProperty("novedades_proceso").GetArrayLength());
    }

    // ----------------------------------------------------------------------------------------------- revision a distancia

    private async Task<Guid> HallazgoDelConductorAsync(Sesion conductor, Guid inspeccion)
    {
        var (ruta, hash) = await SubirAsync(conductor);
        var creado = await Como(conductor).PostAsJsonAsync($"/api/inspecciones/{inspeccion}/hallazgos", new
        {
            zona_provisional = "frente",
            clase_dano = "rotura",
            tamano_dano = "1_5cm",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, hash_sha256 = hash },
        });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var cuerpo = await ComoJsonAsync(creado);
        // El conductor nunca asigna el nivel V.
        Assert.Equal(JsonValueKind.Null, cuerpo.GetProperty("nivel_severidad").ValueKind);
        return cuerpo.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> RevisarAsync(Sesion quien, Guid hallazgo) =>
        Como(quien).PostAsJsonAsync($"/api/hallazgos/{hallazgo}/revision", new { zona_provisional = "frente", clase_dano = "rotura", nivel_severidad = "v2" });

    [Fact]
    public async Task El_coordinador_del_cliente_revisa_a_distancia_los_hallazgos_del_conductor_de_una_salida_hecha_con_la_excepcion_y_no_los_de_otro_cliente()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var inspeccion = (await ComoJsonAsync(await ConMotivoAsync(e.Conductor, e.Etapas[0]))).GetProperty("id").GetGuid();
        var hallazgo = await HallazgoDelConductorAsync(e.Conductor, inspeccion);

        var deOtroCliente = await RevisarAsync(coordinadorOtro, hallazgo);
        var delCoordinador = await RevisarAsync(e.Coordinador, hallazgo);

        Assert.Equal(HttpStatusCode.Forbidden, deOtroCliente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delCoordinador.StatusCode);
        var revisado = await ComoJsonAsync(delCoordinador);
        Assert.Equal("v2", revisado.GetProperty("nivel_severidad").GetString());
        Assert.Equal(e.Coordinador.UsuarioId, revisado.GetProperty("revisado_por").GetGuid());
    }

    // ----------------------------------------------------------------------------------------------- el catalogo

    [Fact]
    public async Task El_catalogo_motivos_sin_inspector_se_publica_con_su_version_y_lo_administran_admin_super_admin_y_coordinador()
    {
        var e = await EscenarioAsync();

        var lectura = await Como(e.Conductor).GetAsync("/api/catalogos/motivos-sin-inspector");
        var todos = await ComoJsonAsync(await Como(e.Conductor).GetAsync("/api/catalogos"));

        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
        Assert.NotNull(lectura.Headers.ETag);
        var items = (await ComoJsonAsync(lectura)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["inspector_no_llego", "inspector_no_disponible", "otro"], items.Select(i => i.GetProperty("codigo").GetString()).ToList());
        Assert.Equal(["id", "codigo", "nombre", "orden", "activo", "requiere_texto"], items[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal([false, false, true], items.Select(i => i.GetProperty("requiere_texto").GetBoolean()).ToList());
        Assert.Equal(3, todos.GetProperty("catalogos").GetProperty("motivos-sin-inspector").GetArrayLength());

        // Quien administra los demas catalogos administra este; el inspector y el conductor no.
        var nuevo = new { codigo = "zz_emergencia", nombre = "Emergencia familiar", requiere_texto = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Conductor).PostAsJsonAsync("/api/catalogos/motivos-sin-inspector", nuevo)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Inspector).PostAsJsonAsync("/api/catalogos/motivos-sin-inspector", nuevo)).StatusCode);
        var creado = await Como(e.Coordinador).PostAsJsonAsync("/api/catalogos/motivos-sin-inspector", nuevo);
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var id = (await ComoJsonAsync(creado)).GetProperty("id").GetGuid();
        var editado = await Como(e.Admin).PatchAsync($"/api/catalogos/motivos-sin-inspector/{id}", Cuerpo(new { nombre = "Emergencia", requiere_texto = false }));
        Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
        var valor = await ComoJsonAsync(editado);
        Assert.Equal("Emergencia", valor.GetProperty("nombre").GetString());
        Assert.False(valor.GetProperty("requiere_texto").GetBoolean());
    }

    [Fact]
    public async Task Un_motivo_nuevo_se_ofrece_y_uno_inactivo_se_rechaza_y_un_motivo_en_uso_no_se_borra()
    {
        var e = await EscenarioAsync();
        var creado = await ComoJsonAsync(await Como(e.Admin).PostAsJsonAsync("/api/catalogos/motivos-sin-inspector", new { codigo = "zz_emergencia", nombre = "Emergencia familiar", requiere_texto = true }));

        var conNuevo = await ConMotivoAsync(e.Conductor, e.Etapas[0], "zz_emergencia", "Una emergencia");
        var enUso = await Como(e.Admin).DeleteAsync($"/api/catalogos/motivos-sin-inspector/{creado.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.Created, conNuevo.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, enUso.StatusCode);
    }
}
