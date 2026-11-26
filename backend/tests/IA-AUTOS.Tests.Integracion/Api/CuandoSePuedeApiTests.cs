using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Cuando se puede: el lugar dice si admite cargue sin inspector y el servidor lo aplica en la excepcion, en el permiso y en la llegada del cargue del conductor; cada unidad dice si el conductor ya puede registrar ruta;
/// «Llegue» y la novedad exigen la entrega del cargue cerrada (tambien sin transferencia) y el informe de transporte lo arma el sistema.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class CuandoSePuedeApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-11-10T08:30:00-05:00";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private sealed record Escenario(Guid Viaje, Guid Vehiculo, Guid[] Etapas, string Vin, Sesion Conductor, Sesion Inspector, Sesion Coordinador, Sesion Admin);

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

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    /// <summary>Un viaje del conductor con una unidad. El lugar de cargue es «Cargue de pruebas» (admite cargue sin inspector y no tiene inspectores) salvo que se pida otra cosa.</summary>
    private async Task<Escenario> EscenarioAsync(string vin = "VIN-CUANDO-0001", bool entregaCerrada = true, string tipoCierre = "firmada")
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (vehiculo, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        if (entregaCerrada)
        {
            var llegada = await Sembrador.InformeAsync(etapas[0], conductor.UsuarioId, cerrada: true, momento: "llegada");
            await Sembrador.TransferenciaAsync(llegada, conductor.UsuarioId, coordinador.UsuarioId, tipoCierre, tipoCierre == "no_firma" ? admin.UsuarioId : null);
        }

        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), vehiculo, etapas, vin, conductor, inspector, coordinador, admin);
    }

    private async Task<Guid> LugarDelViajeAsync(Guid viaje) => (Guid)(await Sembrador.EscalarAsync("SELECT LugarCargueId FROM dbo.tblViajes WHERE Id = @v", ("@v", viaje)))!;

    private Task AdmiteAsync(Guid lugar, bool admite) =>
        Sembrador.EjecutarAsync("UPDATE dbo.tblLugares SET AdmiteCargueSinInspector = @a WHERE Id = @l", ("@a", admite), ("@l", lugar));

    private Task SinLugarDeCargueAsync(Guid viaje) => Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET LugarCargueId = NULL WHERE Id = @v", ("@v", viaje));

    private Task ConInspectorEnElPuntoAsync(Escenario e, Guid lugar) =>
        Sembrador.EjecutarAsync("INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), @u, @l)", ("@u", e.Inspector.UsuarioId), ("@l", lugar));

    private Task<HttpResponseMessage> CrearAsync(Sesion quien, Guid etapa, string momento, string? motivo = null, string? texto = null)
    {
        var cuerpo = new Dictionary<string, object?> { ["etapa_id"] = etapa, ["momento"] = momento, ["fecha"] = FECHA, ["lat"] = 4.6, ["lng"] = -74.1 };
        if (motivo is not null)
        {
            cuerpo["motivo_sin_inspector"] = motivo;
        }

        if (texto is not null)
        {
            cuerpo["motivo_sin_inspector_texto"] = texto;
        }

        return Como(quien).PostAsJsonAsync("/api/inspecciones", cuerpo);
    }

    private Task<HttpResponseMessage> LlegadaAsync(Sesion quien, Guid viaje, string vin) =>
        Como(quien).PostAsJsonAsync($"/api/viajes/{viaje}/vehiculos/{vin}/llegada", new { fecha = FECHA, lat = 4.9, lng = -74.4 });

    private async Task<Dictionary<string, object?>> CuerpoNovedadAsync(Sesion quien)
    {
        var subida = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" });
        var foto = await JsonAsync(subida);
        return new Dictionary<string, object?>
        {
            ["fecha"] = FECHA, ["lat"] = 4.7, ["lng"] = -74.2, ["tipo_novedad"] = "golpe", ["tamano_dano"] = "5_10cm",
            ["evidencia"] = new
            {
                storage_path = foto.GetProperty("storage_path").GetString(), sello_fecha = FECHA, sello_lat = 4.7, sello_lng = -74.2, precision = 5.0,
                dispositivo_id = "celular-1", hash_sha256 = foto.GetProperty("hash_sha256").GetString(),
            },
        };
    }

    private async Task<JsonElement> UnidadAsync(Sesion quien, Guid viaje, string vin, bool mios = false)
    {
        if (mios)
        {
            var lista = await JsonAsync(await Como(quien).GetAsync("/api/viajes/mios"));
            return lista.EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == viaje).GetProperty("vehiculos").EnumerateArray().Single(u => u.GetProperty("vin").GetString() == vin);
        }

        var detalle = await JsonAsync(await Como(quien).GetAsync($"/api/viajes/{viaje}"));
        return detalle.GetProperty("vehiculos").EnumerateArray().Single(u => u.GetProperty("vin").GetString() == vin);
    }

    private static string Motivo(JsonElement cuerpo) => cuerpo.GetProperty("details").GetProperty("motivo").GetString()!;

    private const string MENSAJE_NO_ADMITE = "Este lugar de cargue no admite cargue sin inspector: el cargue lo hace el inspector";

    // ------------------------------------------------------------------------------------------ el dato del lugar

    [Fact]
    public async Task El_dato_del_lugar_nace_en_falso_lo_activa_el_coordinador_y_el_cambio_queda_en_la_auditoria_con_el_antes_y_el_despues()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var creado = await Como(coordinador).PostAsync("/api/catalogos/lugares", Cuerpo(new { nombre = "CEDI Envigado", tipo = "cedi_1", ciudad = "Envigado" }));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var lugar = (await JsonAsync(creado)).GetProperty("id").GetGuid();

        var antes = (await JsonAsync(await Como(coordinador).GetAsync("/api/catalogos/lugares"))).GetProperty("items").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == lugar);
        var todos = (await JsonAsync(await Como(coordinador).GetAsync("/api/catalogos"))).GetProperty("catalogos").GetProperty("lugares").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == lugar);
        var r = await Como(coordinador).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { admite_cargue_sin_inspector = true }));

        Assert.False(antes.GetProperty("admite_cargue_sin_inspector").GetBoolean());
        Assert.False(todos.GetProperty("admite_cargue_sin_inspector").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.True((await JsonAsync(r)).GetProperty("admite_cargue_sin_inspector").GetBoolean());
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'catalogo.lugares.actualizar' AND EntidadId = @l AND UsuarioId = @u AND JSON_VALUE(Detalle, '$.antes.admite_cargue_sin_inspector') = 'false' AND JSON_VALUE(Detalle, '$.despues.admite_cargue_sin_inspector') = 'true'",
            ("@l", lugar), ("@u", coordinador.UsuarioId)));
    }

    [Fact]
    public async Task Admin_y_super_admin_lo_cambian_como_soporte_el_inspector_y_el_conductor_no_y_un_valor_que_no_es_booleano_se_rechaza()
    {
        var e = await EscenarioAsync();
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var lugar = await LugarDelViajeAsync(e.Viaje);

        Assert.Equal(HttpStatusCode.OK, (await Como(e.Admin).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { admite_cargue_sin_inspector = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(superAdmin).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { admite_cargue_sin_inspector = true }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Inspector).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { admite_cargue_sin_inspector = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(e.Conductor).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { admite_cargue_sin_inspector = false }))).StatusCode);
        var invalido = await Como(e.Coordinador).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { admite_cargue_sin_inspector = "si" }));
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE Id = @l AND AdmiteCargueSinInspector = 1", ("@l", lugar)));
    }

    [Fact]
    public async Task El_viaje_trae_si_su_lugar_de_cargue_admite_cargue_sin_inspector_en_el_detalle_el_listado_y_mis_viajes_y_dice_falso_sin_lugar()
    {
        var e = await EscenarioAsync();
        var lugar = await LugarDelViajeAsync(e.Viaje);

        var detalle = await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}"));
        var listado = (await JsonAsync(await Como(e.Coordinador).GetAsync("/api/viajes"))).GetProperty("items").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == e.Viaje);
        var mios = (await JsonAsync(await Como(e.Conductor).GetAsync("/api/viajes/mios"))).EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == e.Viaje);
        Assert.True(detalle.GetProperty("lugar_cargue_admite_sin_inspector").GetBoolean());
        Assert.True(listado.GetProperty("lugar_cargue_admite_sin_inspector").GetBoolean());
        Assert.True(mios.GetProperty("lugar_cargue_admite_sin_inspector").GetBoolean());

        await AdmiteAsync(lugar, false);
        Assert.False((await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}"))).GetProperty("lugar_cargue_admite_sin_inspector").GetBoolean());

        await SinLugarDeCargueAsync(e.Viaje);
        Assert.False((await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}"))).GetProperty("lugar_cargue_admite_sin_inspector").GetBoolean());
    }

    // ------------------------------------------------------------------- donde el lugar no admite cargue sin inspector

    [Fact]
    public async Task Donde_el_lugar_no_admite_la_salida_la_llegada_del_cargue_y_el_permiso_responden_409_con_su_motivo()
    {
        var e = await EscenarioAsync(entregaCerrada: false);
        await AdmiteAsync(await LugarDelViajeAsync(e.Viaje), false);

        var salida = await CrearAsync(e.Conductor, e.Etapas[0], "salida", "inspector_no_llego");
        var llegada = await CrearAsync(e.Conductor, e.Etapas[0], "llegada", "inspector_no_llego");
        var permiso = await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/permiso-sin-inspector", Cuerpo(new { motivo_sin_inspector = "inspector_no_llego", fecha = FECHA }));

        foreach (var r in new[] { salida, llegada, permiso })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var cuerpo = await JsonAsync(r);
            Assert.Equal("lugar_no_admite_cargue_sin_inspector", Motivo(cuerpo));
            Assert.Equal(MENSAJE_NO_ADMITE, cuerpo.GetProperty("message").GetString());
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e", ("@e", e.Etapas[0])));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector"));
    }

    [Fact]
    public async Task Un_viaje_sin_lugar_de_cargue_tampoco_admite_la_excepcion()
    {
        var e = await EscenarioAsync(entregaCerrada: false);
        await SinLugarDeCargueAsync(e.Viaje);

        var salida = await CrearAsync(e.Conductor, e.Etapas[0], "salida", "inspector_no_llego");

        Assert.Equal(HttpStatusCode.Conflict, salida.StatusCode);
        Assert.Equal("lugar_no_admite_cargue_sin_inspector", Motivo(await JsonAsync(salida)));
    }

    [Fact]
    public async Task El_dato_se_lee_en_cada_peticion_y_lo_ya_registrado_con_la_excepcion_se_conserva()
    {
        var e = await EscenarioAsync(entregaCerrada: false);
        var lugar = await LugarDelViajeAsync(e.Viaje);
        var primera = await CrearAsync(e.Conductor, e.Etapas[0], "llegada", "inspector_no_llego");
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        await AdmiteAsync(lugar, false);

        Assert.Equal(HttpStatusCode.Conflict, (await CrearAsync(e.Conductor, e.Etapas[0], "salida", "inspector_no_llego")).StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE ViajeId = @v AND Tipo = 'cargue_sin_inspector'", ("@v", e.Viaje)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'llegada' AND MotivoSinInspector = 'inspector_no_llego'", ("@e", e.Etapas[0])));
    }

    // ----------------------------------------------------- donde lo admite: la llegada del cargue del conductor exige la excepcion

    [Fact]
    public async Task Sin_inspectores_la_llegada_del_cargue_del_conductor_exige_el_motivo_y_con_el_activa_la_excepcion_con_su_novedad()
    {
        var e = await EscenarioAsync(entregaCerrada: false);

        var sin = await CrearAsync(e.Conductor, e.Etapas[0], "llegada");
        var con = await CrearAsync(e.Conductor, e.Etapas[0], "llegada", "otro", "El inspector se enfermo");

        Assert.Equal(HttpStatusCode.Forbidden, sin.StatusCode);
        var rechazo = await JsonAsync(sin);
        Assert.Equal("conductor_sin_permiso_de_excepcion", Motivo(rechazo));
        Assert.Equal("Activa «No hay inspector» con su motivo para hacer esta revisión", rechazo.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Created, con.StatusCode);
        var llegada = await JsonAsync(con);
        Assert.Equal("otro", llegada.GetProperty("motivo_sin_inspector").GetProperty("codigo").GetString());
        Assert.Equal(e.Conductor.UsuarioId, llegada.GetProperty("usuario_id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE ViajeId = @v AND Tipo = 'cargue_sin_inspector' AND MotivoTexto = N'El inspector se enfermo'", ("@v", e.Viaje)));
    }

    [Fact]
    public async Task Con_inspectores_la_llegada_del_cargue_del_conductor_exige_el_permiso_concedido_y_con_el_no_repite_el_motivo()
    {
        var e = await EscenarioAsync(entregaCerrada: false);
        await ConInspectorEnElPuntoAsync(e, await LugarDelViajeAsync(e.Viaje));

        var sinPermiso = await CrearAsync(e.Conductor, e.Etapas[0], "llegada");
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        var rechazo = await JsonAsync(sinPermiso);
        Assert.Equal("conductor_sin_permiso_de_excepcion", Motivo(rechazo));
        Assert.Equal("Pide el permiso «No hay inspector» y espera a que el coordinador lo conceda", rechazo.GetProperty("message").GetString());

        Assert.Equal(HttpStatusCode.Created, (await Como(e.Conductor).PostAsync($"/api/viajes/{e.Viaje}/permiso-sin-inspector", Cuerpo(new { motivo_sin_inspector = "inspector_no_llego", fecha = FECHA, lat = 4.6, lng = -74.1 }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CrearAsync(e.Conductor, e.Etapas[0], "llegada")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/permiso-sin-inspector/conceder", null)).StatusCode);

        var llegada = await CrearAsync(e.Conductor, e.Etapas[0], "llegada");

        Assert.Equal(HttpStatusCode.Created, llegada.StatusCode);
        Assert.Equal("inspector_no_llego", (await JsonAsync(llegada)).GetProperty("motivo_sin_inspector").GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task La_llegada_del_cargue_del_inspector_no_cambia_ni_necesita_que_el_lugar_admita_el_cargue_sin_inspector()
    {
        var e = await EscenarioAsync(entregaCerrada: false);
        await AdmiteAsync(await LugarDelViajeAsync(e.Viaje), false);

        var r = await CrearAsync(e.Inspector, e.Etapas[0], "llegada");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    // -------------------------------------------------------------------------------- cuando el conductor puede registrar ruta

    [Fact]
    public async Task Sin_la_entrega_del_cargue_cerrada_la_unidad_no_puede_registrar_ruta_y_dice_por_que()
    {
        var e = await EscenarioAsync(entregaCerrada: false);

        var sinLlegada = await UnidadAsync(e.Coordinador, e.Viaje, e.Vin);
        Assert.False(sinLlegada.GetProperty("puede_registrar_ruta").GetBoolean());
        Assert.Equal("entrega_del_cargue_sin_cerrar", sinLlegada.GetProperty("motivo_sin_ruta").GetString());

        var llegada = await Sembrador.InformeAsync(e.Etapas[0], e.Conductor.UsuarioId, cerrada: true, momento: "llegada");
        var sinTransferencia = await UnidadAsync(e.Conductor, e.Viaje, e.Vin, mios: true);
        Assert.False(sinTransferencia.GetProperty("puede_registrar_ruta").GetBoolean());
        Assert.Equal("entrega_del_cargue_sin_cerrar", sinTransferencia.GetProperty("motivo_sin_ruta").GetString());

        await Sembrador.TransferenciaAsync(llegada, e.Conductor.UsuarioId, e.Coordinador.UsuarioId, tipoCierre: null);
        var abierta = await UnidadAsync(e.Conductor, e.Viaje, e.Vin, mios: true);
        Assert.False(abierta.GetProperty("puede_registrar_ruta").GetBoolean());
        Assert.Equal("entrega_del_cargue_sin_cerrar", abierta.GetProperty("motivo_sin_ruta").GetString());
    }

    [Theory]
    [InlineData("firmada")]
    [InlineData("con_reservas")]
    [InlineData("no_firma")]
    public async Task Con_la_entrega_del_cargue_cerrada_de_cualquier_forma_la_unidad_puede_registrar_ruta(string tipoCierre)
    {
        var e = await EscenarioAsync(tipoCierre: tipoCierre);

        var detalle = await UnidadAsync(e.Coordinador, e.Viaje, e.Vin);
        var mios = await UnidadAsync(e.Conductor, e.Viaje, e.Vin, mios: true);

        foreach (var unidad in new[] { detalle, mios })
        {
            Assert.True(unidad.GetProperty("puede_registrar_ruta").GetBoolean());
            Assert.Equal(JsonValueKind.Null, unidad.GetProperty("motivo_sin_ruta").ValueKind);
        }
    }

    [Fact]
    public async Task Una_unidad_que_no_sale_no_puede_registrar_ruta_aunque_su_cargue_este_cerrado()
    {
        var e = await EscenarioAsync();
        var (_, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[0], e.Coordinador.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Numero = 50"))!, nivel: "v1_plus");
        await Sembrador.UnidadNoSaleAsync(e.Viaje, e.Vehiculo, hallazgo, e.Coordinador.UsuarioId, "Golpe estructural");

        var unidad = await UnidadAsync(e.Coordinador, e.Viaje, e.Vin);

        Assert.False(unidad.GetProperty("puede_registrar_ruta").GetBoolean());
        Assert.Equal("unidad_no_sale", unidad.GetProperty("motivo_sin_ruta").GetString());
    }

    // ------------------------------------------------------------------------ «Llegue» y la novedad exigen la entrega cerrada

    [Fact]
    public async Task Llegue_y_la_novedad_sin_la_transferencia_del_cargue_responden_409_y_no_crean_nada_aunque_la_salida_de_transporte_ya_exista()
    {
        var e = await EscenarioAsync(entregaCerrada: false);
        await Sembrador.InformeAsync(e.Etapas[0], e.Conductor.UsuarioId, cerrada: true, momento: "llegada");
        await Sembrador.InformeAsync(e.Etapas[1], e.Conductor.UsuarioId, cerrada: false, momento: "salida");

        var llegue = await LlegadaAsync(e.Conductor, e.Viaje, e.Vin);
        var novedad = await Como(e.Conductor).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/novedades", await CuerpoNovedadAsync(e.Conductor));

        foreach (var r in new[] { llegue, novedad })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var cuerpo = await JsonAsync(r);
            Assert.Equal("ETAPA_PREVIA_SIN_CIERRE", cuerpo.GetProperty("code").GetString());
            Assert.Equal("transferencia_sin_cierre", Motivo(cuerpo));
            Assert.Equal("Falta cerrar la entrega del cargue: fírmenla, recíbanla con reservas o registren la no firma", cuerpo.GetProperty("message").GetString());
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'llegada'", ("@e", e.Etapas[1])));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos h JOIN dbo.tblInspecciones i ON i.Id = h.InspeccionId WHERE i.EtapaId = @e", ("@e", e.Etapas[1])));
    }

    [Fact]
    public async Task Con_la_entrega_del_cargue_cerrada_llegue_funciona_y_repetirlo_sigue_siendo_llegada_ya_cerrada()
    {
        var e = await EscenarioAsync();

        var primera = await LlegadaAsync(e.Conductor, e.Viaje, e.Vin);
        var segunda = await LlegadaAsync(e.Conductor, e.Viaje, e.Vin);

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("LLEGADA_YA_CERRADA", (await JsonAsync(segunda)).GetProperty("code").GetString());
    }

    // --------------------------------------------------------------------------------------- el informe de transporte lo arma el sistema

    [Fact]
    public async Task Nadie_crea_a_mano_un_informe_de_transporte_ni_lo_corrige_y_los_que_ya_existen_se_siguen_leyendo()
    {
        var e = await EscenarioAsync();
        var existente = await Sembrador.InformeAsync(e.Etapas[1], e.Inspector.UsuarioId, cerrada: true, momento: "llegada");

        foreach (var quien in new[] { e.Inspector, e.Conductor, e.Admin })
        {
            foreach (var momento in new[] { "salida", "llegada" })
            {
                var r = await CrearAsync(quien, e.Etapas[1], momento);
                Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
                var cuerpo = await JsonAsync(r);
                Assert.Equal("transporte_lo_arma_el_sistema", Motivo(cuerpo));
                Assert.Equal("El informe de transporte lo arma el sistema con «Reportar novedad» y «Llegué»", cuerpo.GetProperty("message").GetString());
            }
        }

        foreach (var quien in new[] { e.Inspector, e.Admin })
        {
            var corregir = await Como(quien).PostAsJsonAsync($"/api/inspecciones/{existente}/corregir", new { notas = "Se corrige", lat = 4.6, lng = -74.1 });
            Assert.Equal(HttpStatusCode.Conflict, corregir.StatusCode);
            Assert.Equal("transporte_lo_arma_el_sistema", Motivo(await JsonAsync(corregir)));
        }

        Assert.Equal(HttpStatusCode.OK, (await Como(e.Coordinador).GetAsync($"/api/inspecciones/{existente}")).StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e", ("@e", e.Etapas[1])));
    }

    [Fact]
    public async Task Reportar_novedad_sigue_armando_el_informe_de_transporte()
    {
        var e = await EscenarioAsync();

        var r = await Como(e.Conductor).PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/novedades", await CuerpoNovedadAsync(e.Conductor));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'salida'", ("@e", e.Etapas[1])));
    }
}
