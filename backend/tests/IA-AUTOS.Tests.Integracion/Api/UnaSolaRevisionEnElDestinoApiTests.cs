using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Una sola revision en el destino y el viaje se cierra cuando termina: la salida del Descargue ya no se crea ni se corrige (409 <c>MOMENTO_NO_ADMITIDO</c>), un borrador que quedo abierto sigue pudiendose cerrar, «Llegue» sin
/// novedades deja la salida de Transporte, el viaje se finaliza al entregar el ultimo carro y a mano solo con motivo cuando quedan carros pendientes.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed class UnaSolaRevisionEnElDestinoApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-11-20T08:30:00-05:00";

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    /// <summary>Un viaje en curso con su conductor y su inspector y la unidad <c>VIN-UNA-0001</c> con su ruta (cargue, transporte y descargue).</summary>
    private sealed record Esc(Guid Viaje, Guid[] Etapas, Sesion Conductor, Sesion Inspector, Sesion Coordinador);

    private async Task<Esc> EscenarioAsync(string estado = "en_curso")
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var viaje = await Sembrador.ViajeAsync(conductor.UsuarioId, inspector.UsuarioId, estado);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-UNA-0001", viajeId: viaje);
        return new Esc(viaje, etapas, conductor, inspector, coordinador);
    }

    private Task<HttpResponseMessage> CrearInspeccionAsync(Sesion quien, Guid etapa, string momento) =>
        Cliente(quien.Token).PostAsJsonAsync("/api/inspecciones", new Dictionary<string, object?> { ["etapa_id"] = etapa, ["momento"] = momento, ["fecha"] = FECHA, ["lat"] = 4.6, ["lng"] = -74.1 });

    private Task<HttpResponseMessage> CambiarEstadoAsync(Sesion quien, Guid viaje, object cuerpo) =>
        Cliente(quien.Token).PatchAsync($"/api/viajes/{viaje}/estado", Cuerpo(cuerpo));

    // ------------------------------------------------------------------------------------ la salida del descargue

    [Fact]
    public async Task La_salida_del_descargue_se_rechaza_para_el_inspector_y_para_el_conductor_con_el_motivo_y_no_queda_guardada()
    {
        var e = await EscenarioAsync();

        foreach (var quien in new[] { e.Inspector, e.Conductor })
        {
            var r = await CrearInspeccionAsync(quien, e.Etapas[2], "salida");

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("MOMENTO_NO_ADMITIDO", error.GetProperty("code").GetString());
            Assert.Equal("salida_descargue", error.GetProperty("details").GetProperty("motivo").GetString());
        }

        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e", ("@e", e.Etapas[2])));
    }

    [Fact]
    public async Task La_llegada_del_descargue_y_las_dos_revisiones_del_cargue_y_del_transporte_se_siguen_creando()
    {
        var e = await EscenarioAsync();
        await Sembrador.InformeAsync(e.Etapas[1], e.Conductor.UsuarioId, cerrada: true, momento: "llegada");

        Assert.Equal(HttpStatusCode.Created, (await CrearInspeccionAsync(e.Inspector, e.Etapas[2], "llegada")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CrearInspeccionAsync(e.Inspector, e.Etapas[0], "llegada")).StatusCode);
    }

    [Fact]
    public async Task Corregir_una_salida_del_descargue_cerrada_de_antes_tambien_se_rechaza()
    {
        var e = await EscenarioAsync();
        var cerrada = await Sembrador.InformeAsync(e.Etapas[2], e.Inspector.UsuarioId, cerrada: true, momento: "salida");

        var correccion = await Cliente(e.Inspector.Token).PostAsync($"/api/inspecciones/{cerrada}/corregir", Cuerpo(new { }));

        Assert.Equal(HttpStatusCode.Conflict, correccion.StatusCode);
        var error = await JsonAsync(correccion);
        Assert.Equal("MOMENTO_NO_ADMITIDO", error.GetProperty("code").GetString());
        Assert.Equal("salida_descargue", error.GetProperty("details").GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task Una_salida_del_descargue_que_quedo_abierta_antes_del_cambio_se_puede_cerrar_sin_exigir_las_vistas()
    {
        var e = await EscenarioAsync();
        var borrador = await Sembrador.InformeAsync(e.Etapas[2], e.Conductor.UsuarioId, cerrada: false, momento: "salida");

        var cierre = await Cliente(e.Conductor.Token).PostAsync($"/api/inspecciones/{borrador}/cerrar", Cuerpo(new { }));

        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
    }

    // ------------------------------------------------------------------------------------ «Llegue» sin novedades

    [Fact]
    public async Task Llegue_sin_novedades_crea_y_cierra_la_salida_de_transporte_con_la_fecha_y_el_gps_del_llegue_y_la_llegada_a_la_misma_hora()
    {
        var e = await EscenarioAsync();

        var r = await Cliente(e.Conductor.Token).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-UNA-0001/llegada", Cuerpo(new { fecha = "2026-11-20T14:10:00-05:00", lat = 4.9, lng = -74.4 }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await JsonAsync(r);
        Assert.Equal(0, cuerpo.GetProperty("novedades").GetInt32());
        Assert.True(cuerpo.GetProperty("sin_novedad").GetBoolean());
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'salida' AND Estado = 'cerrada' AND Lat = 4.9 AND Lng = -74.4 AND Fecha = CAST('2026-11-20T14:10:00-05:00' AS datetimeoffset)", ("@e", e.Etapas[1])));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'llegada' AND Estado = 'cerrada' AND Lat = 4.9 AND Lng = -74.4", ("@e", e.Etapas[1])));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'salida' AND CerradaAt IS NOT NULL", ("@e", e.Etapas[1])));
    }

    [Fact]
    public async Task Llegue_con_una_novedad_cierra_la_salida_que_ya_estaba_abierta_y_no_crea_otra()
    {
        var e = await EscenarioAsync();
        await Sembrador.InformeAsync(e.Etapas[1], e.Conductor.UsuarioId, cerrada: false, momento: "salida");

        var r = await Cliente(e.Conductor.Token).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-UNA-0001/llegada", Cuerpo(new { fecha = "2026-11-20T14:10:00-05:00", lat = 4.9, lng = -74.4 }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'salida'", ("@e", e.Etapas[1])));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'salida' AND Estado = 'cerrada'", ("@e", e.Etapas[1])));
    }

    [Fact]
    public async Task Llegue_repetido_responde_llegada_ya_cerrada_y_no_crea_otra_salida()
    {
        var e = await EscenarioAsync();
        var cuerpo = Cuerpo(new { fecha = "2026-11-20T14:10:00-05:00", lat = 4.9, lng = -74.4 });
        Assert.Equal(HttpStatusCode.OK, (await Cliente(e.Conductor.Token).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-UNA-0001/llegada", cuerpo)).StatusCode);

        var repetido = await Cliente(e.Conductor.Token).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/VIN-UNA-0001/llegada", Cuerpo(new { fecha = "2026-11-20T14:11:00-05:00", lat = 4.9, lng = -74.4 }));

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("LLEGADA_YA_CERRADA", (await JsonAsync(repetido)).GetProperty("code").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Momento = 'salida'", ("@e", e.Etapas[1])));
    }

    // ------------------------------------------------------------------------------------ finalizar a mano

    [Fact]
    public async Task Finalizar_a_mano_con_carros_pendientes_sin_motivo_se_rechaza_y_el_viaje_sigue_en_curso()
    {
        var e = await EscenarioAsync();

        var r = await CambiarEstadoAsync(e.Coordinador, e.Viaje, new { estado = "finalizado" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errores = (await JsonAsync(r)).GetProperty("details").GetProperty("fieldErrors");
        Assert.Contains("El motivo es obligatorio para finalizar un viaje con carros pendientes", errores.GetProperty("motivo").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal("en_curso", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
        // Un motivo en blanco es lo mismo que no mandarlo.
        Assert.Equal(HttpStatusCode.BadRequest, (await CambiarEstadoAsync(e.Coordinador, e.Viaje, new { estado = "finalizado", motivo = "   " })).StatusCode);
    }

    [Fact]
    public async Task Finalizar_a_mano_con_motivo_finaliza_el_viaje_y_deja_en_la_auditoria_el_motivo_con_el_estado_de_antes_y_el_de_despues()
    {
        var e = await EscenarioAsync();

        var r = await CambiarEstadoAsync(e.Coordinador, e.Viaje, new { estado = "finalizado", motivo = "El cliente retiró el carro del patio" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("finalizado", (await JsonAsync(r)).GetProperty("estado").GetString());
        var auditoria = (string)(await Sembrador.EscalarAsync("SELECT TOP 1 Detalle FROM dbo.tblAuditLog WHERE Accion = 'viaje.estado.finalizado' AND ViajeId = @v", ("@v", e.Viaje)))!;
        Assert.Contains("en_curso", auditoria);
        Assert.Contains("finalizado", auditoria);
        Assert.Contains("El cliente retir", auditoria);
    }

    [Fact]
    public async Task El_motivo_para_finalizar_admite_hasta_300_caracteres()
    {
        var e = await EscenarioAsync();

        var largo = await CambiarEstadoAsync(e.Coordinador, e.Viaje, new { estado = "finalizado", motivo = new string('m', 301) });
        Assert.Equal(HttpStatusCode.BadRequest, largo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CambiarEstadoAsync(e.Coordinador, e.Viaje, new { estado = "finalizado", motivo = new string('m', 300) })).StatusCode);
    }

    [Fact]
    public async Task Sin_carros_pendientes_el_motivo_es_opcional_y_cancelar_nunca_lo_pide()
    {
        var sinPendientes = await EscenarioAsync();
        var llegada = await Sembrador.InformeAsync(sinPendientes.Etapas[2], sinPendientes.Inspector.UsuarioId, cerrada: true, momento: "llegada");
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaRecibeAt, TipoCierre, FirmadaAt) VALUES (NEWID(), @i, @a, @b, N'f1', N'f2', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 'firmada', SYSDATETIMEOFFSET())",
            ("@i", llegada), ("@a", sinPendientes.Conductor.UsuarioId), ("@b", sinPendientes.Inspector.UsuarioId));

        var r = await CambiarEstadoAsync(sinPendientes.Coordinador, sinPendientes.Viaje, new { estado = "finalizado" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Cancelar_un_viaje_con_carros_pendientes_no_pide_motivo()
    {
        var e = await EscenarioAsync();

        Assert.Equal(HttpStatusCode.OK, (await CambiarEstadoAsync(e.Coordinador, e.Viaje, new { estado = "cancelado" })).StatusCode);
    }

    [Fact]
    public async Task Finalizar_a_mano_solo_lo_hacen_el_coordinador_el_administrador_y_el_super_admin()
    {
        var e = await EscenarioAsync();
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        Assert.Equal(HttpStatusCode.Forbidden, (await CambiarEstadoAsync(e.Inspector, e.Viaje, new { estado = "finalizado", motivo = "Ya entregué todo" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CambiarEstadoAsync(e.Conductor, e.Viaje, new { estado = "finalizado", motivo = "Ya entregué todo" })).StatusCode);
        Assert.Equal("en_curso", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", e.Viaje)));
        Assert.Equal(HttpStatusCode.BadRequest, (await CambiarEstadoAsync(admin, e.Viaje, new { estado = "finalizado" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CambiarEstadoAsync(superAdmin, e.Viaje, new { estado = "finalizado" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CambiarEstadoAsync(superAdmin, e.Viaje, new { estado = "finalizado", motivo = "Cierre por soporte" })).StatusCode);
    }
}
