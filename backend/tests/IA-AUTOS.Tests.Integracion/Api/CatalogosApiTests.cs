using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>Catalogos maestros: lectura con version (ETag), administracion por rol, valores en uso y dibujo de danos de solo lectura.</summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed class CatalogosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private static readonly string[] NOMBRES =
        ["etapas", "puntos-custodia", "tipos-unidad", "clases-dano", "tamanos-dano", "niveles-severidad", "vistas-foto", "cuadrantes", "paneles-dano", "cuadrante-regiones", "clientes", "estados-sofasa", "motivos-sin-inspector", "tipos-extra", "lugares"];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private async Task<string> TokenAsync(string rol = clsRol.ADMIN)
    {
        var email = $"{rol}@ejemplo.com";
        await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol);
        return await LoginAsync(email, "Clave-Segura-1");
    }

    private static async Task<JsonElement> LeerJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<JsonElement> CatalogoAsync(string token, string nombre)
    {
        var r = await Cliente(token).GetAsync($"/api/catalogos/{nombre}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await LeerJsonAsync(r);
    }

    private async Task<JsonElement> ValorAsync(string token, string catalogo, string codigo) =>
        (await CatalogoAsync(token, catalogo)).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("codigo").GetString() == codigo);

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), System.Text.Encoding.UTF8, "application/json");

    // ----------------------------------------------------------------------------------------------- lectura

    [Fact]
    public async Task Los_catalogos_iniciales_estan_completos()
    {
        var token = await TokenAsync(clsRol.INSPECTOR);

        var r = await Cliente(token).GetAsync("/api/catalogos");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var doc = await LeerJsonAsync(r);
        var catalogos = doc.GetProperty("catalogos");
        Assert.Equal(NOMBRES.Order(), catalogos.EnumerateObject().Select(p => p.Name).Order());
        Assert.False(string.IsNullOrEmpty(doc.GetProperty("version").GetString()));
        Assert.Equal(["cargue", "transporte", "descargue"], catalogos.GetProperty("etapas").EnumerateArray().Select(i => i.GetProperty("codigo").GetString()).ToList());
        Assert.Equal(["CEDI 1", "Origen", "Certificadora", "Destino", "CEDI 2", "Concesionario"], catalogos.GetProperty("puntos-custodia").EnumerateArray().Select(i => i.GetProperty("nombre").GetString()).ToList());
        Assert.Equal(["Niñera", "Nodriza", "Quintinaria", "Planchón grúa"], catalogos.GetProperty("tipos-unidad").EnumerateArray().Select(i => i.GetProperty("nombre").GetString()).ToList());
        Assert.Equal(10, catalogos.GetProperty("clases-dano").GetArrayLength());
        Assert.Equal(6, catalogos.GetProperty("tamanos-dano").GetArrayLength());
        Assert.Equal(["V1+", "V1", "V2", "V3"], catalogos.GetProperty("niveles-severidad").EnumerateArray().Select(i => i.GetProperty("nombre").GetString()).ToList());
        Assert.Equal(
            ["Se recupera antes del despacho", "Se rechaza el veh\u00edculo", "Validaci\u00f3n por Sofasa", "Se deroga (se acepta el defecto)"],
            catalogos.GetProperty("niveles-severidad").EnumerateArray().Select(i => i.GetProperty("accion").GetString()).ToList());
        var vistas = catalogos.GetProperty("vistas-foto").EnumerateArray().ToList();
        Assert.Equal(9, vistas.Count);
        Assert.Equal(7, vistas.Count(v => v.GetProperty("obligatoria").GetBoolean()));
        Assert.Equal(["piezas_baul", "cojineria"], vistas.Where(v => !v.GetProperty("obligatoria").GetBoolean()).Select(v => v.GetProperty("codigo").GetString()).ToList());
        // El dibujo base de las pruebas: los dos cuadrantes sin lado (50 y 51) y los cinco paneles, sin regiones dibujadas (el dibujo real lo siembra el guion 34).
        Assert.Equal(2, catalogos.GetProperty("cuadrantes").GetArrayLength());
        Assert.Equal(5, catalogos.GetProperty("paneles-dano").GetArrayLength());
        Assert.Equal(0, catalogos.GetProperty("cuadrante-regiones").GetArrayLength());
        Assert.False(catalogos.TryGetProperty("cuadrante-vistas", out _));
        Assert.Equal(["sofasa"], catalogos.GetProperty("clientes").EnumerateArray().Select(i => i.GetProperty("codigo").GetString()).ToList());
    }

    [Fact]
    public async Task Los_valores_de_los_catalogos_traen_solo_sus_campos()
    {
        var token = await TokenAsync();

        var etapa = await ValorAsync(token, "etapas", "cargue");
        var nivel = await ValorAsync(token, "niveles-severidad", "v1");
        var vista = await ValorAsync(token, "vistas-foto", "frontal");

        Assert.Equal(["id", "codigo", "nombre", "orden", "activo"], etapa.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(["id", "codigo", "nombre", "orden", "activo", "accion"], nivel.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(["id", "codigo", "nombre", "orden", "activo", "obligatoria", "tipo_vehiculo"], vista.EnumerateObject().Select(p => p.Name).ToList());
    }

    [Fact]
    public async Task La_lectura_exige_sesion()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync("/api/catalogos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync("/api/catalogos/etapas")).StatusCode);
    }

    [Fact]
    public async Task Un_catalogo_desconocido_responde_404_con_los_disponibles()
    {
        var token = await TokenAsync();

        var r = await Cliente(token).GetAsync("/api/catalogos/inventado");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("clases-dano", (await LeerJsonAsync(r)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task La_version_viaja_en_ETag_y_un_If_None_Match_vigente_responde_304()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);

        var primera = await cliente.GetAsync("/api/catalogos");
        var etag = primera.Headers.ETag!.Tag;
        var version = (await LeerJsonAsync(primera)).GetProperty("version").GetString();

        Assert.Equal($"\"{version}\"", etag);
        var cache = primera.Headers.CacheControl!;
        Assert.True(cache.NoCache && cache.Private, "Debe revalidarse en cada uso y no guardarse en caches compartidas");

        var vigente = new HttpRequestMessage(HttpMethod.Get, "/api/catalogos");
        vigente.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        var respuesta = await cliente.SendAsync(vigente);
        Assert.Equal(HttpStatusCode.NotModified, respuesta.StatusCode);
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());

        var distinta = new HttpRequestMessage(HttpMethod.Get, "/api/catalogos");
        distinta.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"otra\""));
        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(distinta)).StatusCode);

        var debil = new HttpRequestMessage(HttpMethod.Get, "/api/catalogos");
        debil.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag, isWeak: true));
        Assert.Equal(HttpStatusCode.NotModified, (await cliente.SendAsync(debil)).StatusCode);
    }

    [Fact]
    public async Task Cada_catalogo_tiene_su_propia_version_que_solo_cambia_si_cambia_su_contenido()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var clasesAntes = (await LeerJsonAsync(await cliente.GetAsync("/api/catalogos/clases-dano"))).GetProperty("version").GetString();
        var etapasAntes = (await LeerJsonAsync(await cliente.GetAsync("/api/catalogos/etapas"))).GetProperty("version").GetString();
        var globalAntes = (await LeerJsonAsync(await cliente.GetAsync("/api/catalogos"))).GetProperty("version").GetString();

        var creada = await cliente.PostAsync("/api/catalogos/clases-dano", Cuerpo(new { codigo = "oxidacion", nombre = "Oxidación" }));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);

        Assert.NotEqual(clasesAntes, (await LeerJsonAsync(await cliente.GetAsync("/api/catalogos/clases-dano"))).GetProperty("version").GetString());
        Assert.Equal(etapasAntes, (await LeerJsonAsync(await cliente.GetAsync("/api/catalogos/etapas"))).GetProperty("version").GetString());
        Assert.NotEqual(globalAntes, (await LeerJsonAsync(await cliente.GetAsync("/api/catalogos"))).GetProperty("version").GetString());
    }

    // ----------------------------------------------------------------------------------- permisos de escritura

    [Theory]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.SUPER_ADMIN)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task Administradores_y_coordinadores_de_calidad_administran_los_catalogos(string rol)
    {
        var cliente = Cliente(await TokenAsync(rol));

        var creada = await cliente.PostAsync("/api/catalogos/tipos-unidad", Cuerpo(new { codigo = "cama_baja", nombre = "Cama baja" }));

        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
    }

    [Theory]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    [InlineData(clsRol.COMERCIAL)]
    public async Task Los_demas_roles_leen_pero_no_modifican_los_catalogos(string rol)
    {
        var cliente = Cliente(await TokenAsync(rol));
        var etapa = await ValorAsync(await TokenAsync(clsRol.ADMIN), "etapas", "cargue");
        var id = etapa.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/catalogos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsync("/api/catalogos/etapas", Cuerpo(new { codigo = "x", nombre = "X" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PatchAsync($"/api/catalogos/etapas/{id}", Cuerpo(new { nombre = "X" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.DeleteAsync($"/api/catalogos/etapas/{id}")).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------- CRUD

    [Fact]
    public async Task Crear_editar_e_inactivar_un_valor_se_refleja_de_inmediato_sin_desplegar()
    {
        var token = await TokenAsync(clsRol.COORDINADOR_CALIDAD);
        var cliente = Cliente(token);

        var creada = await cliente.PostAsync("/api/catalogos/clases-dano", Cuerpo(new { codigo = "oxidacion", nombre = "Oxidación", orden = 11 }));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var valor = await LeerJsonAsync(creada);
        var id = valor.GetProperty("id").GetGuid();
        Assert.Equal("oxidacion", valor.GetProperty("codigo").GetString());
        Assert.True(valor.GetProperty("activo").GetBoolean());
        Assert.Equal(11, (await ValorAsync(token, "clases-dano", "oxidacion")).GetProperty("orden").GetInt32());

        var editada = await cliente.PatchAsync($"/api/catalogos/clases-dano/{id}", Cuerpo(new { nombre = "Oxidación superficial", orden = 3 }));
        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        var tras = await ValorAsync(token, "clases-dano", "oxidacion");
        Assert.Equal("Oxidación superficial", tras.GetProperty("nombre").GetString());
        Assert.Equal(3, tras.GetProperty("orden").GetInt32());

        await cliente.PatchAsync($"/api/catalogos/clases-dano/{id}", Cuerpo(new { activo = false }));
        Assert.False((await ValorAsync(token, "clases-dano", "oxidacion")).GetProperty("activo").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await cliente.DeleteAsync($"/api/catalogos/clases-dano/{id}")).StatusCode);
        Assert.DoesNotContain(
            (await CatalogoAsync(token, "clases-dano")).GetProperty("items").EnumerateArray(),
            i => i.GetProperty("codigo").GetString() == "oxidacion");
    }

    [Theory]
    [InlineData("""{"codigo":"Mayuscula","nombre":"X"}""", "codigo")]
    [InlineData("""{"codigo":"con espacio","nombre":"X"}""", "codigo")]
    [InlineData("""{"codigo":"","nombre":"X"}""", "codigo")]
    [InlineData("""{"nombre":"Sin codigo"}""", "codigo")]
    [InlineData("""{"codigo":"valido"}""", "nombre")]
    [InlineData("""{"codigo":"valido","nombre":"   "}""", "nombre")]
    [InlineData("""{"codigo":"valido","nombre":"X","orden":-1}""", "orden")]
    [InlineData("""{"codigo":"valido","nombre":"X","orden":"uno"}""", "orden")]
    [InlineData("""{"codigo":"valido","nombre":"X","activo":"si"}""", "activo")]
    [InlineData("""{"codigo":"valido","nombre":"X","accion":"no aplica a este catalogo"}""", "accion")]
    public async Task Un_cuerpo_invalido_al_crear_responde_400_indicando_el_campo(string cuerpo, string campo)
    {
        var cliente = Cliente(await TokenAsync());

        var r = await cliente.PostAsync("/api/catalogos/etapas", new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errores = (await LeerJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors");
        Assert.True(errores.TryGetProperty(campo, out _), $"Se esperaba un error en '{campo}': {errores}");
    }

    [Fact]
    public async Task Un_cuerpo_con_texto_que_no_es_UTF8_valido_responde_400_y_no_500()
    {
        var cliente = Cliente(await TokenAsync());
        // 0xF3 es la "o" con tilde en Latin-1, pero no es UTF-8 valido.
        byte[] cuerpo = [.. System.Text.Encoding.ASCII.GetBytes("{\"codigo\":\"oxidacion\",\"nombre\":\"Oxidaci"), 0xF3, .. System.Text.Encoding.ASCII.GetBytes("n\"}")];
        var contenido = new ByteArrayContent(cuerpo);
        contenido.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var r = await cliente.PostAsync("/api/catalogos/clases-dano", contenido);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await LeerJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("nombre", out _));
    }

    [Fact]
    public async Task Un_texto_con_escapes_unicode_se_guarda_con_su_acento()
    {
        var cliente = Cliente(await TokenAsync());
        var contenido = new StringContent("{\"codigo\":\"oxidacion\",\"nombre\":\"Oxidaci\\u00f3n\",\"orden\":11}", System.Text.Encoding.UTF8, "application/json");

        var r = await cliente.PostAsync("/api/catalogos/clases-dano", contenido);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("Oxidación", (await LeerJsonAsync(r)).GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task Un_codigo_repetido_es_conflicto_y_el_codigo_no_se_puede_cambiar()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var cargue = await ValorAsync(token, "etapas", "cargue");

        var repetido = await cliente.PostAsync("/api/catalogos/etapas", Cuerpo(new { codigo = "cargue", nombre = "Otro cargue" }));
        var cambiaCodigo = await cliente.PatchAsync($"/api/catalogos/etapas/{cargue.GetProperty("id").GetGuid()}", Cuerpo(new { codigo = "otro" }));
        var mismoCodigo = await cliente.PatchAsync($"/api/catalogos/etapas/{cargue.GetProperty("id").GetGuid()}", Cuerpo(new { codigo = "cargue", nombre = "Cargue en patio" }));

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cambiaCodigo.StatusCode);
        Assert.True((await LeerJsonAsync(cambiaCodigo)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("codigo", out _));
        Assert.Equal(HttpStatusCode.OK, mismoCodigo.StatusCode);
    }

    [Fact]
    public async Task Un_campo_de_otro_catalogo_o_un_valor_nulo_en_un_campo_obligatorio_se_rechaza()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var cargue = await ValorAsync(token, "etapas", "cargue");
        var ruta = $"/api/catalogos/etapas/{cargue.GetProperty("id").GetGuid()}";

        var campoAjeno = await cliente.PatchAsync(ruta, Cuerpo(new { obligatoria = false }));
        var nombreNulo = await cliente.PatchAsync(ruta, new StringContent("""{"nombre":null}""", System.Text.Encoding.UTF8, "application/json"));
        var inexistente = await cliente.PatchAsync($"/api/catalogos/etapas/{Guid.NewGuid()}", Cuerpo(new { nombre = "X" }));

        Assert.Equal(HttpStatusCode.BadRequest, campoAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nombreNulo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Fact]
    public async Task La_accion_de_un_nivel_de_severidad_se_edita_y_se_borra()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var id = (await ValorAsync(token, "niveles-severidad", "v1_plus")).GetProperty("id").GetGuid();

        await cliente.PatchAsync($"/api/catalogos/niveles-severidad/{id}", Cuerpo(new { accion = "Detener la unidad y escalar" }));
        Assert.Equal("Detener la unidad y escalar", (await ValorAsync(token, "niveles-severidad", "v1_plus")).GetProperty("accion").GetString());

        await cliente.PatchAsync($"/api/catalogos/niveles-severidad/{id}", Cuerpo(new { accion = "" }));
        Assert.Equal(JsonValueKind.Null, (await ValorAsync(token, "niveles-severidad", "v1_plus")).GetProperty("accion").ValueKind);

        var larga = await cliente.PatchAsync($"/api/catalogos/niveles-severidad/{id}", Cuerpo(new { accion = new string('a', 501) }));
        Assert.Equal(HttpStatusCode.BadRequest, larga.StatusCode);
    }

    // ------------------------------------------------------------------------------------- valores en uso

    [Fact]
    public async Task Un_valor_en_uso_no_se_borra_responde_409_y_solo_puede_inactivarse()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var usuario = await Fabrica.SembrarUsuarioAsync("insp@ejemplo.com", "x", clsRol.INSPECTOR);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-EN-USO");
        await Sembrador.InformeAsync(etapas[0], usuario.Id, cerrada: false, hallazgos: 1);

        var clase = await ValorAsync(token, "clases-dano", "golpe");
        var tamano = await ValorAsync(token, "tamanos-dano", "1_5cm");
        var etapa = await ValorAsync(token, "etapas", "cargue");

        var borrarClase = await cliente.DeleteAsync($"/api/catalogos/clases-dano/{clase.GetProperty("id").GetGuid()}");
        var borrarTamano = await cliente.DeleteAsync($"/api/catalogos/tamanos-dano/{tamano.GetProperty("id").GetGuid()}");
        var borrarEtapa = await cliente.DeleteAsync($"/api/catalogos/etapas/{etapa.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.Conflict, borrarClase.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, borrarTamano.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, borrarEtapa.StatusCode);
        var mensaje = (await LeerJsonAsync(borrarClase)).GetProperty("message").GetString();
        Assert.Contains("en uso", mensaje);
        Assert.Contains("inactivar", mensaje);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE ClaseDano = 'golpe'"));

        var inactivada = await cliente.PatchAsync($"/api/catalogos/clases-dano/{clase.GetProperty("id").GetGuid()}", Cuerpo(new { activo = false }));
        Assert.Equal(HttpStatusCode.OK, inactivada.StatusCode);
    }

    [Fact]
    public async Task Un_lugar_asignado_a_una_etapa_o_a_un_viaje_tampoco_se_borra()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-LUGAR");
        var lugar = await ValorAsync(token, "puntos-custodia", "cedi_1");
        var otro = await ValorAsync(token, "puntos-custodia", "certificadora");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblEtapas SET PuntoCustodiaId = @l WHERE Id = @e", ("@l", lugar.GetProperty("id").GetGuid()), ("@e", etapas[0]));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET OrigenId = @l WHERE Id = (SELECT ViajeId FROM dbo.tblEtapas WHERE Id = @e)", ("@l", otro.GetProperty("id").GetGuid()), ("@e", etapas[0]));

        var borrarDeEtapa = await cliente.DeleteAsync($"/api/catalogos/puntos-custodia/{lugar.GetProperty("id").GetGuid()}");
        var borrarDeViaje = await cliente.DeleteAsync($"/api/catalogos/puntos-custodia/{otro.GetProperty("id").GetGuid()}");
        var unidad = await ValorAsync(token, "tipos-unidad", "ninera");
        var borrarUnidad = await cliente.DeleteAsync($"/api/catalogos/tipos-unidad/{unidad.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.Conflict, borrarDeEtapa.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, borrarDeViaje.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, borrarUnidad.StatusCode);
        Assert.Contains("en uso", (await LeerJsonAsync(borrarDeViaje)).GetProperty("message").GetString());
        Assert.Contains("en uso", (await LeerJsonAsync(borrarUnidad)).GetProperty("message").GetString());
    }

    /// <summary>Sube una foto y arma el cuerpo de un hallazgo con ella (un hallazgo exige su foto propia).</summary>
    private async Task<object> HallazgoConFotoAsync(string token, string clase, string tamano)
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var subida = await Cliente(token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, subida.StatusCode);
        var ruta = (await LeerJsonAsync(subida)).GetProperty("storage_path").GetString();
        return new
        {
            cuadrante_numero = 50,
            clase_dano = clase,
            tamano_dano = tamano,
            nivel_severidad = "v2",
            evidencia = new { storage_path = ruta, sello_fecha = "2026-07-17T12:30:15.123Z", sello_lat = 4.6, sello_lng = -74.1 },
        };
    }

    [Fact]
    public async Task Inactivar_una_clase_la_retira_de_los_hallazgos_nuevos_pero_los_guardados_se_siguen_viendo()
    {
        var adminToken = await TokenAsync();
        var admin = Cliente(adminToken);
        var inspectorEmail = "insp2@ejemplo.com";
        var inspector = await Fabrica.SembrarUsuarioAsync(inspectorEmail, "Clave-Segura-1", clsRol.INSPECTOR);
        var inspectorToken = await LoginAsync(inspectorEmail, "Clave-Segura-1");
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-INACTIVA", inspectorId: inspector.Id);
        var inspeccionId = await Sembrador.InformeAsync(etapas[0], inspector.Id, cerrada: false, hallazgos: 1);
        var claseId = (await ValorAsync(adminToken, "clases-dano", "golpe")).GetProperty("id").GetGuid();

        await admin.PatchAsync($"/api/catalogos/clases-dano/{claseId}", Cuerpo(new { activo = false }));

        var nuevo = await Cliente(inspectorToken).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccionId}/hallazgos",
            await HallazgoConFotoAsync(inspectorToken, "golpe", "5_10cm"));
        Assert.Equal(HttpStatusCode.BadRequest, nuevo.StatusCode);
        Assert.True((await LeerJsonAsync(nuevo)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("clase_dano", out _));

        var informe = await LeerJsonAsync(await Cliente(inspectorToken).GetAsync($"/api/inspecciones/{inspeccionId}"));
        Assert.Equal("golpe", informe.GetProperty("hallazgos")[0].GetProperty("clase_dano").GetString());

        await admin.PatchAsync($"/api/catalogos/clases-dano/{claseId}", Cuerpo(new { activo = true }));
        var otraVez = await Cliente(inspectorToken).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccionId}/hallazgos",
            await HallazgoConFotoAsync(inspectorToken, "golpe", "5_10cm"));
        Assert.Equal(HttpStatusCode.Created, otraVez.StatusCode);
    }

    [Fact]
    public async Task Una_clase_nueva_se_puede_usar_en_hallazgos_sin_desplegar()
    {
        var adminToken = await TokenAsync();
        var inspector = await Fabrica.SembrarUsuarioAsync("insp3@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        var inspectorToken = await LoginAsync("insp3@ejemplo.com", "Clave-Segura-1");
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-NUEVA-CLASE");
        var inspeccionId = await Sembrador.InformeAsync(etapas[0], inspector.Id, cerrada: false);

        await Cliente(adminToken).PostAsync("/api/catalogos/clases-dano", Cuerpo(new { codigo = "oxidacion", nombre = "Oxidación" }));

        var r = await Cliente(inspectorToken).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccionId}/hallazgos",
            await HallazgoConFotoAsync(inspectorToken, "oxidacion", "1_5cm"));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    // ----------------------------------------------------------------------------------- la ruta y los filtros

    [Fact]
    public async Task La_ruta_de_los_vin_de_un_viaje_sigue_las_etapas_activas_del_catalogo()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var descargue = await ValorAsync(token, "etapas", "descargue");
        var unidad = (await ValorAsync(token, "tipos-unidad", "ninera")).GetProperty("id").GetGuid();
        await cliente.PostAsync("/api/vehiculos/cargar", Cuerpo(new { items = new[]
        {
            new { vin = "9FBHSR4E8PM000001", marca = "Renault", operacion = "nacional" },
            new { vin = "9FBHSR4E8PM000002", marca = "Renault", operacion = "nacional" },
        } }));

        var lugar = await LugarRealAsync(cliente, "Destino de la ruta");
        await cliente.PatchAsync($"/api/catalogos/etapas/{descargue.GetProperty("id").GetGuid()}", Cuerpo(new { activo = false }));
        var sinDescargue = await cliente.PostAsync("/api/viajes", Cuerpo(new { codigo_mtrack = "MTK-RUTA-1", tipo_unidad_id = unidad, placa = "ABC123", unidades = new[] { new { vin = "9FBHSR4E8PM000001", lugar_destino_id = lugar } } }));
        Assert.Equal(HttpStatusCode.Created, sinDescargue.StatusCode);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = '9FBHSR4E8PM000001'"));

        await cliente.PatchAsync($"/api/catalogos/etapas/{descargue.GetProperty("id").GetGuid()}", Cuerpo(new { activo = true }));
        await cliente.PostAsync("/api/catalogos/etapas", Cuerpo(new { codigo = "entrega_final", nombre = "Entrega final", orden = 4 }));
        var conCuatro = await cliente.PostAsync("/api/viajes", Cuerpo(new { codigo_mtrack = "MTK-RUTA-2", tipo_unidad_id = unidad, placa = "ABC123", unidades = new[] { new { vin = "9FBHSR4E8PM000002", lugar_destino_id = lugar } } }));
        Assert.Equal(HttpStatusCode.Created, conCuatro.StatusCode);
        Assert.Equal(4, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = '9FBHSR4E8PM000002'"));
    }

    [Fact]
    public async Task Una_unidad_o_un_lugar_inactivo_no_se_ofrece_para_viajes_nuevos()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var unidad = await ValorAsync(token, "tipos-unidad", "nodriza");
        var origen = await ValorAsync(token, "puntos-custodia", "origen");
        await cliente.PatchAsync($"/api/catalogos/puntos-custodia/{origen.GetProperty("id").GetGuid()}", Cuerpo(new { activo = false }));
        await cliente.PatchAsync($"/api/catalogos/tipos-unidad/{unidad.GetProperty("id").GetGuid()}", Cuerpo(new { activo = false }));

        var inactivos = await cliente.PostAsync(
            "/api/viajes",
            Cuerpo(new { codigo_mtrack = "MTK-INACT", tipo_unidad_id = unidad.GetProperty("id").GetGuid(), placa = "ABC123", origen_id = origen.GetProperty("id").GetGuid() }));
        var inexistente = await cliente.PostAsync("/api/viajes", Cuerpo(new { codigo_mtrack = "MTK-INEX", tipo_unidad_id = Guid.NewGuid(), placa = "ABC123" }));
        var sinUnidad = await cliente.PostAsync("/api/viajes", Cuerpo(new { codigo_mtrack = "MTK-SIN", placa = "ABC123" }));

        Assert.Equal(HttpStatusCode.BadRequest, inactivos.StatusCode);
        var errores = (await LeerJsonAsync(inactivos)).GetProperty("details").GetProperty("fieldErrors");
        Assert.True(errores.TryGetProperty("tipo_unidad_id", out _));
        Assert.True(errores.TryGetProperty("origen_id", out _));
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinUnidad.StatusCode);
    }

    [Fact]
    public async Task El_filtro_proceso_de_los_informes_acepta_las_etapas_del_catalogo()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var usuario = await Fabrica.SembrarUsuarioAsync("insp4@ejemplo.com", "x", clsRol.INSPECTOR);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-FILTRO");
        await Sembrador.InformeAsync(etapas[1], usuario.Id, cerrada: false);

        var transporte = Items(await LeerJsonAsync(await cliente.GetAsync("/api/inspecciones?proceso=transporte")));
        var cargue = Items(await LeerJsonAsync(await cliente.GetAsync("/api/inspecciones?proceso=cargue")));
        var invalido = await cliente.GetAsync("/api/inspecciones?proceso=inventado");

        Assert.Equal(1, transporte.GetArrayLength());
        Assert.Equal("transporte", transporte[0].GetProperty("proceso").GetString());
        Assert.Equal(0, cargue.GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Contains("cargue", (await LeerJsonAsync(invalido)).GetProperty("details").GetProperty("fieldErrors").GetProperty("proceso")[0].GetString());
    }

    // ----------------------------------------------------------------------- vistas y dibujo de danos (cuadrantes, paneles y regiones)

    private const string MENSAJE_SOLO_LECTURA = "El dibujo se cambia con una versión nueva del archivo";

    private async Task<string?> ErrorDeCampoAsync(HttpResponseMessage r, string campo)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errores = (await LeerJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors");
        return errores.TryGetProperty(campo, out var mensajes) ? mensajes[0].GetString() : null;
    }

    private async Task<Guid> VistaIdAsync(string token, string codigo) =>
        (await ValorAsync(token, "vistas-foto", codigo)).GetProperty("id").GetGuid();

    [Fact]
    public async Task Los_paneles_de_danos_se_publican_con_su_tamano_de_referencia_y_version_y_cuadrante_vistas_ya_no_existe()
    {
        await Sembrador.EjecutarAsync("UPDATE dbo.tblPanelesDano SET AnchoRef = 1071, AltoRef = 491, Version = 6 WHERE Codigo = 'costado_izquierdo'");
        var token = await TokenAsync(clsRol.INSPECTOR);

        var paneles = (await CatalogoAsync(token, "paneles-dano")).GetProperty("items").EnumerateArray().ToList();
        var reemplazado = await Cliente(token).GetAsync("/api/catalogos/cuadrante-vistas");

        Assert.Equal(
            ["superior", "perfil_posterior_izquierdo", "perfil_posterior_derecho", "costado_izquierdo", "costado_derecho"],
            paneles.Select(p => p.GetProperty("codigo").GetString()).ToList());
        Assert.Equal(["id", "codigo", "nombre", "orden", "activo", "ancho_ref", "alto_ref", "version", "imagen_url"], paneles[0].EnumerateObject().Select(p => p.Name).ToList());
        var costado = paneles.Single(p => p.GetProperty("codigo").GetString() == "costado_izquierdo");
        Assert.Equal((1071, 491, 6), (costado.GetProperty("ancho_ref").GetInt32(), costado.GetProperty("alto_ref").GetInt32(), costado.GetProperty("version").GetInt32()));
        Assert.Equal(JsonValueKind.Null, paneles[0].GetProperty("ancho_ref").ValueKind);
        Assert.All(paneles, p => Assert.Equal(JsonValueKind.Null, p.GetProperty("imagen_url").ValueKind));
        Assert.Equal(HttpStatusCode.NotFound, reemplazado.StatusCode);
        Assert.StartsWith("Catálogo desconocido: cuadrante-vistas", (await LeerJsonAsync(reemplazado)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Los_cuadrantes_se_publican_con_lleva_lado_y_sus_zonas_con_nombre_completo_y_codigo_aiag_y_sin_zonas_provisionales()
    {
        // Se siembra antes del primer acceso: la API guarda en cache todos los catalogos la primera vez que se consultan.
        await Sembrador.CuadranteAsync(4, "Guardafango delantero", llevaLado: true);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblZonasDano SET NombreCompleto = N'Guardafango delantero ' + Lado, AiagArea = N'14/16' WHERE Numero = 4");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblZonasDano SET NombreCompleto = N'Frente', AiagArea = N'20' WHERE Numero = 50");
        var token = await TokenAsync(clsRol.COORDINADOR_CALIDAD);

        var items = (await CatalogoAsync(token, "cuadrantes")).GetProperty("items").EnumerateArray().ToList();

        Assert.Equal([4, 50, 51], items.Select(i => i.GetProperty("numero").GetInt32()).Order().ToList());
        Assert.Equal(["id", "codigo", "tipo_vehiculo", "numero", "nombre", "lleva_lado", "zonas", "orden", "activo"], items[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.All(items, i => Assert.Equal(JsonValueKind.Null, i.GetProperty("codigo").ValueKind));
        var cuatro = items.Single(i => i.GetProperty("numero").GetInt32() == 4);
        Assert.True(cuatro.GetProperty("lleva_lado").GetBoolean());
        var zonas = cuatro.GetProperty("zonas").EnumerateArray().ToList();
        Assert.Equal(["izquierdo", "derecho"], zonas.Select(z => z.GetProperty("lado").GetString()).ToList());
        Assert.Equal(["id", "lado", "nombre_completo", "aiag_area"], zonas[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("Guardafango delantero izquierdo", zonas[0].GetProperty("nombre_completo").GetString());
        Assert.All(zonas, z => Assert.Equal("14/16", z.GetProperty("aiag_area").GetString()));
        var frente = items.Single(i => i.GetProperty("numero").GetInt32() == 50);
        Assert.False(frente.GetProperty("lleva_lado").GetBoolean());
        var zonaUnica = Assert.Single(frente.GetProperty("zonas").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, zonaUnica.GetProperty("lado").ValueKind);
        Assert.Equal("20", zonaUnica.GetProperty("aiag_area").GetString());
    }

    [Fact]
    public async Task Las_regiones_se_publican_con_su_prioridad_y_su_geometria_y_la_version_cambia_al_resembrar()
    {
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblRegionesDano (Id, PanelId, ZonaId, CuadranteId, Geometria, Prioridad) SELECT NEWID(), p.Id, z.Id, z.CuadranteId, N'{\"puntos\":[[0.2,0.2],[0.6,0.2],[0.6,0.5]]}', 2 FROM dbo.tblPanelesDano p JOIN dbo.tblZonasDano z ON z.Numero = 50 WHERE p.Codigo = 'frontal' OR p.Codigo = 'superior'");
        var token = await TokenAsync();
        var regiones = await CatalogoAsync(token, "cuadrante-regiones");

        var region = regiones.GetProperty("items").EnumerateArray().First();

        Assert.Equal(["id", "panel", "cuadrante_id", "numero", "nombre", "lado", "prioridad", "geometria"], region.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("superior", region.GetProperty("panel").GetString());
        Assert.Equal((50, "Frente", 2), (region.GetProperty("numero").GetInt32(), region.GetProperty("nombre").GetString(), region.GetProperty("prioridad").GetInt32()));
        Assert.Equal(JsonValueKind.Null, region.GetProperty("lado").ValueKind);
        Assert.Equal(JsonValueKind.Object, region.GetProperty("geometria").ValueKind);
        Assert.Equal(3, region.GetProperty("geometria").GetProperty("puntos").GetArrayLength());

        // Una resiembra cambia la geometria; al renovarse el cache el catalogo cambia de version y la app vuelve a descargarlo.
        await Sembrador.EjecutarAsync("UPDATE dbo.tblRegionesDano SET Geometria = N'{\"puntos\":[[0,0],[1,0],[1,1]]}'");
        var archivo = await SubirImagenAsync(token);
        Assert.Equal(HttpStatusCode.NoContent, (await Cliente(token).PutAsync("/api/catalogos/paneles-dano/superior/imagen", Cuerpo(new { archivo }))).StatusCode);
        var despues = await CatalogoAsync(token, "cuadrante-regiones");
        Assert.NotEqual(regiones.GetProperty("version").GetString(), despues.GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("cuadrantes", """{"numero":99,"nombre":"Nuevo"}""")]
    [InlineData("paneles-dano", """{"codigo":"nuevo","nombre":"Nuevo"}""")]
    [InlineData("cuadrante-regiones", """{"panel":"superior","geometria":{"puntos":[[0,0],[1,0],[1,1]]}}""")]
    public async Task El_dibujo_es_de_solo_lectura_crear_editar_y_borrar_responden_409_con_el_mismo_mensaje(string catalogo, string cuerpo)
    {
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblRegionesDano (Id, PanelId, ZonaId, CuadranteId, Geometria) SELECT NEWID(), p.Id, z.Id, z.CuadranteId, N'{\"puntos\":[[0.2,0.2],[0.6,0.2],[0.6,0.5]]}' FROM dbo.tblPanelesDano p JOIN dbo.tblZonasDano z ON z.Numero = 50 WHERE p.Codigo = 'superior'");
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var existente = (await CatalogoAsync(token, catalogo)).GetProperty("items").EnumerateArray().First().GetProperty("id").GetGuid();

        var crear = await cliente.PostAsync($"/api/catalogos/{catalogo}", new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json"));
        var editar = await cliente.PatchAsync($"/api/catalogos/{catalogo}/{existente}", Cuerpo(new { nombre = "Otro nombre" }));
        var borrar = await cliente.DeleteAsync($"/api/catalogos/{catalogo}/{existente}");

        foreach (var respuesta in new[] { crear, editar, borrar })
        {
            Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
            Assert.Equal(MENSAJE_SOLO_LECTURA, (await LeerJsonAsync(respuesta)).GetProperty("message").GetString());
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblCuadrantes WHERE Numero = 99 OR Nombre = N'Otro nombre'"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblPanelesDano WHERE Codigo = 'nuevo' OR Nombre = N'Otro nombre'"));
    }

    [Fact]
    public async Task Quien_no_administra_los_catalogos_sigue_recibiendo_403_antes_que_el_409_del_dibujo()
    {
        var inspector = await TokenAsync(clsRol.INSPECTOR);

        var crear = await Cliente(inspector).PostAsync("/api/catalogos/cuadrantes", Cuerpo(new { numero = 99, nombre = "Nuevo" }));

        Assert.Equal(HttpStatusCode.Forbidden, crear.StatusCode);
    }

    [Fact]
    public async Task Una_vista_con_poligonos_del_modelo_anterior_no_se_borra_ni_pasa_a_ser_adicional()
    {
        var cuadranteId = await Sembrador.CuadranteAsync(1, "Cuadrante 1");
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var frontal = await VistaIdAsync(token, "frontal");
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblCuadranteVistas (Id, CuadranteId, VistaFotoId, Geometria) VALUES (NEWID(), @c, @v, N'{\"puntos\":[[0,0],[1,0],[1,1]]}')",
            ("@c", cuadranteId), ("@v", frontal));

        var borrar = await cliente.DeleteAsync($"/api/catalogos/vistas-foto/{frontal}");
        var adicional = await cliente.PatchAsync($"/api/catalogos/vistas-foto/{frontal}", Cuerpo(new { obligatoria = false }));

        Assert.Equal(HttpStatusCode.Conflict, borrar.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, adicional.StatusCode);
    }

    private async Task<string> SubirImagenAsync(string token)
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var subida = await Cliente(token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, subida.StatusCode);
        return (await LeerJsonAsync(subida)).GetProperty("storage_path").GetString()!;
    }

    [Fact]
    public async Task La_imagen_de_un_panel_la_cambia_un_administrador_y_se_sirve_publica_con_cache_larga()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var anonimo = Cliente();
        var sinImagen = await anonimo.GetAsync("/api/catalogos/paneles-dano/superior/imagen");
        var archivo = await SubirImagenAsync(token);

        var cambio = await cliente.PutAsync("/api/catalogos/paneles-dano/superior/imagen", Cuerpo(new { archivo }));
        var imagen = await anonimo.GetAsync("/api/catalogos/paneles-dano/superior/imagen");

        Assert.Equal(HttpStatusCode.NotFound, sinImagen.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, cambio.StatusCode);
        var panel = await ValorAsync(token, "paneles-dano", "superior");
        Assert.Equal($"/api/catalogos/paneles-dano/superior/imagen?v={Path.GetFileNameWithoutExtension(archivo)}", panel.GetProperty("imagen_url").GetString());
        Assert.Equal(HttpStatusCode.OK, imagen.StatusCode);
        Assert.Equal("image/png", imagen.Content.Headers.ContentType?.MediaType);
        Assert.Contains("max-age=31536000", imagen.Headers.CacheControl?.ToString());
        Assert.Contains("public", imagen.Headers.CacheControl?.ToString());
        Assert.Equal(await (await anonimo.GetAsync(UrlFirmada(archivo))).Content.ReadAsByteArrayAsync(), await imagen.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Cambiar_la_imagen_de_un_panel_es_solo_de_administradores_y_exige_un_archivo_subido()
    {
        var token = await TokenAsync();
        var coordinador = await TokenAsync(clsRol.COORDINADOR_CALIDAD);
        var inspector = await TokenAsync(clsRol.INSPECTOR);
        var archivo = await SubirImagenAsync(token);

        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(coordinador).PutAsync("/api/catalogos/paneles-dano/superior/imagen", Cuerpo(new { archivo }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(inspector).PutAsync("/api/catalogos/paneles-dano/superior/imagen", Cuerpo(new { archivo }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PutAsync("/api/catalogos/paneles-dano/superior/imagen", Cuerpo(new { archivo }))).StatusCode);
        var sinArchivo = await Cliente(token).PutAsync("/api/catalogos/paneles-dano/superior/imagen", Cuerpo(new { }));
        var inexistente = await Cliente(token).PutAsync("/api/catalogos/paneles-dano/superior/imagen", Cuerpo(new { archivo = "3f2b6c1e-0000-4000-8000-123456789abc.png" }));
        var panelInexistente = await Cliente(token).PutAsync("/api/catalogos/paneles-dano/no_existe/imagen", Cuerpo(new { archivo }));

        Assert.NotNull(await ErrorDeCampoAsync(sinArchivo, "archivo"));
        Assert.Contains("súbalo primero", await ErrorDeCampoAsync(inexistente, "archivo"));
        Assert.Equal(HttpStatusCode.NotFound, panelInexistente.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ValorAsync(token, "paneles-dano", "superior")).GetProperty("imagen_url").ValueKind);
    }

    [Fact]
    public async Task Las_vistas_se_administran_con_obligatoria_y_tipo_de_vehiculo_opcional()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);

        var creada = await cliente.PostAsync("/api/catalogos/vistas-foto", Cuerpo(new { codigo = "motor", nombre = "Motor", obligatoria = false, tipo_vehiculo = "camioneta" }));

        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var vista = await LeerJsonAsync(creada);
        Assert.False(vista.GetProperty("obligatoria").GetBoolean());
        Assert.Equal("camioneta", vista.GetProperty("tipo_vehiculo").GetString());
        var porDefecto = await LeerJsonAsync(await cliente.PostAsync("/api/catalogos/vistas-foto", Cuerpo(new { codigo = "tablero", nombre = "Tablero" })));
        Assert.True(porDefecto.GetProperty("obligatoria").GetBoolean());
        Assert.Equal(JsonValueKind.Null, porDefecto.GetProperty("tipo_vehiculo").ValueKind);

        await cliente.PatchAsync($"/api/catalogos/vistas-foto/{vista.GetProperty("id").GetGuid()}", new StringContent("""{"tipo_vehiculo":null}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(JsonValueKind.Null, (await ValorAsync(token, "vistas-foto", "motor")).GetProperty("tipo_vehiculo").ValueKind);
    }

    [Fact]
    public async Task Cada_cambio_deja_rastro_en_la_auditoria()
    {
        var token = await TokenAsync();
        var cliente = Cliente(token);
        var creada = await LeerJsonAsync(await cliente.PostAsync("/api/catalogos/tipos-unidad", Cuerpo(new { codigo = "cama_baja", nombre = "Cama baja" })));
        var id = creada.GetProperty("id").GetGuid();
        await cliente.PatchAsync($"/api/catalogos/tipos-unidad/{id}", Cuerpo(new { nombre = "Cama baja 2" }));
        await cliente.DeleteAsync($"/api/catalogos/tipos-unidad/{id}");

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'catalogo.tipos-unidad.crear'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'catalogo.tipos-unidad.actualizar'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'catalogo.tipos-unidad.eliminar'"));
    }
}
