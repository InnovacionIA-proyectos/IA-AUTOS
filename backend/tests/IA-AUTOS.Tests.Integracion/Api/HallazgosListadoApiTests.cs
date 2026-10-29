using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Listado y detalle de los hallazgos: paginado, con filtros, con el contexto de su inspeccion, y visible solo para el inspector de sus viajes y para quien se limita a su
/// cliente; el conductor no los consulta.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed class HallazgosListadoApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    /// <summary>
    /// Seis hallazgos en dos viajes de dos clientes. Viaje de Sofasa (VIN-L-1): H1 V1 en el frente (cargue, con foto), H2 V2 en izquierdo 7 (transporte), H3 novedad del conductor sin revisar
    /// (descargue), H4 novedad del conductor revisada V3 (cargue, llegada) y H6 en una inspeccion en borrador (transporte, llegada). Viaje del otro cliente (VIN-L-2): H5 V1+.
    /// </summary>
    private sealed record Escenario(
        Sesion Inspector, Sesion Conductor, Guid Otro, Guid ViajeSofasa, Guid ViajeOtro,
        Guid H1, Guid H2, Guid H3, Guid H4, Guid H5, Guid H6, Guid InspeccionH1, Guid InspeccionH2);

    private async Task<Escenario> EscenarioAsync()
    {
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var otro = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, 'otro', N'Otro cliente', 2)", ("@id", otro));
        await Sembrador.CuadranteAsync(7, "Puerta delantera");
        var izquierdo7 = await Sembrador.ZonaAsync(7, "izquierdo");
        var frente = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!;

        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-L-1", conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        var (inspeccionH1, h1) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], inspector.UsuarioId, frente, nivel: "v1", conFoto: true);
        await Task.Delay(25);
        var (inspeccionH2, h2) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[1], inspector.UsuarioId, izquierdo7, clase: "rotura", nivel: "v2");
        await Task.Delay(25);
        var (_, h3) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[2], inspector.UsuarioId, frente, nivel: null, origen: "conductor");
        await Task.Delay(25);
        var (_, h4) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], inspector.UsuarioId, frente, momento: "llegada", nivel: "v3", origen: "conductor");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblHallazgos SET RevisadoAt = SYSDATETIMEOFFSET(), RevisadoPor = @u WHERE Id = @h", ("@u", inspector.UsuarioId), ("@h", h4));
        var (_, h6) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[1], inspector.UsuarioId, frente, momento: "llegada", nivel: "v2", cerrada: false);

        var (_, etapasOtro) = await Sembrador.VehiculoConRutaAsync("VIN-L-2");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET ClienteId = @c WHERE Vin = 'VIN-L-2'", ("@c", otro));
        var (_, h5) = await Sembrador.InformeCerradoConHallazgoAsync(etapasOtro[0], inspector.UsuarioId, frente, nivel: "v1_plus", conFoto: true);

        return new Escenario(
            inspector, conductor, otro, await Sembrador.ViajeDeEtapaAsync(etapas[0]), await Sembrador.ViajeDeEtapaAsync(etapasOtro[0]),
            h1, h2, h3, h4, h5, h6, inspeccionH1, inspeccionH2);
    }

    private async Task<(JsonElement Cuerpo, List<Guid> Ids)> ListarAsync(Sesion quien, string consulta = "")
    {
        var r = await Cliente(quien.Token).GetAsync("/api/hallazgos" + consulta);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await ComoJsonAsync(r);
        return (cuerpo, cuerpo.GetProperty("items").EnumerateArray().Select(h => h.GetProperty("id").GetGuid()).ToList());
    }

    // ------------------------------------------------------------------------------------------- forma

    [Fact]
    public async Task El_listado_es_una_pagina_y_cada_fila_trae_el_hallazgo_con_el_contexto_de_su_inspeccion()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var (cuerpo, ids) = await ListarAsync(coordinador, "?orden=antiguos");

        Assert.Equal(["items", "total", "pagina", "tamano"], cuerpo.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(5, cuerpo.GetProperty("total").GetInt32());
        Assert.Equal(1, cuerpo.GetProperty("pagina").GetInt32());
        Assert.Equal(50, cuerpo.GetProperty("tamano").GetInt32());
        Assert.Equal([e.H1, e.H2, e.H3, e.H4, e.H6], ids);
        var fila = cuerpo.GetProperty("items")[0];
        var nombres = fila.EnumerateObject().Select(p => p.Name).ToList();
        foreach (var campo in new[]
        {
            "id", "inspeccion_id", "clase_dano", "tamano_dano", "cuadrante_numero", "lado", "zona_provisional", "zona", "nivel_severidad", "nivel_nombre", "nivel_accion", "origen", "nota",
            "es_preexistente", "estado_seguimiento", "evidencia_completa", "evidencia_faltante", "vin", "viaje_id", "codigo_mtrack", "proceso", "momento", "inspeccion_estado", "cerrada_at", "evidencia",
        })
        {
            Assert.Contains(campo, nombres);
        }

        Assert.Equal("VIN-L-1", fila.GetProperty("vin").GetString());
        Assert.Equal(e.ViajeSofasa, fila.GetProperty("viaje_id").GetGuid());
        Assert.StartsWith("MTK-S", fila.GetProperty("codigo_mtrack").GetString());
        Assert.Equal("cargue", fila.GetProperty("proceso").GetString());
        Assert.Equal("salida", fila.GetProperty("momento").GetString());
        Assert.Equal("cerrada", fila.GetProperty("inspeccion_estado").GetString());
        Assert.Equal(JsonValueKind.String, fila.GetProperty("cerrada_at").ValueKind);
        Assert.Equal(e.InspeccionH1, fila.GetProperty("inspeccion_id").GetGuid());
        Assert.Equal("Frente", fila.GetProperty("zona").GetString());
        // La foto propia viene como una evidencia con su URL; un hallazgo sin foto trae nulo.
        Assert.Equal(e.H1, fila.GetProperty("evidencia").GetProperty("hallazgo_id").GetGuid());
        Assert.StartsWith("http://localhost/api/uploads/hallazgo-", fila.GetProperty("evidencia").GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, cuerpo.GetProperty("items")[1].GetProperty("evidencia").ValueKind);
        // Una inspeccion en borrador no tiene fecha de cierre.
        var enBorrador = cuerpo.GetProperty("items").EnumerateArray().Single(h => h.GetProperty("id").GetGuid() == e.H6);
        Assert.Equal("borrador", enBorrador.GetProperty("inspeccion_estado").GetString());
        Assert.Equal(JsonValueKind.Null, enBorrador.GetProperty("cerrada_at").ValueKind);
    }

    [Fact]
    public async Task La_pagina_y_el_tamano_se_respetan_y_el_tamano_no_pasa_de_200()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var (primera, idsPrimera) = await ListarAsync(coordinador, "?tamano=2&pagina=1&orden=antiguos");
        var (segunda, idsSegunda) = await ListarAsync(coordinador, "?tamano=2&pagina=2&orden=antiguos");
        var (ultima, idsUltima) = await ListarAsync(coordinador, "?tamano=2&pagina=3&orden=antiguos");
        var (enorme, _) = await ListarAsync(coordinador, "?tamano=1000");
        var (minimo, _) = await ListarAsync(coordinador, "?tamano=0&pagina=0");

        Assert.Equal(2, idsPrimera.Count);
        Assert.Equal(2, idsSegunda.Count);
        Assert.Single(idsUltima);
        Assert.Empty(idsPrimera.Intersect(idsSegunda));
        Assert.Equal(5, primera.GetProperty("total").GetInt32());
        Assert.Equal(2, segunda.GetProperty("pagina").GetInt32());
        Assert.Equal(3, ultima.GetProperty("pagina").GetInt32());
        Assert.Equal(200, enorme.GetProperty("tamano").GetInt32());
        Assert.Equal(1, minimo.GetProperty("tamano").GetInt32());
        Assert.Equal(1, minimo.GetProperty("pagina").GetInt32());
    }

    // ------------------------------------------------------------------------------------------- filtros

    [Fact]
    public async Task Los_filtros_por_estado_origen_revisado_nivel_y_zona_traen_solo_lo_que_coincide()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        await Cliente(coordinador.Token).PostAsJsonAsync($"/api/hallazgos/{e.H1}/seguimiento", new { estado = "rechazado" });

        Assert.Equal([e.H1], (await ListarAsync(coordinador, "?estado_seguimiento=rechazado")).Ids);
        Assert.Equal(4, (await ListarAsync(coordinador, "?estado_seguimiento=pendiente")).Ids.Count);
        Assert.Equal([e.H3, e.H4], (await ListarAsync(coordinador, "?origen=conductor&orden=antiguos")).Ids);
        Assert.Equal(new[] { e.H1, e.H2, e.H6 }.Order(), (await ListarAsync(coordinador, "?origen=inspector")).Ids.Order());
        // Revisado solo aplica a las novedades del conductor: pendientes de revisar o ya revisadas.
        Assert.Equal([e.H3], (await ListarAsync(coordinador, "?revisado=false")).Ids);
        Assert.Equal([e.H4], (await ListarAsync(coordinador, "?revisado=true")).Ids);
        Assert.Equal([e.H3], (await ListarAsync(coordinador, "?origen=conductor&revisado=false")).Ids);
        Assert.Empty((await ListarAsync(coordinador, "?origen=inspector&revisado=false")).Ids);
        Assert.Equal([e.H2, e.H6], (await ListarAsync(coordinador, "?nivel=v2&orden=antiguos")).Ids);
        Assert.Equal([e.H4], (await ListarAsync(coordinador, "?nivel=v3")).Ids);
        Assert.Equal([e.H2], (await ListarAsync(coordinador, "?cuadrante_numero=7")).Ids);
        Assert.Equal([e.H2], (await ListarAsync(coordinador, "?cuadrante_numero=7&lado=izquierdo")).Ids);
        Assert.Empty((await ListarAsync(coordinador, "?cuadrante_numero=7&lado=derecho")).Ids);
        Assert.Empty((await ListarAsync(coordinador, "?lado=derecho")).Ids);
    }

    [Fact]
    public async Task Los_filtros_por_viaje_vin_inspeccion_proceso_y_fecha_de_cierre_traen_solo_lo_que_coincide()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        Assert.Equal(5, (await ListarAsync(coordinador, $"?viaje_id={e.ViajeSofasa}")).Ids.Count);
        Assert.Empty((await ListarAsync(coordinador, $"?viaje_id={e.ViajeOtro}")).Ids);
        Assert.Equal(5, (await ListarAsync(coordinador, "?vin=vin-l-1")).Ids.Count);
        Assert.Empty((await ListarAsync(coordinador, "?vin=VIN-L-2")).Ids);
        Assert.Equal([e.H1], (await ListarAsync(coordinador, $"?inspeccion_id={e.InspeccionH1}")).Ids);
        Assert.Equal([e.H1, e.H4], (await ListarAsync(coordinador, "?proceso=cargue&orden=antiguos")).Ids);
        Assert.Equal([e.H3], (await ListarAsync(coordinador, "?proceso=descargue")).Ids);
        Assert.Empty((await ListarAsync(coordinador, "?proceso=inventado")).Ids);
        // desde y hasta son el cierre de la inspeccion: la que sigue en borrador no tiene cierre y no entra en un rango.
        var ayer = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("o"));
        var manana = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("o"));
        Assert.Equal(4, (await ListarAsync(coordinador, $"?desde={ayer}&hasta={manana}")).Ids.Count);
        Assert.Empty((await ListarAsync(coordinador, $"?desde={manana}")).Ids);
        Assert.Empty((await ListarAsync(coordinador, $"?hasta={ayer}")).Ids);
    }

    [Fact]
    public async Task El_orden_por_defecto_es_el_cierre_mas_reciente_y_tambien_hay_antiguos_y_severidad()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var recientes = (await ListarAsync(coordinador)).Ids;
        var antiguos = (await ListarAsync(coordinador, "?orden=antiguos")).Ids;
        var severidad = (await ListarAsync(coordinador, "?orden=severidad")).Ids;

        // H6 esta en borrador (sin cierre): se ordena por su creacion, la mas reciente.
        Assert.Equal([e.H6, e.H4, e.H3, e.H2, e.H1], recientes);
        Assert.Equal([e.H1, e.H2, e.H3, e.H4, e.H6], antiguos);
        // V1, luego los V2 (el mas reciente primero: H6 y luego H2), V3 y al final la novedad sin nivel.
        Assert.Equal([e.H1, e.H6, e.H2, e.H4, e.H3], severidad);
    }

    [Fact]
    public async Task Un_filtro_invalido_responde_400_con_todos_sus_errores_juntos()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var r = await Cliente(coordinador.Token).GetAsync("/api/hallazgos?estado_seguimiento=x&origen=y&revisado=quiza&viaje_id=no&inspeccion_id=no&cuadrante_numero=0&lado=arriba&nivel=v9&desde=ayer&orden=azar");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var campos = (await ComoJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            ["cuadrante_numero", "desde", "estado_seguimiento", "inspeccion_id", "lado", "nivel", "orden", "origen", "revisado", "viaje_id"],
            campos);
    }

    // -------------------------------------------------------------------------------------------- alcance

    [Fact]
    public async Task El_inspector_solo_ve_los_hallazgos_de_sus_viajes_y_el_conductor_recibe_403()
    {
        var e = await EscenarioAsync();
        var otroInspector = await SesionAsync("otro-inspector@ejemplo.com", clsRol.INSPECTOR);

        var (_, propios) = await ListarAsync(e.Inspector);
        var (_, ajenos) = await ListarAsync(otroInspector);
        var conductor = await Cliente(e.Conductor.Token).GetAsync("/api/hallazgos");
        var detalleConductor = await Cliente(e.Conductor.Token).GetAsync($"/api/hallazgos/{e.H1}");
        var detallePropio = await Cliente(e.Inspector.Token).GetAsync($"/api/hallazgos/{e.H1}");
        var detalleAjeno = await Cliente(otroInspector.Token).GetAsync($"/api/hallazgos/{e.H1}");
        var anonimo = await Cliente().GetAsync("/api/hallazgos");

        Assert.Equal(5, propios.Count);
        Assert.DoesNotContain(e.H5, propios);
        Assert.Empty(ajenos);
        Assert.Equal(HttpStatusCode.Forbidden, conductor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detalleConductor.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detallePropio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detalleAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
    }

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.ADMIN)]
    public async Task Quien_se_limita_a_su_cliente_solo_ve_los_hallazgos_de_su_cliente_y_pedir_otro_es_403(string rol)
    {
        var e = await EscenarioAsync();
        var delSofasa = await SesionAsync("sofasa@ejemplo.com", rol);
        var delOtro = await SesionAsync("otro@ejemplo.com", rol, e.Otro);
        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;

        var (_, deSofasa) = await ListarAsync(delSofasa);
        var (_, deOtro) = await ListarAsync(delOtro);
        var conSuCliente = await Cliente(delSofasa.Token).GetAsync($"/api/hallazgos?cliente_id={sofasa}");
        var conOtroCliente = await Cliente(delSofasa.Token).GetAsync($"/api/hallazgos?cliente_id={e.Otro}");
        var detalleAjeno = await Cliente(delSofasa.Token).GetAsync($"/api/hallazgos/{e.H5}");

        Assert.Equal(5, deSofasa.Count);
        Assert.Equal([e.H5], deOtro);
        Assert.Equal(HttpStatusCode.OK, conSuCliente.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, conOtroCliente.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detalleAjeno.StatusCode);
    }

    [Fact]
    public async Task El_super_administrador_ve_todos_los_clientes_y_es_el_unico_que_elige_uno()
    {
        var e = await EscenarioAsync();
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var sofasa = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblClientes WHERE Codigo = 'sofasa'"))!;

        var (todos, _) = await ListarAsync(superAdmin);
        var (delOtro, idsOtro) = await ListarAsync(superAdmin, $"?cliente_id={e.Otro}");
        var (deSofasa, _) = await ListarAsync(superAdmin, $"?cliente_id={sofasa}");

        Assert.Equal(6, todos.GetProperty("total").GetInt32());
        Assert.Equal([e.H5], idsOtro);
        Assert.Equal(1, delOtro.GetProperty("total").GetInt32());
        Assert.Equal(5, deSofasa.GetProperty("total").GetInt32());
    }

    // -------------------------------------------------------------------------------------------- detalle

    [Fact]
    public async Task El_detalle_es_la_misma_fila_con_el_historial_de_seguimiento_y_las_notas_aclaratorias_vacias()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        await Cliente(coordinador.Token).PostAsJsonAsync($"/api/hallazgos/{e.H1}/seguimiento", new { estado = "rechazado", nota = "Se rechaza" });

        var r = await Cliente(coordinador.Token).GetAsync($"/api/hallazgos/{e.H1}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var h = await ComoJsonAsync(r);
        Assert.Equal(e.H1, h.GetProperty("id").GetGuid());
        Assert.Equal("rechazado", h.GetProperty("estado_seguimiento").GetString());
        Assert.Equal("VIN-L-1", h.GetProperty("vin").GetString());
        Assert.Equal("cargue", h.GetProperty("proceso").GetString());
        Assert.Equal(e.H1, h.GetProperty("evidencia").GetProperty("hallazgo_id").GetGuid());
        var historial = h.GetProperty("seguimiento").EnumerateArray().ToList();
        Assert.Single(historial);
        Assert.Equal("rechazado", historial[0].GetProperty("estado_nuevo").GetString());
        Assert.Equal("Se rechaza", historial[0].GetProperty("nota").GetString());
        Assert.Equal(0, h.GetProperty("notas_aclaratorias").GetArrayLength());
        var sinCambios = await ComoJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/hallazgos/{e.H2}"));
        Assert.Equal(0, sinCambios.GetProperty("seguimiento").GetArrayLength());
    }

    [Fact]
    public async Task El_detalle_de_un_hallazgo_inexistente_es_404()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var r = await Cliente(coordinador.Token).GetAsync($"/api/hallazgos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
