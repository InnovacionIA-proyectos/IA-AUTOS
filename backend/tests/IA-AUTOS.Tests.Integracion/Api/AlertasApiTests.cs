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
/// Las alertas preventivas (hallazgo critico al cerrar, patron por umbral) y los avisos operativos (captura incompleta, transferencia pendiente): a quien llega cada una, una sola vez, con los
/// parametros vigentes y sin correo.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class AlertasApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-10-21T08:30:00-05:00";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly clsEnviadorPushFalso _push = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Un viaje con su conductor y su inspector, un coordinador y un administrador del cliente, y la unidad <c>VIN-ALR-0001</c> con su ruta.</summary>
    private sealed record Escenario(Guid Viaje, Guid Vehiculo, Guid[] Etapas, Sesion Conductor, Sesion Inspector, Sesion Coordinador, Sesion Admin);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _api = CrearFabrica(servicios: s => s.AddSingleton<IEnviadorPush>(_push));
    }

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await _api.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1", _api), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token, _api);

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync(string vin = "VIN-ALR-0001", bool conInspector = true)
    {
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (vehiculo, etapas) = await Sembrador.VehiculoConRutaAsync(vin, conductorId: conductor.UsuarioId, inspectorId: conInspector ? inspector.UsuarioId : null);
        return new Escenario(await Sembrador.ViajeDeEtapaAsync(etapas[0]), vehiculo, etapas, conductor, inspector, coordinador, admin);
    }

    /// <summary>Las notificaciones no se borran (son evidencia): la prueba apaga sus triggers solo para empezar de cero.</summary>
    private Task BorrarNotificacionesAsync() =>
        Sembrador.EjecutarAsync("ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL; DELETE FROM dbo.tblNotificaciones; ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL");

    private Task<int> NotificacionesAsync(string tipo, Guid? usuario = null, string canal = "bandeja") =>
        Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = @t AND Canal = @c AND (@u IS NULL OR UsuarioId = @u)",
            ("@t", tipo), ("@c", canal), ("@u", (object?)usuario ?? DBNull.Value));

    private async Task<T> ConProcesosAsync<T>(Func<clsAlertasPeriodicas, Task<T>> accion)
    {
        using var alcance = _api.Services.CreateScope();
        return await accion(alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>());
    }

    /// <summary>Envejece una inspeccion: cuando se inicio o cuando se cerro (los triggers de inmutabilidad se apagan solo para esto).</summary>
    private Task EnvejecerAsync(Guid inspeccion, string columna, int minutos) =>
        Sembrador.EjecutarAsync(
            $"ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL; UPDATE dbo.tblInspecciones SET {columna} = DATEADD(MINUTE, -@m, SYSDATETIMEOFFSET()) WHERE Id = @i; ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL",
            ("@m", minutos), ("@i", inspeccion));

    private async Task<Guid> ZonaFrenteAsync() => (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Numero = 50"))!;

    // ------------------------------------------------------------------------------ hallazgo critico

    /// <summary>El inspector abre, llena y cierra la salida de Transporte de la unidad con los hallazgos de esos niveles (cada uno con su foto).</summary>
    private async Task<HttpResponseMessage> CerrarTransporteConHallazgosAsync(Escenario e, params string[] niveles)
    {
        await Sembrador.InformeAsync(e.Etapas[0], e.Inspector.UsuarioId, cerrada: true, momento: "llegada");
        var inspeccion = await ComoJsonAsync(await Como(e.Inspector).PostAsJsonAsync("/api/inspecciones", new { etapa_id = e.Etapas[1], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1 }));
        var id = inspeccion.GetProperty("id").GetGuid();
        foreach (var nivel in niveles)
        {
            var subida = await ComoJsonAsync(await Como(e.Inspector).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" }));
            var creado = await Como(e.Inspector).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
            {
                cuadrante_numero = 50,
                clase_dano = "golpe",
                tamano_dano = "1_5cm",
                nivel_severidad = nivel,
                evidencia = new { storage_path = subida.GetProperty("storage_path").GetString(), sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, hash_sha256 = subida.GetProperty("hash_sha256").GetString() },
            });
            Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        }

        return await Como(e.Inspector).PostAsync($"/api/inspecciones/{id}/cerrar", null);
    }

    [Fact]
    public async Task Al_cerrar_una_inspeccion_con_un_hallazgo_critico_alerta_al_coordinador_y_al_administrador_del_cliente()
    {
        var e = await EscenarioAsync();
        await Como(e.Coordinador).PostAsJsonAsync("/api/dispositivos", new { token_fcm = "tk-coord", plataforma = "android" });

        var r = await CerrarTransporteConHallazgosAsync(e, "v1_plus");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        foreach (var destinatario in new[] { e.Coordinador, e.Admin })
        {
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'hallazgo_critico' AND Urgencia = 'alta' AND Canal = 'bandeja' AND UsuarioId = @u AND ViajeId = @v",
                ("@u", destinatario.UsuarioId), ("@v", e.Viaje)));
            Assert.Equal(1, await Sembrador.ContarAsync(
                "SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'hallazgo_critico' AND Canal = 'push' AND UsuarioId = @u", ("@u", destinatario.UsuarioId)));
        }

        Assert.Equal(0, await NotificacionesAsync("hallazgo_critico", e.Inspector.UsuarioId) + await NotificacionesAsync("hallazgo_critico", e.Conductor.UsuarioId));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Canal = 'correo'"));
        Assert.Contains(_push.Enviados, x => x.Token == "tk-coord" && x.Mensaje.Datos["tipo"] == "hallazgo_critico");
    }

    [Fact]
    public async Task Dos_hallazgos_criticos_de_la_misma_inspeccion_dan_una_sola_alerta()
    {
        var e = await EscenarioAsync();

        await CerrarTransporteConHallazgosAsync(e, "v1_plus", "v1", "v3");

        Assert.Equal(1, await NotificacionesAsync("hallazgo_critico", e.Coordinador.UsuarioId));
        Assert.Equal(1, await NotificacionesAsync("hallazgo_critico", e.Admin.UsuarioId));
    }

    [Fact]
    public async Task Un_hallazgo_que_no_es_critico_no_alerta()
    {
        var e = await EscenarioAsync();

        var r = await CerrarTransporteConHallazgosAsync(e, "v2");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, await NotificacionesAsync("hallazgo_critico"));
    }

    [Fact]
    public async Task Los_niveles_criticos_son_un_parametro_que_se_cambia_sin_desplegar()
    {
        var e = await EscenarioAsync();
        await CambiarParametroAsync("niveles_criticos", "v2", _api);

        var r = await CerrarTransporteConHallazgosAsync(e, "v2");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1, await NotificacionesAsync("hallazgo_critico", e.Coordinador.UsuarioId));
        Assert.Equal(1, await NotificacionesAsync("hallazgo_critico", e.Admin.UsuarioId));
    }

    [Fact]
    public async Task La_alerta_del_hallazgo_critico_no_llega_a_otro_cliente()
    {
        var e = await EscenarioAsync();
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);

        await CerrarTransporteConHallazgosAsync(e, "v1_plus");

        Assert.Equal(0, await NotificacionesAsync("hallazgo_critico", coordinadorOtro.UsuarioId));
        Assert.Equal(1, await NotificacionesAsync("hallazgo_critico", e.Coordinador.UsuarioId));
    }

    // ----------------------------------------------------------------------------------- umbral de patron

    /// <summary>Un hallazgo en el frente en una inspeccion cerrada de la etapa y el momento indicados (con la antiguedad de su cierre).</summary>
    private async Task HallazgoCerradoAsync(Guid etapa, Guid usuario, string momento, int minutosDesdeElCierre = 0)
    {
        var (inspeccion, _) = await Sembrador.InformeCerradoConHallazgoAsync(etapa, usuario, await ZonaFrenteAsync(), momento: momento);
        if (minutosDesdeElCierre > 0)
        {
            await EnvejecerAsync(inspeccion, "CerradaAt", minutosDesdeElCierre);
        }
    }

    [Fact]
    public async Task Al_llegar_al_umbral_una_zona_y_un_proceso_alertan_una_sola_vez_por_ventana()
    {
        var e = await EscenarioAsync();
        await CambiarParametroAsync("umbral_patron_cantidad", "2", _api);
        await HallazgoCerradoAsync(e.Etapas[0], e.Inspector.UsuarioId, "salida");
        await HallazgoCerradoAsync(e.Etapas[0], e.Inspector.UsuarioId, "llegada");
        await HallazgoCerradoAsync(e.Etapas[1], e.Inspector.UsuarioId, "salida");

        var primera = await ConProcesosAsync(p => p.EjecutarUmbralesAsync());
        var segunda = await ConProcesosAsync(p => p.EjecutarUmbralesAsync());

        // El frente suma 3 y el cargue 2 (llegan al umbral de 2); el transporte tiene 1 y no alerta.
        Assert.Equal(4, primera);
        Assert.Equal(4, segunda);
        foreach (var destinatario in new[] { e.Coordinador, e.Admin })
        {
            Assert.Equal(2, await NotificacionesAsync("umbral_patron", destinatario.UsuarioId));
            Assert.Equal(2, await NotificacionesAsync("umbral_patron", destinatario.UsuarioId, "push"));
        }

        var mensajes = (await ComoJsonAsync(await Como(e.Coordinador).GetAsync("/api/notificaciones?tipo=umbral_patron"))).GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("mensaje").GetString()).ToList();
        Assert.Contains(mensajes, m => m!.StartsWith("La zona 50 acumula 3 hallazgos en los últimos 30 días (umbral 2)", StringComparison.Ordinal));
        Assert.Contains(mensajes, m => m!.StartsWith("El proceso cargue acumula 2 hallazgos", StringComparison.Ordinal));
        Assert.All(await ComoJsonListaAsync(e.Coordinador), n => Assert.Equal("media", n.GetProperty("urgencia").GetString()));
        Assert.Equal(0, await NotificacionesAsync("umbral_patron", e.Inspector.UsuarioId) + await NotificacionesAsync("umbral_patron", e.Conductor.UsuarioId));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Canal = 'correo'"));
    }

    private async Task<List<JsonElement>> ComoJsonListaAsync(Sesion quien) =>
        (await ComoJsonAsync(await Como(quien).GetAsync("/api/notificaciones?tipo=umbral_patron"))).GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Por_debajo_del_umbral_no_alerta_y_el_umbral_es_un_parametro()
    {
        var e = await EscenarioAsync();
        await HallazgoCerradoAsync(e.Etapas[0], e.Inspector.UsuarioId, "salida");
        await HallazgoCerradoAsync(e.Etapas[0], e.Inspector.UsuarioId, "llegada");

        var conUmbralDeDiez = await ConProcesosAsync(p => p.EjecutarUmbralesAsync());
        Assert.Equal(0, conUmbralDeDiez);
        Assert.Equal(0, await NotificacionesAsync("umbral_patron"));

        // Cambiado el parametro, una instancia que lee de nuevo (la lectura se guarda un minuto) alerta.
        await CambiarParametroAsync("umbral_patron_cantidad", "2", _api);
        _api = CrearFabrica(servicios: s => s.AddSingleton<IEnviadorPush>(_push));
        Assert.Equal(4, await ConProcesosAsync(p => p.EjecutarUmbralesAsync()));
    }

    [Fact]
    public async Task Los_hallazgos_fuera_de_la_ventana_y_los_de_otro_cliente_no_cuentan()
    {
        var e = await EscenarioAsync();
        await CambiarParametroAsync("umbral_patron_cantidad", "3", _api);
        await HallazgoCerradoAsync(e.Etapas[0], e.Inspector.UsuarioId, "salida");
        await HallazgoCerradoAsync(e.Etapas[0], e.Inspector.UsuarioId, "llegada");
        // Un tercero de hace 40 dias: fuera de la ventana de 30.
        await HallazgoCerradoAsync(e.Etapas[1], e.Inspector.UsuarioId, "salida", minutosDesdeElCierre: 40 * 24 * 60);
        // Dos de otro cliente en la misma zona y proceso: no se mezclan con los de este.
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var (_, etapasOtro) = await Sembrador.VehiculoConRutaAsync("VIN-ALR-OTRO", conductorId: e.Conductor.UsuarioId);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET ClienteId = @c WHERE Vin = 'VIN-ALR-OTRO'", ("@c", otroCliente));
        await HallazgoCerradoAsync(etapasOtro[0], e.Inspector.UsuarioId, "salida");
        await HallazgoCerradoAsync(etapasOtro[0], e.Inspector.UsuarioId, "llegada");

        var emitidas = await ConProcesosAsync(p => p.EjecutarUmbralesAsync());

        Assert.Equal(0, emitidas);
        Assert.Equal(0, await NotificacionesAsync("umbral_patron"));
    }

    [Fact]
    public async Task Un_hallazgo_de_otro_cliente_alerta_solo_al_coordinador_de_su_cliente()
    {
        var e = await EscenarioAsync();
        await CambiarParametroAsync("umbral_patron_cantidad", "2", _api);
        var otroCliente = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otroCliente));
        var coordinadorOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, otroCliente);
        var (_, etapasOtro) = await Sembrador.VehiculoConRutaAsync("VIN-ALR-OTRO", conductorId: e.Conductor.UsuarioId);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET ClienteId = @c WHERE Vin = 'VIN-ALR-OTRO'", ("@c", otroCliente));
        await HallazgoCerradoAsync(etapasOtro[0], e.Inspector.UsuarioId, "salida");
        await HallazgoCerradoAsync(etapasOtro[0], e.Inspector.UsuarioId, "llegada");

        await ConProcesosAsync(p => p.EjecutarUmbralesAsync());

        Assert.Equal(2, await NotificacionesAsync("umbral_patron", coordinadorOtro.UsuarioId));
        Assert.Equal(0, await NotificacionesAsync("umbral_patron", e.Coordinador.UsuarioId));
    }

    // ------------------------------------------------------------------------------ captura incompleta

    private Task<Guid> BorradorAsync(Guid etapa, Guid usuario, string momento = "salida", int minutosAbierta = 40) =>
        Sembrador.InformeAsync(etapa, usuario, cerrada: false, momento: momento).ContinueWith(async t =>
        {
            await EnvejecerAsync(t.Result, "IniciadaAt", minutosAbierta);
            return t.Result;
        }).Unwrap();

    [Fact]
    public async Task Una_captura_abierta_mas_del_tiempo_sin_todas_las_vistas_avisa_a_quien_la_inicio_una_sola_vez()
    {
        var e = await EscenarioAsync();
        var inspeccion = await BorradorAsync(e.Etapas[0], e.Inspector.UsuarioId);

        var primera = await ConProcesosAsync(p => p.EjecutarAvisosAsync());
        var segunda = await ConProcesosAsync(p => p.EjecutarAvisosAsync());

        Assert.Equal(1, primera);
        Assert.Equal(1, segunda);
        Assert.Equal(1, await NotificacionesAsync("captura_incompleta", e.Inspector.UsuarioId));
        Assert.Equal(1, await NotificacionesAsync("captura_incompleta", e.Inspector.UsuarioId, "push"));
        var aviso = (await ComoJsonAsync(await Como(e.Inspector).GetAsync("/api/notificaciones?tipo=captura_incompleta"))).GetProperty("items").EnumerateArray().Single();
        Assert.Equal("media", aviso.GetProperty("urgencia").GetString());
        Assert.Equal(inspeccion, aviso.GetProperty("inspeccion_id").GetGuid());
        Assert.Equal("VIN-ALR-0001", aviso.GetProperty("vin").GetString());
        Assert.Contains("Frontal", aviso.GetProperty("mensaje").GetString());
        // Es un aviso operativo: no llega al coordinador ni al administrador.
        Assert.Equal(0, await NotificacionesAsync("captura_incompleta", e.Coordinador.UsuarioId) + await NotificacionesAsync("captura_incompleta", e.Admin.UsuarioId));
    }

    [Fact]
    public async Task No_avisa_si_la_captura_es_reciente_esta_completa_esta_cerrada_o_no_es_de_cargue_ni_descargue()
    {
        var e = await EscenarioAsync();
        await BorradorAsync(e.Etapas[0], e.Inspector.UsuarioId, "salida", minutosAbierta: 10);
        // Completa: la foto de cada vista obligatoria.
        var completa = await BorradorAsync(e.Etapas[0], e.Inspector.UsuarioId, "llegada");
        foreach (var (vista, _) in await Sembrador.VistasAsync(obligatoria: true))
        {
            await Sembrador.EjecutarAsync(
                "INSERT dbo.tblEvidencias (Id, InspeccionId, VistaId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), @i, @v, @p, SYSDATETIMEOFFSET(), 4.6, -74.1)",
                ("@i", completa), ("@v", vista), ("@p", $"vista-{vista}.png"));
        }

        var cerrada = await Sembrador.InformeAsync(e.Etapas[2], e.Inspector.UsuarioId, cerrada: true);
        await EnvejecerAsync(cerrada, "IniciadaAt", 120);
        await BorradorAsync(e.Etapas[1], e.Inspector.UsuarioId, "salida");

        var emitidas = await ConProcesosAsync(p => p.EjecutarAvisosAsync());

        Assert.Equal(0, emitidas);
        Assert.Equal(0, await NotificacionesAsync("captura_incompleta"));
    }

    [Fact]
    public async Task El_conductor_solo_recibe_el_aviso_de_la_salida_del_cargue_que_el_inicio()
    {
        var e = await EscenarioAsync();
        var delCargue = await BorradorAsync(e.Etapas[0], e.Conductor.UsuarioId);
        await BorradorAsync(e.Etapas[2], e.Conductor.UsuarioId);

        await ConProcesosAsync(p => p.EjecutarAvisosAsync());

        // Del Descargue que inicio el conductor no hay aviso.
        Assert.Equal(1, await NotificacionesAsync("captura_incompleta", e.Conductor.UsuarioId));
        Assert.Equal(delCargue, (Guid)(await Sembrador.EscalarAsync("SELECT InspeccionId FROM dbo.tblNotificaciones WHERE Tipo = 'captura_incompleta' AND Canal = 'bandeja'"))!);

        // Ya avisado: volver a correr el proceso no repite el aviso.
        await ConProcesosAsync(p => p.EjecutarAvisosAsync());
        Assert.Equal(1, await NotificacionesAsync("captura_incompleta", e.Conductor.UsuarioId));
    }

    [Fact]
    public async Task Los_minutos_de_la_captura_incompleta_son_un_parametro()
    {
        var e = await EscenarioAsync();
        await BorradorAsync(e.Etapas[0], e.Inspector.UsuarioId, minutosAbierta: 20);
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));

        await CambiarParametroAsync("captura_incompleta_minutos", "15", _api);
        _api = CrearFabrica(servicios: s => s.AddSingleton<IEnviadorPush>(_push));

        Assert.Equal(1, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));
    }

    [Fact]
    public async Task Un_viaje_finalizado_o_un_usuario_retirado_no_reciben_avisos()
    {
        var e = await EscenarioAsync();
        await BorradorAsync(e.Etapas[0], e.Inspector.UsuarioId);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = 'finalizado' WHERE Id = @v", ("@v", e.Viaje));
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));

        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = 'en_curso' WHERE Id = @v", ("@v", e.Viaje));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblUsuarios SET Activo = 0 WHERE Id = @u", ("@u", e.Inspector.UsuarioId));
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));
    }

    // ------------------------------------------------------------------------- transferencia pendiente

    private async Task<Guid> LlegadaCerradaAsync(Guid etapa, Guid usuario, int minutosDesdeElCierre = 300)
    {
        var inspeccion = await Sembrador.InformeAsync(etapa, usuario, cerrada: true, momento: "llegada");
        await EnvejecerAsync(inspeccion, "CerradaAt", minutosDesdeElCierre);
        return inspeccion;
    }

    [Fact]
    public async Task Sin_transferencia_el_aviso_va_al_conductor_si_sigue_el_transporte_y_al_inspector_si_sigue_el_descargue()
    {
        var e = await EscenarioAsync();
        var cargue = await LlegadaCerradaAsync(e.Etapas[0], e.Inspector.UsuarioId);
        var transporte = await LlegadaCerradaAsync(e.Etapas[1], e.Conductor.UsuarioId);

        var primera = await ConProcesosAsync(p => p.EjecutarAvisosAsync());
        var segunda = await ConProcesosAsync(p => p.EjecutarAvisosAsync());

        Assert.Equal(2, primera);
        Assert.Equal(2, segunda);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'transferencia_pendiente' AND Canal = 'bandeja' AND InspeccionId = @i AND UsuarioId = @u", ("@i", cargue), ("@u", e.Conductor.UsuarioId)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'transferencia_pendiente' AND Canal = 'bandeja' AND InspeccionId = @i AND UsuarioId = @u", ("@i", transporte), ("@u", e.Inspector.UsuarioId)));
        var aviso = (await ComoJsonAsync(await Como(e.Conductor).GetAsync("/api/notificaciones?tipo=transferencia_pendiente"))).GetProperty("items").EnumerateArray().Single();
        Assert.Equal("media", aviso.GetProperty("urgencia").GetString());
        Assert.Equal("VIN-ALR-0001", aviso.GetProperty("vin").GetString());
        Assert.Equal(0, await NotificacionesAsync("transferencia_pendiente", e.Coordinador.UsuarioId) + await NotificacionesAsync("transferencia_pendiente", e.Admin.UsuarioId));
    }

    [Fact]
    public async Task Antes_del_descargue_sin_inspector_el_aviso_va_al_conductor()
    {
        var e = await EscenarioAsync(conInspector: false);
        var transporte = await LlegadaCerradaAsync(e.Etapas[1], e.Conductor.UsuarioId);

        await ConProcesosAsync(p => p.EjecutarAvisosAsync());

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'transferencia_pendiente' AND Canal = 'bandeja' AND InspeccionId = @i AND UsuarioId = @u", ("@i", transporte), ("@u", e.Conductor.UsuarioId)));
    }

    [Fact]
    public async Task Con_transferencia_el_aviso_va_al_firmante_que_falta()
    {
        var e = await EscenarioAsync();
        var cargue = await LlegadaCerradaAsync(e.Etapas[0], e.Inspector.UsuarioId);
        // Solo firmo quien entrega (el inspector): falta quien recibe (el conductor).
        await Sembrador.TransferenciaAsync(cargue, e.Inspector.UsuarioId, e.Conductor.UsuarioId, tipoCierre: null);

        await ConProcesosAsync(p => p.EjecutarAvisosAsync());

        Assert.Equal(1, await NotificacionesAsync("transferencia_pendiente", e.Conductor.UsuarioId));
        Assert.Equal(0, await NotificacionesAsync("transferencia_pendiente", e.Inspector.UsuarioId));
    }

    [Fact]
    public async Task Si_faltan_las_dos_firmas_avisa_a_las_dos_partes()
    {
        var e = await EscenarioAsync();
        var cargue = await LlegadaCerradaAsync(e.Etapas[0], e.Inspector.UsuarioId);
        await Sembrador.TransferenciaAsync(cargue, e.Inspector.UsuarioId, e.Conductor.UsuarioId, tipoCierre: null);
        await Sembrador.EjecutarAsync("ALTER TABLE dbo.tblTransferenciasCustodia DISABLE TRIGGER ALL; UPDATE dbo.tblTransferenciasCustodia SET FirmaEntrega = NULL, FirmaEntregaAt = NULL; ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL");

        var emitidas = await ConProcesosAsync(p => p.EjecutarAvisosAsync());

        Assert.Equal(2, emitidas);
        Assert.Equal(1, await NotificacionesAsync("transferencia_pendiente", e.Inspector.UsuarioId));
        Assert.Equal(1, await NotificacionesAsync("transferencia_pendiente", e.Conductor.UsuarioId));
    }

    [Theory]
    [InlineData("firmada")]
    [InlineData("con_reservas")]
    [InlineData("no_firma")]
    public async Task Una_transferencia_cerrada_de_cualquier_forma_no_avisa(string cierre)
    {
        var e = await EscenarioAsync();
        var cargue = await LlegadaCerradaAsync(e.Etapas[0], e.Inspector.UsuarioId);
        await Sembrador.TransferenciaAsync(cargue, e.Inspector.UsuarioId, e.Conductor.UsuarioId, cierre, testigoId: e.Coordinador.UsuarioId);

        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));
    }

    [Fact]
    public async Task No_avisa_de_una_llegada_reciente_de_una_unidad_que_no_sale_ni_de_la_ultima_etapa_y_los_minutos_son_un_parametro()
    {
        var e = await EscenarioAsync();
        await LlegadaCerradaAsync(e.Etapas[0], e.Inspector.UsuarioId, minutosDesdeElCierre: 60);
        // La llegada del Descargue no tiene etapa siguiente: no hay a quien avisar.
        await LlegadaCerradaAsync(e.Etapas[2], e.Inspector.UsuarioId);
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));

        await CambiarParametroAsync("transferencia_pendiente_minutos", "30", _api);
        _api = CrearFabrica(servicios: s => s.AddSingleton<IEnviadorPush>(_push));
        Assert.Equal(1, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));

        // Una unidad que no sale del viaje deja de avisarse.
        await BorrarNotificacionesAsync();
        var (_, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[1], e.Inspector.UsuarioId, await ZonaFrenteAsync(), nivel: "v1_plus");
        await Sembrador.UnidadNoSaleAsync(e.Viaje, e.Vehiculo, hallazgo, e.Coordinador.UsuarioId, "Golpe");
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAvisosAsync()));
    }

    // ----------------------------------------------------------------------------- el proceso periodico

    [Fact]
    public async Task El_proceso_periodico_corre_al_arrancar_y_crea_los_avisos_pendientes()
    {
        var e = await EscenarioAsync();
        var inspeccion = await BorradorAsync(e.Etapas[0], e.Inspector.UsuarioId);
        var conProceso = CrearFabrica(new Dictionary<string, string?> { ["Alertas:ProcesosActivos"] = "true" }, s => s.AddSingleton<IEnviadorPush>(_push));

        // Con la aplicacion arriba, el proceso revisa de inmediato lo pendiente.
        _ = conProceso.Server;
        var fin = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < fin && await NotificacionesAsync("captura_incompleta", e.Inspector.UsuarioId) == 0)
        {
            await Task.Delay(500);
        }

        Assert.Equal(1, await NotificacionesAsync("captura_incompleta", e.Inspector.UsuarioId));
        Assert.Equal(inspeccion, (Guid)(await Sembrador.EscalarAsync("SELECT InspeccionId FROM dbo.tblNotificaciones WHERE Tipo = 'captura_incompleta' AND Canal = 'bandeja'"))!);
    }
}
