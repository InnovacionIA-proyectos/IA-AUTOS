using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El conductor en ruta: registra las novedades del recorrido de cada unidad de su viaje y confirma su llegada; las novedades avisan a los coordinadores de calidad y administradores del
/// cliente del vehiculo y el conductor no ve los hallazgos que registraron otros.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class ConductorApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-21T08:30:00-05:00";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private sealed record Escenario(Guid Viaje, Guid Vehiculo, Guid[] Etapas, string Vin, Sesion Conductor, Sesion Coordinador, Sesion Admin);

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

    /// <summary>Un viaje del conductor con la unidad indicada y, salvo que se pida lo contrario, el Cargue ya cerrado (su llegada cerrada).</summary>
    private async Task<Escenario> EscenarioAsync(bool cargueCerrado = true, string vin = "VIN-CND-0001")
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (vehiculo, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId);
        if (cargueCerrado)
        {
            await Sembrador.InformeAsync(etapas[0], conductor.UsuarioId, cerrada: true, momento: "llegada");
        }

        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), vehiculo, etapas, vin, conductor, coordinador, admin);
    }

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var r = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await ComoJsonAsync(r);
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    private async Task<Dictionary<string, object?>> CuerpoNovedadAsync(Sesion quien, string tipo = "golpe", string tamano = "5_10cm")
    {
        var (ruta, hash) = await SubirAsync(quien);
        return new Dictionary<string, object?>
        {
            ["fecha"] = FECHA,
            ["lat"] = 4.7,
            ["lng"] = -74.2,
            ["tipo_novedad"] = tipo,
            ["tamano_dano"] = tamano,
            ["evidencia"] = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.7, sello_lng = -74.2, precision = 5.0, dispositivo_id = "celular-1", hash_sha256 = hash },
        };
    }

    private Task<HttpResponseMessage> NovedadAsync(Sesion quien, Guid viaje, string vin, object cuerpo) =>
        Como(quien).PostAsJsonAsync($"/api/viajes/{viaje}/vehiculos/{vin}/novedades", cuerpo);

    private Task<HttpResponseMessage> LlegadaAsync(Sesion quien, Guid viaje, string vin, object? cuerpo = null) =>
        Como(quien).PostAsJsonAsync($"/api/viajes/{viaje}/vehiculos/{vin}/llegada", cuerpo ?? new { fecha = FECHA, lat = 4.9, lng = -74.4 });

    private async Task<Guid> ZonaFrenteAsync() => (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!;

    private static JsonElement Errores(JsonElement cuerpo) => cuerpo.GetProperty("details").GetProperty("fieldErrors");

    private async Task<JsonElement> UnidadDelViajeAsync(Escenario e)
    {
        var viaje = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}"));
        return viaje.GetProperty("vehiculos").EnumerateArray().Single(v => v.GetProperty("vin").GetString() == e.Vin);
    }

    private Task<int> InspeccionesDeTransporteAsync(Escenario e, string momento) =>
        Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = @m", ("@e", e.Etapas[1]), ("@m", momento));

    // -------------------------------------------------------------------------------------------- novedades

    [Fact]
    public async Task El_conductor_registra_una_novedad_que_crea_la_salida_de_transporte_y_queda_sin_zona_clase_ni_nivel()
    {
        var e = await EscenarioAsync();

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor, "granizo", "mas_20cm"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var novedad = await ComoJsonAsync(r);
        Assert.Equal("granizo", novedad.GetProperty("tipo_novedad").GetString());
        Assert.Equal("mas_20cm", novedad.GetProperty("tamano_dano").GetString());
        Assert.Equal("conductor", novedad.GetProperty("origen").GetString());
        foreach (var vacio in new[] { "clase_dano", "cuadrante_numero", "lado", "zona_provisional", "nivel_severidad", "nota", "revisado_at" })
        {
            Assert.Equal(JsonValueKind.Null, novedad.GetProperty(vacio).ValueKind);
        }

        Assert.Equal("pendiente", novedad.GetProperty("estado_seguimiento").GetString());
        Assert.Equal(novedad.GetProperty("id").GetGuid(), novedad.GetProperty("evidencia").GetProperty("hallazgo_id").GetGuid());
        // La salida de Transporte nace en borrador con la fecha, la hora y el GPS de la novedad, a nombre del conductor.
        var salida = await Sembrador.EscalarAsync(
            "SELECT CONCAT(Estado, '|', UsuarioId, '|', Lat, '|', Lng, '|', Fecha) FROM dbo.tblInspecciones WHERE Id = @i", ("@i", novedad.GetProperty("inspeccion_id").GetGuid()));
        Assert.StartsWith($"borrador|{e.Conductor.UsuarioId}|4.7|-74.2|2026-10-21 08:30:00", (string)salida!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblInspecciones i JOIN dbo.tblEtapas t ON t.Id = i.EtapaId WHERE i.Id = @i AND t.Id = @e AND i.Momento = 'salida'",
            ("@i", novedad.GetProperty("inspeccion_id").GetGuid()), ("@e", e.Etapas[1])));
        // El viaje programado pasa a en curso y todo queda en la auditoria.
        Assert.Equal("en_curso", (string?)await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'novedad.crear'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'evidencia.crear'"));
    }

    [Fact]
    public async Task Una_segunda_novedad_va_a_la_misma_salida_de_transporte()
    {
        var e = await EscenarioAsync();

        var primera = await ComoJsonAsync(await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor, "golpe")));
        var segunda = await ComoJsonAsync(await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor, "piedra", "1_5cm")));

        Assert.Equal(primera.GetProperty("inspeccion_id").GetGuid(), segunda.GetProperty("inspeccion_id").GetGuid());
        Assert.Equal(1, await InspeccionesDeTransporteAsync(e, "salida"));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE InspeccionId = @i AND TipoNovedad IS NOT NULL", ("@i", primera.GetProperty("inspeccion_id").GetGuid())));
    }

    [Fact]
    public async Task La_novedad_de_otro_conductor_o_de_otro_rol_responde_403_y_no_se_guarda()
    {
        var e = await EscenarioAsync();
        var otro = await SesionAsync("otro-conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);

        var deOtroConductor = await NovedadAsync(otro, e.Viaje, e.Vin, await CuerpoNovedadAsync(otro));
        var deInspector = await NovedadAsync(inspector, e.Viaje, e.Vin, await CuerpoNovedadAsync(inspector));
        var deCoordinador = await NovedadAsync(e.Coordinador, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Coordinador));
        var anonimo = await Cliente(fabrica: _api).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/novedades", new { });

        Assert.Equal(HttpStatusCode.Forbidden, deOtroConductor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deCoordinador.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE TipoNovedad IS NOT NULL"));
        Assert.Equal(0, await InspeccionesDeTransporteAsync(e, "salida"));
    }

    [Theory]
    [InlineData("nota")]
    [InlineData("clase_dano")]
    [InlineData("nivel_severidad")]
    [InlineData("cuadrante_numero")]
    public async Task La_novedad_no_admite_texto_libre_ni_clase_ni_nivel_ni_zona(string campo)
    {
        var e = await EscenarioAsync();
        var cuerpo = await CuerpoNovedadAsync(e.Conductor);
        cuerpo[campo] = campo == "cuadrante_numero" ? 7 : "texto";

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Campo no admitido", Errores(await ComoJsonAsync(r)).GetProperty(campo)[0].GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task La_foto_de_la_novedad_tampoco_admite_nota()
    {
        var e = await EscenarioAsync();
        var (ruta, hash) = await SubirAsync(e.Conductor);
        var cuerpo = await CuerpoNovedadAsync(e.Conductor);
        cuerpo["evidencia"] = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.7, sello_lng = -74.2, hash_sha256 = hash, nota = "texto libre" };

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(r)).TryGetProperty("evidencia", out _));
    }

    [Fact]
    public async Task Sin_foto_tipo_tamano_fecha_o_gps_la_novedad_responde_400_con_todos_los_errores_juntos()
    {
        var e = await EscenarioAsync();

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, new { });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errores = Errores(await ComoJsonAsync(r));
        foreach (var campo in new[] { "fecha", "lat", "lng", "tipo_novedad", "tamano_dano", "evidencia" })
        {
            Assert.True(errores.TryGetProperty(campo, out _), $"Falta el error de {campo}");
        }

        Assert.Equal(0, await InspeccionesDeTransporteAsync(e, "salida"));
    }

    [Theory]
    [InlineData("robo", "5_10cm", "tipo_novedad")]
    [InlineData("golpe", "enorme", "tamano_dano")]
    public async Task Un_tipo_o_un_tamano_fuera_de_la_lista_responde_400(string tipo, string tamano, string campo)
    {
        var e = await EscenarioAsync();

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor, tipo, tamano));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(r)).TryGetProperty(campo, out _));
    }

    [Fact]
    public async Task Un_tamano_inactivo_ya_no_se_ofrece_para_una_novedad()
    {
        var e = await EscenarioAsync();
        var admin = await SesionAsync("admin-catalogos@ejemplo.com", clsRol.ADMIN);
        var tamano = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTamanosDano WHERE Codigo = '1_5cm'"))!;
        Assert.Equal(HttpStatusCode.OK, (await Como(admin).PatchAsync($"/api/catalogos/tamanos-dano/{tamano}", JsonContent.Create(new { activo = false }))).StatusCode);

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor, "golpe", "1_5cm"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(r)).TryGetProperty("tamano_dano", out _));
    }

    [Fact]
    public async Task La_foto_de_la_novedad_exige_su_ubicacion_y_que_el_archivo_este_subido()
    {
        var e = await EscenarioAsync();
        var sinUbicacion = await CuerpoNovedadAsync(e.Conductor);
        sinUbicacion["evidencia"] = new { storage_path = "x.png", sello_fecha = FECHA };
        var noSubida = await CuerpoNovedadAsync(e.Conductor);
        noSubida["evidencia"] = new { storage_path = "no-existe.png", sello_fecha = FECHA, sello_lat = 4.7, sello_lng = -74.2 };

        var a = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, sinUbicacion);
        var b = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, noSubida);

        Assert.Equal(HttpStatusCode.BadRequest, a.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, b.StatusCode);
        Assert.True(Errores(await ComoJsonAsync(b)).TryGetProperty("storage_path", out _));
        // Nada de la novedad que fallo queda guardado, ni siquiera la salida de Transporte.
        Assert.Equal(0, await InspeccionesDeTransporteAsync(e, "salida"));
    }

    [Fact]
    public async Task Con_el_cargue_sin_cerrar_la_novedad_responde_409_etapa_previa_sin_cierre_y_no_se_guarda()
    {
        var e = await EscenarioAsync(cargueCerrado: false);

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("ETAPA_PREVIA_SIN_CIERRE", (await ComoJsonAsync(r)).GetProperty("code").GetString());
        Assert.Equal(0, await InspeccionesDeTransporteAsync(e, "salida"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones"));
    }

    [Fact]
    public async Task Con_la_transferencia_del_cargue_sin_cierre_la_novedad_tambien_responde_409()
    {
        var e = await EscenarioAsync(cargueCerrado: false);
        var cargue = await Sembrador.InformeAsync(e.Etapas[0], e.Conductor.UsuarioId, cerrada: true, momento: "llegada");
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        await Sembrador.TransferenciaAsync(cargue, e.Conductor.UsuarioId, inspector.UsuarioId, tipoCierre: null);

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("transferencia_sin_cierre", (await ComoJsonAsync(r)).GetProperty("details").GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task Una_unidad_que_no_sale_no_admite_novedades_ni_llegada()
    {
        var e = await EscenarioAsync();
        var (_, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(
            e.Etapas[0], e.Coordinador.UsuarioId, await ZonaFrenteAsync(), nivel: "v1_plus");
        await Sembrador.UnidadNoSaleAsync(e.Viaje, e.Vehiculo, hallazgo, e.Coordinador.UsuarioId, "Golpe estructural");

        var novedad = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor));
        var llegada = await LlegadaAsync(e.Conductor, e.Viaje, e.Vin);

        foreach (var r in new[] { novedad, llegada })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("UNIDAD_NO_SALE", (await ComoJsonAsync(r)).GetProperty("code").GetString());
        }

        Assert.Equal(0, await InspeccionesDeTransporteAsync(e, "salida") + await InspeccionesDeTransporteAsync(e, "llegada"));
    }

    [Fact]
    public async Task Un_vin_que_no_existe_o_no_es_del_viaje_responde_404_y_un_viaje_inexistente_tambien()
    {
        var e = await EscenarioAsync();
        await Sembrador.VehiculoAsync("VIN-OTRO-VIAJE");

        var noExiste = await NovedadAsync(e.Conductor, e.Viaje, "VIN-NO-EXISTE", await CuerpoNovedadAsync(e.Conductor));
        var deOtroViaje = await NovedadAsync(e.Conductor, e.Viaje, "VIN-OTRO-VIAJE", await CuerpoNovedadAsync(e.Conductor));
        var viajeInexistente = await NovedadAsync(e.Conductor, Guid.NewGuid(), e.Vin, await CuerpoNovedadAsync(e.Conductor));

        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deOtroViaje.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viajeInexistente.StatusCode);
    }

    [Fact]
    public async Task Un_viaje_finalizado_no_admite_novedades()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = 'finalizado' WHERE Id = @v", ("@v", e.Viaje));

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    // ---------------------------------------------------------------------------------------- alertas

    [Fact]
    public async Task La_novedad_alerta_a_cada_coordinador_y_administrador_del_cliente_del_vehiculo_y_a_ninguno_de_otro_cliente()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var adminOtro = await SesionAsync("admin-otro@ejemplo.com", clsRol.ADMIN, otroCliente);
        var segundoCoordinador = await SesionAsync("coord2@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var retirado = await _api.SembrarUsuarioAsync("retirado@ejemplo.com", "Clave-Segura-1", clsRol.COORDINADOR_CALIDAD, activo: false);
        // Cada uno con un celular donde recibir el push.
        foreach (var (token, sesion) in new[] { ("tk-coord", e.Coordinador), ("tk-admin", e.Admin), ("tk-coord2", segundoCoordinador), ("tk-otro", coordinadorOtro) })
        {
            Assert.Equal(HttpStatusCode.OK, (await Como(sesion).PostAsJsonAsync("/api/dispositivos", new { token_fcm = token, plataforma = "android", modelo = "Pixel" })).StatusCode);
        }

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor, "incidente_vial", "requiere_reemplazo"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var hallazgo = (await ComoJsonAsync(r)).GetProperty("id").GetGuid();
        foreach (var destinatario in new[] { e.Coordinador, e.Admin, segundoCoordinador })
        {
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'novedad_conductor' AND Urgencia = 'alta' AND Canal = 'bandeja' AND UsuarioId = @u AND HallazgoId = @h AND ViajeId = @v AND EnviadaAt IS NOT NULL",
                ("@u", destinatario.UsuarioId), ("@h", hallazgo), ("@v", e.Viaje)));
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Canal = 'push' AND UsuarioId = @u AND HallazgoId = @h AND EnviadaAt IS NOT NULL AND ErrorEnvio IS NULL",
                ("@u", destinatario.UsuarioId), ("@h", hallazgo)));
        }

        // Ni otro cliente, ni el comercial, el inspector, el conductor o un usuario retirado reciben la alerta.
        foreach (var ajeno in new[] { coordinadorOtro.UsuarioId, adminOtro.UsuarioId, comercial.UsuarioId, inspector.UsuarioId, e.Conductor.UsuarioId, retirado.Id })
        {
            Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE UsuarioId = @u", ("@u", ajeno)));
        }

        Assert.Equal(6, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones"));
        // El push sale a cada celular de los destinatarios, con lo que la app necesita para abrir la alerta, y no sale correo.
        Assert.Equal(["tk-admin", "tk-coord", "tk-coord2"], _push.Enviados.Select(x => x.Token).Order().ToList());
        var enviado = _push.Enviados.First().Mensaje;
        Assert.Equal("novedad_conductor", enviado.Datos["tipo"]);
        Assert.Equal(hallazgo.ToString(), enviado.Datos["hallazgo_id"]);
        Assert.Equal(e.Viaje.ToString(), enviado.Datos["viaje_id"]);
        Assert.Contains(e.Vin, enviado.Titulo);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Canal = 'correo'"));
    }

    [Fact]
    public async Task Si_el_push_falla_o_no_hay_celular_la_novedad_se_guarda_y_la_notificacion_queda_con_su_error()
    {
        var e = await EscenarioAsync();
        await Como(e.Coordinador).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-roto", plataforma = "android" });
        _push.Respuesta = _ => clsResultadoPush.Fallo("FCM respondió 503: Service Unavailable");

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE TipoNovedad IS NOT NULL"));
        Assert.Equal("FCM respondió 503: Service Unavailable", (string?)await Sembrador.EscalarAsync(
            "SELECT ErrorEnvio FROM dbo.tblNotificaciones WHERE Canal = 'push' AND UsuarioId = @u AND EnviadaAt IS NULL", ("@u", e.Coordinador.UsuarioId)));
        // El administrador no tiene celular: su push queda con ese motivo, y su bandeja si se entrego.
        Assert.Equal(clsEmisorNotificaciones.SIN_DISPOSITIVOS, (string?)await Sembrador.EscalarAsync(
            "SELECT ErrorEnvio FROM dbo.tblNotificaciones WHERE Canal = 'push' AND UsuarioId = @u", ("@u", e.Admin.UsuarioId)));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Canal = 'bandeja' AND EnviadaAt IS NOT NULL"));
        // El dispositivo sigue activo: un fallo de FCM no es un token invalido.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblDispositivos WHERE TokenFcm = 'tk-roto' AND Activo = 1"));
    }

    [Fact]
    public async Task Una_excepcion_del_cliente_de_push_tampoco_rompe_la_novedad()
    {
        var e = await EscenarioAsync();
        await Como(e.Coordinador).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-lanza", plataforma = "ios" });
        _push.Lanza = true;

        var r = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("No se pudo enviar el push", (string?)await Sembrador.EscalarAsync(
            "SELECT ErrorEnvio FROM dbo.tblNotificaciones WHERE Canal = 'push' AND UsuarioId = @u", ("@u", e.Coordinador.UsuarioId)));
    }

    [Fact]
    public async Task Un_token_que_fcm_ya_no_reconoce_desactiva_el_dispositivo()
    {
        var e = await EscenarioAsync();
        await Como(e.Coordinador).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-vencido", plataforma = "android" });
        _push.Respuesta = _ => clsResultadoPush.Fallo("FCM respondió 404: Requested entity was not found.", tokenInvalido: true);

        await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor));

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblDispositivos WHERE TokenFcm = 'tk-vencido' AND Activo = 0"));
    }

    // ------------------------------------------------------------------------------------------ llegada

    [Fact]
    public async Task La_llegada_sin_novedad_queda_cerrada_con_su_hora_y_ubicacion_y_cuenta_para_la_unidad()
    {
        var e = await EscenarioAsync();
        Assert.Equal("sin_registro", (await UnidadDelViajeAsync(e)).GetProperty("estado_transporte").GetString());

        var r = await LlegadaAsync(e.Conductor, e.Viaje, e.Vin, new { fecha = FECHA, lat = 4.9, lng = -74.4 });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var llegada = await ComoJsonAsync(r);
        Assert.Equal(0, llegada.GetProperty("novedades").GetInt32());
        Assert.True(llegada.GetProperty("sin_novedad").GetBoolean());
        var inspeccion = llegada.GetProperty("inspeccion");
        Assert.Equal("llegada", inspeccion.GetProperty("momento").GetString());
        Assert.Equal("cerrada", inspeccion.GetProperty("estado").GetString());
        Assert.Equal(4.9, inspeccion.GetProperty("lat").GetDouble());
        Assert.Equal(-74.4, inspeccion.GetProperty("lng").GetDouble());
        Assert.Equal(e.Conductor.UsuarioId, inspeccion.GetProperty("usuario_id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'llegada' AND Estado = 'cerrada' AND CerradaAt IS NOT NULL", ("@e", e.Etapas[1])));
        Assert.Equal(0, await InspeccionesDeTransporteAsync(e, "salida"));
        var unidad = await UnidadDelViajeAsync(e);
        Assert.Equal("sin_novedad", unidad.GetProperty("estado_transporte").GetString());
        Assert.Equal(0, unidad.GetProperty("novedades").GetInt32());
        // El Cargue y el Transporte ya tienen su inspeccion cerrada.
        Assert.Equal(2, unidad.GetProperty("completas").GetInt32());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'llegada.registrar'"));
    }

    [Fact]
    public async Task La_llegada_con_novedades_cierra_la_salida_de_transporte_y_cuenta_las_novedades()
    {
        var e = await EscenarioAsync();
        var primera = await ComoJsonAsync(await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor, "golpe")));
        await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor, "piedra", "1_5cm"));
        var antes = await UnidadDelViajeAsync(e);

        var r = await LlegadaAsync(e.Conductor, e.Viaje, e.Vin);

        Assert.Equal("con_novedad", antes.GetProperty("estado_transporte").GetString());
        Assert.Equal(2, antes.GetProperty("novedades").GetInt32());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var llegada = await ComoJsonAsync(r);
        Assert.Equal(2, llegada.GetProperty("novedades").GetInt32());
        Assert.False(llegada.GetProperty("sin_novedad").GetBoolean());
        Assert.Equal("cerrada", (string?)await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblInspecciones WHERE Id = @i", ("@i", primera.GetProperty("inspeccion_id").GetGuid())));
        Assert.Equal(1, await InspeccionesDeTransporteAsync(e, "salida"));
        Assert.Equal(1, await InspeccionesDeTransporteAsync(e, "llegada"));
        var despues = await UnidadDelViajeAsync(e);
        Assert.Equal("llegada_con_novedad", despues.GetProperty("estado_transporte").GetString());
        Assert.Equal(2, despues.GetProperty("novedades").GetInt32());
        // Con la salida cerrada ya no se agregan novedades a esa unidad.
        var tarde = await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor));
        Assert.Equal(HttpStatusCode.Conflict, tarde.StatusCode);
    }

    [Fact]
    public async Task Confirmar_otra_vez_la_llegada_responde_409_llegada_ya_cerrada_y_conserva_la_primera()
    {
        var e = await EscenarioAsync();
        await LlegadaAsync(e.Conductor, e.Viaje, e.Vin, new { fecha = FECHA, lat = 4.9, lng = -74.4 });

        var repetida = await LlegadaAsync(e.Conductor, e.Viaje, e.Vin, new { fecha = "2026-10-21T20:00:00-05:00", lat = 1.0, lng = 1.0 });

        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal("LLEGADA_YA_CERRADA", (await ComoJsonAsync(repetida)).GetProperty("code").GetString());
        Assert.Equal(1, await InspeccionesDeTransporteAsync(e, "llegada"));
        Assert.Equal(4.9, (double)(await Sembrador.EscalarAsync("SELECT Lat FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'llegada'", ("@e", e.Etapas[1])))!);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"fecha\":\"2026-10-21T08:30:00-05:00\"}")]
    [InlineData("{\"fecha\":\"2026-10-21T08:30:00-05:00\",\"lat\":4.9}")]
    [InlineData("{\"lat\":4.9,\"lng\":-74.4}")]
    [InlineData("{\"fecha\":\"2026-10-21T08:30:00-05:00\",\"lat\":95,\"lng\":-74.4}")]
    public async Task La_llegada_sin_gps_ni_fecha_o_con_ellos_fuera_de_rango_responde_400(string cuerpo)
    {
        var e = await EscenarioAsync();

        var r = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/llegada", Json(cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, await InspeccionesDeTransporteAsync(e, "llegada"));
    }

    [Fact]
    public async Task La_llegada_es_solo_del_conductor_del_viaje()
    {
        var e = await EscenarioAsync();
        var otro = await SesionAsync("otro-conductor@ejemplo.com", clsRol.CONDUCTOR);

        var deOtro = await LlegadaAsync(otro, e.Viaje, e.Vin);
        var deCoordinador = await LlegadaAsync(e.Coordinador, e.Viaje, e.Vin);

        Assert.Equal(HttpStatusCode.Forbidden, deOtro.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deCoordinador.StatusCode);
        Assert.Equal(0, await InspeccionesDeTransporteAsync(e, "llegada"));
    }

    [Fact]
    public async Task La_llegada_con_una_foto_de_la_salida_sin_ubicacion_no_se_cierra()
    {
        var e = await EscenarioAsync();
        var novedad = await ComoJsonAsync(await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor)));
        // Una foto sin ubicacion (0, 0) bloquea el cierre de la salida: se repite antes de confirmar la llegada.
        await Sembrador.EjecutarAsync("UPDATE dbo.tblEvidencias SET SelloLat = 0, SelloLng = 0 WHERE InspeccionId = @i", ("@i", novedad.GetProperty("inspeccion_id").GetGuid()));

        var r = await LlegadaAsync(e.Conductor, e.Viaje, e.Vin);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, await InspeccionesDeTransporteAsync(e, "llegada"));
        Assert.Equal("borrador", (string?)await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblInspecciones WHERE Id = @i", ("@i", novedad.GetProperty("inspeccion_id").GetGuid())));
    }

    // ------------------------------------------------------------------------------ lo que ve el conductor

    /// <summary>Una inspeccion de salida del Cargue con un hallazgo del inspector (con su foto) y la foto del sello de quien lo valida.</summary>
    private async Task<(Guid Inspeccion, Guid Hallazgo)> HallazgoDelInspectorAsync(Escenario e, Sesion inspector)
    {
        var (inspeccion, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(
            e.Etapas[0], inspector.UsuarioId, await ZonaFrenteAsync(), nivel: "v2", conFoto: true, cerrada: false);
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, EsSello) VALUES (NEWID(), @i, 'sello.png', SYSDATETIMEOFFSET(), 4.6, -74.1, 1)",
            ("@i", inspeccion));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i", ("@i", inspeccion));
        return (inspeccion, hallazgo);
    }

    private static IEnumerable<JsonElement> EvidenciasDelInventario(JsonElement inventario) =>
        inventario.GetProperty("etapas").EnumerateArray().SelectMany(et => et.GetProperty("inspecciones").EnumerateArray()).SelectMany(i => i.GetProperty("evidencias").EnumerateArray());

    private static int HallazgosDelInventario(JsonElement inventario) =>
        inventario.GetProperty("etapas").EnumerateArray().SelectMany(et => et.GetProperty("inspecciones").EnumerateArray()).Sum(i => i.GetProperty("hallazgos").GetArrayLength());

    [Fact]
    public async Task El_conductor_ve_en_lectura_los_hallazgos_del_inspector_de_su_viaje_sin_nivel_seguimiento_evidencia_revision_ni_la_foto_del_sello()
    {
        var e = await EscenarioAsync();
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var (inspeccion, hallazgo) = await HallazgoDelInspectorAsync(e, inspector);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET InspectorId = @i WHERE Id = @v", ("@i", inspector.UsuarioId), ("@v", e.Viaje));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblHallazgos SET Nota = N'Nota interna del inspector', RevisadoPor = @u, RevisadoAt = SYSDATETIMEOFFSET() WHERE Id = @h", ("@u", inspector.UsuarioId), ("@h", hallazgo));

        var informeConductor = await ComoJsonAsync(await Como(e.Conductor).GetAsync($"/api/inspecciones/{inspeccion}"));
        var informeCoordinador = await ComoJsonAsync(await Como(e.Coordinador).GetAsync($"/api/inspecciones/{inspeccion}"));
        var inventarioConductor = await ComoJsonAsync(await Como(e.Conductor).GetAsync($"/api/vehiculos/{e.Vin}/inventario"));
        var inventarioInspector = await ComoJsonAsync(await Como(inspector).GetAsync($"/api/vehiculos/{e.Vin}/inventario"));

        var h = informeConductor.GetProperty("hallazgos").EnumerateArray().Single();
        Assert.Equal(hallazgo, h.GetProperty("id").GetGuid());
        // Lo que ve en lectura: zona, clase, tamano, origen, si es preexistente y la fecha.
        Assert.Equal("Frente", h.GetProperty("zona").GetString());
        Assert.Equal("golpe", h.GetProperty("clase_dano").GetString());
        Assert.Equal("inspector", h.GetProperty("origen").GetString());
        Assert.False(h.GetProperty("es_preexistente").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, h.GetProperty("created_at").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, h.GetProperty("tamano_dano").ValueKind);
        // Lo que no: el nivel, el seguimiento, la evidencia, la revision, la nota y el hallazgo previo.
        foreach (var campo in new[] { "nivel_severidad", "nivel_nombre", "nivel_accion", "nota", "hallazgo_origen_id", "confirmado_por", "estado_seguimiento", "evidencia_completa", "evidencia_faltante", "revisado_por", "revisado_at" })
        {
            Assert.Equal(JsonValueKind.Null, h.GetProperty(campo).ValueKind);
        }

        // Su foto si (con su URL firmada) y la del sello de quien valida no.
        var fotos = informeConductor.GetProperty("evidencias").EnumerateArray().ToList();
        Assert.Single(fotos, f => f.GetProperty("hallazgo_id").ValueKind != JsonValueKind.Null);
        Assert.DoesNotContain(fotos, f => f.GetProperty("es_sello").GetBoolean());
        Assert.Equal(1, HallazgosDelInventario(inventarioConductor));
        Assert.Single(EvidenciasDelInventario(inventarioConductor), f => f.GetProperty("hallazgo_id").ValueKind != JsonValueKind.Null);
        Assert.DoesNotContain(EvidenciasDelInventario(inventarioConductor), f => f.GetProperty("es_sello").GetBoolean());
        // Los demas roles no cambian: el coordinador y el inspector lo ven todo.
        var completo = informeCoordinador.GetProperty("hallazgos").EnumerateArray().Single();
        Assert.Equal(hallazgo, completo.GetProperty("id").GetGuid());
        Assert.Equal("v2", completo.GetProperty("nivel_severidad").GetString());
        Assert.Equal("pendiente", completo.GetProperty("estado_seguimiento").GetString());
        Assert.NotEqual(JsonValueKind.Null, completo.GetProperty("evidencia_faltante").ValueKind);
        Assert.Equal("Nota interna del inspector", completo.GetProperty("nota").GetString());
        Assert.Equal(2, informeCoordinador.GetProperty("evidencias").GetArrayLength());
        Assert.Equal(1, HallazgosDelInventario(inventarioInspector));
        Assert.Contains(EvidenciasDelInventario(inventarioInspector), f => f.GetProperty("es_sello").GetBoolean());
    }

    [Fact]
    public async Task El_inventario_del_conductor_no_trae_los_hallazgos_de_otros_viajes_del_mismo_vin()
    {
        var e = await EscenarioAsync();
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var otroConductor = await SesionAsync("otro-conductor@ejemplo.com", clsRol.CONDUCTOR);
        await HallazgoDelInspectorAsync(e, inspector);
        // El mismo VIN en otro viaje, de otro conductor, con su propio hallazgo.
        var otroViaje = await Sembrador.ViajeAsync(conductorId: otroConductor.UsuarioId, inspectorId: inspector.UsuarioId);
        var otraEtapa = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @v, @ve)", ("@v", otroViaje), ("@ve", e.Vehiculo));
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e, @ve, @v, (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue'), 1)",
            ("@e", otraEtapa), ("@ve", e.Vehiculo), ("@v", otroViaje));
        var (_, ajeno) = await Sembrador.InformeCerradoConHallazgoAsync(otraEtapa, inspector.UsuarioId, await ZonaFrenteAsync(), nivel: "v1", conFoto: true);

        var inventario = await ComoJsonAsync(await Como(e.Conductor).GetAsync($"/api/vehiculos/{e.Vin}/inventario"));
        var deOtro = await ComoJsonAsync(await Como(otroConductor).GetAsync($"/api/vehiculos/{e.Vin}/inventario"));

        Assert.Equal(1, HallazgosDelInventario(inventario));
        Assert.DoesNotContain(inventario.GetProperty("etapas").EnumerateArray(), et => et.GetProperty("viaje_id").GetGuid() == otroViaje);
        Assert.Equal(1, HallazgosDelInventario(deOtro));
        Assert.Equal(ajeno, deOtro.GetProperty("etapas").EnumerateArray().SelectMany(et => et.GetProperty("inspecciones").EnumerateArray()).Single().GetProperty("hallazgos")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task El_conductor_si_ve_sus_propias_novedades_en_el_informe_y_el_inventario()
    {
        var e = await EscenarioAsync();
        var novedad = await ComoJsonAsync(await NovedadAsync(e.Conductor, e.Viaje, e.Vin, await CuerpoNovedadAsync(e.Conductor)));

        var informe = await ComoJsonAsync(await Como(e.Conductor).GetAsync($"/api/inspecciones/{novedad.GetProperty("inspeccion_id").GetGuid()}"));
        var inventario = await ComoJsonAsync(await Como(e.Conductor).GetAsync($"/api/vehiculos/{e.Vin}/inventario"));

        Assert.Equal(novedad.GetProperty("id").GetGuid(), informe.GetProperty("hallazgos").EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.Equal(1, informe.GetProperty("evidencias").GetArrayLength());
        Assert.Equal(1, HallazgosDelInventario(inventario));
    }

    [Fact]
    public async Task Los_hallazgos_previos_y_el_listado_de_hallazgos_responden_403_al_conductor()
    {
        var e = await EscenarioAsync();
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var (inspeccion, _) = await HallazgoDelInspectorAsync(e, inspector);

        var previos = await Como(e.Conductor).GetAsync($"/api/inspecciones/{inspeccion}/hallazgos-previos");
        var listado = await Como(e.Conductor).GetAsync("/api/hallazgos");
        var detalle = await Como(e.Conductor).GetAsync($"/api/hallazgos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, previos.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listado.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detalle.StatusCode);
    }

    [Fact]
    public async Task El_listado_de_inspecciones_del_conductor_no_trae_hallazgos()
    {
        var e = await EscenarioAsync();
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        await HallazgoDelInspectorAsync(e, inspector);

        var listado = Items(await ComoJsonAsync(await Como(e.Conductor).GetAsync($"/api/inspecciones?viaje_id={e.Viaje}")));

        Assert.True(listado.GetArrayLength() >= 1);
        Assert.All(listado.EnumerateArray(), fila => Assert.False(fila.TryGetProperty("hallazgos", out _)));
    }

    // ------------------------------------------------------------------------------------ el perfil

    [Theory]
    [InlineData(clsRol.CONDUCTOR, true)]
    [InlineData(clsRol.INSPECTOR, true)]
    [InlineData(clsRol.COORDINADOR_CALIDAD, true)]
    [InlineData(clsRol.ADMIN, true)]
    [InlineData(clsRol.SUPER_ADMIN, true)]
    [InlineData(clsRol.COMERCIAL, false)]
    public async Task El_perfil_expone_ver_alertas_a_los_roles_que_las_reciben(string rol, bool tiene)
    {
        var sesion = await SesionAsync($"{rol}@ejemplo.com", rol);

        var me = await ComoJsonAsync(await Como(sesion).GetAsync("/api/auth/me"));

        Assert.Equal(tiene, me.GetProperty("capacidades").EnumerateArray().Any(c => c.GetString() == "ver_alertas"));
    }
}
