using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Alertas;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Los puntos del inspector se administran desde el portal (ESC-01 y ESC-02) y la excepcion «No hay inspector» se pide cuando el punto de cargue tiene inspectores (ESC-04): el coordinador la concede o, si nadie
/// responde en 10 minutos, el reloj la concede marcada como autorizada por tiempo (ESC-05); donde no hay inspectores se activa en el momento (ESC-03) y el conductor con permiso cierra la salida del Cargue con las
/// posiciones de la niñera (ESC-06).
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed class PermisoSinInspectorApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-11-10T08:30:00-05:00";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>El viaje MT-PERMISO con una unidad cuyo cargue es en «Puerto de Cartagena», su conductor, su inspector y el coordinador y el administrador del cliente.</summary>
    private sealed record Escenario(Guid Viaje, Guid Lugar, Guid EtapaCargue, string Vin, Sesion Conductor, Sesion Inspector, Sesion Coordinador, Sesion Admin);

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

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<T> ConProcesosAsync<T>(Func<clsAlertasPeriodicas, Task<T>> accion)
    {
        using var alcance = _api.Services.CreateScope();
        return await accion(alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>());
    }

    private Task PuntoAsync(Sesion inspector, Guid lugar) =>
        Sembrador.EjecutarAsync("INSERT dbo.tblInspectorLugares (Id, UsuarioId, LugarId) VALUES (NEWID(), @u, @l)", ("@u", inspector.UsuarioId), ("@l", lugar));

    private async Task<Escenario> EscenarioAsync(bool conInspectoresEnElPunto = true)
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var lugar = await LugarRealAsync(Como(coordinador), "Puerto de Cartagena", "cedi_2", "Cartagena");
        await Sembrador.VehiculoAsync("VIN-PERMISO-1");
        var tipo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;
        var creado = await Como(coordinador).PostAsync(
            "/api/viajes",
            Cuerpo(new
            {
                codigo_mtrack = "MT-PERMISO", tipo_unidad_id = tipo, placa = "ABC123", conductor_id = conductor.UsuarioId, fecha_programada = FECHA, lugar_cargue_id = lugar,
                unidades = new[] { new { vin = "VIN-PERMISO-1", lugar_destino_id = lugar } },
            }));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var viaje = (await JsonAsync(creado)).GetProperty("id").GetGuid();
        if (conInspectoresEnElPunto)
        {
            await PuntoAsync(inspector, lugar);
        }

        var etapa = (Guid)(await Sembrador.EscalarAsync(
            "SELECT e.Id FROM dbo.tblEtapas e JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId WHERE e.ViajeId = @v AND c.Codigo = 'cargue'", ("@v", viaje)))!;
        return new Escenario(viaje, lugar, etapa, "VIN-PERMISO-1", conductor, inspector, coordinador, admin);
    }

    private Task<HttpResponseMessage> PedirAsync(Sesion quien, Guid viaje, object? cuerpo = null) =>
        Como(quien).PostAsync($"/api/viajes/{viaje}/permiso-sin-inspector", Cuerpo(cuerpo ?? new { motivo_sin_inspector = "inspector_no_llego", fecha = FECHA, lat = 10.4, lng = -75.5 }));

    private Task<HttpResponseMessage> ConcederAsync(Sesion quien, Guid viaje) => Como(quien).PostAsync($"/api/viajes/{viaje}/permiso-sin-inspector/conceder", null);

    private Task<HttpResponseMessage> ExcepcionAsync(Sesion quien, Guid etapa) =>
        Como(quien).PostAsJsonAsync(
            "/api/inspecciones",
            new Dictionary<string, object?>
            {
                ["etapa_id"] = etapa, ["momento"] = "salida", ["fecha"] = FECHA, ["lat"] = 10.4, ["lng"] = -75.5, ["motivo_sin_inspector"] = "inspector_no_llego",
            });

    private Task<int> NotificacionesDeAsync(Guid usuario) =>
        Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'permiso_sin_inspector' AND Urgencia = 'alta' AND Canal = 'bandeja' AND UsuarioId = @u", ("@u", usuario));

    private Task EnvejecerPermisoAsync(Guid viaje, int minutos) =>
        Sembrador.EjecutarAsync("UPDATE dbo.tblPermisosSinInspector SET CreatedAt = DATEADD(MINUTE, -@m, SYSDATETIMEOFFSET()) WHERE ViajeId = @v", ("@m", minutos), ("@v", viaje));

    // ------------------------------------------------------------------------------------------------ los puntos del inspector

    [Fact]
    public async Task El_coordinador_asigna_y_quita_puntos_a_un_inspector_y_queda_en_la_auditoria()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var inspector = await SesionAsync("luis@ejemplo.com", clsRol.INSPECTOR);
        var envigado = await LugarRealAsync(Como(coordinador), "CEDI Envigado", "cedi_1", "Envigado");
        var cartagena = await LugarRealAsync(Como(coordinador), "Puerto de Cartagena", "cedi_2", "Cartagena");

        var r = await Como(coordinador).PostAsync($"/api/usuarios/{inspector.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { envigado, cartagena } }));

        // ESC-01: queda con sus dos puntos (por nombre) y se puede consultar.
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var puntos = (await JsonAsync(r)).GetProperty("puntos").EnumerateArray().ToList();
        Assert.Equal(["CEDI Envigado", "Puerto de Cartagena"], puntos.Select(p => p.GetProperty("lugar").GetString()).ToList());
        Assert.Equal(["Envigado", "Cartagena"], puntos.Select(p => p.GetProperty("ciudad").GetString()).ToList());
        Assert.Equal(2, (await JsonAsync(await Como(coordinador).GetAsync($"/api/usuarios/{inspector.UsuarioId}/puntos"))).GetProperty("puntos").GetArrayLength());
        Assert.Equal(2, (await JsonAsync(await Como(inspector).GetAsync($"/api/usuarios/{inspector.UsuarioId}/puntos"))).GetProperty("puntos").GetArrayLength());
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspectorLugares WHERE UsuarioId = @u", ("@u", inspector.UsuarioId)));
        // CA-4: la asignacion deja quien y cuando.
        Assert.Equal(
            1,
            await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'usuario.puntos_asignar' AND EntidadId = @u AND UsuarioId = @c AND CreatedAt IS NOT NULL", ("@u", inspector.UsuarioId), ("@c", coordinador.UsuarioId)));

        // Repetir un punto no cambia nada ni audita de nuevo; agregar uno nuevo conserva los anteriores.
        var otra = await Como(coordinador).PostAsync($"/api/usuarios/{inspector.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { envigado } }));
        Assert.Equal(2, (await JsonAsync(otra)).GetProperty("puntos").GetArrayLength());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'usuario.puntos_asignar'"));

        var quitar = await Como(coordinador).DeleteAsync($"/api/usuarios/{inspector.UsuarioId}/puntos/{envigado}");
        Assert.Equal(HttpStatusCode.OK, quitar.StatusCode);
        Assert.Equal(["Puerto de Cartagena"], (await JsonAsync(quitar)).GetProperty("puntos").EnumerateArray().Select(p => p.GetProperty("lugar").GetString()).ToList());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'usuario.punto_quitar' AND EntidadId = @u AND UsuarioId = @c", ("@u", inspector.UsuarioId), ("@c", coordinador.UsuarioId)));
        Assert.Equal(HttpStatusCode.NotFound, (await Como(coordinador).DeleteAsync($"/api/usuarios/{inspector.UsuarioId}/puntos/{envigado}")).StatusCode);
    }

    [Fact]
    public async Task Quien_no_tiene_permiso_no_asigna_y_un_punto_o_un_usuario_que_no_existe_se_rechaza()
    {
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var inspector = await SesionAsync("luis@ejemplo.com", clsRol.INSPECTOR);
        var otro = await SesionAsync("otro@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var envigado = await LugarRealAsync(Como(coordinador), "CEDI Envigado", "cedi_1", "Envigado");

        // ESC-02: el inspector no se asigna a si mismo ni a otro, ni el conductor.
        var propio = await Como(inspector).PostAsync($"/api/usuarios/{inspector.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { envigado } }));
        var deOtro = await Como(inspector).PostAsync($"/api/usuarios/{otro.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { envigado } }));
        var comoConductor = await Como(conductor).PostAsync($"/api/usuarios/{inspector.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { envigado } }));
        var quitarPropio = await Como(inspector).DeleteAsync($"/api/usuarios/{inspector.UsuarioId}/puntos/{envigado}");
        var leerAjeno = await Como(inspector).GetAsync($"/api/usuarios/{otro.UsuarioId}/puntos");
        Assert.All(new[] { propio, deOtro, comoConductor, quitarPropio, leerAjeno }, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspectorLugares"));

        // CA-2: usuario inexistente 404; lugar inexistente, inactivo o lista vacia, y un usuario que no es inspector, 400.
        var sinUsuario = await Como(admin).PostAsync($"/api/usuarios/{Guid.NewGuid()}/puntos", Cuerpo(new { lugar_ids = new[] { envigado } }));
        var sinLugar = await Como(admin).PostAsync($"/api/usuarios/{inspector.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { Guid.NewGuid() } }));
        var vacia = await Como(admin).PostAsync($"/api/usuarios/{inspector.UsuarioId}/puntos", Cuerpo(new { lugar_ids = Array.Empty<Guid>() }));
        var noInspector = await Como(admin).PostAsync($"/api/usuarios/{conductor.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { envigado } }));
        Assert.Equal(HttpStatusCode.NotFound, sinUsuario.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinLugar.StatusCode);
        Assert.True((await JsonAsync(sinLugar)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("lugar_ids", out _));
        Assert.Equal(HttpStatusCode.BadRequest, vacia.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noInspector.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspectorLugares"));

        // El administrador si asigna, y quien no esta autenticado recibe 401.
        Assert.Equal(HttpStatusCode.OK, (await Como(admin).PostAsync($"/api/usuarios/{inspector.UsuarioId}/puntos", Cuerpo(new { lugar_ids = new[] { envigado } }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente(null, _api).GetAsync($"/api/usuarios/{inspector.UsuarioId}/puntos")).StatusCode);
    }

    // ------------------------------------------------------------------------------- la excepcion donde no hay inspectores

    [Fact]
    public async Task En_un_punto_sin_inspectores_el_conductor_activa_la_excepcion_en_el_momento_sin_permiso()
    {
        var e = await EscenarioAsync(conInspectoresEnElPunto: false);

        var pedir = await PedirAsync(e.Conductor, e.Viaje);
        var excepcion = await ExcepcionAsync(e.Conductor, e.EtapaCargue);

        // ESC-03: no hay nada que pedir; la excepcion se activa como hasta ahora y la novedad no lleva permiso.
        Assert.Equal(HttpStatusCode.Conflict, pedir.StatusCode);
        Assert.Equal(HttpStatusCode.Created, excepcion.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso WHERE Tipo = 'cargue_sin_inspector' AND PermisoId IS NULL"));
        Assert.Equal(HttpStatusCode.NotFound, (await Como(e.Conductor).GetAsync($"/api/viajes/{e.Viaje}/permiso-sin-inspector")).StatusCode);
    }

    // ------------------------------------------------------------------------------- pedir, conceder y activar

    [Fact]
    public async Task Con_inspectores_en_el_punto_el_conductor_pide_el_permiso_se_avisa_el_coordinador_lo_concede_y_el_conductor_queda_habilitado()
    {
        var e = await EscenarioAsync();

        // Sin permiso la excepcion se rechaza y dice que hay que pedirlo.
        var sinPermiso = await ExcepcionAsync(e.Conductor, e.EtapaCargue);
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        Assert.Contains("pida el permiso", (await JsonAsync(sinPermiso)).GetProperty("message").GetString());

        var pedido = await PedirAsync(e.Conductor, e.Viaje);

        // ESC-04: la solicitud queda pendiente con su motivo, su momento y su GPS, y vence a los 10 minutos.
        Assert.Equal(HttpStatusCode.Created, pedido.StatusCode);
        var permiso = await JsonAsync(pedido);
        Assert.Equal("pendiente", permiso.GetProperty("estado").GetString());
        Assert.Equal("Puerto de Cartagena", permiso.GetProperty("lugar").GetString());
        Assert.Equal("inspector_no_llego", permiso.GetProperty("motivo_codigo").GetString());
        Assert.Equal(10.4, permiso.GetProperty("lat").GetDouble());
        Assert.Equal(permiso.GetProperty("solicitado_at").GetDateTimeOffset().AddMinutes(10), permiso.GetProperty("vence_at").GetDateTimeOffset());
        Assert.False(permiso.GetProperty("autorizado_por_tiempo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, permiso.GetProperty("concedido_por").ValueKind);

        // Le llega al inspector del punto y al coordinador y administrador del cliente (urgencia alta); al conductor no. Repetir la solicitud no la duplica.
        foreach (var destinatario in new[] { e.Inspector, e.Coordinador, e.Admin })
        {
            Assert.Equal(1, await NotificacionesDeAsync(destinatario.UsuarioId));
        }

        Assert.Equal(0, await NotificacionesDeAsync(e.Conductor.UsuarioId));
        Assert.Equal(permiso.GetProperty("id").GetGuid(), (await JsonAsync(await PedirAsync(e.Conductor, e.Viaje))).GetProperty("id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector"));
        // Mientras esta pendiente la excepcion sigue rechazada.
        Assert.Equal(HttpStatusCode.Forbidden, (await ExcepcionAsync(e.Conductor, e.EtapaCargue)).StatusCode);
        Assert.Equal("pendiente", (await JsonAsync(await Como(e.Conductor).GetAsync($"/api/viajes/{e.Viaje}/permiso-sin-inspector"))).GetProperty("estado").GetString());

        // El inspector no concede; el coordinador si, y el conductor queda habilitado de inmediato (CA-6).
        Assert.Equal(HttpStatusCode.Forbidden, (await ConcederAsync(e.Inspector, e.Viaje)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ConcederAsync(e.Conductor, e.Viaje)).StatusCode);
        var concedido = await ConcederAsync(e.Coordinador, e.Viaje);
        Assert.Equal(HttpStatusCode.OK, concedido.StatusCode);
        var dto = await JsonAsync(concedido);
        Assert.Equal("concedido", dto.GetProperty("estado").GetString());
        Assert.Equal(e.Coordinador.UsuarioId, dto.GetProperty("concedido_por").GetGuid());
        Assert.False(dto.GetProperty("autorizado_por_tiempo").GetBoolean());
        Assert.Equal(1, await NotificacionesDeAsync(e.Conductor.UsuarioId));
        Assert.Equal(HttpStatusCode.Conflict, (await ConcederAsync(e.Admin, e.Viaje)).StatusCode);

        var excepcion = await ExcepcionAsync(e.Conductor, e.EtapaCargue);
        Assert.Equal(HttpStatusCode.Created, excepcion.StatusCode);

        // CA-8: la novedad de proceso guarda quien pidio, cuando y desde donde, y quien concedio.
        var viaje = await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}"));
        var novedad = Assert.Single(viaje.GetProperty("novedades_proceso").EnumerateArray());
        var delPermiso = novedad.GetProperty("permiso");
        Assert.Equal("Nombre conductor@ejemplo.com", delPermiso.GetProperty("solicitado_por_nombre").GetString());
        Assert.Equal(10.4, delPermiso.GetProperty("solicitado_lat").GetDouble());
        Assert.Equal(-75.5, delPermiso.GetProperty("solicitado_lng").GetDouble());
        Assert.Equal("Nombre coord@ejemplo.com", delPermiso.GetProperty("concedido_por_nombre").GetString());
        Assert.False(delPermiso.GetProperty("autorizado_por_tiempo").GetBoolean());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNovedadesProceso n JOIN dbo.tblPermisosSinInspector p ON p.Id = n.PermisoId WHERE p.ViajeId = @v", ("@v", e.Viaje)));
        // La auditoria: la solicitud, la concesion y la novedad con su permiso.
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'permiso_sin_inspector.solicitar' AND ViajeId = @v AND UsuarioId = @u", ("@v", e.Viaje), ("@u", e.Conductor.UsuarioId)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'permiso_sin_inspector.conceder' AND ViajeId = @v AND UsuarioId = @u", ("@v", e.Viaje), ("@u", e.Coordinador.UsuarioId)));
    }

    [Fact]
    public async Task Solo_el_conductor_del_viaje_pide_y_la_solicitud_valida_el_motivo_y_la_fecha()
    {
        var e = await EscenarioAsync();
        var otroConductor = await SesionAsync("otro@ejemplo.com", clsRol.CONDUCTOR);

        var ajeno = await PedirAsync(otroConductor, e.Viaje);
        var inspector = await PedirAsync(e.Inspector, e.Viaje);
        var sinNada = await PedirAsync(e.Conductor, e.Viaje, new { });
        var motivoInventado = await PedirAsync(e.Conductor, e.Viaje, new { motivo_sin_inspector = "inventado", fecha = FECHA });
        var sinTexto = await PedirAsync(e.Conductor, e.Viaje, new { motivo_sin_inspector = "otro", fecha = FECHA });
        var textoDeMas = await PedirAsync(e.Conductor, e.Viaje, new { motivo_sin_inspector = "inspector_no_llego", motivo_sin_inspector_texto = "sobra", fecha = FECHA });
        var sinZona = await PedirAsync(e.Conductor, e.Viaje, new { motivo_sin_inspector = "inspector_no_llego", fecha = "2026-11-10T08:30:00" });

        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, inspector.StatusCode);
        foreach (var r in new[] { sinNada, motivoInventado, sinTexto, textoDeMas, sinZona })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        var campos = (await JsonAsync(sinNada)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["fecha", "motivo_sin_inspector"], campos);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector"));
        Assert.Equal(HttpStatusCode.NotFound, (await ConcederAsync(e.Coordinador, e.Viaje)).StatusCode);
    }

    // ------------------------------------------------------------------------------------------------ el reloj

    [Fact]
    public async Task Sin_respuesta_el_reloj_concede_a_los_10_minutos_nunca_antes_y_lo_deja_marcado_como_autorizado_por_tiempo()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await PedirAsync(e.Conductor, e.Viaje)).StatusCode);

        // A los 9 minutos no concede (ni el proceso ni abrir la solicitud).
        await EnvejecerPermisoAsync(e.Viaje, 9);
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarPermisosSinInspectorAsync()));
        Assert.Equal("pendiente", (await JsonAsync(await Como(e.Conductor).GetAsync($"/api/viajes/{e.Viaje}/permiso-sin-inspector"))).GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await ExcepcionAsync(e.Conductor, e.EtapaCargue)).StatusCode);

        // Pasados los 10 minutos concede el proceso periodico, una sola vez, sin que nadie abra nada.
        await EnvejecerPermisoAsync(e.Viaje, 11);
        Assert.Equal(1, await ConProcesosAsync(p => p.EjecutarPermisosSinInspectorAsync()));
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarPermisosSinInspectorAsync()));
        var dto = await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}/permiso-sin-inspector"));
        Assert.Equal("concedido", dto.GetProperty("estado").GetString());
        Assert.True(dto.GetProperty("autorizado_por_tiempo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("concedido_por").ValueKind);
        Assert.True(dto.GetProperty("concedido_at").GetDateTimeOffset() >= dto.GetProperty("solicitado_at").GetDateTimeOffset().AddMinutes(10));

        // El conductor queda habilitado y la novedad de proceso lo marca como autorizado por tiempo.
        Assert.Equal(HttpStatusCode.Created, (await ExcepcionAsync(e.Conductor, e.EtapaCargue)).StatusCode);
        var novedad = Assert.Single((await JsonAsync(await Como(e.Coordinador).GetAsync($"/api/viajes/{e.Viaje}"))).GetProperty("novedades_proceso").EnumerateArray());
        Assert.True(novedad.GetProperty("permiso").GetProperty("autorizado_por_tiempo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, novedad.GetProperty("permiso").GetProperty("concedido_por_nombre").ValueKind);
        // Se avisa al conductor y al coordinador; la auditoria lo dice, sin usuario que concede.
        Assert.Equal(2, await NotificacionesDeAsync(e.Coordinador.UsuarioId));
        Assert.Equal(1, await NotificacionesDeAsync(e.Conductor.UsuarioId));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'permiso_sin_inspector.conceder_por_tiempo' AND ViajeId = @v", ("@v", e.Viaje)));
        Assert.Equal(HttpStatusCode.Conflict, (await ConcederAsync(e.Coordinador, e.Viaje)).StatusCode);
    }

    [Fact]
    public async Task Si_el_coordinador_responde_antes_el_reloj_no_cambia_la_concesion()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await PedirAsync(e.Conductor, e.Viaje)).StatusCode);
        await EnvejecerPermisoAsync(e.Viaje, 5);
        Assert.Equal(HttpStatusCode.OK, (await ConcederAsync(e.Coordinador, e.Viaje)).StatusCode);

        await EnvejecerPermisoAsync(e.Viaje, 30);
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarPermisosSinInspectorAsync()));

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblPermisosSinInspector WHERE Estado = 'concedido' AND PorTiempo = 0 AND ConcedidoPor = @c", ("@c", e.Coordinador.UsuarioId)));
    }

    // ------------------------------------------------------------------------------------------------ el conductor con permiso cierra el cargue

    [Fact]
    public async Task El_conductor_con_el_permiso_concedido_registra_las_posiciones_y_cierra_la_salida_del_cargue_y_sin_el_no_puede()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await PedirAsync(e.Conductor, e.Viaje)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConcederAsync(e.Coordinador, e.Viaje)).StatusCode);
        var creada = await ExcepcionAsync(e.Conductor, e.EtapaCargue);
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var inspeccion = (await JsonAsync(creada)).GetProperty("id").GetGuid();

        // ESC-06: acomoda la unidad y cierra, sin que se le pida ningun inspector.
        var posicion = await Como(e.Conductor).PutAsync($"/api/viajes/{e.Viaje}/vehiculos/{e.Vin}/posicion", Cuerpo(new { posicion = 3 }));
        Assert.Equal(HttpStatusCode.OK, posicion.StatusCode);
        var bytes = FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var subida = await JsonAsync(await Como(e.Conductor).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" }));
        var foto = await Como(e.Conductor).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccion}/evidencias",
            new { storage_path = subida.GetProperty("storage_path").GetString(), sello_fecha = FECHA, sello_lat = 10.4, sello_lng = -75.5, precision = 4.5, dispositivo_id = "tablet-1", hash_sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.Created, foto.StatusCode);
        var cierre = await Como(e.Conductor).PostAsync($"/api/inspecciones/{inspeccion}/cerrar", null);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal(3, Convert.ToInt32(await Sembrador.EscalarAsync("SELECT Posicion FROM dbo.tblViajeVehiculos WHERE ViajeId = @v", ("@v", e.Viaje))));
    }

    [Fact]
    public async Task El_conductor_sin_permiso_no_registra_posiciones()
    {
        var conPunto = await EscenarioAsync();
        var sinPermiso = await Como(conPunto.Conductor).PutAsync($"/api/viajes/{conPunto.Viaje}/vehiculos/{conPunto.Vin}/posicion", Cuerpo(new { posicion = 1 }));
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        Assert.Null(await Sembrador.EscalarAsync("SELECT Posicion FROM dbo.tblViajeVehiculos WHERE ViajeId = @v", ("@v", conPunto.Viaje)));
    }
}
