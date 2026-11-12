using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Los extras del vehiculo fuera del dibujo estandar (barras de techo, volco, platon, otro...): se reportan con su tipo, sus fotos selladas y su observacion sin tocar el dibujo (ESC-01 y ESC-02), el cierre
/// exige una foto por extra (ESC-03), el catalogo de tipos lo administra Calidad (ESC-04), inactivar un tipo no borra los extras ya reportados (ESC-05) y los extras acompanan el inventario y los informes sin sumar
/// al mapa de calor (ESC-06).
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed partial class ExtrasApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-11-03T06:00:00-05:00";
    private const string VIN = "9FBBR8A04PM123456";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>El VIN con su ruta (la llegada del cargue cerrada) y su inspeccion de salida de Transporte en borrador (no exige las vistas, solo el GPS del registro).</summary>
    private sealed record Escenario(Guid Viaje, Guid[] Etapas, Sesion Inspector, Sesion Coordinador, Guid Inspeccion);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private async Task<Escenario> EscenarioAsync()
    {
        var inspector = await SesionAsync("esleider@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("calidad@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync(VIN, inspectorId: inspector.UsuarioId);
        await Sembrador.InformeAsync(etapas[0], inspector.UsuarioId, cerrada: true, momento: "llegada");
        var r = await Como(inspector).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = etapas[1], momento = "salida", fecha = FECHA, lat = 6.2, lng = -75.5 }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), etapas, inspector, coordinador, (await JsonAsync(r)).GetProperty("id").GetGuid());
    }

    private Task<HttpResponseMessage> AgregarExtraAsync(Sesion quien, Guid inspeccion, string? tipo, string? texto = null, string? observacion = null) =>
        Como(quien).PostAsync($"/api/inspecciones/{inspeccion}/extras", Cuerpo(new { tipo, texto, observacion }));

    private async Task<Guid> ExtraAsync(Sesion quien, Guid inspeccion, string tipo, string? texto = null, string? observacion = null)
    {
        var r = await AgregarExtraAsync(quien, inspeccion, tipo, texto, observacion);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonAsync(r)).GetProperty("id").GetGuid();
    }

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var bytes = FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var r = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await JsonAsync(r);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), cuerpo.GetProperty("hash_sha256").GetString());
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    /// <summary>Una foto del extra: sellada con fecha, ubicacion, precision, dispositivo y hash; el servidor agrega su propio sello.</summary>
    private async Task<HttpResponseMessage> FotoDeExtraAsync(Sesion quien, Guid inspeccion, Guid extra)
    {
        var (ruta, hash) = await SubirAsync(quien);
        return await Como(quien).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccion}/evidencias",
            new { extra_id = extra, storage_path = ruta, sello_fecha = FECHA, sello_lat = 6.2, sello_lng = -75.5, precision = 4.5, dispositivo_id = "tablet-1", hash_sha256 = hash });
    }

    private Task<HttpResponseMessage> CerrarAsync(Sesion quien, Guid inspeccion) => Como(quien).PostAsync($"/api/inspecciones/{inspeccion}/cerrar", null);

    private async Task<JsonElement> InformeAsync(Sesion quien, Guid inspeccion) => await JsonAsync(await Como(quien).GetAsync($"/api/inspecciones/{inspeccion}"));

    private static List<string?> Detalles(JsonElement cuerpo) => cuerpo.GetProperty("details").EnumerateArray().Select(d => d.GetString()).ToList();

    private async Task<byte[]> PdfAsync(Sesion quien, string ruta)
    {
        var r = await Como(quien).GetAsync(ruta);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsByteArrayAsync();
    }

    /// <summary>El texto del PDF en una sola linea; los caracteres fuera de ASCII quedan como escapes octales (por eso se busca «Plat\363n»).</summary>
    private static string Plano(byte[] pdf) =>
        string.Join(' ', TextoDeLinea().Matches(Encoding.Latin1.GetString(pdf)).Select(m => m.Groups[1].Value.Replace(@"\(", "(").Replace(@"\)", ")").Replace(@"\\", @"\")));

    [GeneratedRegex(@"\(((?:\\.|[^\\)])*)\) Tj")]
    private static partial Regex TextoDeLinea();

    // ------------------------------------------------------------------------------------------------ ESC-01 y ESC-02: reportar extras

    [Fact]
    public async Task El_inspector_reporta_un_extra_con_dos_fotos_selladas_y_su_observacion_y_el_dibujo_queda_igual()
    {
        var e = await EscenarioAsync();

        var creado = await AgregarExtraAsync(e.Inspector, e.Inspeccion, "platon", observacion: "  rayón en la compuerta trasera  ");
        var dto = await JsonAsync(creado);
        var extra = dto.GetProperty("id").GetGuid();
        var foto1 = await FotoDeExtraAsync(e.Inspector, e.Inspeccion, extra);
        var foto2 = await FotoDeExtraAsync(e.Inspector, e.Inspeccion, extra);

        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        Assert.Equal(["id", "inspeccion_id", "tipo", "tipo_nombre", "texto", "nombre", "observacion", "created_at", "updated_at", "fotos", "dano"], dto.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("platon", dto.GetProperty("tipo").GetString());
        Assert.Equal("Platón", dto.GetProperty("nombre").GetString());
        Assert.Equal("rayón en la compuerta trasera", dto.GetProperty("observacion").GetString());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("texto").ValueKind);
        Assert.Equal(HttpStatusCode.Created, foto1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, foto2.StatusCode);
        Assert.Equal(extra, (await JsonAsync(foto1)).GetProperty("extra_id").GetGuid());

        // El informe muestra el extra con sus 2 fotos selladas (fecha, ubicacion, precision, hash y sello del servidor) y su observacion.
        var informe = await InformeAsync(e.Coordinador, e.Inspeccion);
        var delInforme = Assert.Single(informe.GetProperty("extras").EnumerateArray());
        Assert.Equal(extra, delInforme.GetProperty("id").GetGuid());
        Assert.Equal("rayón en la compuerta trasera", delInforme.GetProperty("observacion").GetString());
        var fotos = delInforme.GetProperty("fotos").EnumerateArray().ToList();
        Assert.Equal(2, fotos.Count);
        Assert.All(fotos, f =>
        {
            Assert.Equal(extra, f.GetProperty("extra_id").GetGuid());
            Assert.Equal(6.2, f.GetProperty("sello_lat").GetDouble());
            Assert.Equal(-75.5, f.GetProperty("sello_lng").GetDouble());
            Assert.Equal(4.5, f.GetProperty("precision").GetDouble());
            Assert.Equal(64, f.GetProperty("hash_sha256").GetString()!.Length);
            Assert.False(f.GetProperty("sin_ubicacion").GetBoolean());
            Assert.Equal(JsonValueKind.String, f.GetProperty("sello_servidor_at").ValueKind);
            Assert.Equal(JsonValueKind.Null, f.GetProperty("vista_id").ValueKind);
            Assert.Equal(JsonValueKind.Null, f.GetProperty("hallazgo_id").ValueKind);
        });
        // El dibujo estandar queda igual: ni hallazgos ni zonas marcadas por el extra.
        Assert.Equal(0, informe.GetProperty("hallazgos").GetArrayLength());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE ExtraId IS NOT NULL AND VistaId IS NULL AND HallazgoId IS NULL AND EsConstancia = 0 AND EsSello = 0"));
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion IN ('extra.crear', 'evidencia.crear') AND UsuarioId = @u", ("@u", e.Inspector.UsuarioId)));

        // El PDF del informe lo presenta en su propia seccion con sus dos fotos.
        var pdf = await PdfAsync(e.Coordinador, $"/api/inspecciones/{e.Inspeccion}/pdf");
        var texto = Plano(pdf);
        Assert.Contains(@"Extras del veh\355culo", texto);
        Assert.Contains(@"Plat\363n", texto);
        Assert.Contains(@"Observaci\363n: ray\363n en la compuerta trasera", texto);
        // Cada foto lleva su pie con el sello; las dos del extra salen en su galeria (las imagenes de prueba no son PNG reales y el PDF solo indica que no se pudieron incrustar).
        Assert.Equal(2, Regex.Matches(texto, Regex.Escape(@"Foto del extra: Plat\363n")).Count);
    }

    [Fact]
    public async Task Un_elemento_fuera_del_catalogo_se_reporta_como_otro_con_su_texto_y_su_foto()
    {
        var e = await EscenarioAsync();

        var extra = await ExtraAsync(e.Inspector, e.Inspeccion, "otro", texto: "portabicicletas");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, extra)).StatusCode);

        var delInforme = (await InformeAsync(e.Coordinador, e.Inspeccion)).GetProperty("extras")[0];
        Assert.Equal("Otro: portabicicletas", delInforme.GetProperty("nombre").GetString());
        Assert.Equal("portabicicletas", delInforme.GetProperty("texto").GetString());
        Assert.Equal("Otro", delInforme.GetProperty("tipo_nombre").GetString());
        Assert.Single(delInforme.GetProperty("fotos").EnumerateArray());
    }

    [Fact]
    public async Task Los_datos_del_extra_se_validan_el_texto_solo_con_otro_el_tipo_existente_y_activo_y_la_observacion_de_hasta_500()
    {
        var e = await EscenarioAsync();

        var otroSinTexto = await AgregarExtraAsync(e.Inspector, e.Inspeccion, "otro");
        var textoConOtroTipo = await AgregarExtraAsync(e.Inspector, e.Inspeccion, "volco", texto: "algo");
        var sinTipo = await AgregarExtraAsync(e.Inspector, e.Inspeccion, null);
        var inexistente = await AgregarExtraAsync(e.Inspector, e.Inspeccion, "no_existe");
        var observacionLarga = await AgregarExtraAsync(e.Inspector, e.Inspeccion, "volco", observacion: new string('x', 501));
        var observacionExacta = await AgregarExtraAsync(e.Inspector, e.Inspeccion, "volco", observacion: new string('x', 500));

        Assert.Equal(HttpStatusCode.BadRequest, otroSinTexto.StatusCode);
        Assert.True((await JsonAsync(otroSinTexto)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("texto", out _));
        Assert.Equal(HttpStatusCode.BadRequest, textoConOtroTipo.StatusCode);
        Assert.Equal("El texto libre solo se admite cuando el tipo es «otro»", (await JsonAsync(textoConOtroTipo)).GetProperty("details").GetProperty("fieldErrors").GetProperty("texto")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinTipo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.Contains("Tipo de extra inválido", (await JsonAsync(inexistente)).GetProperty("details").GetProperty("fieldErrors").GetProperty("tipo")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, observacionLarga.StatusCode);
        Assert.Equal(HttpStatusCode.Created, observacionExacta.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras"));
    }

    [Fact]
    public async Task El_extra_se_cambia_y_se_quita_en_un_borrador_pero_con_fotos_no_se_quita_y_una_foto_es_de_una_sola_cosa()
    {
        var e = await EscenarioAsync();
        var sinFotos = await ExtraAsync(e.Inspector, e.Inspeccion, "volco");
        var conFotos = await ExtraAsync(e.Inspector, e.Inspeccion, "platon", observacion: "antes");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, conFotos)).StatusCode);

        var cambiado = await Como(e.Inspector).PutAsync($"/api/inspecciones/{e.Inspeccion}/extras/{conFotos}", Cuerpo(new { tipo = "otro", texto = "escalera", observacion = "despues" }));
        var quitadoConFotos = await Como(e.Inspector).DeleteAsync($"/api/inspecciones/{e.Inspeccion}/extras/{conFotos}");
        var quitadoSinFotos = await Como(e.Inspector).DeleteAsync($"/api/inspecciones/{e.Inspeccion}/extras/{sinFotos}");
        var ajeno = await Como(e.Inspector).DeleteAsync($"/api/inspecciones/{e.Inspeccion}/extras/{Guid.NewGuid()}");
        var (ruta, hash) = await SubirAsync(e.Inspector);
        var vista = (Guid)(await Sembrador.EscalarAsync("SELECT TOP 1 Id FROM dbo.tblVistasFoto WHERE Obligatoria = 1"))!;
        var fotoDeDosCosas = await Como(e.Inspector).PostAsJsonAsync(
            $"/api/inspecciones/{e.Inspeccion}/evidencias", new { extra_id = conFotos, vista_id = vista, storage_path = ruta, sello_fecha = FECHA, sello_lat = 6.2, sello_lng = -75.5, hash_sha256 = hash });
        var fotoDeExtraAjeno = await Como(e.Inspector).PostAsJsonAsync(
            $"/api/inspecciones/{e.Inspeccion}/evidencias", new { extra_id = Guid.NewGuid(), storage_path = ruta, sello_fecha = FECHA, sello_lat = 6.2, sello_lng = -75.5, hash_sha256 = hash });

        Assert.Equal(HttpStatusCode.OK, cambiado.StatusCode);
        var dto = await JsonAsync(cambiado);
        Assert.Equal("Otro: escalera", dto.GetProperty("nombre").GetString());
        Assert.Equal("despues", dto.GetProperty("observacion").GetString());
        Assert.Single(dto.GetProperty("fotos").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, quitadoConFotos.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, quitadoSinFotos.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fotoDeDosCosas.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fotoDeExtraAjeno.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'extra.quitar'"));
    }

    // ------------------------------------------------------------------------------------------------ ESC-03: el cierre

    [Fact]
    public async Task Un_extra_sin_foto_no_deja_cerrar_el_informe_y_con_sus_fotos_el_cierre_pasa()
    {
        var e = await EscenarioAsync();
        var barras = await ExtraAsync(e.Inspector, e.Inspeccion, "barras_techo");
        var otro = await ExtraAsync(e.Inspector, e.Inspeccion, "otro", texto: "portabicicletas");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, otro)).StatusCode);

        var rechazado = await CerrarAsync(e.Inspector, e.Inspeccion);
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, barras)).StatusCode);
        var cerrado = await CerrarAsync(e.Inspector, e.Inspeccion);

        Assert.Equal(HttpStatusCode.BadRequest, rechazado.StatusCode);
        Assert.Equal(["El extra 'Barras de techo' no tiene foto"], Detalles(await JsonAsync(rechazado)));
        Assert.Equal(HttpStatusCode.OK, cerrado.StatusCode);
        Assert.Equal(2, (await JsonAsync(cerrado)).GetProperty("resumen").GetProperty("evidencias").GetInt32());
        // Cerrada la inspeccion, el extra queda inmutable: no se agrega, cambia ni quita.
        Assert.Equal(HttpStatusCode.Conflict, (await AgregarExtraAsync(e.Inspector, e.Inspeccion, "volco")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Como(e.Inspector).PutAsync($"/api/inspecciones/{e.Inspeccion}/extras/{barras}", Cuerpo(new { tipo = "volco" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Como(e.Inspector).DeleteAsync($"/api/inspecciones/{e.Inspeccion}/extras/{barras}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, barras)).StatusCode);
    }

    [Fact]
    public async Task Un_extra_con_otro_sin_foto_se_nombra_con_su_texto_en_el_rechazo()
    {
        var e = await EscenarioAsync();
        await ExtraAsync(e.Inspector, e.Inspeccion, "otro", texto: "portabicicletas");

        var rechazado = await CerrarAsync(e.Inspector, e.Inspeccion);

        Assert.Equal(["El extra 'Otro: portabicicletas' no tiene foto"], Detalles(await JsonAsync(rechazado)));
    }

    // ------------------------------------------------------------------------------------------------ ESC-04 y ESC-05: el catalogo

    [Fact]
    public async Task Un_inspector_no_puede_cambiar_el_catalogo_de_tipos_de_extra_y_Calidad_si()
    {
        var e = await EscenarioAsync();
        var (inspector, coordinador) = (e.Inspector, e.Coordinador);
        var lectura = await Como(inspector).GetAsync("/api/catalogos/tipos-extra");

        var delInspector = await Como(inspector).PostAsync("/api/catalogos/tipos-extra", Cuerpo(new { codigo = "spoiler", nombre = "Spoiler", orden = 5 }));
        var deCalidad = await Como(coordinador).PostAsync("/api/catalogos/tipos-extra", Cuerpo(new { codigo = "spoiler", nombre = "Spoiler", orden = 5 }));

        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
        var items = (await JsonAsync(lectura)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["barras_techo", "volco", "platon", "otro"], items.Select(i => i.GetProperty("codigo").GetString()).ToList());
        Assert.Equal(HttpStatusCode.Forbidden, delInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Created, deCalidad.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposExtra WHERE Codigo = 'spoiler'"));
        // El nuevo tipo ya se ofrece para extras nuevos y aparece en el conjunto completo de catalogos.
        Assert.Equal(HttpStatusCode.Created, (await AgregarExtraAsync(e.Inspector, e.Inspeccion, "spoiler")).StatusCode);
        Assert.True((await JsonAsync(await Como(coordinador).GetAsync("/api/catalogos"))).GetProperty("catalogos").TryGetProperty("tipos-extra", out _));
    }

    [Fact]
    public async Task Inactivar_un_tipo_no_borra_los_extras_ya_reportados_y_un_tipo_en_uso_no_se_borra()
    {
        var e = await EscenarioAsync();
        var coordinador = e.Coordinador;
        var volco = (await JsonAsync(await Como(coordinador).GetAsync("/api/catalogos/tipos-extra"))).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("codigo").GetString() == "volco").GetProperty("id").GetGuid();
        var extra = await ExtraAsync(e.Inspector, e.Inspeccion, "volco", observacion: "con golpe");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, extra)).StatusCode);

        var borrado = await Como(coordinador).DeleteAsync($"/api/catalogos/tipos-extra/{volco}");
        var inactivado = await Como(coordinador).PatchAsync($"/api/catalogos/tipos-extra/{volco}", Cuerpo(new { activo = false }));

        Assert.Equal(HttpStatusCode.Conflict, borrado.StatusCode);
        Assert.Equal("El valor está en uso y no se puede borrar: solo puede inactivarlo", (await JsonAsync(borrado)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, inactivado.StatusCode);
        // El extra ya reportado conserva «Volco» (tambien en el informe) y se puede seguir editando sin cambiar su tipo; el tipo ya no se ofrece para extras nuevos.
        var delInforme = (await InformeAsync(coordinador, e.Inspeccion)).GetProperty("extras")[0];
        Assert.Equal("volco", delInforme.GetProperty("tipo").GetString());
        Assert.Equal("Volco", delInforme.GetProperty("tipo_nombre").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Inspector).PutAsync($"/api/inspecciones/{e.Inspeccion}/extras/{extra}", Cuerpo(new { tipo = "volco", observacion = "sin cambios de tipo" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AgregarExtraAsync(e.Inspector, e.Inspeccion, "volco")).StatusCode);
        // Cambiarlo a otro tipo activo se admite; a uno inactivo, no.
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Inspector).PutAsync($"/api/inspecciones/{e.Inspeccion}/extras/{extra}", Cuerpo(new { tipo = "platon" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(e.Inspector).PutAsync($"/api/inspecciones/{e.Inspeccion}/extras/{extra}", Cuerpo(new { tipo = "volco" }))).StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras WHERE Tipo = 'platon'"));
    }

    // ------------------------------------------------------------------------------------------------ ESC-06: inventario, exportaciones y mapa de calor

    [Fact]
    public async Task Los_extras_acompanan_el_inventario_y_los_pdf_y_no_suman_al_mapa_de_calor()
    {
        var e = await EscenarioAsync();
        var coordinador = e.Coordinador;
        // 3 hallazgos en la zona 21 del lado izquierdo (en inspecciones cerradas del mismo VIN) y 2 extras con sus fotos en la inspeccion de Transporte.
        await Sembrador.CuadranteAsync(21, "Puerta delantera");
        var zona = await Sembrador.ZonaAsync(21, "izquierdo");
        foreach (var (etapa, momento) in new[] { (e.Etapas[2], "salida"), (e.Etapas[2], "llegada"), (e.Etapas[1], "llegada") })
        {
            await Sembrador.InformeCerradoConHallazgoAsync(etapa, e.Inspector.UsuarioId, zona, momento: momento, nivel: "v2");
        }

        var barras = await ExtraAsync(e.Inspector, e.Inspeccion, "barras_techo", observacion: "barras originales");
        var otro = await ExtraAsync(e.Inspector, e.Inspeccion, "otro", texto: "portabicicletas");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, barras)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, otro)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(e.Inspector, e.Inspeccion)).StatusCode);
        var hoy = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd");
        var desde = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).AddDays(-10).ToString("yyyy-MM-dd");

        var inventario = await JsonAsync(await Como(coordinador).GetAsync($"/api/vehiculos/{VIN}/inventario"));
        var patrones = await JsonAsync(await Como(coordinador).GetAsync($"/api/reportes/patrones?desde={desde}&hasta={hoy}"));
        var pdf = Plano(await PdfAsync(coordinador, $"/api/vehiculos/{VIN}/inventario/pdf"));

        var inspecciones = inventario.GetProperty("etapas").EnumerateArray().SelectMany(x => x.GetProperty("inspecciones").EnumerateArray()).ToList();
        Assert.Equal(3, inspecciones.Sum(i => i.GetProperty("hallazgos").GetArrayLength()));
        var extras = inspecciones.SelectMany(i => i.GetProperty("extras").EnumerateArray()).ToList();
        Assert.Equal(["Barras de techo", "Otro: portabicicletas"], extras.Select(x => x.GetProperty("nombre").GetString()).ToList());
        Assert.All(extras, x => Assert.Single(x.GetProperty("fotos").EnumerateArray()));
        // El mapa de calor cuenta solo los 3 hallazgos de la zona 21; los extras no suman.
        Assert.Equal(3, patrones.GetProperty("total").GetInt32());
        Assert.Contains(@"Extra: Barras de techo", pdf);
        Assert.Contains(@"Extra: Otro: portabicicletas", pdf);
        Assert.Contains(@"Observaci\363n: barras originales", pdf);
        // La exportacion de inspecciones gana la columna «Extras» con la cantidad por inspeccion.
        var excel = await Como(coordinador).GetAsync("/api/exportaciones/inspecciones.xlsx");
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        var (titulos, filas) = LeerHoja(await excel.Content.ReadAsByteArrayAsync());
        var columna = titulos.IndexOf("Extras");
        Assert.True(columna >= 0, "Falta la columna Extras");
        Assert.Equal(titulos.Count - 1, columna);
        Assert.Equal(2, filas.Sum(f => f.TryGetValue(columna, out var cantidad) && int.TryParse(cantidad, out var n) ? n : 0));
    }

    /// <summary>Los titulos y las filas (por columna) de la primera hoja de un xlsx; las celdas vacias no se guardan.</summary>
    private static (List<string> Titulos, List<Dictionary<int, string>> Filas) LeerHoja(byte[] xlsx)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(xlsx), System.IO.Compression.ZipArchiveMode.Read);
        using var flujo = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var filas = XDocument.Load(flujo).Descendants(ns + "row")
            .Select(fila => fila.Elements(ns + "c").ToDictionary(c => ColumnaDe((string)c.Attribute("r")!), c => (string?)c.Element(ns + "is")?.Element(ns + "t") ?? (string?)c.Element(ns + "v") ?? string.Empty))
            .ToList();
        return ([.. filas[0].OrderBy(c => c.Key).Select(c => c.Value)], filas.Skip(1).ToList());
    }

    private static int ColumnaDe(string referencia) =>
        referencia.TakeWhile(char.IsLetter).Aggregate(0, (acumulado, c) => (acumulado * 26) + (c - 'A' + 1)) - 1;

    // ------------------------------------------------------------------------------ S7-HU-04: el dano en un extra cuenta

    private Task<HttpResponseMessage> AgregarExtraDanadoAsync(Sesion quien, Guid inspeccion, string tipo, object? cuerpoDano) =>
        Como(quien).PostAsync(
            $"/api/inspecciones/{inspeccion}/extras",
            Cuerpo(cuerpoDano is null ? new { tipo } : new { tipo, danado = true, clase_dano = "golpe", tamano_dano = "5_10cm", nivel_severidad = "v3" }));

    private async Task<(Guid Extra, Guid Hallazgo)> ExtraDanadoAsync(Sesion quien, Guid inspeccion, string tipo = "platon")
    {
        var r = await AgregarExtraDanadoAsync(quien, inspeccion, tipo, new { });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var dto = await JsonAsync(r);
        return (dto.GetProperty("id").GetGuid(), dto.GetProperty("dano").GetProperty("hallazgo_id").GetGuid());
    }

    [Fact]
    public async Task Un_extra_danado_se_guarda_con_clase_tamano_y_nivel_y_aparece_en_la_lista_de_danos_sin_zona()
    {
        var e = await EscenarioAsync();

        var r = await AgregarExtraDanadoAsync(e.Inspector, e.Inspeccion, "platon", new { });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var extra = await JsonAsync(r);
        var dano = extra.GetProperty("dano");
        Assert.Equal("golpe", dano.GetProperty("clase_dano").GetString());
        Assert.Equal("5_10cm", dano.GetProperty("tamano_dano").GetString());
        Assert.Equal("v3", dano.GetProperty("nivel_severidad").GetString());

        // El dano es un hallazgo de siempre, sin zona del dibujo, del inspector y ligado a su extra.
        var hallazgo = dano.GetProperty("hallazgo_id").GetGuid();
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Id = @h AND ExtraId = @e AND ZonaId IS NULL AND Origen = 'inspector' AND NivelSeveridad = 'v3'", ("@h", hallazgo), ("@e", extra.GetProperty("id").GetGuid())));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'hallazgo.crear' AND EntidadId = @h", ("@h", hallazgo)));

        // ESC-01: aparece en la lista de danos; la ausencia de zona viaja como nulos y el nombre del extra en la zona de texto.
        var lista = await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/hallazgos?inspeccion_id={e.Inspeccion}"));
        var fila = Assert.Single(lista.GetProperty("items").EnumerateArray());
        Assert.Equal(hallazgo, fila.GetProperty("id").GetGuid());
        Assert.Equal(extra.GetProperty("id").GetGuid(), fila.GetProperty("extra_id").GetGuid());
        Assert.Equal("Platón", fila.GetProperty("zona").GetString());
        Assert.Equal(JsonValueKind.Null, fila.GetProperty("cuadrante_numero").ValueKind);
        Assert.Equal(JsonValueKind.Null, fila.GetProperty("lado").ValueKind);
        Assert.False(fila.GetProperty("sin_calificar").GetBoolean());
        Assert.Equal("v3", fila.GetProperty("nivel_severidad").GetString());
    }

    [Fact]
    public async Task Marcar_un_extra_como_danado_exige_clase_tamano_y_nivel_y_sin_marcarlo_no_se_admiten()
    {
        var e = await EscenarioAsync();

        var sinNada = await Como(e.Inspector).PostAsync($"/api/inspecciones/{e.Inspeccion}/extras", Cuerpo(new { tipo = "platon", danado = true }));
        var sinNivel = await Como(e.Inspector).PostAsync($"/api/inspecciones/{e.Inspeccion}/extras", Cuerpo(new { tipo = "platon", danado = true, clase_dano = "golpe", tamano_dano = "5_10cm" }));
        var invalidos = await Como(e.Inspector).PostAsync($"/api/inspecciones/{e.Inspeccion}/extras", Cuerpo(new { tipo = "platon", danado = true, clase_dano = "inventada", tamano_dano = "gigante", nivel_severidad = "v9" }));
        var sinMarcar = await Como(e.Inspector).PostAsync($"/api/inspecciones/{e.Inspeccion}/extras", Cuerpo(new { tipo = "platon", clase_dano = "golpe", nivel_severidad = "v3" }));
        var noDanado = await Como(e.Inspector).PostAsync($"/api/inspecciones/{e.Inspeccion}/extras", Cuerpo(new { tipo = "platon", danado = false, tamano_dano = "5_10cm" }));

        foreach (var r in new[] { sinNada, sinNivel, invalidos, sinMarcar, noDanado })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        static List<string> Campos(JsonElement c) => c.GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["clase_dano", "nivel_severidad", "tamano_dano"], Campos(await JsonAsync(sinNada)));
        Assert.Equal(["nivel_severidad"], Campos(await JsonAsync(sinNivel)));
        Assert.Equal(["clase_dano", "nivel_severidad", "tamano_dano"], Campos(await JsonAsync(invalidos)));
        Assert.Equal(["clase_dano", "nivel_severidad"], Campos(await JsonAsync(sinMarcar)));
        Assert.Equal(["tamano_dano"], Campos(await JsonAsync(noDanado)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task El_dano_del_extra_cuenta_en_los_patrones_y_se_cierra_con_la_foto_del_extra_y_uno_sin_dano_no_cuenta()
    {
        var e = await EscenarioAsync();
        var (extra, hallazgo) = await ExtraDanadoAsync(e.Inspector, e.Inspeccion);
        var barras = await ExtraAsync(e.Inspector, e.Inspeccion, "barras_techo");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, extra)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, barras)).StatusCode);
        var antes = await JsonAsync(await Como(e.Coordinador).GetAsync("/api/reportes/patrones"));
        Assert.Equal(0, antes.GetProperty("total").GetInt32());

        // El dano no pide foto propia: la foto del extra es la suya (el cierre no se bloquea).
        var cierre = await CerrarAsync(e.Inspector, e.Inspeccion);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);

        // CA-2: cuenta en los patrones (por clase, nivel y proceso; sin zona) y las barras sin dano no cuentan.
        var patrones = await JsonAsync(await Como(e.Coordinador).GetAsync("/api/reportes/patrones"));
        Assert.Equal(1, patrones.GetProperty("total").GetInt32());
        Assert.Equal("golpe", Assert.Single(patrones.GetProperty("por_clase").EnumerateArray()).GetProperty("clase").GetString());
        Assert.Equal("v3", Assert.Single(patrones.GetProperty("por_nivel").EnumerateArray()).GetProperty("nivel").GetString());
        Assert.Equal(0, patrones.GetProperty("sin_calificar").GetInt32());
        var averias = await JsonAsync(await Como(e.Coordinador).GetAsync("/api/reportes/averias"));
        Assert.Equal("golpe", Assert.Single(averias.GetProperty("por_clase").EnumerateArray()).GetProperty("clase").GetString());

        // Su evidencia: la foto es la del extra; solo falta el cierre de la transferencia.
        var fila = Assert.Single((await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/hallazgos?inspeccion_id={e.Inspeccion}"))).GetProperty("items").EnumerateArray());
        Assert.Equal(hallazgo, fila.GetProperty("id").GetGuid());
        Assert.Equal(["transferencia_sin_cierre"], fila.GetProperty("evidencia_faltante").EnumerateArray().Select(x => x.GetString()).ToList());
        // ESC-04: las barras sin dano no son un hallazgo.
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE ExtraId = @e", ("@e", barras)));
    }

    [Fact]
    public async Task Un_extra_sin_dano_no_cambia_ninguna_cifra_y_los_extras_de_antes_siguen_validos_sin_dano()
    {
        var e = await EscenarioAsync();
        // Un extra de antes del cambio: sin dano y sin migracion (ningun hallazgo ligado).
        var viejo = await ExtraAsync(e.Inspector, e.Inspeccion, "platon", observacion: "reportado antes");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, viejo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(e.Inspector, e.Inspeccion)).StatusCode);

        var informe = await InformeAsync(e.Inspector, e.Inspeccion);

        // ESC-06: sigue con su foto y su observacion y no aparece como dano.
        var extra = Assert.Single(informe.GetProperty("extras").EnumerateArray());
        Assert.Equal("reportado antes", extra.GetProperty("observacion").GetString());
        Assert.Equal(1, extra.GetProperty("fotos").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, extra.GetProperty("dano").ValueKind);
        Assert.Equal(0, informe.GetProperty("hallazgos").GetArrayLength());
        Assert.Equal(0, (await JsonAsync(await Como(e.Coordinador).GetAsync("/api/reportes/patrones"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await JsonAsync(await Como(e.Coordinador).GetAsync("/api/hallazgos"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task El_dano_de_un_extra_se_agrega_despues_pero_una_vez_guardado_no_se_quita_ni_se_cambia_y_el_extra_no_se_borra()
    {
        var e = await EscenarioAsync();
        var extra = await ExtraAsync(e.Inspector, e.Inspeccion, "platon");

        var agregar = await Como(e.Inspector).PutAsync(
            $"/api/inspecciones/{e.Inspeccion}/extras/{extra}", Cuerpo(new { tipo = "platon", danado = true, clase_dano = "golpe", tamano_dano = "5_10cm", nivel_severidad = "v2" }));
        Assert.Equal(HttpStatusCode.OK, agregar.StatusCode);
        Assert.Equal("v2", (await JsonAsync(agregar)).GetProperty("dano").GetProperty("nivel_severidad").GetString());

        var conservar = await Como(e.Inspector).PutAsync($"/api/inspecciones/{e.Inspeccion}/extras/{extra}", Cuerpo(new { tipo = "platon", observacion = "con el dano" }));
        var mismo = await Como(e.Inspector).PutAsync(
            $"/api/inspecciones/{e.Inspeccion}/extras/{extra}", Cuerpo(new { tipo = "platon", danado = true, clase_dano = "golpe", tamano_dano = "5_10cm", nivel_severidad = "v2" }));
        var quitar = await Como(e.Inspector).PutAsync($"/api/inspecciones/{e.Inspeccion}/extras/{extra}", Cuerpo(new { tipo = "platon", danado = false }));
        var cambiar = await Como(e.Inspector).PutAsync(
            $"/api/inspecciones/{e.Inspeccion}/extras/{extra}", Cuerpo(new { tipo = "platon", danado = true, clase_dano = "golpe", tamano_dano = "5_10cm", nivel_severidad = "v1" }));
        var borrar = await Como(e.Inspector).DeleteAsync($"/api/inspecciones/{e.Inspeccion}/extras/{extra}");

        Assert.Equal(HttpStatusCode.OK, conservar.StatusCode);
        Assert.NotEqual(JsonValueKind.Null, (await JsonAsync(conservar)).GetProperty("dano").ValueKind);
        Assert.Equal(HttpStatusCode.OK, mismo.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, quitar.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cambiar.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, borrar.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE ExtraId = @e AND NivelSeveridad = 'v2'", ("@e", extra)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspeccionExtras WHERE Id = @e", ("@e", extra)));
    }

    [Fact]
    public async Task La_lista_general_de_evidencias_del_informe_incluye_las_fotos_de_los_extras_con_su_extra_id()
    {
        var e = await EscenarioAsync();
        var extra = await ExtraAsync(e.Inspector, e.Inspeccion, "volco");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeExtraAsync(e.Inspector, e.Inspeccion, extra)).StatusCode);

        var informe = await InformeAsync(e.Inspector, e.Inspeccion);

        // CA-5: las fotos de los extras estan en la lista general, marcadas con su extra_id (y tambien en las fotos de cada extra).
        var evidencia = Assert.Single(informe.GetProperty("evidencias").EnumerateArray());
        Assert.Equal(extra, evidencia.GetProperty("extra_id").GetGuid());
        Assert.Equal(JsonValueKind.Null, evidencia.GetProperty("vista_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, evidencia.GetProperty("hallazgo_id").ValueKind);
        Assert.Equal(evidencia.GetProperty("id").GetGuid(), Assert.Single(informe.GetProperty("extras").EnumerateArray()).GetProperty("fotos")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task El_tipo_otro_esta_protegido_contra_la_inactivacion_el_cambio_de_nombre_y_el_borrado_y_los_demas_tipos_no()
    {
        var e = await EscenarioAsync();
        var tipos = (await JsonAsync(await Como(e.Coordinador).GetAsync("/api/catalogos/tipos-extra"))).GetProperty("items").EnumerateArray().ToList();
        var otro = tipos.Single(t => t.GetProperty("codigo").GetString() == "otro").GetProperty("id").GetGuid();
        var barras = tipos.Single(t => t.GetProperty("codigo").GetString() == "barras_techo").GetProperty("id").GetGuid();

        var inactivar = await Como(e.Coordinador).PatchAsync($"/api/catalogos/tipos-extra/{otro}", Cuerpo(new { activo = false }));
        var renombrar = await Como(e.Coordinador).PatchAsync($"/api/catalogos/tipos-extra/{otro}", Cuerpo(new { nombre = "Otros accesorios" }));
        var borrar = await Como(e.Coordinador).DeleteAsync($"/api/catalogos/tipos-extra/{otro}");

        // CA-6: «otro» es el que describe un extra con texto libre y no tiene reemplazo.
        Assert.Equal(HttpStatusCode.Conflict, inactivar.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, renombrar.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, borrar.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTiposExtra WHERE Codigo = 'otro' AND Activo = 1 AND Nombre = N'Otro'"));
        // Lo demas del catalogo no cambia: sus otros tipos se renombran e inactivan como siempre, y «otro» admite el orden.
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsync($"/api/catalogos/tipos-extra/{barras}", Cuerpo(new { nombre = "Barras del techo", activo = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsync($"/api/catalogos/tipos-extra/{otro}", Cuerpo(new { orden = 9 }))).StatusCode);
        // El catalogo se comparte con las demas pruebas: lo que se cambio vuelve a como estaba.
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsync($"/api/catalogos/tipos-extra/{barras}", Cuerpo(new { nombre = "Barras de techo", activo = true }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PatchAsync($"/api/catalogos/tipos-extra/{otro}", Cuerpo(new { orden = 4 }))).StatusCode);
    }
}
