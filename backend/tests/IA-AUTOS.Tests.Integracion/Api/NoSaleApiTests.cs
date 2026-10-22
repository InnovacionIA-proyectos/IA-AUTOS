using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Una unidad con un hallazgo V1+ puede no salir del viaje sin bloquearlo: la marcan el inspector del viaje o el coordinador de calidad con su motivo y el hallazgo que lo causa,
/// conserva su trazabilidad y puede quedar reemplazada por otro VIN.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class NoSaleApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    /// <summary>Un viaje con su inspector y su conductor, con la unidad <c>VIN-NS-A</c> y un hallazgo V1+ en una inspeccion cerrada de su primera etapa.</summary>
    private sealed record Escenario(Guid Viaje, Guid Vehiculo, Guid[] Etapas, Guid Inspeccion, Guid Hallazgo, Sesion Inspector, Sesion Conductor, Guid Zona);

    private async Task<Escenario> UnidadConV1PlusAsync(string nivel = "v1_plus", bool cerrada = true)
    {
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var (vehiculo, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-NS-A", conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        var zona = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!;
        var (inspeccion, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], inspector.UsuarioId, zona, nivel: nivel, cerrada: cerrada);
        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), vehiculo, etapas, inspeccion, hallazgo, inspector, conductor, zona);
    }

    private Task<HttpResponseMessage> MarcarAsync(Sesion quien, Guid viaje, string vin, object cuerpo) =>
        Cliente(quien.Token).PostAsJsonAsync($"/api/viajes/{viaje}/vehiculos/{vin}/no-sale", cuerpo);

    private static string Mensaje(JsonElement cuerpo) => cuerpo.GetProperty("message").GetString()!;

    private static JsonElement Unidad(JsonElement viaje, string vin) =>
        viaje.GetProperty("vehiculos").EnumerateArray().Single(v => v.GetProperty("vin").GetString() == vin);

    // ---------------------------------------------------------------------------------------------- camino feliz

    [Fact]
    public async Task El_inspector_del_viaje_marca_la_unidad_con_su_reemplazo_y_el_viaje_sigue_con_ella_sin_contar_como_completa()
    {
        var e = await UnidadConV1PlusAsync();
        await Sembrador.VehiculoAsync("VIN-NS-R");

        var r = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "  Golpe estructural en el frente  ", hallazgo_id = e.Hallazgo, vin_reemplazo = "vin-ns-r" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var viaje = await ComoJsonAsync(r);
        // El viaje sigue: ni se finaliza ni se cancela.
        Assert.Contains(viaje.GetProperty("estado").GetString(), new[] { "programado", "en_curso" });
        var unidad = Unidad(viaje, "VIN-NS-A");
        Assert.Equal("no_sale", unidad.GetProperty("estado_unidad").GetString());
        var noSale = unidad.GetProperty("no_sale");
        Assert.Equal("Golpe estructural en el frente", noSale.GetProperty("motivo").GetString());
        Assert.Equal(e.Hallazgo, noSale.GetProperty("hallazgo_id").GetGuid());
        Assert.Equal(e.Inspector.UsuarioId, noSale.GetProperty("marcado_por").GetGuid());
        Assert.Equal("Nombre inspector@ejemplo.com", noSale.GetProperty("marcado_por_nombre").GetString());
        Assert.Equal("VIN-NS-R", noSale.GetProperty("vin_reemplazo").GetString());
        // El reemplazo entra al viaje con sus etapas nuevas y la unidad conserva las suyas.
        var reemplazo = Unidad(viaje, "VIN-NS-R");
        Assert.Equal("activa", reemplazo.GetProperty("estado_unidad").GetString());
        Assert.Equal(3, reemplazo.GetProperty("etapas").GetInt32());
        Assert.Equal(3, unidad.GetProperty("etapas").GetInt32());
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas WHERE VehiculoId = @v", ("@v", e.Vehiculo)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE Id = @i AND Estado = 'cerrada'", ("@i", e.Inspeccion)));
        // Queda en el historial del hallazgo y en la auditoria, y la unidad no cuenta como completa.
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblHallazgoSeguimiento WHERE HallazgoId = @h AND Accion = 'no_sale' AND UsuarioId = @u AND Nota = N'Golpe estructural en el frente'",
            ("@h", e.Hallazgo), ("@u", e.Inspector.UsuarioId)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.no_sale'"));
        Assert.Equal("pendiente", (string?)await Sembrador.EscalarAsync("SELECT EstadoSeguimiento FROM dbo.tblHallazgos WHERE Id = @h", ("@h", e.Hallazgo)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos WHERE VehiculoId = @v", ("@v", e.Vehiculo)));
    }

    [Fact]
    public async Task El_coordinador_de_calidad_tambien_la_marca_y_el_reemplazo_es_opcional()
    {
        var e = await UnidadConV1PlusAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var r = await MarcarAsync(coordinador, e.Viaje, "VIN-NS-A", new { motivo = "Se cambia por otra", hallazgo_id = e.Hallazgo });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var noSale = Unidad(await ComoJsonAsync(r), "VIN-NS-A").GetProperty("no_sale");
        Assert.Equal(JsonValueKind.Null, noSale.GetProperty("vin_reemplazo").ValueKind);
        Assert.Equal(coordinador.UsuarioId, noSale.GetProperty("marcado_por").GetGuid());
    }

    [Fact]
    public async Task El_viaje_sigue_y_la_unidad_que_no_sale_deja_de_contar_en_sus_vehiculos_completos()
    {
        var e = await UnidadConV1PlusAsync();
        var (_, etapasCompanera) = await Sembrador.VehiculoConRutaAsync("VIN-NS-B", viajeId: e.Viaje);
        // Las dos unidades terminan sus tres etapas: las dos cuentan como completas hasta que una no sale.
        foreach (var etapa in etapasCompanera.Concat(e.Etapas.Skip(1)))
        {
            await Sembrador.InformeAsync(etapa, e.Inspector.UsuarioId, cerrada: true);
        }

        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var antes = (Items(await ComoJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes")))).EnumerateArray().Single();
        var marcada = await MarcarAsync(coordinador, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });
        var despues = (Items(await ComoJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes")))).EnumerateArray().Single();

        Assert.Equal(HttpStatusCode.OK, marcada.StatusCode);
        Assert.Equal(2, antes.GetProperty("vehiculos_completos").GetInt32());
        Assert.Equal(2, despues.GetProperty("total_vehiculos").GetInt32());
        Assert.Equal(1, despues.GetProperty("vehiculos_completos").GetInt32());
        Assert.Contains(despues.GetProperty("estado").GetString(), new[] { "programado", "en_curso" });
    }

    // ----------------------------------------------------------------------------------------------- quien puede

    [Theory]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.SUPER_ADMIN)]
    [InlineData(clsRol.CONDUCTOR)]
    public async Task Admin_comercial_super_administrador_y_conductor_reciben_403(string rol)
    {
        var e = await UnidadConV1PlusAsync();
        var quien = await SesionAsync("otro@ejemplo.com", rol);

        var r = await MarcarAsync(quien, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE EstadoUnidad = 'no_sale'"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgoSeguimiento"));
    }

    [Fact]
    public async Task Un_inspector_que_no_es_el_del_viaje_y_quien_no_inicio_sesion_no_la_marcan()
    {
        var e = await UnidadConV1PlusAsync();
        var otroInspector = await SesionAsync("otro-inspector@ejemplo.com", clsRol.INSPECTOR);

        var ajeno = await MarcarAsync(otroInspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });
        var anonimo = await Cliente().PostAsJsonAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-NS-A/no-sale", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });

        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Contains("inspector del viaje", Mensaje(await ComoJsonAsync(ajeno)));
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    [Fact]
    public async Task El_coordinador_de_otro_cliente_recibe_403_en_un_viaje_que_no_es_de_su_cliente()
    {
        var e = await UnidadConV1PlusAsync();
        var otro = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otro));
        var delOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otro);

        var r = await MarcarAsync(delOtro, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ------------------------------------------------------------------------------------------------ conflictos

    [Fact]
    public async Task Un_hallazgo_que_no_es_v1_plus_responde_409()
    {
        var e = await UnidadConV1PlusAsync(nivel: "v2");

        var r = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("V1+", Mensaje(await ComoJsonAsync(r)));
    }

    [Fact]
    public async Task Un_hallazgo_de_otro_vin_o_de_otro_viaje_o_inexistente_responde_409()
    {
        var e = await UnidadConV1PlusAsync();
        var (_, etapasB) = await Sembrador.VehiculoConRutaAsync("VIN-NS-B", viajeId: e.Viaje);
        var (_, hallazgoDeB) = await Sembrador.InformeCerradoConHallazgoAsync(etapasB[0], e.Inspector.UsuarioId, e.Zona, nivel: "v1_plus");
        var (_, etapasOtroViaje) = await Sembrador.VehiculoConRutaAsync("VIN-NS-C");
        var (_, hallazgoDeOtroViaje) = await Sembrador.InformeCerradoConHallazgoAsync(etapasOtroViaje[0], e.Inspector.UsuarioId, e.Zona, nivel: "v1_plus");

        var otroVin = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = hallazgoDeB });
        var otroViaje = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = hallazgoDeOtroViaje });
        var inexistente = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = Guid.NewGuid() });

        Assert.All(new[] { otroVin, otroViaje, inexistente }, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Contains("no es de ese VIN en ese viaje", Mensaje(await ComoJsonAsync(otroVin)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE EstadoUnidad = 'no_sale'"));
    }

    [Fact]
    public async Task Si_la_inspeccion_del_hallazgo_sigue_en_borrador_responde_409()
    {
        var e = await UnidadConV1PlusAsync(cerrada: false);

        var r = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("borrador", Mensaje(await ComoJsonAsync(r)));
    }

    [Fact]
    public async Task Una_unidad_que_ya_no_sale_no_se_marca_otra_vez()
    {
        var e = await UnidadConV1PlusAsync();
        var primera = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });

        var repetida = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Otra vez", hallazgo_id = e.Hallazgo });

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Contains("ya está marcada", Mensaje(await ComoJsonAsync(repetida)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgoSeguimiento WHERE Accion = 'no_sale'"));
    }

    [Fact]
    public async Task El_vin_de_reemplazo_en_un_viaje_activo_o_ya_en_este_viaje_responde_409()
    {
        var e = await UnidadConV1PlusAsync();
        await Sembrador.VehiculoConRutaAsync("VIN-NS-ACTIVO");
        await Sembrador.VehiculoConRutaAsync("VIN-NS-MISMO", viajeId: e.Viaje);

        var enActivo = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo, vin_reemplazo = "VIN-NS-ACTIVO" });
        var enEsteViaje = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo, vin_reemplazo = "VIN-NS-MISMO" });

        Assert.Equal(HttpStatusCode.Conflict, enActivo.StatusCode);
        Assert.Contains("otro viaje activo", Mensaje(await ComoJsonAsync(enActivo)));
        Assert.Equal(HttpStatusCode.Conflict, enEsteViaje.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE EstadoUnidad = 'no_sale'"));
    }

    [Fact]
    public async Task Un_reemplazo_que_antes_no_salio_solo_entra_si_su_hallazgo_v1_plus_esta_recuperado()
    {
        var e = await UnidadConV1PlusAsync();
        // VIN-NS-P no salio de otro viaje (ya finalizado) por un hallazgo V1+.
        var (previo, etapasPrevio) = await Sembrador.VehiculoConRutaAsync("VIN-NS-P", inspectorId: e.Inspector.UsuarioId);
        var viajePrevio = await Sembrador.ViajeDeEtapaAsync(etapasPrevio[0]);
        var (_, hallazgoPrevio) = await Sembrador.InformeCerradoConHallazgoAsync(etapasPrevio[0], e.Inspector.UsuarioId, e.Zona, nivel: "v1_plus");
        await Sembrador.UnidadNoSaleAsync(viajePrevio, previo, hallazgoPrevio, e.Inspector.UsuarioId, "Golpe en otro viaje");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = 'finalizado' WHERE Id = @v", ("@v", viajePrevio));
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var sinRecuperar = await MarcarAsync(coordinador, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo, vin_reemplazo = "VIN-NS-P" });
        var recuperar = await Cliente(coordinador.Token).PostAsJsonAsync($"/api/hallazgos/{hallazgoPrevio}/seguimiento", new { estado = "recuperado" });
        var recuperado = await MarcarAsync(coordinador, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo, vin_reemplazo = "VIN-NS-P" });

        Assert.Equal(HttpStatusCode.Conflict, sinRecuperar.StatusCode);
        Assert.Contains("no está recuperado", Mensaje(await ComoJsonAsync(sinRecuperar)));
        Assert.Equal(HttpStatusCode.OK, recuperar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recuperado.StatusCode);
        Assert.Equal("VIN-NS-P", Unidad(await ComoJsonAsync(recuperado), "VIN-NS-A").GetProperty("no_sale").GetProperty("vin_reemplazo").GetString());
    }

    // --------------------------------------------------------------------------------------- datos invalidos

    [Fact]
    public async Task Sin_motivo_o_con_un_hallazgo_invalido_o_un_reemplazo_inexistente_responde_400()
    {
        var e = await UnidadConV1PlusAsync();

        var sinMotivo = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = " ", hallazgo_id = e.Hallazgo });
        var motivoLargo = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = new string('m', 501), hallazgo_id = e.Hallazgo });
        var sinHallazgo = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe" });
        var reemplazoInexistente = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo, vin_reemplazo = "NO-EXISTE" });
        var reemplazoIgual = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo, vin_reemplazo = "VIN-NS-A" });

        async Task<bool> TieneCampo(HttpResponseMessage r, string campo) =>
            r.StatusCode == HttpStatusCode.BadRequest && (await ComoJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty(campo, out _);
        Assert.True(await TieneCampo(sinMotivo, "motivo"));
        Assert.True(await TieneCampo(motivoLargo, "motivo"));
        Assert.True(await TieneCampo(sinHallazgo, "hallazgo_id"));
        Assert.True(await TieneCampo(reemplazoInexistente, "vin_reemplazo"));
        Assert.True(await TieneCampo(reemplazoIgual, "vin_reemplazo"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE EstadoUnidad = 'no_sale'"));
    }

    [Fact]
    public async Task Un_vin_que_no_esta_en_el_viaje_o_un_viaje_inexistente_responde_404()
    {
        var e = await UnidadConV1PlusAsync();
        await Sembrador.VehiculoAsync("VIN-NS-SUELTO");

        var vinAjeno = await MarcarAsync(e.Inspector, e.Viaje, "VIN-NS-SUELTO", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });
        var vinInexistente = await MarcarAsync(e.Inspector, e.Viaje, "NO-EXISTE", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });
        var viajeInexistente = await MarcarAsync(e.Inspector, Guid.NewGuid(), "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });

        Assert.Equal(HttpStatusCode.NotFound, vinAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, vinInexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viajeInexistente.StatusCode);
    }

    // ----------------------------------------------------------------------------------- trazabilidad

    [Fact]
    public async Task La_unidad_que_no_salio_se_puede_asignar_a_otro_viaje_y_su_inventario_conserva_el_historial_completo()
    {
        var e = await UnidadConV1PlusAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        await MarcarAsync(coordinador, e.Viaje, "VIN-NS-A", new { motivo = "Golpe", hallazgo_id = e.Hallazgo });
        var otroViaje = await Sembrador.ViajeAsync();

        var asignada = await Cliente(coordinador.Token).PostAsJsonAsync($"/api/viajes/{otroViaje}/vehiculos", new { vines = new[] { "VIN-NS-A" } });

        Assert.Equal(HttpStatusCode.OK, asignada.StatusCode);
        var inventario = await ComoJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/vehiculos/VIN-NS-A/inventario"));
        var etapas = inventario.GetProperty("etapas").EnumerateArray().ToList();
        Assert.Equal(6, etapas.Count);
        Assert.Equal(3, etapas.Count(x => x.GetProperty("estado_unidad").GetString() == "no_sale"));
        Assert.Equal(3, etapas.Count(x => x.GetProperty("estado_unidad").GetString() == "activa"));
        var delViajeOriginal = etapas.First(x => x.GetProperty("estado_unidad").GetString() == "no_sale");
        Assert.Equal(e.Hallazgo, delViajeOriginal.GetProperty("no_sale").GetProperty("hallazgo_id").GetGuid());
        Assert.Equal("v1_plus", delViajeOriginal.GetProperty("inspecciones")[0].GetProperty("hallazgos")[0].GetProperty("nivel_severidad").GetString());
    }
}
