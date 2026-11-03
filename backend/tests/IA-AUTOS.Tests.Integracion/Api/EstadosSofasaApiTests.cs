using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El catalogo <c>estados-sofasa</c>: el codigo es el StatusCode real de la orden (con sus mayusculas), el nombre su etiqueta; sale en <c>GET /api/catalogos</c>, se administra con las reglas de los demas
/// catalogos (codigo fijo, unico, un valor en uso no se borra) y el listado de ordenes sigue devolviendo <c>status_code</c> sin traducir.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class EstadosSofasaApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string RUTA = "/api/catalogos/estados-sofasa";

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private async Task<string> TokenAsync(string rol = clsRol.ADMIN)
    {
        var email = $"{rol}@ejemplo.com";
        await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol);
        return await LoginAsync(email, "Clave-Segura-1");
    }

    private static async Task<JsonElement> LeerJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), System.Text.Encoding.UTF8, "application/json");

    private static async Task<JsonElement> CrearAsync(HttpClient cliente, object cuerpo)
    {
        var r = await cliente.PostAsync(RUTA, Cuerpo(cuerpo));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await LeerJsonAsync(r);
    }

    private Task OrdenAsync(long sourceId, string? estado) =>
        Sembrador.EjecutarAsync("INSERT dbo.tblOrdenesSofasa (Id, SourceId, StatusCode) VALUES (NEWID(), @s, @e)", ("@s", sourceId), ("@e", (object?)estado ?? DBNull.Value));

    [Fact]
    public async Task El_catalogo_sale_en_el_conjunto_de_catalogos_y_empieza_vacio_con_sus_campos()
    {
        var token = await TokenAsync(clsRol.INSPECTOR);

        var todos = await LeerJsonAsync(await Cliente(token).GetAsync("/api/catalogos"));
        var solo = await LeerJsonAsync(await Cliente(token).GetAsync(RUTA));

        Assert.Equal(0, todos.GetProperty("catalogos").GetProperty("estados-sofasa").GetArrayLength());
        Assert.Equal(0, solo.GetProperty("items").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(solo.GetProperty("version").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync(RUTA)).StatusCode);
    }

    [Fact]
    public async Task Un_estado_conserva_el_codigo_tal_como_llega_con_sus_mayusculas_y_trae_codigo_nombre_orden_y_activo()
    {
        var cliente = Cliente(await TokenAsync());

        var creado = await CrearAsync(cliente, new { codigo = "  ENTREGADA_AL_CLIENTE  ", nombre = "Entregada al cliente", orden = 2 });
        var otro = await CrearAsync(cliente, new { codigo = "En ruta 2", nombre = "En ruta" });

        Assert.Equal(["id", "codigo", "nombre", "orden", "activo"], creado.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("ENTREGADA_AL_CLIENTE", creado.GetProperty("codigo").GetString());
        Assert.Equal("Entregada al cliente", creado.GetProperty("nombre").GetString());
        Assert.Equal(2, creado.GetProperty("orden").GetInt32());
        Assert.True(creado.GetProperty("activo").GetBoolean());
        Assert.Equal("En ruta 2", otro.GetProperty("codigo").GetString());
        var items = (await LeerJsonAsync(await cliente.GetAsync(RUTA))).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("codigo").GetString()).ToList();
        Assert.Equal(["En ruta 2", "ENTREGADA_AL_CLIENTE"], items);
    }

    [Theory]
    [InlineData(clsRol.ADMIN, HttpStatusCode.Created)]
    [InlineData(clsRol.SUPER_ADMIN, HttpStatusCode.Created)]
    [InlineData(clsRol.COORDINADOR_CALIDAD, HttpStatusCode.Created)]
    [InlineData(clsRol.COMERCIAL, HttpStatusCode.Forbidden)]
    [InlineData(clsRol.INSPECTOR, HttpStatusCode.Forbidden)]
    [InlineData(clsRol.CONDUCTOR, HttpStatusCode.Forbidden)]
    public async Task Crear_un_estado_es_de_quien_administra_los_catalogos(string rol, HttpStatusCode esperado)
    {
        var cliente = Cliente(await TokenAsync(rol));

        var r = await cliente.PostAsync(RUTA, Cuerpo(new { codigo = "ENTREGADA", nombre = "Entregada" }));

        Assert.Equal(esperado, r.StatusCode);
    }

    [Fact]
    public async Task El_codigo_es_obligatorio_unico_sin_distinguir_mayusculas_y_el_nombre_obligatorio_con_el_error_en_su_campo()
    {
        var cliente = Cliente(await TokenAsync());
        await CrearAsync(cliente, new { codigo = "ENTREGADA", nombre = "Entregada" });

        var repetido = await cliente.PostAsync(RUTA, Cuerpo(new { codigo = "entregada", nombre = "Otra" }));
        var sinCodigo = await cliente.PostAsync(RUTA, Cuerpo(new { nombre = "Sin codigo" }));
        var codigoLargo = await cliente.PostAsync(RUTA, Cuerpo(new { codigo = new string('X', 101), nombre = "Largo" }));
        var conSalto = await cliente.PostAsync(RUTA, Cuerpo(new { codigo = "UNO\nDOS", nombre = "Salto" }));
        var sinNombre = await cliente.PostAsync(RUTA, Cuerpo(new { codigo = "NUEVO" }));
        var ordenNegativo = await cliente.PostAsync(RUTA, Cuerpo(new { codigo = "OTRO", nombre = "Otro", orden = -1 }));
        var campoAjeno = await cliente.PostAsync(RUTA, Cuerpo(new { codigo = "AJENO", nombre = "Ajeno", accion = "x" }));

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        foreach (var (respuesta, campo) in new[] { (sinCodigo, "codigo"), (codigoLargo, "codigo"), (conSalto, "codigo"), (sinNombre, "nombre"), (ordenNegativo, "orden") })
        {
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.True((await LeerJsonAsync(respuesta)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty(campo, out _), $"Falta el error de {campo}");
        }

        Assert.Equal(HttpStatusCode.BadRequest, campoAjeno.StatusCode);
    }

    [Fact]
    public async Task Se_edita_el_nombre_el_orden_y_el_estado_pero_el_codigo_no_cambia()
    {
        var cliente = Cliente(await TokenAsync());
        var id = (await CrearAsync(cliente, new { codigo = "EN_RUTA", nombre = "En ruta", orden = 1 })).GetProperty("id").GetGuid();

        var cambio = await cliente.PatchAsync($"{RUTA}/{id}", Cuerpo(new { nombre = "En camino", orden = 5, activo = false }));
        var mismoCodigo = await cliente.PatchAsync($"{RUTA}/{id}", Cuerpo(new { codigo = "EN_RUTA", nombre = "En camino 2" }));
        var otroCodigo = await cliente.PatchAsync($"{RUTA}/{id}", Cuerpo(new { codigo = "EN_CAMINO" }));
        var nulo = await cliente.PatchAsync($"{RUTA}/{id}", Cuerpo(new { nombre = (string?)null }));
        var inexistente = await cliente.PatchAsync($"{RUTA}/{Guid.NewGuid()}", Cuerpo(new { nombre = "x" }));

        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        var valor = await LeerJsonAsync(cambio);
        Assert.Equal("En camino", valor.GetProperty("nombre").GetString());
        Assert.Equal(5, valor.GetProperty("orden").GetInt32());
        Assert.False(valor.GetProperty("activo").GetBoolean());
        Assert.Equal("EN_RUTA", valor.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.OK, mismoCodigo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otroCodigo.StatusCode);
        Assert.True((await LeerJsonAsync(otroCodigo)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("codigo", out _));
        Assert.Equal(HttpStatusCode.BadRequest, nulo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Fact]
    public async Task Un_estado_sin_ordenes_se_borra_y_uno_que_alguna_orden_usa_responde_409_y_solo_se_inactiva()
    {
        var cliente = Cliente(await TokenAsync());
        var libre = (await CrearAsync(cliente, new { codigo = "SIN_USO", nombre = "Sin uso" })).GetProperty("id").GetGuid();
        var enUso = (await CrearAsync(cliente, new { codigo = "ENTREGADA", nombre = "Entregada" })).GetProperty("id").GetGuid();
        await OrdenAsync(9001, "entregada");

        var borrado = await cliente.DeleteAsync($"{RUTA}/{libre}");
        var rechazado = await cliente.DeleteAsync($"{RUTA}/{enUso}");
        var inactivado = await cliente.PatchAsync($"{RUTA}/{enUso}", Cuerpo(new { activo = false }));

        Assert.Equal(HttpStatusCode.NoContent, borrado.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rechazado.StatusCode);
        Assert.Equal("El valor está en uso y no se puede borrar: solo puede inactivarlo", (await LeerJsonAsync(rechazado)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, inactivado.StatusCode);
        Assert.Equal(["ENTREGADA"], (await LeerJsonAsync(await cliente.GetAsync(RUTA))).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("codigo").GetString()).ToList());
    }

    [Fact]
    public async Task Cada_cambio_cambia_la_version_y_queda_en_la_auditoria()
    {
        var cliente = Cliente(await TokenAsync());
        var antes = (await LeerJsonAsync(await cliente.GetAsync("/api/catalogos"))).GetProperty("version").GetString();

        var id = (await CrearAsync(cliente, new { codigo = "ENTREGADA", nombre = "Entregada" })).GetProperty("id").GetGuid();
        await cliente.PatchAsync($"{RUTA}/{id}", Cuerpo(new { nombre = "Entregada al cliente" }));

        Assert.NotEqual(antes, (await LeerJsonAsync(await cliente.GetAsync("/api/catalogos"))).GetProperty("version").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'catalogo.estados-sofasa.crear'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'catalogo.estados-sofasa.actualizar'"));
    }

    [Fact]
    public async Task El_listado_de_ordenes_sigue_devolviendo_el_status_code_sin_traducir_y_una_orden_con_un_estado_fuera_del_catalogo_se_guarda()
    {
        // El super administrador ve las ordenes de todos los clientes (las de los demas se limitan a los VIN de su cliente).
        var cliente = Cliente(await TokenAsync(clsRol.SUPER_ADMIN));
        await CrearAsync(cliente, new { codigo = "ENTREGADA", nombre = "Entregada al cliente" });
        await OrdenAsync(9101, "ENTREGADA");
        await OrdenAsync(9102, "ESTADO_NUEVO_DEL_ORIGEN");

        var ordenes = (await LeerJsonAsync(await cliente.GetAsync("/api/sofasa/ordenes?orden=fecha:asc"))).GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(["ENTREGADA", "ESTADO_NUEVO_DEL_ORIGEN"], ordenes.Select(o => o.GetProperty("status_code").GetString()).Order().ToList());
        Assert.All(ordenes, o => Assert.False(o.TryGetProperty("estado_nombre", out _)));
    }
}
