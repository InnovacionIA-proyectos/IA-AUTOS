using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Parametros editables: quien administra los catalogos los consulta y cambia el valor (sin crear ni borrar claves), un valor que no cumple su tipo se rechaza en el campo valor, el cambio se aplica
/// de inmediato en los procesos que lo leen y queda en la auditoria con el valor antes y despues.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class ParametrosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string rol, string? email = null)
    {
        email ??= $"{rol}@ejemplo.com";
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + rol);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(string json) => new(json, Encoding.UTF8, "application/json");

    private Task<HttpResponseMessage> CambiarAsync(Sesion quien, string clave, string cuerpoJson) =>
        Cliente(quien.Token).PatchAsync($"/api/parametros/{clave}", Cuerpo(cuerpoJson));

    private async Task<object?> ValorEnBaseAsync(string clave) =>
        await Sembrador.EscalarAsync("SELECT Valor FROM dbo.tblParametros WHERE Clave = @c", ("@c", clave));

    private static string? Error(JsonElement cuerpo, string campo) =>
        cuerpo.GetProperty("details").GetProperty("fieldErrors").TryGetProperty(campo, out var e) ? e[0].GetString() : null;

    // ------------------------------------------------------------------------------------ consultar

    [Theory]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.SUPER_ADMIN)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task Quien_administra_los_catalogos_consulta_los_parametros_con_su_forma_y_ordenados_por_clave(string rol)
    {
        var sesion = await SesionAsync(rol);

        var r = await Cliente(sesion.Token).GetAsync("/api/parametros");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var lista = (await ComoJsonAsync(r)).EnumerateArray().ToList();
        Assert.Equal(13, lista.Count);
        Assert.Equal(["clave", "valor", "tipo", "descripcion", "actualizado_por", "updated_at"], lista[0].EnumerateObject().Select(p => p.Name).ToList());
        var claves = lista.Select(p => p.GetProperty("clave").GetString()!).ToList();
        Assert.Equal(claves.Order(StringComparer.Ordinal).ToList(), claves);
        var hora = lista.Single(p => p.GetProperty("clave").GetString() == "reporte_diario_hora");
        Assert.Equal("06:00", hora.GetProperty("valor").GetString());
        Assert.Equal("hora", hora.GetProperty("tipo").GetString());
        Assert.False(string.IsNullOrWhiteSpace(hora.GetProperty("descripcion").GetString()));
        Assert.Equal(JsonValueKind.Null, hora.GetProperty("actualizado_por").ValueKind);
        var destinatarios = lista.Single(p => p.GetProperty("clave").GetString() == "reporte_diario_destinatarios");
        Assert.Equal(JsonValueKind.Null, destinatarios.GetProperty("valor").ValueKind);
        Assert.Equal("correos", destinatarios.GetProperty("tipo").GetString());
    }

    [Theory]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.INSPECTOR)]
    [InlineData(clsRol.CONDUCTOR)]
    public async Task Los_demas_roles_no_consultan_ni_cambian_los_parametros_y_el_valor_no_cambia(string rol)
    {
        var sesion = await SesionAsync(rol);

        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(sesion.Token).GetAsync("/api/parametros")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CambiarAsync(sesion, "reporte_diario_hora", "{\"valor\":\"07:00\"}")).StatusCode);

        Assert.Equal("06:00", await ValorEnBaseAsync("reporte_diario_hora"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'parametro.actualizar'"));
    }

    [Fact]
    public async Task Sin_sesion_responde_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync("/api/parametros")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PatchAsync("/api/parametros/reporte_diario_hora", Cuerpo("{\"valor\":\"07:00\"}"))).StatusCode);
    }

    // ------------------------------------------------------------------------------------ cambiar

    [Theory]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.SUPER_ADMIN)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task Cambiar_un_parametro_responde_200_con_el_parametro_y_guarda_quien_y_cuando(string rol)
    {
        var sesion = await SesionAsync(rol);

        var r = await CambiarAsync(sesion, "reporte_diario_hora", "{\"valor\":\" 07:30 \"}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var parametro = await ComoJsonAsync(r);
        Assert.Equal("reporte_diario_hora", parametro.GetProperty("clave").GetString());
        Assert.Equal("07:30", parametro.GetProperty("valor").GetString());
        Assert.Equal(sesion.UsuarioId, parametro.GetProperty("actualizado_por").GetGuid());
        Assert.True((DateTimeOffset.UtcNow - parametro.GetProperty("updated_at").GetDateTimeOffset()).Duration() < TimeSpan.FromMinutes(5));
        Assert.Equal("07:30", await ValorEnBaseAsync("reporte_diario_hora"));
        var lista = (await ComoJsonAsync(await Cliente(sesion.Token).GetAsync("/api/parametros"))).EnumerateArray();
        Assert.Equal("07:30", lista.Single(p => p.GetProperty("clave").GetString() == "reporte_diario_hora").GetProperty("valor").GetString());
    }

    [Fact]
    public async Task Un_valor_que_no_cumple_su_tipo_responde_400_en_valor_y_el_parametro_sigue_igual()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var casos = new (string Clave, string Valor)[]
        {
            ("reporte_diario_hora", "\"6am\""),
            ("reporte_diario_hora", "\"25:00\""),
            ("objetivo_diario_novedad", "\"101\""),
            ("objetivo_diario_novedad", "\"-1\""),
            ("objetivo_diario_novedad", "\"uno\""),
            ("url_firmada_minutos", "\"0\""),
            ("url_firmada_minutos", "\"1.5\""),
            ("url_firmada_minutos", "\"mucho\""),
            ("niveles_criticos", "\"v1,v9\""),
            ("niveles_criticos", "\",\""),
            ("reporte_diario_destinatarios", "\"no-es-correo\""),
            ("reporte_diario_destinatarios", "\"bien@empresa.com,mal\""),
            ("url_firmada_minutos", "null"),
            ("reporte_diario_hora", "\"\""),
        };
        var antes = new Dictionary<string, object?>();
        foreach (var clave in casos.Select(c => c.Clave).Distinct())
        {
            antes[clave] = await ValorEnBaseAsync(clave);
        }

        foreach (var (clave, valor) in casos)
        {
            var r = await CambiarAsync(coordinador, clave, $"{{\"valor\":{valor}}}");

            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{clave} = {valor} debia dar 400 y dio {(int)r.StatusCode}");
            Assert.False(string.IsNullOrWhiteSpace(Error(await ComoJsonAsync(r), "valor")), $"{clave} = {valor} sin mensaje en valor");
        }

        foreach (var (clave, valorAntes) in antes)
        {
            Assert.Equal(valorAntes, await ValorEnBaseAsync(clave));
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'parametro.actualizar'"));
    }

    [Fact]
    public async Task El_mensaje_de_una_hora_mal_escrita_dice_el_formato_y_la_hora_sigue_en_0600()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);

        var r = await CambiarAsync(coordinador, "reporte_diario_hora", "{\"valor\":\"medianoche\"}");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("HH:mm", Error(await ComoJsonAsync(r), "valor"));
        Assert.Equal("06:00", await ValorEnBaseAsync("reporte_diario_hora"));
    }

    [Fact]
    public async Task Cada_tipo_acepta_su_valor_y_lo_guarda_normalizado()
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        Assert.Equal(HttpStatusCode.OK, (await CambiarAsync(admin, "objetivo_diario_novedad", "{\"valor\":\"2.50\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CambiarAsync(admin, "umbral_patron_cantidad", "{\"valor\":20}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CambiarAsync(admin, "niveles_criticos", "{\"valor\":\" v1 , v1_plus ,v1\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CambiarAsync(admin, "reporte_diario_destinatarios", "{\"valor\":\"Calidad@Empresa.com, gerencia@empresa.com\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CambiarAsync(admin, "retencion_dias", "{\"valor\":\"365\"}")).StatusCode);

        Assert.Equal("2.5", await ValorEnBaseAsync("objetivo_diario_novedad"));
        Assert.Equal("20", await ValorEnBaseAsync("umbral_patron_cantidad"));
        Assert.Equal("v1,v1_plus", await ValorEnBaseAsync("niveles_criticos"));
        Assert.Equal("calidad@empresa.com,gerencia@empresa.com", await ValorEnBaseAsync("reporte_diario_destinatarios"));
        Assert.Equal("365", await ValorEnBaseAsync("retencion_dias"));
    }

    [Fact]
    public async Task Solo_los_destinatarios_y_la_retencion_pueden_quedar_sin_valor()
    {
        var admin = await SesionAsync(clsRol.ADMIN);
        await CambiarAsync(admin, "reporte_diario_destinatarios", "{\"valor\":\"calidad@empresa.com\"}");
        await CambiarAsync(admin, "retencion_dias", "{\"valor\":\"30\"}");

        var destinatarios = await CambiarAsync(admin, "reporte_diario_destinatarios", "{\"valor\":null}");
        var retencion = await CambiarAsync(admin, "retencion_dias", "{\"valor\":\"\"}");

        Assert.Equal(HttpStatusCode.OK, destinatarios.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ComoJsonAsync(destinatarios)).GetProperty("valor").ValueKind);
        Assert.Equal(HttpStatusCode.OK, retencion.StatusCode);
        Assert.Null(await ValorEnBaseAsync("reporte_diario_destinatarios") as string);
        Assert.Null(await ValorEnBaseAsync("retencion_dias") as string);
    }

    [Fact]
    public async Task Una_clave_que_no_existe_responde_404_y_no_hay_api_para_crear_ni_borrar_claves()
    {
        var admin = await SesionAsync(clsRol.ADMIN);
        var cliente = Cliente(admin.Token);
        var antes = await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros");

        Assert.Equal(HttpStatusCode.NotFound, (await CambiarAsync(admin, "no_existe", "{\"valor\":\"1\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await cliente.PostAsync("/api/parametros", Cuerpo("{\"clave\":\"nueva\",\"valor\":\"1\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await cliente.DeleteAsync("/api/parametros/reporte_diario_hora")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await cliente.PutAsync("/api/parametros/reporte_diario_hora", Cuerpo("{\"valor\":\"07:00\"}"))).StatusCode);

        Assert.Equal(antes, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblParametros"));
    }

    [Theory]
    [InlineData("{}", "valor")]
    [InlineData("{\"valor\":true}", "valor")]
    [InlineData("{\"valor\":[\"07:00\"]}", "valor")]
    [InlineData("{\"valor\":\"07:00\",\"tipo\":\"texto\"}", "tipo")]
    [InlineData("{\"valor\":\"07:00\",\"clave\":\"otra\"}", "clave")]
    public async Task Un_cuerpo_incompleto_o_con_campos_que_no_se_cambian_responde_400(string cuerpo, string campo)
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        var r = await CambiarAsync(admin, "reporte_diario_hora", cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.NotNull(Error(await ComoJsonAsync(r), campo));
        Assert.Equal("06:00", await ValorEnBaseAsync("reporte_diario_hora"));
    }

    // ------------------------------------------------------------------------------------ auditoria y vigencia

    [Fact]
    public async Task Cada_cambio_queda_en_la_auditoria_con_el_valor_antes_y_despues_y_dar_el_mismo_valor_no_audita()
    {
        var admin = await SesionAsync(clsRol.ADMIN);

        await CambiarAsync(admin, "reporte_diario_hora", "{\"valor\":\"07:00\"}");
        await CambiarAsync(admin, "reporte_diario_hora", "{\"valor\":\"07:00\"}");
        await CambiarAsync(admin, "reporte_diario_hora", "{\"valor\":\"08:15\"}");

        var auditoria = await ComoJsonAsync(await Cliente(admin.Token).GetAsync("/api/auditoria?accion=parametro.actualizar"));
        var filas = auditoria.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, filas.Count);
        var ultima = filas[0];
        Assert.Equal("parametros", ultima.GetProperty("entidad").GetString());
        Assert.Equal(admin.UsuarioId, ultima.GetProperty("usuario_id").GetGuid());
        Assert.Equal("reporte_diario_hora", ultima.GetProperty("detalle").GetProperty("antes").GetProperty("parametro").GetString());
        Assert.Equal("07:00", ultima.GetProperty("detalle").GetProperty("antes").GetProperty("valor").GetString());
        Assert.Equal("08:15", ultima.GetProperty("detalle").GetProperty("despues").GetProperty("valor").GetString());
        var primera = filas[1];
        Assert.Equal("06:00", primera.GetProperty("detalle").GetProperty("antes").GetProperty("valor").GetString());
        Assert.Equal("07:00", primera.GetProperty("detalle").GetProperty("despues").GetProperty("valor").GetString());
    }

    [Fact]
    public async Task Un_cambio_se_aplica_de_inmediato_en_los_procesos_que_leen_el_parametro()
    {
        var admin = await SesionAsync(clsRol.ADMIN);
        using (var alcance = Fabrica.Services.CreateScope())
        {
            // Se calienta la lectura guardada: el valor viejo esta en la cache del proceso.
            Assert.Equal(15, await alcance.ServiceProvider.GetRequiredService<IParametros>().EnteroAsync("url_firmada_minutos", 0));
        }

        Assert.Equal(HttpStatusCode.OK, (await CambiarAsync(admin, "url_firmada_minutos", "{\"valor\":\"5\"}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CambiarAsync(admin, "niveles_criticos", "{\"valor\":\"v1_plus\"}")).StatusCode);

        using var despues = Fabrica.Services.CreateScope();
        var parametros = despues.ServiceProvider.GetRequiredService<IParametros>();
        Assert.Equal(5, await parametros.EnteroAsync("url_firmada_minutos", 0));
        Assert.Equal(["v1_plus"], await parametros.ListaAsync("niveles_criticos", []));
    }

    [Fact]
    public async Task La_vigencia_de_las_urls_firmadas_sigue_el_parametro_cambiado_por_la_api()
    {
        var admin = await SesionAsync(clsRol.ADMIN);
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        await CambiarAsync(admin, "url_firmada_minutos", "{\"valor\":\"2\"}");

        var r = await Cliente(admin.Token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var expira = (await ComoJsonAsync(r)).GetProperty("url_expira_at").GetDateTimeOffset();
        Assert.InRange((expira - DateTimeOffset.UtcNow).TotalSeconds, 60, 125);
    }
}
