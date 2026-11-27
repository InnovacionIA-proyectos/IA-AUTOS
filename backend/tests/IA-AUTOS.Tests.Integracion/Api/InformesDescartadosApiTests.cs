using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La lista de informes dice el rol de quien hizo cada uno y puede pedir los descartados con <c>estado=descartada</c>: el coordinador de calidad, el administrador y el super administrador ven todos los del cliente; el
/// inspector y el conductor, solo los que hicieron ellos; el comercial recibe 403. El valor solo lo admite la lista y siempre solo; la exportacion y la creacion lo rechazan con 400. Sin el filtro nada cambia: los
/// descartados no salen.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed class InformesDescartadosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-21T08:30:00-05:00";

    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private sealed record Escenario(Guid Viaje, Guid[] Etapas, Sesion Ana, Sesion Diana, Sesion Conductor, Sesion Coordinador, Sesion Admin, Sesion SuperAdmin, Sesion Comercial);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _api = CrearFabrica();
    }

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await _api.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1", _api), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token, _api);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync()
    {
        var ana = await SesionAsync("ana@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-DESC-0473", conductorId: conductor.UsuarioId, inspectorId: ana.UsuarioId);
        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), etapas, ana, diana, conductor, coordinador, admin, superAdmin, comercial);
    }

    private Task<Guid> BorradorAsync(Guid etapa, Guid usuario, string momento) => Sembrador.InformeAsync(etapa, usuario, cerrada: false, momento: momento);

    private Task<HttpResponseMessage> DescartarAsync(Sesion quien, Guid inspeccion) => Como(quien).PostAsync($"/api/inspecciones/{inspeccion}/descartar", null);

    private async Task<JsonElement> ListaAsync(Sesion quien, string consulta)
    {
        var r = await Como(quien).GetAsync("/api/inspecciones?" + consulta);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await JsonAsync(r);
    }

    private static List<Guid> Ids(JsonElement lista) => lista.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task Cada_fila_de_la_lista_dice_el_rol_de_quien_hizo_el_informe()
    {
        var e = await EscenarioAsync();
        var deAna = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId, "llegada");
        var delConductor = await BorradorAsync(e.Etapas[0], e.Conductor.UsuarioId, "salida");

        var lista = await ListaAsync(e.Coordinador, $"viaje_id={e.Viaje}");

        var roles = lista.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetGuid(), i => i.GetProperty("usuario_rol").GetString());
        Assert.Equal(2, roles.Count);
        Assert.Equal("inspector", roles[deAna]);
        Assert.Equal("conductor", roles[delConductor]);
        // Los datos de siempre siguen donde estaban y la lista no trae ni descartador ni cedulas.
        var fila = lista.GetProperty("items").EnumerateArray().First();
        Assert.True(fila.TryGetProperty("usuario_nombre", out _));
        Assert.Equal(JsonValueKind.Null, fila.GetProperty("descartada_por_nombre").ValueKind);
        Assert.Equal(JsonValueKind.Null, fila.GetProperty("descartada_at").ValueKind);
        Assert.DoesNotContain("cedula", fila.GetRawText());
    }

    [Fact]
    public async Task El_coordinador_el_administrador_y_el_super_administrador_listan_todos_los_descartados_con_quien_y_cuando()
    {
        var e = await EscenarioAsync();
        var deAna = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId, "llegada");
        var delConductor = await BorradorAsync(e.Etapas[0], e.Conductor.UsuarioId, "salida");
        Assert.Equal(HttpStatusCode.OK, (await DescartarAsync(e.Ana, deAna)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DescartarAsync(e.Coordinador, delConductor)).StatusCode);

        foreach (var quien in new[] { e.Coordinador, e.Admin, e.SuperAdmin })
        {
            var lista = await ListaAsync(quien, $"estado=descartada&viaje_id={e.Viaje}");
            Assert.Equal(2, lista.GetProperty("total").GetInt32());
            var filas = lista.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetGuid());
            Assert.Equal(new HashSet<Guid> { deAna, delConductor }, filas.Keys.ToHashSet());
            Assert.All(filas.Values, f => Assert.Equal("descartada", f.GetProperty("estado").GetString()));
            // Quien lo descarto y cuando salen de la auditoria del descarte.
            Assert.Equal("Nombre ana@ejemplo.com", filas[deAna].GetProperty("descartada_por_nombre").GetString());
            Assert.Equal("Nombre coord@ejemplo.com", filas[delConductor].GetProperty("descartada_por_nombre").GetString());
            Assert.All(filas.Values, f => Assert.True(f.GetProperty("descartada_at").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-10)));
            Assert.Equal("inspector", filas[deAna].GetProperty("usuario_rol").GetString());
        }
    }

    [Fact]
    public async Task El_inspector_y_el_conductor_solo_ven_los_descartados_que_hicieron_ellos_y_la_lista_vacia_si_no_hicieron_ninguno()
    {
        var e = await EscenarioAsync();
        var deAna = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId, "llegada");
        var delConductor = await BorradorAsync(e.Etapas[0], e.Conductor.UsuarioId, "salida");
        await DescartarAsync(e.Ana, deAna);
        await DescartarAsync(e.Coordinador, delConductor);

        Assert.Equal([deAna], Ids(await ListaAsync(e.Ana, "estado=descartada")));
        Assert.Equal([delConductor], Ids(await ListaAsync(e.Conductor, "estado=descartada")));
        // Otra inspectora no hizo ninguno: lista vacia, tambien si pide los de la autora.
        var diana = await ListaAsync(e.Diana, "estado=descartada");
        Assert.Equal(0, diana.GetProperty("total").GetInt32());
        Assert.Empty(diana.GetProperty("items").EnumerateArray());
        Assert.Equal(0, (await ListaAsync(e.Diana, $"estado=descartada&usuario_id={e.Ana.UsuarioId}")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await ListaAsync(e.Ana, $"estado=descartada&usuario_id={e.Diana.UsuarioId}")).GetProperty("total").GetInt32());
        Assert.Equal([deAna], Ids(await ListaAsync(e.Ana, $"estado=descartada&usuario_id={e.Ana.UsuarioId}")));
    }

    [Fact]
    public async Task El_comercial_recibe_403_y_el_valor_descartada_solo_se_admite_solo_en_la_lista_y_nunca_en_la_exportacion_ni_al_crear()
    {
        var e = await EscenarioAsync();

        // El comercial sigue viendo la lista de siempre, pero no la de descartados.
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Comercial).GetAsync("/api/inspecciones")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Comercial).GetAsync("/api/inspecciones?estado=descartada")).StatusCode);
        // Junto a otro valor de estado es un valor invalido.
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(e.Coordinador).GetAsync("/api/inspecciones?estado=descartada,cerrada")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(e.Coordinador).GetAsync("/api/inspecciones?estado=superada")).StatusCode);
        // La exportacion a Excel y la creacion no aceptan «descartada».
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(e.Coordinador).GetAsync("/api/exportaciones/inspecciones.xlsx?estado=descartada")).StatusCode);
        var crear = await Como(e.Ana).PostAsJsonAsync("/api/inspecciones", new { etapa_id = e.Etapas[0], momento = "llegada", fecha = FECHA, estado = "descartada" });
        Assert.Equal(HttpStatusCode.BadRequest, crear.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }

    [Fact]
    public async Task Sin_el_filtro_los_descartados_y_los_superados_siguen_fuera_de_la_lista_y_el_filtro_no_trae_borradores_ni_cerrados()
    {
        var e = await EscenarioAsync();
        var descartado = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId, "llegada");
        var cerrado = await Sembrador.InformeAsync(e.Etapas[0], e.Ana.UsuarioId, cerrada: true, momento: "salida");
        await DescartarAsync(e.Ana, descartado);

        foreach (var consulta in new[] { $"viaje_id={e.Viaje}", $"viaje_id={e.Viaje}&estado=borrador", $"viaje_id={e.Viaje}&estado=cerrada" })
        {
            Assert.DoesNotContain(descartado, Ids(await ListaAsync(e.Coordinador, consulta)));
        }

        Assert.Equal([cerrado], Ids(await ListaAsync(e.Coordinador, $"viaje_id={e.Viaje}")));
        Assert.Equal([descartado], Ids(await ListaAsync(e.Coordinador, $"estado=descartada&viaje_id={e.Viaje}")));
    }
}
