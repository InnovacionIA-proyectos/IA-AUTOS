using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Calificar y contar los danos, tambien los de un extra: el conductor registra el dano de un extra sin nivel y lo califica un inspector (o el coordinador a distancia), el nivel que manda el conductor se rechaza igual
/// en una zona y en un extra, «sin calificar» es un filtro del listado y de la exportacion de hallazgos, la calificacion acepta cualquier dano sin nivel, el conductor solo sabe si esta calificado y las cifras de
/// «novedades» cuentan la version vigente con «sin calificar» aparte.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_1)]
public sealed class CalificacionDeDanosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-21T08:30:00-05:00";
    private const string VIN = "9BWZZZ377VT004251";
    private const string MENSAJE_NIVEL_DEL_CONDUCTOR = "El conductor no asigna el nivel de severidad: lo asigna el inspector";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>
    /// Un VIN con su ruta (la llegada del Cargue cerrada), el viaje con su conductor y su inspector, y dos inspecciones en borrador: la salida del Cargue que hizo el conductor por la excepcion «No hay inspector» y la
    /// salida de Transporte del inspector.
    /// </summary>
    private sealed record Escenario(Guid Viaje, Guid[] Etapas, Sesion Conductor, Sesion Inspector, Sesion Coordinador, Sesion Admin, Guid InspeccionDelConductor, Guid InspeccionDelInspector);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static JsonElement Errores(JsonElement cuerpo) => cuerpo.GetProperty("details").GetProperty("fieldErrors");

    private async Task<Escenario> EscenarioAsync(string vin = VIN)
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        await Sembrador.InformeAsync(etapas[0], conductor.UsuarioId, cerrada: true, momento: "llegada");
        var delConductor = await Como(conductor).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1, motivo_sin_inspector = "inspector_no_llego" });
        Assert.Equal(HttpStatusCode.Created, delConductor.StatusCode);
        // El informe de transporte lo arma el sistema (nadie lo crea por la API): se siembra el borrador de la salida.
        var delInspector = await Sembrador.InformeAsync(etapas[1], inspector.UsuarioId, cerrada: false, momento: "salida");
        return new Escenario(
            await Sembrador.ViajeDeEtapaAsync(etapas[0]), etapas, conductor, inspector, coordinador, admin,
            (await JsonAsync(delConductor)).GetProperty("id").GetGuid(), delInspector);
    }

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var r = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" });
        var cuerpo = await JsonAsync(r);
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    private Task<HttpResponseMessage> ExtraDanadoAsync(Sesion quien, Guid inspeccion, string tipo = "platon", string clase = "golpe", string tamano = "5_10cm", string? nivel = null, bool danado = true) =>
        Como(quien).PostAsJsonAsync($"/api/inspecciones/{inspeccion}/extras", new { tipo, danado, clase_dano = clase, tamano_dano = tamano, nivel_severidad = nivel });

    private async Task<Guid> HallazgoDelExtraAsync(HttpResponseMessage creado) => (await HallazgoYExtraAsync(creado)).Hallazgo;

    private async Task<(Guid Hallazgo, JsonElement Extra)> HallazgoYExtraAsync(HttpResponseMessage creado)
    {
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var extra = await JsonAsync(creado);
        return ((Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblHallazgos WHERE ExtraId = @e", ("@e", extra.GetProperty("id").GetGuid())))!, extra);
    }

    private async Task<HttpResponseMessage> DanoDeZonaAsync(Sesion quien, Guid inspeccion, string? nivel)
    {
        var (ruta, hash) = await SubirAsync(quien);
        return await Como(quien).PostAsJsonAsync($"/api/inspecciones/{inspeccion}/hallazgos", new
        {
            cuadrante_numero = 50,
            clase_dano = "rotura",
            tamano_dano = "1_5cm",
            nivel_severidad = nivel,
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, hash_sha256 = hash },
        });
    }

    private Task<HttpResponseMessage> CalificarAsync(Sesion quien, Guid hallazgo, object cuerpo) => Como(quien).PostAsJsonAsync($"/api/hallazgos/{hallazgo}/revision", cuerpo);

    private async Task<Guid> ZonaAsync() => (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Numero = 50"))!;

    private async Task<HashSet<Guid>> IdsAsync(Sesion quien, string consulta)
    {
        var r = await Como(quien).GetAsync($"/api/hallazgos{consulta}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await JsonAsync(r)).GetProperty("items").EnumerateArray().Select(f => f.GetProperty("id").GetGuid()).ToHashSet();
    }

    // ------------------------------------------------------------------------------ CA-1: el extra dañado del conductor

    [Fact]
    public async Task El_conductor_registra_el_dano_de_un_extra_sin_nivel_y_nace_del_conductor_sin_calificar_y_sin_zona()
    {
        var e = await EscenarioAsync();

        var creado = await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor);

        var (hallazgo, extra) = await HallazgoYExtraAsync(creado);
        Assert.Equal("conductor", (string?)await Sembrador.EscalarAsync("SELECT Origen FROM dbo.tblHallazgos WHERE Id = @h", ("@h", hallazgo)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND (ZonaId IS NOT NULL OR NivelSeveridad IS NOT NULL)", ("@h", hallazgo)));
        Assert.Equal("Platón", (string?)await Sembrador.EscalarAsync("SELECT ParteVehiculo FROM dbo.tblHallazgos WHERE Id = @h", ("@h", hallazgo)));
        var dano = extra.GetProperty("dano");
        Assert.Equal("golpe", dano.GetProperty("clase_dano").GetString());
        Assert.Equal(JsonValueKind.Null, dano.GetProperty("nivel_severidad").ValueKind);
    }

    [Fact]
    public async Task El_inspector_sigue_dando_los_tres_datos_del_extra_con_los_mensajes_de_siempre()
    {
        var e = await EscenarioAsync();

        var sinNivel = await ExtraDanadoAsync(e.Inspector, e.InspeccionDelInspector, tipo: "barras_techo");
        var completo = await ExtraDanadoAsync(e.Inspector, e.InspeccionDelInspector, nivel: "v2");

        Assert.Equal(HttpStatusCode.BadRequest, sinNivel.StatusCode);
        Assert.Equal("El nivel de severidad es obligatorio cuando el extra está dañado", Errores(await JsonAsync(sinNivel)).GetProperty("nivel_severidad")[0].GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras WHERE InspeccionId = @i AND Tipo = 'barras_techo'", ("@i", e.InspeccionDelInspector)));
        var hallazgo = await HallazgoDelExtraAsync(completo);
        Assert.Equal("inspector", (string?)await Sembrador.EscalarAsync("SELECT Origen FROM dbo.tblHallazgos WHERE Id = @h", ("@h", hallazgo)));
        Assert.Equal("v2", (string?)await Sembrador.EscalarAsync("SELECT NivelSeveridad FROM dbo.tblHallazgos WHERE Id = @h", ("@h", hallazgo)));
    }

    // -------------------------------------------------------------------- CA-2: el nivel del conductor, igual en zona y extra

    [Fact]
    public async Task El_nivel_que_manda_el_conductor_recibe_la_misma_respuesta_en_una_zona_y_en_un_extra_y_nunca_se_guarda()
    {
        var e = await EscenarioAsync();

        var enZona = await DanoDeZonaAsync(e.Conductor, e.InspeccionDelConductor, nivel: "v2");
        var enExtra = await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor, nivel: "v2");

        foreach (var r in new[] { enZona, enExtra })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(MENSAJE_NIVEL_DEL_CONDUCTOR, Errores(await JsonAsync(r)).GetProperty("nivel_severidad")[0].GetString());
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE InspeccionId = @i", ("@i", e.InspeccionDelConductor)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras WHERE InspeccionId = @i", ("@i", e.InspeccionDelConductor)));
    }

    // ------------------------------------------------------------------------------------------- CA-3: el filtro «sin calificar»

    /// <summary>Tres danos sin nivel de origenes distintos (zona del conductor, extra del conductor y uno migrado del inspector) y uno con nivel, en cuatro inspecciones del mismo viaje.</summary>
    private async Task<(Guid Zona, Guid Extra, Guid Migrado, Guid ConNivel)> SembrarMezclaAsync(Escenario e)
    {
        var zona = await ZonaAsync();
        var extra = await HallazgoDelExtraAsync(await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor));
        var (_, deZona) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[1], e.Conductor.UsuarioId, zona, nivel: null, origen: "conductor", momento: "llegada");
        var (_, migrado) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2], e.Inspector.UsuarioId, zona, nivel: null, origen: "inspector", momento: "salida");
        var (_, conNivel) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2], e.Inspector.UsuarioId, zona, nivel: "v2", origen: "inspector", momento: "llegada");
        return (deZona, extra, migrado, conNivel);
    }

    [Fact]
    public async Task Sin_calificar_trae_los_danos_sin_nivel_de_cualquier_origen_y_ninguno_con_nivel()
    {
        var e = await EscenarioAsync();
        var (zona, extra, migrado, conNivel) = await SembrarMezclaAsync(e);

        var sinCalificar = await IdsAsync(e.Inspector, $"?viaje_id={e.Viaje}&sin_calificar=true");
        var conNiveles = await IdsAsync(e.Inspector, $"?viaje_id={e.Viaje}&sin_calificar=false");

        Assert.True(sinCalificar.SetEquals([zona, extra, migrado]));
        Assert.DoesNotContain(conNivel, sinCalificar);
        Assert.Equal([conNivel], conNiveles.ToList());
    }

    [Fact]
    public async Task Sin_calificar_combina_con_los_demas_filtros_sin_contradecirlos()
    {
        var e = await EscenarioAsync();
        var (zona, extra, migrado, _) = await SembrarMezclaAsync(e);
        var vin = VIN;

        Assert.True((await IdsAsync(e.Coordinador, $"?viaje_id={e.Viaje}&sin_calificar=true&origen=conductor")).SetEquals([zona, extra]));
        Assert.Equal([migrado], (await IdsAsync(e.Coordinador, $"?viaje_id={e.Viaje}&sin_calificar=true&origen=inspector")).ToList());
        Assert.Equal(2, (await IdsAsync(e.Coordinador, $"?vin={vin}&sin_calificar=true&revisado=false")).Count);
        Assert.Empty(await IdsAsync(e.Coordinador, $"?viaje_id={e.Viaje}&sin_calificar=true&revisado=true"));
        Assert.Equal([migrado], (await IdsAsync(e.Coordinador, $"?viaje_id={e.Viaje}&sin_calificar=true&proceso=descargue")).ToList());
        Assert.Equal(3, (await IdsAsync(e.Coordinador, $"?viaje_id={e.Viaje}&sin_calificar=true&vin={vin}")).Count);
    }

    [Theory]
    [InlineData("si")]
    [InlineData("1")]
    [InlineData("quizas")]
    public async Task Un_valor_distinto_de_true_y_false_responde_400(string valor)
    {
        var e = await EscenarioAsync();

        var r = await Como(e.Inspector).GetAsync($"/api/hallazgos?sin_calificar={valor}");
        var exportacion = await Como(e.Admin).GetAsync($"/api/exportaciones/hallazgos.xlsx?sin_calificar={valor}");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("sin_calificar debe ser true o false", Errores(await JsonAsync(r)).GetProperty("sin_calificar")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, exportacion.StatusCode);
    }

    [Fact]
    public async Task Sin_calificar_verdadero_no_se_combina_con_un_nivel()
    {
        var e = await EscenarioAsync();

        var r = await Como(e.Inspector).GetAsync("/api/hallazgos?sin_calificar=true&nivel=v1");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(Errores(await JsonAsync(r)).TryGetProperty("sin_calificar", out _));
    }

    [Fact]
    public async Task La_exportacion_de_hallazgos_admite_el_mismo_filtro()
    {
        var e = await EscenarioAsync();
        await SembrarMezclaAsync(e);

        var todos = await Como(e.Admin).GetAsync($"/api/exportaciones/hallazgos.xlsx?viaje_id={e.Viaje}");
        var sinCalificar = await Como(e.Admin).GetAsync($"/api/exportaciones/hallazgos.xlsx?viaje_id={e.Viaje}&sin_calificar=true");

        Assert.Equal(HttpStatusCode.OK, todos.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sinCalificar.StatusCode);
        Assert.True((await todos.Content.ReadAsByteArrayAsync()).Length > 0);
        Assert.NotEqual(await todos.Content.ReadAsByteArrayAsync(), await sinCalificar.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Sin_calificar_no_trae_los_danos_de_una_version_que_una_correccion_cerrada_ya_reemplazo()
    {
        var e = await EscenarioAsync();
        var zona = await ZonaAsync();
        var (original, _) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2], e.Inspector.UsuarioId, zona, nivel: null, origen: "inspector", momento: "salida");
        var reemplazado = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblHallazgos WHERE InspeccionId = @i", ("@i", original)))!;
        Assert.Contains(reemplazado, await IdsAsync(e.Inspector, $"?viaje_id={e.Viaje}&sin_calificar=true"));

        await CorregirAsync(e.Etapas[2], e.Inspector.UsuarioId, original, conNivel: 0, sinNivel: 1);

        var vigentes = await IdsAsync(e.Inspector, $"?viaje_id={e.Viaje}&sin_calificar=true");
        Assert.DoesNotContain(reemplazado, vigentes);
        Assert.Single(vigentes);
    }

    // ------------------------------------------------------------------------------------------------- CA-4: la calificacion

    [Fact]
    public async Task El_inspector_califica_el_dano_de_un_extra_sin_zona_y_el_hallazgo_conserva_su_extra_y_su_nombre()
    {
        var e = await EscenarioAsync();
        var hallazgo = await HallazgoDelExtraAsync(await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor));

        var r = await CalificarAsync(e.Inspector, hallazgo, new { clase_dano = "golpe", nivel_severidad = "v2" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var h = await JsonAsync(r);
        Assert.Equal("v2", h.GetProperty("nivel_severidad").GetString());
        Assert.Equal(e.Inspector.UsuarioId, h.GetProperty("revisado_por").GetGuid());
        Assert.Equal(JsonValueKind.Null, h.GetProperty("cuadrante_numero").ValueKind);
        Assert.Equal("Platón", h.GetProperty("zona").GetString());
        Assert.NotEqual(JsonValueKind.Null, h.GetProperty("extra_id").ValueKind);
        Assert.False(h.GetProperty("sin_calificar").GetBoolean());
        Assert.True(h.GetProperty("calificado").GetBoolean());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND (ZonaId IS NOT NULL OR ExtraId IS NULL)", ("@h", hallazgo)));
    }

    [Fact]
    public async Task Calificar_un_extra_con_zona_responde_400_y_no_cambia_nada()
    {
        var e = await EscenarioAsync();
        var hallazgo = await HallazgoDelExtraAsync(await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor));

        var r = await CalificarAsync(e.Inspector, hallazgo, new { cuadrante_numero = 50, clase_dano = "golpe", nivel_severidad = "v2" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(Errores(await JsonAsync(r)).TryGetProperty("cuadrante_numero", out _));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND NivelSeveridad IS NOT NULL", ("@h", hallazgo)));
    }

    [Fact]
    public async Task Un_dano_con_zona_conserva_la_suya_si_la_peticion_no_manda_otra_y_la_cambia_si_manda_otra()
    {
        var e = await EscenarioAsync();
        var zona = await ZonaAsync();
        var (_, conservada) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[1], e.Conductor.UsuarioId, zona, nivel: null, origen: "conductor", momento: "llegada");
        var (_, cambiada) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2], e.Conductor.UsuarioId, zona, nivel: null, origen: "conductor", momento: "llegada");
        await Sembrador.CuadranteAsync(7, "Puerta delantera");

        var sinZona = await CalificarAsync(e.Inspector, conservada, new { clase_dano = "golpe", nivel_severidad = "v3" });
        var conOtra = await CalificarAsync(e.Inspector, cambiada, new { cuadrante_numero = 7, lado = "izquierdo", clase_dano = "golpe", nivel_severidad = "v3" });

        Assert.Equal(HttpStatusCode.OK, sinZona.StatusCode);
        Assert.Equal(50, (await JsonAsync(sinZona)).GetProperty("cuadrante_numero").GetInt32());
        Assert.Equal(HttpStatusCode.OK, conOtra.StatusCode);
        Assert.Equal(7, (await JsonAsync(conOtra)).GetProperty("cuadrante_numero").GetInt32());
    }

    [Fact]
    public async Task Un_hallazgo_que_ya_tiene_nivel_responde_409_y_una_segunda_calificacion_sigue_respondiendo_hallazgo_ya_revisado()
    {
        var e = await EscenarioAsync();
        var hallazgo = await HallazgoDelExtraAsync(await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor));
        var (_, conNivel) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[1], e.Inspector.UsuarioId, await ZonaAsync(), nivel: "v2", origen: "inspector", momento: "llegada");
        await CalificarAsync(e.Inspector, hallazgo, new { clase_dano = "golpe", nivel_severidad = "v3" });

        var yaTieneNivel = await CalificarAsync(e.Inspector, conNivel, new { cuadrante_numero = 50, clase_dano = "golpe", nivel_severidad = "v1" });
        var segunda = await CalificarAsync(e.Admin, hallazgo, new { clase_dano = "golpe", nivel_severidad = "v1" });

        Assert.Equal(HttpStatusCode.Conflict, yaTieneNivel.StatusCode);
        Assert.Equal("El hallazgo ya tiene nivel", (await JsonAsync(yaTieneNivel)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("HALLAZGO_YA_REVISADO", (await JsonAsync(segunda)).GetProperty("code").GetString());
        Assert.Equal("v3", (string?)await Sembrador.EscalarAsync("SELECT NivelSeveridad FROM dbo.tblHallazgos WHERE Id = @h", ("@h", hallazgo)));
    }

    [Fact]
    public async Task Se_califica_tambien_un_dano_sin_nivel_de_origen_inspector()
    {
        var e = await EscenarioAsync();
        var (_, migrado) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2], e.Inspector.UsuarioId, await ZonaAsync(), nivel: null, origen: "inspector", momento: "salida");

        var r = await CalificarAsync(e.Inspector, migrado, new { clase_dano = "golpe", nivel_severidad = "v2" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("v2", (await JsonAsync(r)).GetProperty("nivel_severidad").GetString());
    }

    // --------------------------------------------------------------------------------------------- CA-5: quien califica

    [Fact]
    public async Task Otro_inspector_recibe_403_y_el_dano_sigue_sin_calificar()
    {
        var e = await EscenarioAsync();
        var hallazgo = await HallazgoDelExtraAsync(await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor));
        var otro = await SesionAsync("otro-inspector@ejemplo.com", clsRol.INSPECTOR);

        var r = await CalificarAsync(otro, hallazgo, new { clase_dano = "golpe", nivel_severidad = "v2" });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("El VIN del hallazgo no pertenece a ninguno de sus viajes", (await JsonAsync(r)).GetProperty("message").GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND NivelSeveridad IS NOT NULL", ("@h", hallazgo)));
    }

    [Fact]
    public async Task El_coordinador_califica_a_distancia_el_extra_del_conductor_del_cargue_sin_inspector_y_la_auditoria_lo_registra()
    {
        var e = await EscenarioAsync();
        var hallazgo = await HallazgoDelExtraAsync(await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor));

        var r = await CalificarAsync(e.Coordinador, hallazgo, new { clase_dano = "golpe", nivel_severidad = "v1" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(e.Coordinador.UsuarioId, (await JsonAsync(r)).GetProperty("revisado_por").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'hallazgo.revision' AND UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)));
    }

    [Fact]
    public async Task El_coordinador_califica_el_dano_del_conductor_de_la_llegada_del_descargue_y_no_el_de_otras_inspecciones_ni_uno_que_no_es_del_conductor()
    {
        var e = await EscenarioAsync();
        var zona = await ZonaAsync();
        var (_, deLaLlegada) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2], e.Conductor.UsuarioId, zona, nivel: null, origen: "conductor", momento: "llegada");
        var (_, deTransporte) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[1], e.Conductor.UsuarioId, zona, nivel: null, origen: "conductor", momento: "llegada");
        var (_, delInspector) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2], e.Inspector.UsuarioId, zona, nivel: null, origen: "inspector", momento: "salida");

        var llegada = await CalificarAsync(e.Coordinador, deLaLlegada, new { clase_dano = "golpe", nivel_severidad = "v2" });
        var transporte = await CalificarAsync(e.Coordinador, deTransporte, new { clase_dano = "golpe", nivel_severidad = "v2" });
        var inspector = await CalificarAsync(e.Coordinador, delInspector, new { clase_dano = "golpe", nivel_severidad = "v2" });

        Assert.Equal(HttpStatusCode.OK, llegada.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, transporte.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, inspector.StatusCode);
        Assert.StartsWith("El coordinador de calidad solo revisa los hallazgos del conductor", (await JsonAsync(transporte)).GetProperty("message").GetString());
    }

    // ------------------------------------------------------------------------------- CA-6: nada obliga a calificar

    [Fact]
    public async Task Un_dano_sin_nivel_no_impide_cerrar_el_informe()
    {
        var e = await EscenarioAsync();
        await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor);

        var r = await Como(e.Conductor).GetAsync($"/api/inspecciones/{e.InspeccionDelConductor}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE InspeccionId = @i AND NivelSeveridad IS NULL", ("@i", e.InspeccionDelConductor)));
    }

    // ------------------------------------------------------------------------------- CA-7: lo que recibe el conductor

    [Fact]
    public async Task El_conductor_ve_calificado_falso_y_despues_verdadero_sin_ver_nunca_el_nivel()
    {
        var e = await EscenarioAsync();
        var hallazgo = await HallazgoDelExtraAsync(await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor));

        var antes = (await JsonAsync(await Como(e.Conductor).GetAsync($"/api/inspecciones/{e.InspeccionDelConductor}"))).GetProperty("hallazgos").EnumerateArray().Single();
        await CalificarAsync(e.Inspector, hallazgo, new { clase_dano = "golpe", nivel_severidad = "v2", nota = "Se ve la abolladura" });
        var despues = (await JsonAsync(await Como(e.Conductor).GetAsync($"/api/inspecciones/{e.InspeccionDelConductor}"))).GetProperty("hallazgos").EnumerateArray().Single();
        var inventario = (await JsonAsync(await Como(e.Conductor).GetAsync($"/api/vehiculos/{VIN}/inventario")))
            .GetProperty("etapas").EnumerateArray().SelectMany(et => et.GetProperty("inspecciones").EnumerateArray()).SelectMany(i => i.GetProperty("hallazgos").EnumerateArray()).Single();

        Assert.False(antes.GetProperty("calificado").GetBoolean());
        Assert.True(despues.GetProperty("calificado").GetBoolean());
        Assert.True(inventario.GetProperty("calificado").GetBoolean());
        foreach (var h in new[] { antes, despues, inventario })
        {
            Assert.Equal(JsonValueKind.Null, h.GetProperty("nivel_severidad").ValueKind);
            Assert.Equal(JsonValueKind.Null, h.GetProperty("nota").ValueKind);
            Assert.Equal(JsonValueKind.Null, h.GetProperty("revisado_por").ValueKind);
            Assert.Equal(JsonValueKind.Null, h.GetProperty("revisado_at").ValueKind);
            Assert.False(h.GetProperty("sin_calificar").GetBoolean());
        }
    }

    // -------------------------------------------------------------------- CA-8: las cifras con la version vigente

    /// <summary>Una correccion cerrada de la inspeccion indicada con los hallazgos pedidos (con y sin nivel): la version vigente.</summary>
    private async Task CorregirAsync(Guid etapa, Guid usuario, Guid original, int conNivel, int sinNivel)
    {
        var correccion = Guid.NewGuid();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Lat, Lng, Estado, Version, CorrigeInspeccionId) "
            + "SELECT @c, e.Id, e.ViajeId, @u, i.Momento, SYSDATETIMEOFFSET(), 4.6, -74.1, 'borrador', 2, i.Id FROM dbo.tblEtapas e JOIN dbo.tblInspecciones i ON i.Id = @o WHERE e.Id = @e",
            ("@c", correccion), ("@e", etapa), ("@u", usuario), ("@o", original));
        for (var i = 0; i < conNivel + sinNivel; i++)
        {
            await Sembrador.EjecutarAsync(
                "INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) SELECT NEWID(), @i, z.Id, 'golpe', '5_10cm', @n FROM dbo.tblZonasDano z WHERE z.Numero = 50",
                ("@i", correccion), ("@n", i < conNivel ? "v2" : (object)DBNull.Value));
        }

        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @c", ("@c", correccion));
    }

    /// <summary>La version 1 con cuatro danos (tres con nivel y uno sin) y su correccion con uno con nivel y uno sin: la vigente tiene 1 novedad y 1 sin calificar.</summary>
    private async Task<Escenario> InformeCorregidoAsync()
    {
        var e = await EscenarioAsync();
        var original = await Sembrador.InformeAsync(e.Etapas[2], e.Inspector.UsuarioId, cerrada: false, momento: "salida");
        for (var i = 0; i < 4; i++)
        {
            await Sembrador.EjecutarAsync(
                "INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) SELECT NEWID(), @i, z.Id, 'golpe', '5_10cm', @n FROM dbo.tblZonasDano z WHERE z.Numero = 50",
                ("@i", original), ("@n", i < 3 ? "v2" : (object)DBNull.Value));
        }

        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i", ("@i", original));
        await CorregirAsync(e.Etapas[2], e.Inspector.UsuarioId, original, conNivel: 1, sinNivel: 1);
        return e;
    }

    [Fact]
    public async Task El_listado_de_vehiculos_cuenta_solo_la_version_vigente_con_sin_calificar_aparte()
    {
        var e = await InformeCorregidoAsync();

        var r = await Como(e.Admin).GetAsync($"/api/vehiculos/listado?tipo=novedad&vin={VIN}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var fila = (await JsonAsync(r)).GetProperty("items").EnumerateArray().Single(f => f.GetProperty("vin").GetString() == VIN);
        Assert.Equal(1, fila.GetProperty("novedades").GetInt32());
        Assert.Equal(1, fila.GetProperty("sin_calificar").GetInt32());
    }

    [Fact]
    public async Task El_informe_del_carro_trae_las_mismas_dos_cifras_y_coinciden_con_el_indicador_diario()
    {
        var e = await InformeCorregidoAsync();
        var dia = ((DateTime)(await Sembrador.EscalarAsync("SELECT CAST(SWITCHOFFSET(CerradaAt, N'-05:00') AS date) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'llegada'", ("@e", e.Etapas[0])))!).ToString("yyyy-MM-dd");
        // El indicador cuenta las unidades despachadas ese dia: la salida del Cargue cerrada.
        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i", ("@i", e.InspeccionDelConductor));

        var inventario = await JsonAsync(await Como(e.Admin).GetAsync($"/api/vehiculos/{VIN}/inventario"));
        var indicador = await JsonAsync(await Como(e.Admin).GetAsync($"/api/reportes/indicador-diario?fecha={dia}"));

        Assert.Equal(1, inventario.GetProperty("novedades").GetInt32());
        Assert.Equal(1, inventario.GetProperty("sin_calificar").GetInt32());
        Assert.Equal(1, indicador.GetProperty("sin_calificar").GetInt32());
    }

    [Fact]
    public async Task Un_dano_sin_calificar_pasa_a_contar_como_novedad_cuando_se_califica()
    {
        var e = await EscenarioAsync();
        var hallazgo = await HallazgoDelExtraAsync(await ExtraDanadoAsync(e.Conductor, e.InspeccionDelConductor));
        var antes = await JsonAsync(await Como(e.Admin).GetAsync($"/api/vehiculos/{VIN}/inventario"));

        await CalificarAsync(e.Inspector, hallazgo, new { clase_dano = "golpe", nivel_severidad = "v2" });
        var despues = await JsonAsync(await Como(e.Admin).GetAsync($"/api/vehiculos/{VIN}/inventario"));

        Assert.Equal(0, antes.GetProperty("novedades").GetInt32());
        Assert.Equal(1, antes.GetProperty("sin_calificar").GetInt32());
        Assert.Equal(1, despues.GetProperty("novedades").GetInt32());
        Assert.Equal(0, despues.GetProperty("sin_calificar").GetInt32());
    }
}
