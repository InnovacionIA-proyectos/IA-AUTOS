using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Aplicacion.Alertas;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Descartar un informe en borrador: lo descartan quien lo creo y el coordinador de calidad (nadie mas, tampoco el administrador), nunca uno cerrado o con firmas, queda en la auditoria con lo que tenia, su momento queda libre para
/// una original nueva y no sale en los listados. Quien toma el carro con el reclamo liberado captura un momento con borrador ajeno de cero: el borrador se conserva con su autor como superado. Ninguno de los dos avisa «Captura incompleta».
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_1)]
public sealed class DescartarInformeApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-21T08:30:00-05:00";

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Un viaje activo de la inspectora Ana (y del conductor) con una unidad y su ruta, y quienes no son su autora: otra inspectora, el coordinador, un administrador y un super administrador.</summary>
    private sealed record Escenario(Guid Viaje, Guid[] Etapas, Sesion Ana, Sesion Diana, Sesion Conductor, Sesion Coordinador, Sesion Admin, Sesion SuperAdmin);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _api = CrearFabrica(servicios: s => s.AddSingleton<IEnviadorPush>(_push));
    }

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await _api.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1", _api), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token, _api);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync(bool anaEsInspectoraDelViaje = true)
    {
        var ana = await SesionAsync("ana@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-DESC-0001", conductorId: conductor.UsuarioId, inspectorId: anaEsInspectoraDelViaje ? ana.UsuarioId : diana.UsuarioId);
        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), etapas, ana, diana, conductor, coordinador, admin, superAdmin);
    }

    private Task<HttpResponseMessage> DescartarAsync(Sesion quien, Guid inspeccion) => Como(quien).PostAsync($"/api/inspecciones/{inspeccion}/descartar", null);

    private Task<HttpResponseMessage> CrearLlegadaAsync(Sesion quien, Guid etapa) =>
        Como(quien).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapa, momento = "llegada", fecha = FECHA });

    private async Task<string> EstadoAsync(Guid inspeccion) => (string)(await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblInspecciones WHERE Id = @i", ("@i", inspeccion)))!;

    private Task<Guid> BorradorAsync(Guid etapa, Guid usuario, int fotos = 0, int danos = 0) => Sembrador.InformeAsync(etapa, usuario, cerrada: false, hallazgos: danos, evidencias: fotos, momento: "llegada");

    private async Task<T> ConProcesosAsync<T>(Func<clsAlertasPeriodicas, Task<T>> accion)
    {
        using var alcance = _api.Services.CreateScope();
        return await accion(alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>());
    }

    private Task EnvejecerAsync(Guid inspeccion, int minutos) =>
        Sembrador.EjecutarAsync(
            "ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL; UPDATE dbo.tblInspecciones SET IniciadaAt = DATEADD(MINUTE, -@m, SYSDATETIMEOFFSET()) WHERE Id = @i; ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL",
            ("@m", minutos), ("@i", inspeccion));

    private Task<int> AvisosDeCapturaAsync() => Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'captura_incompleta'");

    // ---------------------------------------------------------------------------------- quien descarta

    [Fact]
    public async Task El_autor_descarta_su_borrador_con_1_foto_y_el_momento_queda_libre_y_la_auditoria_dice_lo_que_tenia()
    {
        var e = await EscenarioAsync();
        var borrador = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId, fotos: 1);

        var r = await DescartarAsync(e.Ana, borrador);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var dto = await JsonAsync(r);
        Assert.Equal("descartada", dto.GetProperty("estado").GetString());
        Assert.Equal(borrador, dto.GetProperty("id").GetGuid());
        // Se conserva con su autora: no se borra nada.
        Assert.Equal("descartada", await EstadoAsync(borrador));
        Assert.Equal(e.Ana.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT UsuarioId FROM dbo.tblInspecciones WHERE Id = @i", ("@i", borrador)))!);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE InspeccionId = @i", ("@i", borrador)));
        // La auditoria del viaje dice quien, cuando y cuantas fotos y danos tenia.
        var auditoria = await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.descartar' AND EntidadId = @i AND UsuarioId = @u AND ViajeId = @v AND Detalle LIKE '%\"fotos\":1%' AND Detalle LIKE '%\"danos\":0%'",
            ("@i", borrador), ("@u", e.Ana.UsuarioId), ("@v", e.Viaje));
        Assert.Equal(1, auditoria);
        // El momento vuelve a ofrecer «Capturar»: una original nueva se crea sin chocar.
        var nueva = await CrearLlegadaAsync(e.Ana, e.Etapas[0]);
        Assert.Equal(HttpStatusCode.Created, nueva.StatusCode);
        Assert.NotEqual(borrador, (await JsonAsync(nueva)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Otra_inspectora_el_conductor_el_administrador_y_el_super_administrador_reciben_403_con_codigo_y_motivo_y_el_borrador_sigue_igual()
    {
        var e = await EscenarioAsync();
        var borrador = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId);

        foreach (var quien in new[] { e.Diana, e.Conductor, e.Admin, e.SuperAdmin })
        {
            var r = await DescartarAsync(quien, borrador);

            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("DESCARTE_SOLO_AUTOR_O_COORDINADOR", error.GetProperty("code").GetString());
            Assert.Equal("solo_autor_o_coordinador", error.GetProperty("details").GetProperty("motivo").GetString());
            Assert.Contains("lo creó otra persona", error.GetProperty("message").GetString());
        }

        Assert.Equal("borrador", await EstadoAsync(borrador));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.descartar'"));
    }

    [Fact]
    public async Task El_coordinador_de_calidad_descarta_el_borrador_de_otra_persona()
    {
        var e = await EscenarioAsync();
        var borrador = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId, fotos: 2, danos: 1);

        var r = await DescartarAsync(e.Coordinador, borrador);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("descartada", await EstadoAsync(borrador));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.descartar' AND UsuarioId = @u AND Detalle LIKE '%\"fotos\":2%' AND Detalle LIKE '%\"danos\":1%'", ("@u", e.Coordinador.UsuarioId)));
        Assert.Equal(HttpStatusCode.Created, (await CrearLlegadaAsync(e.Ana, e.Etapas[0])).StatusCode);
    }

    // ------------------------------------------------------------------------------ lo que no se descarta

    [Fact]
    public async Task Un_informe_cerrado_no_se_descarta_ni_el_autor_ni_el_coordinador_y_no_cambia_nada()
    {
        var e = await EscenarioAsync();
        var cerrada = await Sembrador.InformeAsync(e.Etapas[0], e.Ana.UsuarioId, cerrada: true, momento: "salida");

        foreach (var quien in new[] { e.Ana, e.Coordinador })
        {
            var r = await DescartarAsync(quien, cerrada);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("INFORME_CERRADO_NO_SE_DESCARTA", error.GetProperty("code").GetString());
            Assert.Equal("informe_cerrado", error.GetProperty("details").GetProperty("motivo").GetString());
            Assert.Equal("Este informe ya está cerrado: es evidencia y no se descarta", error.GetProperty("message").GetString());
        }

        Assert.Equal("cerrada", await EstadoAsync(cerrada));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.descartar'"));
    }

    [Fact]
    public async Task Un_borrador_cuya_transferencia_ya_tiene_una_firma_es_evidencia_y_no_se_descarta()
    {
        var e = await EscenarioAsync();
        var borrador = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId);
        await Sembrador.TransferenciaAsync(borrador, e.Ana.UsuarioId, e.Conductor.UsuarioId, tipoCierre: null);

        var r = await DescartarAsync(e.Ana, borrador);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var error = await JsonAsync(r);
        Assert.Equal("INFORME_CERRADO_NO_SE_DESCARTA", error.GetProperty("code").GetString());
        Assert.Equal("informe_con_firmas", error.GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal("borrador", await EstadoAsync(borrador));
    }

    [Fact]
    public async Task Descartar_dos_veces_o_uno_que_no_existe_responde_409_y_404()
    {
        var e = await EscenarioAsync();
        var borrador = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId);

        Assert.Equal(HttpStatusCode.OK, (await DescartarAsync(e.Ana, borrador)).StatusCode);
        var otraVez = await DescartarAsync(e.Ana, borrador);
        var inexistente = await DescartarAsync(e.Ana, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        Assert.Equal("informe_descartado", (await JsonAsync(otraVez)).GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.descartar'"));
    }

    // ------------------------------------------------------------------- un descartado ya no es el informe

    [Fact]
    public async Task Un_descartado_no_sale_en_el_listado_ni_admite_cambios_pero_se_abre_por_su_id_con_su_estado()
    {
        var e = await EscenarioAsync();
        var borrador = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId, fotos: 1);
        Assert.Equal(1, (await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/inspecciones?viaje_id={e.Viaje}"))).GetProperty("total").GetInt32());

        await DescartarAsync(e.Ana, borrador);

        foreach (var quien in new[] { e.Coordinador, e.Ana })
        {
            Assert.Equal(0, (await JsonAsync(await Como(quien).GetAsync($"/api/inspecciones?viaje_id={e.Viaje}"))).GetProperty("total").GetInt32());
        }

        var porId = await Como(e.Coordinador).GetAsync($"/api/inspecciones/{borrador}");
        Assert.Equal(HttpStatusCode.OK, porId.StatusCode);
        Assert.Equal("descartada", (await JsonAsync(porId)).GetProperty("estado").GetString());
        // Ya no admite cerrar.
        var cerrar = await Como(e.Ana).PostAsync($"/api/inspecciones/{borrador}/cerrar", null);
        Assert.Equal(HttpStatusCode.Conflict, cerrar.StatusCode);
        Assert.Equal("informe_descartado", (await JsonAsync(cerrar)).GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal("descartada", await EstadoAsync(borrador));
    }

    [Fact]
    public async Task La_correccion_abandonada_se_descarta_y_la_cerrada_puede_corregirse_de_nuevo()
    {
        var e = await EscenarioAsync();
        var cerrada = await Sembrador.InformeAsync(e.Etapas[0], e.Ana.UsuarioId, cerrada: true, momento: "salida");
        var primera = await JsonAsync(await Como(e.Ana).PostAsync($"/api/inspecciones/{cerrada}/corregir", null));
        var vacia = primera.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Como(e.Ana).PostAsync($"/api/inspecciones/{cerrada}/corregir", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await DescartarAsync(e.Ana, vacia)).StatusCode);
        var otra = await Como(e.Ana).PostAsync($"/api/inspecciones/{cerrada}/corregir", null);

        Assert.Equal(HttpStatusCode.Created, otra.StatusCode);
        Assert.Equal("descartada", await EstadoAsync(vacia));
        Assert.Equal("cerrada", await EstadoAsync(cerrada));
    }

    // ----------------------------------------------------------------- borrador ajeno con el reclamo liberado

    [Fact]
    public async Task Con_el_borrador_de_otra_persona_que_ya_no_tiene_el_reclamo_quien_toma_el_carro_captura_de_cero_y_el_borrador_queda_superado_con_su_autora()
    {
        var e = await EscenarioAsync(anaEsInspectoraDelViaje: false);
        var deAna = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId, fotos: 3);

        var r = await CrearLlegadaAsync(e.Diana, e.Etapas[0]);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var nueva = (await JsonAsync(r)).GetProperty("id").GetGuid();
        Assert.Equal("borrador", await EstadoAsync(nueva));
        // El de Ana se conserva con su autora y sus fotos, pero ya no es el informe de ese momento.
        Assert.Equal("superada", await EstadoAsync(deAna));
        Assert.Equal(e.Ana.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT UsuarioId FROM dbo.tblInspecciones WHERE Id = @i", ("@i", deAna)))!);
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE InspeccionId = @i", ("@i", deAna)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.superar' AND EntidadId = @i", ("@i", deAna)));
        // Ni el listado ni el detalle del viaje muestran el superado como el informe vigente: solo el nuevo.
        var lista = await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/inspecciones?viaje_id={e.Viaje}"));
        Assert.Equal(1, lista.GetProperty("total").GetInt32());
        Assert.Equal(nueva, lista.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());
        // Ana ya no puede seguir editando el suyo.
        Assert.Equal(HttpStatusCode.Conflict, (await Como(e.Ana).PostAsync($"/api/inspecciones/{deAna}/cerrar", null)).StatusCode);
    }

    [Fact]
    public async Task Si_la_autora_del_borrador_sigue_teniendo_el_reclamo_o_ya_firmo_nadie_la_deja_atras_y_responde_409_como_hasta_ahora()
    {
        var e = await EscenarioAsync(anaEsInspectoraDelViaje: false);
        var deAna = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId);
        await Sembrador.EjecutarAsync("INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso) VALUES (NEWID(), @v, @a, 'cargue')", ("@v", e.Viaje), ("@a", e.Ana.UsuarioId));

        var conReclamo = await CrearLlegadaAsync(e.Diana, e.Etapas[0]);

        Assert.Equal(HttpStatusCode.Conflict, conReclamo.StatusCode);
        Assert.Equal("borrador", await EstadoAsync(deAna));

        // Liberado el reclamo, pero con una firma en su transferencia: es evidencia y tampoco se deja atras.
        await Sembrador.EjecutarAsync("UPDATE dbo.tblReclamos SET LiberadoAt = SYSDATETIMEOFFSET(), LiberadoPor = InspectorId WHERE ViajeId = @v", ("@v", e.Viaje));
        await Sembrador.TransferenciaAsync(deAna, e.Ana.UsuarioId, e.Conductor.UsuarioId, tipoCierre: null);

        var conFirma = await CrearLlegadaAsync(e.Diana, e.Etapas[0]);

        Assert.Equal(HttpStatusCode.Conflict, conFirma.StatusCode);
        Assert.Equal("borrador", await EstadoAsync(deAna));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.superar'"));
    }

    [Fact]
    public async Task Quien_repite_su_propio_borrador_sigue_recibiendo_409_y_no_se_supera_a_si_mismo()
    {
        var e = await EscenarioAsync();
        var propio = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId);

        var r = await CrearLlegadaAsync(e.Ana, e.Etapas[0]);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("borrador", await EstadoAsync(propio));
    }

    // ----------------------------------------------------------------------------------- captura incompleta

    [Fact]
    public async Task Un_borrador_descartado_o_superado_ya_no_genera_el_aviso_de_captura_incompleta()
    {
        var e = await EscenarioAsync(anaEsInspectoraDelViaje: false);
        var abandonado = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId);
        await EnvejecerAsync(abandonado, 40);
        // Con el borrador abierto mas del tiempo, el aviso si sale.
        await ConProcesosAsync(p => p.EjecutarAvisosAsync());
        Assert.Equal(1, await AvisosDeCapturaAsync());
        await Sembrador.EjecutarAsync("ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL; DELETE FROM dbo.tblNotificaciones; ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL");

        // Descartado por su autora: no avisa.
        Assert.Equal(HttpStatusCode.OK, (await DescartarAsync(e.Ana, abandonado)).StatusCode);
        await ConProcesosAsync(p => p.EjecutarAvisosAsync());
        Assert.Equal(0, await AvisosDeCapturaAsync());

        // Superado por quien tomo el carro: tampoco; solo cuenta el nuevo, que esta reciente.
        var deAna = await BorradorAsync(e.Etapas[0], e.Ana.UsuarioId);
        await EnvejecerAsync(deAna, 40);
        Assert.Equal(HttpStatusCode.Created, (await CrearLlegadaAsync(e.Diana, e.Etapas[0])).StatusCode);
        Assert.Equal("superada", await EstadoAsync(deAna));
        await ConProcesosAsync(p => p.EjecutarAvisosAsync());
        Assert.Equal(0, await AvisosDeCapturaAsync());
    }
}
