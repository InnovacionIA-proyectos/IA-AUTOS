using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La novedad del viaje: el conductor del viaje y el inspector que participa la reportan con un tipo del catalogo, un texto, la fecha y el GPS del celular (opcional) y una foto opcional; no toca ningun carro, avisa a los
/// coordinadores de calidad y administradores de cada cliente con carros en el viaje con la urgencia del tipo y se lee en el detalle del viaje. El identificador de envio del celular evita duplicarla, tambien en la novedad de un
/// carro. El catalogo de tipos se administra como los demas.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed class NovedadDelViajeApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-21T14:05:00-05:00";

    private static readonly byte[] PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

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

    private static JsonElement Errores(JsonElement cuerpo) => cuerpo.GetProperty("details").GetProperty("fieldErrors");

    /// <summary>Un viaje con su conductor, su inspector del viaje, un coordinador de calidad y un administrador, con una unidad y su ruta.</summary>
    private async Task<Escenario> EscenarioAsync(string vin = "VIN-NOV-0001")
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (vehiculo, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), vehiculo, etapas, vin, conductor, inspector, coordinador, admin);
    }

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var r = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await ComoJsonAsync(r);
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    private async Task<Dictionary<string, object?>> CuerpoAsync(
        Sesion quien, string tipo = "falla_camion", string texto = "Se reventó una llanta en la vía", bool conFoto = false, bool conGps = true, Guid? idEnvio = null)
    {
        var cuerpo = new Dictionary<string, object?> { ["tipo"] = tipo, ["texto"] = texto, ["fecha"] = FECHA };
        if (conGps)
        {
            cuerpo["lat"] = 4.7;
            cuerpo["lng"] = -74.2;
        }

        if (conFoto)
        {
            var (ruta, hash) = await SubirAsync(quien);
            cuerpo["evidencia"] = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.7, sello_lng = -74.2, precision = 5.0, dispositivo_id = "celular-1", hash_sha256 = hash };
        }

        if (idEnvio is not null)
        {
            cuerpo["id_envio"] = idEnvio;
        }

        return cuerpo;
    }

    private Task<HttpResponseMessage> ReportarAsync(Sesion quien, Guid viaje, object cuerpo) => Como(quien).PostAsJsonAsync($"/api/viajes/{viaje}/novedades-viaje", cuerpo);

    private Task<int> NovedadesDelViajeAsync(Guid viaje) => Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesViaje WHERE ViajeId = @v", ("@v", viaje));

    private Task<int> AvisosAsync(Guid usuario, string canal = "bandeja") =>
        Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_viaje' AND Canal = @c AND UsuarioId = @u", ("@c", canal), ("@u", usuario));

    /// <summary>El inspector toma la entrega de una parada (un lugar nuevo): queda con el reclamo activo de esa parada.</summary>
    private async Task<Guid> ParadaReclamadaAsync(Guid viaje, Guid inspector)
    {
        var lugar = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad) VALUES (@l, N'Puerto de Cartagena', (SELECT TOP 1 Id FROM dbo.tblPuntosCustodia), N'Cartagena')", ("@l", lugar));
        await Sembrador.EjecutarAsync("INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso, LugarId) VALUES (NEWID(), @v, @i, 'entrega', @l)", ("@v", viaje), ("@i", inspector), ("@l", lugar));
        return lugar;
    }

    // -------------------------------------------------------------------------------------------- el conductor

    [Fact]
    public async Task El_conductor_reporta_la_novedad_del_viaje_con_foto_y_queda_a_su_nombre_sin_tocar_ningun_carro()
    {
        var e = await EscenarioAsync();

        var r = await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor, conFoto: true));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var novedad = await ComoJsonAsync(r);
        Assert.Equal("falla_camion", novedad.GetProperty("tipo").GetString());
        Assert.Equal("Falla del camión", novedad.GetProperty("tipo_nombre").GetString());
        Assert.Equal("alta", novedad.GetProperty("urgencia").GetString());
        Assert.Equal("conductor", novedad.GetProperty("rol").GetString());
        Assert.Equal(JsonValueKind.Null, novedad.GetProperty("proceso").ValueKind);
        Assert.Equal(JsonValueKind.Null, novedad.GetProperty("lugar").ValueKind);
        Assert.Equal("Se reventó una llanta en la vía", novedad.GetProperty("texto").GetString());
        Assert.Equal("Nombre conductor@ejemplo.com", novedad.GetProperty("usuario_nombre").GetString());
        Assert.Equal(DateTimeOffset.Parse(FECHA), novedad.GetProperty("fecha").GetDateTimeOffset());
        Assert.Equal(4.7, novedad.GetProperty("lat").GetDouble());
        Assert.Equal(-74.2, novedad.GetProperty("lng").GetDouble());
        Assert.False(novedad.GetProperty("sin_ubicacion").GetBoolean());
        var foto = novedad.GetProperty("foto");
        Assert.StartsWith("http", foto.GetProperty("url").GetString());
        Assert.Contains("firma=", foto.GetProperty("url").GetString());
        Assert.NotEqual(JsonValueKind.Null, foto.GetProperty("url_expira_at").ValueKind);
        Assert.False(foto.GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal(64, foto.GetProperty("hash_sha256").GetString()!.Length);
        Assert.False(foto.TryGetProperty("ruta_archivo", out _));

        // No es de un carro: ninguna inspeccion, hallazgo ni novedad del carro, y el viaje sigue como estaba.
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(1, await NovedadesDelViajeAsync(e.Viaje));
        var viaje = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}"));
        Assert.Equal("programado", viaje.GetProperty("estado").GetString());
        var unidad = viaje.GetProperty("vehiculos").EnumerateArray().Single();
        Assert.Equal(0, unidad.GetProperty("novedades").GetInt32());
        Assert.Equal("sin_registro", unidad.GetProperty("estado_transporte").GetString());
        Assert.Equal(0, viaje.GetProperty("novedades_proceso").GetArrayLength());
    }

    [Fact]
    public async Task Sin_GPS_la_novedad_se_acepta_y_queda_sin_ubicacion_y_sin_foto_no_trae_foto()
    {
        var e = await EscenarioAsync();

        var r = await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor, "intento_robo", "Intentaron abrir la niñera en la parada", conGps: false));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var novedad = await ComoJsonAsync(r);
        Assert.True(novedad.GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal(JsonValueKind.Null, novedad.GetProperty("lat").ValueKind);
        Assert.Equal(JsonValueKind.Null, novedad.GetProperty("lng").ValueKind);
        Assert.Equal(JsonValueKind.Null, novedad.GetProperty("foto").ValueKind);
        Assert.Equal(DateTimeOffset.Parse(FECHA), novedad.GetProperty("fecha").GetDateTimeOffset());
        Assert.Equal(1, await AvisosAsync(e.Coordinador.UsuarioId));
    }

    [Fact]
    public async Task La_latitud_y_la_longitud_van_juntas_o_ninguna()
    {
        var e = await EscenarioAsync();
        var cuerpo = await CuerpoAsync(e.Conductor);
        cuerpo.Remove("lng");

        var r = await ReportarAsync(e.Conductor, e.Viaje, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("juntas", Errores(await ComoJsonAsync(r)).GetProperty("lng")[0].GetString());
        Assert.Equal(0, await NovedadesDelViajeAsync(e.Viaje));
    }

    // -------------------------------------------------------------------------------------------- el inspector

    [Fact]
    public async Task El_inspector_del_viaje_reporta_en_el_cargue_y_quien_reclamo_la_entrega_reporta_en_la_entrega_con_urgencia_media()
    {
        var e = await EscenarioAsync();
        var inspectorDeLaEntrega = await SesionAsync("inspector-entrega@ejemplo.com", clsRol.INSPECTOR);
        var parada = await ParadaReclamadaAsync(e.Viaje, inspectorDeLaEntrega.UsuarioId);

        var cargue = await ReportarAsync(e.Inspector, e.Viaje, await CuerpoAsync(e.Inspector, "carro_no_llego_patio", "El carro del puesto 9 no está en el patio", conFoto: true).ContinuarAsync(c => c["proceso"] = "cargue"));
        var entrega = await ReportarAsync(
            inspectorDeLaEntrega, e.Viaje, await CuerpoAsync(inspectorDeLaEntrega, "camion_llego_tarde", "Llegó 3 horas después de la cita").ContinuarAsync(c => { c["proceso"] = "entrega"; c["lugar_id"] = parada; }));

        Assert.Equal(HttpStatusCode.Created, cargue.StatusCode);
        Assert.Equal(HttpStatusCode.Created, entrega.StatusCode);
        var delCargue = await ComoJsonAsync(cargue);
        Assert.Equal("inspector", delCargue.GetProperty("rol").GetString());
        Assert.Equal("cargue", delCargue.GetProperty("proceso").GetString());
        Assert.Equal("media", delCargue.GetProperty("urgencia").GetString());
        Assert.Equal(JsonValueKind.Null, delCargue.GetProperty("lugar_id").ValueKind);
        var delaEntrega = await ComoJsonAsync(entrega);
        Assert.Equal("entrega", delaEntrega.GetProperty("proceso").GetString());
        Assert.Equal(parada, delaEntrega.GetProperty("lugar_id").GetGuid());
        Assert.Equal("Puerto de Cartagena", delaEntrega.GetProperty("lugar").GetString());
        Assert.Equal("Nombre inspector-entrega@ejemplo.com", delaEntrega.GetProperty("usuario_nombre").GetString());

        // Ningun carro gano una novedad ni un dano; el coordinador y el administrador reciben un aviso de cada una.
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        foreach (var destinatario in new[] { e.Coordinador, e.Admin })
        {
            Assert.Equal(2, await AvisosAsync(destinatario.UsuarioId));
        }
    }

    [Fact]
    public async Task Un_inspector_sin_ese_proceso_recibe_403_con_su_motivo_y_sin_proceso_o_sin_parada_recibe_400()
    {
        var e = await EscenarioAsync();
        var ajeno = await SesionAsync("inspector-ajeno@ejemplo.com", clsRol.INSPECTOR);
        var otraParada = await ParadaReclamadaAsync(e.Viaje, ajeno.UsuarioId);

        // El inspector del viaje no tiene el reclamo de la entrega de esa parada; otro inspector no es del cargue.
        var entregaSinReclamo = await ReportarAsync(e.Inspector, e.Viaje, await CuerpoAsync(e.Inspector, "camion_llego_tarde").ContinuarAsync(c => { c["proceso"] = "entrega"; c["lugar_id"] = otraParada; }));
        var cargueAjeno = await ReportarAsync(ajeno, e.Viaje, await CuerpoAsync(ajeno, "faltan_documentos").ContinuarAsync(c => c["proceso"] = "cargue"));
        var sinProceso = await ReportarAsync(e.Inspector, e.Viaje, await CuerpoAsync(e.Inspector, "faltan_documentos"));
        var entregaSinParada = await ReportarAsync(ajeno, e.Viaje, await CuerpoAsync(ajeno, "camion_llego_tarde").ContinuarAsync(c => c["proceso"] = "entrega"));
        var cargueConParada = await ReportarAsync(e.Inspector, e.Viaje, await CuerpoAsync(e.Inspector, "faltan_documentos").ContinuarAsync(c => { c["proceso"] = "cargue"; c["lugar_id"] = otraParada; }));

        Assert.Equal(HttpStatusCode.Forbidden, entregaSinReclamo.StatusCode);
        Assert.Equal("inspector_sin_ese_proceso", (await ComoJsonAsync(entregaSinReclamo)).GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, cargueAjeno.StatusCode);
        Assert.Equal("inspector_sin_ese_proceso", (await ComoJsonAsync(cargueAjeno)).GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinProceso.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(sinProceso)).TryGetProperty("proceso", out _));
        Assert.Equal(HttpStatusCode.BadRequest, entregaSinParada.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(entregaSinParada)).TryGetProperty("lugar_id", out _));
        Assert.Equal(HttpStatusCode.BadRequest, cargueConParada.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(cargueConParada)).TryGetProperty("lugar_id", out _));
        Assert.Equal(0, await NovedadesDelViajeAsync(e.Viaje));
    }

    [Fact]
    public async Task El_conductor_no_indica_proceso_ni_parada()
    {
        var e = await EscenarioAsync();

        var r = await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor).ContinuarAsync(c => { c["proceso"] = "cargue"; c["lugar_id"] = Guid.NewGuid(); }));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errores = Errores(await ComoJsonAsync(r));
        Assert.True(errores.TryGetProperty("proceso", out _));
        Assert.True(errores.TryGetProperty("lugar_id", out _));
    }

    // -------------------------------------------------------------------------------------------- lo que no se acepta

    [Fact]
    public async Task Cada_rechazo_responde_su_mensaje_y_no_guarda_nada()
    {
        var e = await EscenarioAsync();
        var otroConductor = await SesionAsync("jaime@ejemplo.com", clsRol.CONDUCTOR);
        await Sembrador.VehiculoConRutaAsync("VIN-NOV-0002", conductorId: otroConductor.UsuarioId);
        var accidente = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposNovedadViaje WHERE Codigo = 'accidente'"))!;
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsJsonAsync($"/api/catalogos/tipos-novedad-viaje/{accidente}", new { activo = false })).StatusCode);

        async Task<JsonElement> RechazoAsync(Sesion quien, string tipo, string texto, HttpStatusCode esperado)
        {
            var r = await ReportarAsync(quien, e.Viaje, await CuerpoAsync(quien, tipo, texto));
            var cuerpo = await ComoJsonAsync(r);
            Assert.True(r.StatusCode == esperado, $"{tipo}: se esperaba {esperado} y respondió {r.StatusCode}: {cuerpo}");
            return cuerpo;
        }

        Assert.Equal("Tipo de novedad desconocido", Errores(await RechazoAsync(e.Conductor, "inventado", "Algo", HttpStatusCode.BadRequest)).GetProperty("tipo")[0].GetString());
        Assert.Equal("Tipo de novedad desconocido", Errores(await RechazoAsync(e.Conductor, "accidente", "Algo", HttpStatusCode.BadRequest)).GetProperty("tipo")[0].GetString());
        Assert.Equal("Ese tipo de novedad no es para tu rol", Errores(await RechazoAsync(e.Conductor, "faltan_documentos", "Falta el manifiesto", HttpStatusCode.BadRequest)).GetProperty("tipo")[0].GetString());
        Assert.Equal("Escribe qué pasó", Errores(await RechazoAsync(e.Conductor, "falla_camion", "   ", HttpStatusCode.BadRequest)).GetProperty("texto")[0].GetString());
        Assert.Equal("Escribe máximo 300 caracteres", Errores(await RechazoAsync(e.Conductor, "otro", new string('x', 301), HttpStatusCode.BadRequest)).GetProperty("texto")[0].GetString());
        await RechazoAsync(e.Coordinador, "otro", "Aviso de prueba", HttpStatusCode.Forbidden);
        await RechazoAsync(e.Admin, "otro", "Aviso de prueba", HttpStatusCode.Forbidden);
        await RechazoAsync(otroConductor, "falla_camion", "Se varó el camión", HttpStatusCode.Forbidden);

        Assert.Equal(0, await NovedadesDelViajeAsync(e.Viaje));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_viaje'"));
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsJsonAsync($"/api/catalogos/tipos-novedad-viaje/{accidente}", new { activo = true })).StatusCode);
    }

    [Fact]
    public async Task El_texto_de_300_caracteres_se_acepta_y_un_tipo_para_los_dos_roles_lo_eligen_ambos()
    {
        var e = await EscenarioAsync();

        var delConductor = await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor, "otro", new string('x', 300)));
        var delInspector = await ReportarAsync(e.Inspector, e.Viaje, await CuerpoAsync(e.Inspector, "otro", "Otra cosa").ContinuarAsync(c => c["proceso"] = "cargue"));

        Assert.Equal(HttpStatusCode.Created, delConductor.StatusCode);
        Assert.Equal(HttpStatusCode.Created, delInspector.StatusCode);
        Assert.Equal(2, await NovedadesDelViajeAsync(e.Viaje));
    }

    [Fact]
    public async Task Un_viaje_que_ya_no_esta_activo_responde_409()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = 'finalizado' WHERE Id = @v", ("@v", e.Viaje));

        var r = await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(0, await NovedadesDelViajeAsync(e.Viaje));
    }

    // -------------------------------------------------------------------------------------------- el aviso

    [Fact]
    public async Task El_aviso_lleva_el_tipo_y_el_texto_sin_el_nombre_de_nadie_y_sus_datos_y_la_urgencia_alta_sale_tambien_por_push()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorAjeno = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL);
        foreach (var (token, sesion) in new[] { ("tk-coord", e.Coordinador), ("tk-admin", e.Admin), ("tk-otro", coordinadorAjeno) })
        {
            Assert.Equal(HttpStatusCode.OK, (await Como(sesion).PostAsJsonAsync("/api/dispositivos", new { token_fcm = token, plataforma = "android", modelo = "Pixel" })).StatusCode);
        }

        var r = await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor, conFoto: true));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await ComoJsonAsync(r)).GetProperty("id").GetGuid();
        foreach (var destinatario in new[] { e.Coordinador, e.Admin })
        {
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_viaje' AND Urgencia = 'alta' AND Canal = 'bandeja' AND UsuarioId = @u AND ViajeId = @v AND VehiculoId IS NULL AND HallazgoId IS NULL AND InspeccionId IS NULL",
                ("@u", destinatario.UsuarioId), ("@v", e.Viaje)));
            Assert.Equal(1, await AvisosAsync(destinatario.UsuarioId, "push"));
        }

        // Ni otro cliente, ni el comercial, el inspector o el conductor reciben el aviso.
        foreach (var ajeno in new[] { coordinadorAjeno.UsuarioId, comercial.UsuarioId, e.Inspector.UsuarioId, e.Conductor.UsuarioId })
        {
            Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE UsuarioId = @u", ("@u", ajeno)));
        }

        var fila = await ComoJsonAsync(await Como(e.Coordinador).GetAsync("/api/notificaciones"));
        var aviso = fila.GetProperty("items")[0];
        Assert.Equal("novedad_viaje", aviso.GetProperty("tipo").GetString());
        Assert.StartsWith("Novedad del viaje: Falla del camión (", aviso.GetProperty("titulo").GetString());
        Assert.Equal("Falla del camión: Se reventó una llanta en la vía", aviso.GetProperty("mensaje").GetString());
        Assert.DoesNotContain("conductor@ejemplo.com", aviso.GetProperty("mensaje").GetString());
        var datos = JsonDocument.Parse((string)(await Sembrador.EscalarAsync("SELECT TOP 1 Datos FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_viaje'"))!).RootElement;
        Assert.Equal(
            ["tipo_novedad", "tipo_novedad_nombre", "rol", "proceso", "lugar", "usuario_nombre", "con_foto"],
            datos.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("falla_camion", datos.GetProperty("tipo_novedad").GetString());
        Assert.Equal("conductor", datos.GetProperty("rol").GetString());
        Assert.Equal("Nombre conductor@ejemplo.com", datos.GetProperty("usuario_nombre").GetString());
        Assert.True(datos.GetProperty("con_foto").GetBoolean());
        Assert.Equal(["tk-admin", "tk-coord"], _push.Enviados.Select(x => x.Token).Order().ToList());
        Assert.Equal(e.Viaje.ToString(), _push.Enviados.First().Mensaje.Datos["viaje_id"]);
        Assert.Equal(id.ToString(), (await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}"))).GetProperty("novedades_viaje")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Un_tipo_de_urgencia_media_queda_solo_en_la_bandeja_sin_push()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-coord", plataforma = "android", modelo = "Pixel" })).StatusCode);

        var r = await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor, "bloqueo_via", "Paro en el peaje"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("media", (await ComoJsonAsync(r)).GetProperty("urgencia").GetString());
        Assert.Equal(1, await AvisosAsync(e.Coordinador.UsuarioId));
        Assert.Equal(0, await AvisosAsync(e.Coordinador.UsuarioId, "push"));
        Assert.Empty(_push.Enviados);
    }

    // -------------------------------------------------------------------------------------------- el reenvio

    [Fact]
    public async Task Un_reenvio_con_el_mismo_identificador_devuelve_la_misma_novedad_sin_otra_fila_ni_otro_aviso()
    {
        var e = await EscenarioAsync();
        var cuerpo = await CuerpoAsync(e.Conductor, "bloqueo_via", "Paro en el peaje", conFoto: true, idEnvio: Guid.NewGuid());

        var primera = await ReportarAsync(e.Conductor, e.Viaje, cuerpo);
        var segunda = await ReportarAsync(e.Conductor, e.Viaje, cuerpo);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var uno = await ComoJsonAsync(primera);
        var dos = await ComoJsonAsync(segunda);
        Assert.Equal(uno.GetProperty("id").GetGuid(), dos.GetProperty("id").GetGuid());
        Assert.Equal(uno.GetProperty("foto").GetProperty("hash_sha256").GetString(), dos.GetProperty("foto").GetProperty("hash_sha256").GetString());
        Assert.Equal(DateTimeOffset.Parse(FECHA), dos.GetProperty("fecha").GetDateTimeOffset());
        Assert.Equal(1, await NovedadesDelViajeAsync(e.Viaje));
        Assert.Equal(1, await AvisosAsync(e.Coordinador.UsuarioId));
    }

    [Fact]
    public async Task El_mismo_identificador_con_otro_contenido_responde_409_y_sin_identificador_cada_envio_es_una_novedad()
    {
        var e = await EscenarioAsync();
        var envio = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Created, (await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor, "bloqueo_via", "Paro en el peaje", idEnvio: envio))).StatusCode);

        var distinto = await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor, "bloqueo_via", "Otro texto distinto", idEnvio: envio));

        Assert.Equal(HttpStatusCode.Conflict, distinto.StatusCode);
        Assert.Equal("envio_repetido_distinto", (await ComoJsonAsync(distinto)).GetProperty("details").GetProperty("motivo").GetString());
        var sinEnvio = await CuerpoAsync(e.Conductor, "bloqueo_via", "Sin identificador");
        Assert.Equal(HttpStatusCode.Created, (await ReportarAsync(e.Conductor, e.Viaje, sinEnvio)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReportarAsync(e.Conductor, e.Viaje, sinEnvio)).StatusCode);
        Assert.Equal(3, await NovedadesDelViajeAsync(e.Viaje));
    }

    [Fact]
    public async Task El_identificador_de_envio_debe_ser_un_uuid_valido()
    {
        var e = await EscenarioAsync();
        var cuerpo = await CuerpoAsync(e.Conductor);
        cuerpo["id_envio"] = Guid.Empty;

        var r = await ReportarAsync(e.Conductor, e.Viaje, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(r)).TryGetProperty("id_envio", out _));
    }

    [Fact]
    public async Task La_cola_reenvia_la_novedad_de_un_carro_y_queda_una_sola_con_una_sola_foto_y_un_solo_aviso()
    {
        var e = await EscenarioAsync();
        await Sembrador.EntregaDelCargueCerradaAsync(e.Etapas[0], e.Conductor.UsuarioId, e.Coordinador.UsuarioId);
        var (ruta, hash) = await SubirAsync(e.Conductor);
        var cuerpo = new Dictionary<string, object?>
        {
            ["fecha"] = FECHA, ["lat"] = 4.7, ["lng"] = -74.2, ["tipo_novedad"] = "piedra", ["tamano_dano"] = "5_10cm", ["id_envio"] = Guid.NewGuid(),
            ["evidencia"] = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.7, sello_lng = -74.2, precision = 5.0, dispositivo_id = "celular-1", hash_sha256 = hash },
        };

        var primera = await Como(e.Conductor).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/novedades", cuerpo);
        var segunda = await Como(e.Conductor).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/novedades", cuerpo);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var uno = await ComoJsonAsync(primera);
        var dos = await ComoJsonAsync(segunda);
        Assert.Equal(uno.GetProperty("id").GetGuid(), dos.GetProperty("id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE TipoNovedad = 'piedra'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE HallazgoId IS NOT NULL"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_conductor' AND Canal = 'bandeja' AND UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)));

        // Con otro contenido el mismo identificador es un conflicto; sin identificador todo se comporta como siempre.
        var distinto = new Dictionary<string, object?>(cuerpo) { ["tipo_novedad"] = "granizo" };
        var rechazo = await Como(e.Conductor).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/novedades", distinto);
        Assert.Equal(HttpStatusCode.Conflict, rechazo.StatusCode);
        Assert.Equal("envio_repetido_distinto", (await ComoJsonAsync(rechazo)).GetProperty("details").GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task La_novedad_de_un_carro_reenviada_despues_de_confirmar_la_llegada_responde_lo_mismo()
    {
        var e = await EscenarioAsync();
        await Sembrador.EntregaDelCargueCerradaAsync(e.Etapas[0], e.Conductor.UsuarioId, e.Coordinador.UsuarioId);
        var (ruta, hash) = await SubirAsync(e.Conductor);
        var cuerpo = new Dictionary<string, object?>
        {
            ["fecha"] = FECHA, ["lat"] = 4.7, ["lng"] = -74.2, ["tipo_novedad"] = "golpe", ["tamano_dano"] = "5_10cm", ["id_envio"] = Guid.NewGuid(),
            ["evidencia"] = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.7, sello_lng = -74.2, precision = 5.0, dispositivo_id = "celular-1", hash_sha256 = hash },
        };
        var primera = await Como(e.Conductor).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/novedades", cuerpo);
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Conductor).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/llegada", new { fecha = FECHA, lat = 4.9, lng = -74.4 })).StatusCode);

        var reenvio = await Como(e.Conductor).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/novedades", cuerpo);

        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);
        Assert.Equal((await ComoJsonAsync(primera)).GetProperty("id").GetGuid(), (await ComoJsonAsync(reenvio)).GetProperty("id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE TipoNovedad = 'golpe'"));
    }

    // -------------------------------------------------------------------------------------------- la lectura

    [Fact]
    public async Task Las_novedades_del_viaje_salen_en_el_detalle_y_en_mis_viajes_de_la_mas_antigua_a_la_mas_reciente()
    {
        var e = await EscenarioAsync();
        var segunda = await CuerpoAsync(e.Conductor, "bloqueo_via", "Paro en el peaje");
        segunda["fecha"] = "2026-10-21T16:20:00-05:00";
        var primera = await CuerpoAsync(e.Conductor, "falla_camion", "Se reventó una llanta");
        Assert.Equal(HttpStatusCode.Created, (await ReportarAsync(e.Conductor, e.Viaje, segunda)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ReportarAsync(e.Conductor, e.Viaje, primera)).StatusCode);

        var detalle = await ComoJsonAsync(await Como(e.Inspector).GetAsync($"/api/viajes/{e.Viaje}"));
        var mios = await ComoJsonAsync(await Como(e.Conductor).GetAsync("/api/viajes/mios"));

        foreach (var viaje in new[] { detalle, mios.EnumerateArray().Single() })
        {
            Assert.Equal(["falla_camion", "bloqueo_via"], viaje.GetProperty("novedades_viaje").EnumerateArray().Select(n => n.GetProperty("tipo").GetString()).ToList());
        }

        var campos = detalle.GetProperty("novedades_viaje")[0].EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(
            ["id", "tipo", "tipo_nombre", "urgencia", "rol", "proceso", "lugar_id", "lugar", "texto", "usuario_nombre", "fecha", "lat", "lng", "sin_ubicacion", "foto", "created_at"], campos);
        // Un viaje sin novedades trae la lista vacia, y las novedades de proceso siguen siendo otra cosa.
        var otro = await Sembrador.ViajeAsync(e.Conductor.UsuarioId);
        var vacio = await ComoJsonAsync(await Como(e.Conductor).GetAsync($"/api/viajes/{otro}"));
        Assert.Equal(0, vacio.GetProperty("novedades_viaje").GetArrayLength());
        Assert.Equal(0, detalle.GetProperty("novedades_proceso").GetArrayLength());
    }

    // -------------------------------------------------------------------------------------------- el catalogo

    [Fact]
    public async Task El_catalogo_trae_los_diez_tipos_con_su_rol_y_su_urgencia_y_lo_administran_el_coordinador_y_los_administradores()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);

        var catalogos = (await ComoJsonAsync(await Como(conductor).GetAsync("/api/catalogos"))).GetProperty("catalogos");
        var tipos = catalogos.GetProperty("tipos-novedad-viaje").EnumerateArray().ToList();

        Assert.Equal(10, tipos.Count);
        Assert.Equal(["id", "codigo", "nombre", "orden", "activo", "rol", "urgencia"], tipos[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(["conductor", "conductor", "conductor", "conductor", "conductor", "inspector", "inspector", "inspector", "inspector", "todos"], tipos.Select(t => t.GetProperty("rol").GetString()).ToList());
        Assert.Equal(["alta", "media", "media", "alta", "alta", "media", "media", "media", "media", "media"], tipos.Select(t => t.GetProperty("urgencia").GetString()).ToList());

        var creado = await Como(coordinador).PostAsJsonAsync("/api/catalogos/tipos-novedad-viaje", new { codigo = "animal_en_la_via", nombre = "Animal en la vía", rol = "conductor", urgencia = "alta", orden = 11 });
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var valor = await ComoJsonAsync(creado);
        Assert.Equal("conductor", valor.GetProperty("rol").GetString());
        Assert.Equal("alta", valor.GetProperty("urgencia").GetString());

        var cambiado = await Como(coordinador).PatchAsJsonAsync($"/api/catalogos/tipos-novedad-viaje/{valor.GetProperty("id").GetGuid()}", new { urgencia = "media", rol = "todos" });
        Assert.Equal(HttpStatusCode.OK, cambiado.StatusCode);
        Assert.Equal("media", (await ComoJsonAsync(cambiado)).GetProperty("urgencia").GetString());

        var invalido = await Como(coordinador).PostAsJsonAsync("/api/catalogos/tipos-novedad-viaje", new { codigo = "otro_tipo", nombre = "Otro tipo", rol = "administrador" });
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(invalido)).TryGetProperty("rol", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(coordinador).PostAsJsonAsync("/api/catalogos/tipos-novedad-viaje", new { codigo = "otro_tipo", nombre = "Otro tipo", urgencia = "critica" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(conductor).PostAsJsonAsync("/api/catalogos/tipos-novedad-viaje", new { codigo = "x", nombre = "X" })).StatusCode);

        // El catalogo se conserva entre pruebas: el tipo que se creo se borra (ninguna novedad lo usa).
        Assert.Equal(HttpStatusCode.NoContent, (await Como(coordinador).DeleteAsync($"/api/catalogos/tipos-novedad-viaje/{valor.GetProperty("id").GetGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Un_tipo_que_una_novedad_usa_no_se_borra_sino_que_se_inactiva()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ReportarAsync(e.Conductor, e.Viaje, await CuerpoAsync(e.Conductor, "bloqueo_via", "Paro en el peaje"))).StatusCode);
        var id = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposNovedadViaje WHERE Codigo = 'bloqueo_via'"))!;

        var r = await Como(e.Coordinador).DeleteAsync($"/api/catalogos/tipos-novedad-viaje/{id}");

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("en uso", (await ComoJsonAsync(r)).GetProperty("message").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposNovedadViaje WHERE Codigo = 'bloqueo_via'"));
    }
}

/// <summary>Arma el cuerpo de una peticion en pasos sin salir de la expresion que la espera.</summary>
internal static class clsCuerpoExtensiones
{
    public static async Task<Dictionary<string, object?>> ContinuarAsync(this Task<Dictionary<string, object?>> cuerpo, Action<Dictionary<string, object?>> ajustar)
    {
        var valor = await cuerpo;
        ajustar(valor);
        return valor;
    }
}
