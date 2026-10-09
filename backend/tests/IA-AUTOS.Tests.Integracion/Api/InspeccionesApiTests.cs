using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class InspeccionesApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-07-17T12:30:15.123Z";

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol = clsRol.INSPECTOR)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ClonarAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Guid> CrearInspeccionAsync(Sesion sesion, Guid etapaId, string momento = "informe")
    {
        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapaId, momento, fecha = FECHA, lat = 4.6, lng = -74.1 });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await ClonarAsync(r)).GetProperty("id").GetGuid();
    }

    private async Task<(Sesion Sesion, Guid[] Etapas)> EscenarioAsync(string email = "insp@ejemplo.com")
    {
        var sesion = await SesionAsync(email);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-INSP-0001");
        return (sesion, etapas);
    }

    // ---------------------------------------------------------------- crear

    [Fact]
    public async Task Crear_toma_el_usuario_del_token_y_la_inspeccion_nace_en_borrador()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var otro = await Fabrica.SembrarUsuarioAsync("otro@ejemplo.com", "x");

        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new
        {
            etapa_id = etapas[0],
            usuario_id = otro.Id,
            momento = "informe",
            fecha = FECHA,
            lat = 4.60971,
            lng = -74.08175,
            conductor_nombre = "  María Pérez ",
            placa = "ABC123",
            trailer = "T-9",
            notas = "línea 1\nlínea 2",
        });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var i = await ClonarAsync(r);
        Assert.Equal(
            ["id", "etapa_id", "usuario_id", "momento", "fecha", "lat", "lng", "estado", "conductor_nombre", "placa", "trailer", "notas", "created_at", "updated_at"],
            i.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(sesion.UsuarioId, i.GetProperty("usuario_id").GetGuid());
        Assert.Equal("borrador", i.GetProperty("estado").GetString());
        Assert.Equal(FECHA, i.GetProperty("fecha").GetString());
        Assert.Equal("María Pérez", i.GetProperty("conductor_nombre").GetString());
        Assert.Equal(4.60971, i.GetProperty("lat").GetDouble());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.crear'"));
    }

    [Fact]
    public async Task Crear_con_datos_invalidos_responde_400_con_errores_por_campo()
    {
        var (sesion, etapas) = await EscenarioAsync();

        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = Guid.Empty, momento = "otro", lat = 95, estado = "cerrada" });
        var sinFecha = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "informe" });
        var fechaSinZona = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "informe", fecha = "2026-07-17T12:00:00" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var campos = (await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["etapa_id", "momento", "fecha", "lat", "estado"], campos);
        Assert.Equal(HttpStatusCode.BadRequest, sinFecha.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fechaSinZona.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }

    [Fact]
    public async Task Crear_con_etapa_inexistente_es_404_y_un_segundo_informe_de_la_etapa_es_409()
    {
        var (sesion, etapas) = await EscenarioAsync();

        var inexistente = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = Guid.NewGuid(), momento = "informe", fecha = FECHA });
        await CrearInspeccionAsync(sesion, etapas[0]);
        var repetido = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "informe", fecha = FECHA });

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("La etapa o el usuario no existen", (await ClonarAsync(inexistente)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("Esta etapa ya tiene un informe registrado", (await ClonarAsync(repetido)).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(clsRol.SUPER_ADMIN)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task Los_roles_sin_escritura_de_campo_no_pueden_registrar_informes(string rol)
    {
        var sesion = await SesionAsync("rol@ejemplo.com", rol);

        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = Guid.NewGuid(), momento = "informe", fecha = FECHA });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ---------------------------------------------------------------- hallazgos y evidencias

    [Fact]
    public async Task Agregar_hallazgo_devuelve_201_y_valida_catalogos_y_coordenadas()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var ok = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            parte_vehiculo = "  Guardafango delantero derecho ",
            clase_dano = "golpe",
            tamano_dano = "5_10cm",
            coords_diagrama = new { x = 0.25, y = 0.75 },
        });
        var malo = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            parte_vehiculo = " ",
            clase_dano = "inventada",
            tamano_dano = "enorme",
            coords_diagrama = new { x = 2, y = 0.5 },
        });

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var h = await ClonarAsync(ok);
        Assert.Equal("Guardafango delantero derecho", h.GetProperty("parte_vehiculo").GetString());
        Assert.Equal(0.75, h.GetProperty("coords_diagrama").GetProperty("y").GetDouble());
        Assert.Equal(id, h.GetProperty("inspeccion_id").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, malo.StatusCode);
        var campos = (await ClonarAsync(malo)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        // El orden de los errores no importa: llegan los del cuerpo y los del catalogo juntos.
        Assert.Equal(["clase_dano", "coords_diagrama", "parte_vehiculo", "tamano_dano"], campos.Order().ToList());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task Agregar_evidencia_devuelve_la_url_servible_y_no_expone_la_ruta_interna()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var r = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new
        {
            storage_path = "3f2b6c1e-0000-4000-8000-123456789abc.jpg",
            sello_fecha = FECHA,
            sello_lat = 4.6,
            sello_lng = -74.1,
        });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var e = await ClonarAsync(r);
        Assert.Equal(
            ["id", "inspeccion_id", "hallazgo_id", "sello_fecha", "sello_lat", "sello_lng", "url", "created_at", "updated_at"],
            e.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("/api/uploads/3f2b6c1e-0000-4000-8000-123456789abc.jpg", e.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, e.GetProperty("hallazgo_id").ValueKind);
    }

    [Theory]
    [InlineData("../secreto.jpg")]
    [InlineData("carpeta/foto.jpg")]
    [InlineData("a..b.jpg")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task La_evidencia_solo_acepta_nombres_planos_de_archivo(string ruta)
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var r = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new { storage_path = ruta, sello_fecha = FECHA });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("storage_path", out _));
    }

    [Fact]
    public async Task La_evidencia_vinculada_a_un_hallazgo_exige_que_pertenezca_a_la_misma_inspeccion()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var propia = await CrearInspeccionAsync(sesion, etapas[0]);
        var ajena = await CrearInspeccionAsync(sesion, etapas[1]);
        var hallazgo = (await ClonarAsync(await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{propia}/hallazgos",
            new { parte_vehiculo = "Capot", clase_dano = "golpe", tamano_dano = "1_5cm" }))).GetProperty("id").GetGuid();

        var bien = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{propia}/evidencias", new { storage_path = "a.jpg", sello_fecha = FECHA, hallazgo_id = hallazgo });
        var mal = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{ajena}/evidencias", new { storage_path = "b.jpg", sello_fecha = FECHA, hallazgo_id = hallazgo });

        Assert.Equal(HttpStatusCode.Created, bien.StatusCode);
        Assert.Equal(hallazgo, (await ClonarAsync(bien)).GetProperty("hallazgo_id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, mal.StatusCode);
        Assert.Equal("El hallazgo indicado no pertenece a esta inspección", (await ClonarAsync(mal)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Agregar_a_una_inspeccion_inexistente_responde_404()
    {
        var (sesion, _) = await EscenarioAsync();
        var id = Guid.NewGuid();

        var r = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new { parte_vehiculo = "Capot", clase_dano = "golpe", tamano_dano = "1_5cm" });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal($"Inspección {id} no encontrada", (await ClonarAsync(r)).GetProperty("message").GetString());
    }

    // ---------------------------------------------------------------- propiedad

    [Fact]
    public async Task Solo_el_creador_o_un_administrador_modifica_la_inspeccion()
    {
        var (creador, etapas) = await EscenarioAsync("creador@ejemplo.com");
        var intruso = await SesionAsync("intruso@ejemplo.com");
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var id = await CrearInspeccionAsync(creador, etapas[0]);
        var hallazgo = new { parte_vehiculo = "Capot", clase_dano = "golpe", tamano_dano = "1_5cm" };

        var deIntruso = await Cliente(intruso.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", hallazgo);
        var cierreIntruso = await Cliente(intruso.Token).PostAsync($"/api/inspecciones/{id}/cerrar", null);
        var deAdmin = await Cliente(admin.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", hallazgo);

        Assert.Equal(HttpStatusCode.Forbidden, deIntruso.StatusCode);
        Assert.Equal("No tiene permisos sobre esta inspección", (await ClonarAsync(deIntruso)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, cierreIntruso.StatusCode);
        Assert.Equal(HttpStatusCode.Created, deAdmin.StatusCode);
    }

    [Fact]
    public async Task La_validacion_de_propiedad_se_puede_desactivar_por_configuracion()
    {
        var (creador, etapas) = await EscenarioAsync("creador@ejemplo.com");
        var intruso = await SesionAsync("intruso@ejemplo.com");
        var id = await CrearInspeccionAsync(creador, etapas[0]);
        var abierta = CrearFabrica(new Dictionary<string, string?> { ["Seguridad:ValidarPropiedadInspeccion"] = "false" });

        var r = await Cliente(intruso.Token, abierta).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new { parte_vehiculo = "Capot", clase_dano = "golpe", tamano_dano = "1_5cm" });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    // ---------------------------------------------------------------- cierre

    [Fact]
    public async Task Cerrar_sin_novedades_ni_fotos_es_valido_y_devuelve_el_resumen()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var r = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/cerrar", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var c = await ClonarAsync(r);
        Assert.Equal(["inspeccion", "resumen"], c.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("cerrada", c.GetProperty("inspeccion").GetProperty("estado").GetString());
        Assert.Equal(0, c.GetProperty("resumen").GetProperty("evidencias").GetInt32());
        Assert.Equal(0, c.GetProperty("resumen").GetProperty("hallazgos").GetInt32());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.cerrar'"));
    }

    [Fact]
    public async Task Cerrar_con_novedad_exige_foto_y_toda_foto_debe_estar_geolocalizada()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new { parte_vehiculo = "Capot", clase_dano = "golpe", tamano_dano = "1_5cm" });

        var sinFoto = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/cerrar", null);
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new { storage_path = "sin-geo.jpg", sello_fecha = FECHA });
        var sinGeo = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/cerrar", null);

        Assert.Equal(HttpStatusCode.BadRequest, sinFoto.StatusCode);
        var c1 = await ClonarAsync(sinFoto);
        Assert.Equal("La inspección no cumple los requisitos para cerrarse", c1.GetProperty("message").GetString());
        Assert.Equal("Hay una novedad registrada: adjunta al menos una foto de la novedad", c1.GetProperty("details")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinGeo.StatusCode);
        Assert.Equal("1 foto(s) sin geolocalización del sello", (await ClonarAsync(sinGeo)).GetProperty("details")[0].GetString());
        Assert.Equal("borrador", (string?)await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblInspecciones WHERE Id = @i", ("@i", id)));
    }

    [Fact]
    public async Task Una_inspeccion_cerrada_es_inmutable_y_cerrarla_de_nuevo_es_409()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new { storage_path = "a.jpg", sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 });
        var cierre = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/cerrar", null);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal(1, (await ClonarAsync(cierre)).GetProperty("resumen").GetProperty("evidencias").GetInt32());

        var otraVez = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/cerrar", null);
        var hallazgo = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new { parte_vehiculo = "Capot", clase_dano = "golpe", tamano_dano = "1_5cm" });
        var evidencia = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new { storage_path = "b.jpg", sello_fecha = FECHA });

        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        Assert.Equal("La inspección ya está cerrada", (await ClonarAsync(otraVez)).GetProperty("message").GetString());
        foreach (var r in new[] { hallazgo, evidencia })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("La inspección está cerrada y es inmutable; no admite cambios", (await ClonarAsync(r)).GetProperty("message").GetString());
        }
    }

    // ---------------------------------------------------------------- transferencia

    [Fact]
    public async Task La_firma_parcial_se_acumula_y_el_sello_se_fija_al_completarla()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var receptor = await SesionAsync("recibe@ejemplo.com", clsRol.CONDUCTOR);
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var primera = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia",
            new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_entrega = "M0 0 L10 10" });
        var t1 = await ClonarAsync(primera);
        var segunda = await Cliente(receptor.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia",
            new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_recibe = "M5 5 L20 20" });
        var t2 = await ClonarAsync(segunda);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(
            ["id", "inspeccion_id", "entrega_usuario_id", "recibe_usuario_id", "firmada", "firmada_at", "created_at", "updated_at"],
            t1.EnumerateObject().Select(p => p.Name).ToList());
        Assert.False(t1.GetProperty("firmada").GetBoolean());
        Assert.Equal(JsonValueKind.Null, t1.GetProperty("firmada_at").ValueKind);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.True(t2.GetProperty("firmada").GetBoolean());
        Assert.Equal(JsonValueKind.String, t2.GetProperty("firmada_at").ValueKind);
        Assert.Equal(t1.GetProperty("id").GetGuid(), t2.GetProperty("id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal("M0 0 L10 10", (string?)await Sembrador.EscalarAsync("SELECT FirmaEntrega FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Se_puede_firmar_despues_de_cerrar_con_cualquier_usuario_de_campo_pero_una_transferencia_completa_es_inmutable()
    {
        var (creador, etapas) = await EscenarioAsync("creador@ejemplo.com");
        var tercero = await SesionAsync("tercero@ejemplo.com");
        var receptor = await SesionAsync("recibe@ejemplo.com", clsRol.CONDUCTOR);
        var id = await CrearInspeccionAsync(creador, etapas[0]);
        await Cliente(creador.Token).PostAsync($"/api/inspecciones/{id}/cerrar", null);
        var cuerpo = new { entrega_usuario_id = creador.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_entrega = "A", firma_recibe = "B" };

        var firma = await Cliente(tercero.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", cuerpo);
        var repetida = await Cliente(tercero.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", cuerpo);
        var alterada = await Cliente(tercero.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia",
            new { entrega_usuario_id = creador.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_entrega = "OTRA", firma_recibe = "B" });

        Assert.Equal(HttpStatusCode.Created, firma.StatusCode);
        Assert.True((await ClonarAsync(firma)).GetProperty("firmada").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, repetida.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, alterada.StatusCode);
        Assert.Equal("La transferencia ya está firmada por ambas partes y es inmutable", (await ClonarAsync(alterada)).GetProperty("message").GetString());
        Assert.Equal("A", (string?)await Sembrador.EscalarAsync("SELECT FirmaEntrega FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task La_transferencia_valida_usuarios_distintos_y_existentes()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var mismo = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = sesion.UsuarioId });
        var inexistente = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = Guid.NewGuid() });
        var sinIds = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/transferencia", Json("{}"));
        var inspeccionAjena = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{Guid.NewGuid()}/transferencia", new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, mismo.StatusCode);
        Assert.Equal(
            ["Quien entrega y quien recibe deben ser usuarios distintos"],
            (await ClonarAsync(mismo)).GetProperty("details").GetProperty("fieldErrors").GetProperty("recibe_usuario_id").EnumerateArray().Select(m => m.GetString()).ToList());
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("Algún usuario (entrega/recibe) no existe", (await ClonarAsync(inexistente)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinIds.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inspeccionAjena.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    // ---------------------------------------------------------------- informe y listado

    [Fact]
    public async Task El_informe_consolida_contexto_hallazgos_evidencias_y_transferencia_con_nombres_y_firmas()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var receptor = await SesionAsync("recibe@ejemplo.com", clsRol.CONDUCTOR);
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new { parte_vehiculo = "Capot", clase_dano = "golpe", tamano_dano = "1_5cm", coords_diagrama = new { x = 0.1, y = 0.2 } });
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new { storage_path = "foto.jpg", sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 });
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_entrega = "TRAZO-A", firma_recibe = "TRAZO-B" });

        var r = await Cliente(receptor.Token).GetAsync($"/api/inspecciones/{id}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var inf = await ClonarAsync(r);
        Assert.Equal("VIN-INSP-0001", inf.GetProperty("vin").GetString());
        Assert.Equal("Renault", inf.GetProperty("marca").GetString());
        Assert.Equal("cargue", inf.GetProperty("proceso").GetString());
        Assert.True(inf.GetProperty("requiere_inspeccion").GetBoolean());
        Assert.Equal("Nombre insp@ejemplo.com", inf.GetProperty("inspector_nombre").GetString());
        Assert.Equal(1, inf.GetProperty("hallazgos").GetArrayLength());
        Assert.Equal("/api/uploads/foto.jpg", inf.GetProperty("evidencias")[0].GetProperty("url").GetString());
        var t = inf.GetProperty("transferencia");
        Assert.Equal("Nombre insp@ejemplo.com", t.GetProperty("entrega_nombre").GetString());
        Assert.Equal("Nombre recibe@ejemplo.com", t.GetProperty("recibe_nombre").GetString());
        Assert.Equal("TRAZO-A", t.GetProperty("firma_entrega").GetString());
        Assert.Equal("TRAZO-B", t.GetProperty("firma_recibe").GetString());
        Assert.True(t.GetProperty("firmada").GetBoolean());
    }

    [Fact]
    public async Task El_informe_usa_la_asignacion_como_respaldo_de_placa_y_conductor()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var conductor = await Fabrica.SembrarUsuarioAsync("cond@ejemplo.com", "x", clsRol.CONDUCTOR, nombre: "Conductor Asignado");
        var vehiculoId = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblVehiculos WHERE Vin = 'VIN-INSP-0001'"))!;
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblAsignaciones (Id, VehiculoId, Ninera, ConductorId, AsignadoPor, CreatedAt) VALUES (NEWID(), @v, N'NIN-VIEJA', NULL, @u, DATEADD(DAY, -2, SYSDATETIMEOFFSET()))",
            ("@v", vehiculoId), ("@u", sesion.UsuarioId));
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblAsignaciones (Id, VehiculoId, Ninera, ConductorId, AsignadoPor) VALUES (NEWID(), @v, N'NIN-RECIENTE', @c, @u)",
            ("@v", vehiculoId), ("@c", conductor.Id), ("@u", sesion.UsuarioId));
        var sinDatos = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "informe", fecha = FECHA });
        var conDatos = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[1], momento = "informe", fecha = FECHA, placa = "PLACA-APP", conductor_nombre = "Capturado en app" });

        var a = await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{(await ClonarAsync(sinDatos)).GetProperty("id").GetGuid()}"));
        var b = await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{(await ClonarAsync(conDatos)).GetProperty("id").GetGuid()}"));

        Assert.Equal("NIN-RECIENTE", a.GetProperty("ninera").GetString());
        Assert.Equal("Conductor Asignado", a.GetProperty("conductor_nombre").GetString());
        Assert.Equal("PLACA-APP", b.GetProperty("ninera").GetString());
        Assert.Equal("Capturado en app", b.GetProperty("conductor_nombre").GetString());
    }

    [Fact]
    public async Task El_informe_de_una_inspeccion_inexistente_responde_404()
    {
        var (sesion, _) = await EscenarioAsync();

        var r = await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task El_listado_filtra_ordena_y_devuelve_vin_y_proceso()
    {
        var (sesion, etapas) = await EscenarioAsync("uno@ejemplo.com");
        var otra = await SesionAsync("dos@ejemplo.com");
        var (_, etapas2) = await Sembrador.VehiculoConRutaAsync("9FBHSR4E8PM000999");
        var a = await CrearInspeccionAsync(sesion, etapas[0]);
        await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{a}/cerrar", null);
        await Task.Delay(20);
        var b = await CrearInspeccionAsync(otra, etapas2[1]);

        var todas = await ClonarAsync(await Cliente(sesion.Token).GetAsync("/api/inspecciones"));
        var porUsuario = await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones?usuarioId={otra.UsuarioId}"));
        var porEstado = await ClonarAsync(await Cliente(sesion.Token).GetAsync("/api/inspecciones?estado=cerrada"));
        var porProceso = await ClonarAsync(await Cliente(sesion.Token).GetAsync("/api/inspecciones?proceso=transporte"));
        var porVin = await ClonarAsync(await Cliente(sesion.Token).GetAsync("/api/inspecciones?vin=9fbhsr4e8pm000999"));
        var porFecha = await ClonarAsync(await Cliente(sesion.Token).GetAsync("/api/inspecciones?desde=2026-07-01T00:00:00Z&hasta=2026-07-31T00:00:00Z"));

        Assert.Equal(2, todas.GetArrayLength());
        Assert.Equal(["vin", "proceso"], todas[0].EnumerateObject().Select(p => p.Name).TakeLast(2).ToList());
        Assert.Equal(1, porUsuario.GetArrayLength());
        Assert.Equal(b, porUsuario[0].GetProperty("id").GetGuid());
        Assert.Equal(a, porEstado[0].GetProperty("id").GetGuid());
        Assert.Equal("transporte", porProceso[0].GetProperty("proceso").GetString());
        Assert.Equal("9FBHSR4E8PM000999", porVin[0].GetProperty("vin").GetString());
        Assert.Equal(2, porFecha.GetArrayLength());
    }

    [Fact]
    public async Task El_listado_valida_los_filtros()
    {
        var (sesion, _) = await EscenarioAsync();

        var r = await Cliente(sesion.Token).GetAsync("/api/inspecciones?vin=corto&usuarioId=no&momento=x&estado=y&proceso=z&desde=ayer");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var campos = (await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["vin", "usuarioId", "momento", "estado", "proceso", "desde"], campos);
    }

    [Fact]
    public async Task Todos_los_endpoints_de_inspecciones_exigen_autenticacion()
    {
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync("/api/inspecciones")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync($"/api/inspecciones/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsync("/api/inspecciones", Json("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsync($"/api/inspecciones/{id}/cerrar", null)).StatusCode);
    }
}
