using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Alertas;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La ubicacion de los lugares: la carga manual por el catalogo (confirmada), «fijar ubicacion aqui» (pendiente de confirmar), la autocalibracion que propone centro y radio con las fotos selladas de varias visitas y que
/// el coordinador confirma o descarta, y el aviso de un cargue programado que nadie reclama.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_1)]
public sealed class UbicacionDeLugaresApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const double LAT = 6.1706;
    private const double LNG = -75.5874;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static double AlNorte(double metros) => LAT + (metros / clsConfirmacionSitio.RADIO_TIERRA_M * 180 / Math.PI);

    /// <summary>Los lugares se siembran antes del primer acceso a los catalogos: la API los guarda en cache la primera vez que se consultan.</summary>
    private async Task<Guid> LugarAsync(string nombre, string estado = "sin_ubicacion", double? lat = null, double? lng = null, int? radio = null)
    {
        var id = Guid.NewGuid();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad, Latitud, Longitud, RadioMetros, EstadoUbicacion) SELECT @id, @n, Id, N'Medellin', @lat, @lng, @r, @e FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1'",
            ("@id", id), ("@n", nombre), ("@lat", (object?)lat ?? DBNull.Value), ("@lng", (object?)lng ?? DBNull.Value), ("@r", (object?)radio ?? DBNull.Value), ("@e", estado));
        return id;
    }

    private async Task<JsonElement> LugarDelCatalogoAsync(Sesion quien, Guid id) =>
        (await JsonAsync(await Como(quien).GetAsync("/api/catalogos/lugares"))).GetProperty("items").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == id);

    private async Task<(string Estado, double? Lat, int? Radio)> EstadoDelLugarAsync(Guid id)
    {
        var fila = await Sembrador.EscalarAsync("SELECT CONCAT(EstadoUbicacion, '|', ISNULL(CAST(Latitud AS varchar(30)), ''), '|', ISNULL(CAST(RadioMetros AS varchar(10)), '')) FROM dbo.tblLugares WHERE Id = @i", ("@i", id));
        var partes = ((string)fila!).Split('|');
        return (partes[0], partes[1].Length == 0 ? null : double.Parse(partes[1], System.Globalization.CultureInfo.InvariantCulture), partes[2].Length == 0 ? null : int.Parse(partes[2], System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task<T> ConProcesosAsync<T>(Func<clsAlertasPeriodicas, Task<T>> accion)
    {
        using var alcance = Fabrica.Services.CreateScope();
        return await accion(alcance.ServiceProvider.GetRequiredService<clsAlertasPeriodicas>());
    }

    // ------------------------------------------------------------------------------------------------ la carga manual

    [Fact]
    public async Task El_coordinador_carga_la_ubicacion_de_un_lugar_y_queda_confirmada_con_su_autor()
    {
        var lugar = await LugarAsync("CEDI Envigado");
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        var r = await Como(diana).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { latitud = LAT, longitud = LNG, radio_metros = 1000 }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var valor = await JsonAsync(r);
        Assert.Equal(LAT, valor.GetProperty("latitud").GetDouble());
        Assert.Equal(LNG, valor.GetProperty("longitud").GetDouble());
        Assert.Equal(1000, valor.GetProperty("radio_metros").GetInt32());
        Assert.Equal("confirmada", valor.GetProperty("estado_ubicacion").GetString());
        Assert.Equal(diana.UsuarioId, valor.GetProperty("ubicacion_fijada_por").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, valor.GetProperty("ubicacion_fijada_at").ValueKind);
        Assert.Equal(("confirmada", LAT, 1000), await EstadoDelLugarAsync(lugar));
    }

    [Fact]
    public async Task Quien_no_administra_el_catalogo_no_carga_la_ubicacion_y_los_datos_invalidos_se_rechazan()
    {
        var lugar = await LugarAsync("CEDI Envigado");
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        Assert.Equal(HttpStatusCode.Forbidden, (await Como(inspector).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { latitud = LAT, longitud = LNG, radio_metros = 1000 }))).StatusCode);
        foreach (var invalido in new object[]
        {
            new { latitud = 91, longitud = LNG, radio_metros = 100 },
            new { latitud = LAT, longitud = 181, radio_metros = 100 },
            new { latitud = LAT, radio_metros = 100 },
            new { latitud = LAT, longitud = LNG, radio_metros = 0 },
            new { radio_metros = 100 },
            new { latitud = "norte", longitud = LNG },
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Como(diana).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(invalido))).StatusCode);
        }

        Assert.Equal(("sin_ubicacion", null, null), await EstadoDelLugarAsync(lugar));
    }

    [Fact]
    public async Task Cambiar_un_dato_del_lugar_conserva_su_ubicacion_y_enviar_las_coordenadas_en_nulo_la_quita()
    {
        var lugar = await LugarAsync("CEDI Envigado", "confirmada", LAT, LNG, 800);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

        Assert.Equal(HttpStatusCode.OK, (await Como(diana).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { ciudad = "Itagüí" }))).StatusCode);
        Assert.Equal(("confirmada", LAT, 800), await EstadoDelLugarAsync(lugar));

        var quitada = await Como(diana).PatchAsync($"/api/catalogos/lugares/{lugar}", Cuerpo(new { latitud = (double?)null, longitud = (double?)null, radio_metros = (int?)null }));

        Assert.Equal(HttpStatusCode.OK, quitada.StatusCode);
        Assert.Equal(("sin_ubicacion", null, null), await EstadoDelLugarAsync(lugar));
    }

    // ------------------------------------------------------------------------------------------------ fijar ubicacion aqui

    [Fact]
    public async Task Quien_esta_en_un_lugar_sin_ubicacion_la_fija_y_queda_pendiente_de_confirmar_sin_radio()
    {
        var lugar = await LugarAsync("CEDI Envigado");
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);

        var r = await Como(inspector).PostAsync($"/api/lugares/{lugar}/ubicacion", Cuerpo(new { lat = LAT, lng = LNG, precision = 18.5 }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var valor = await JsonAsync(r);
        Assert.Equal("pendiente_confirmar", valor.GetProperty("estado_ubicacion").GetString());
        Assert.Equal(LAT, valor.GetProperty("latitud").GetDouble());
        Assert.Equal(JsonValueKind.Null, valor.GetProperty("radio_metros").ValueKind);
        Assert.Equal(inspector.UsuarioId, valor.GetProperty("ubicacion_fijada_por").GetGuid());
        Assert.Equal(18.5, valor.GetProperty("precision_m").GetDouble());
        Assert.Equal(("pendiente_confirmar", LAT, null), await EstadoDelLugarAsync(lugar));
        Assert.Equal("pendiente_confirmar", (await LugarDelCatalogoAsync(inspector, lugar)).GetProperty("estado_ubicacion").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'lugar.fijar_ubicacion' AND UsuarioId = @u", ("@u", inspector.UsuarioId)));
    }

    [Fact]
    public async Task Fijar_aqui_no_cambia_una_ubicacion_que_ya_existe_pendiente_o_confirmada()
    {
        var pendiente = await LugarAsync("Patio Bello", "pendiente_confirmar", LAT, LNG);
        var confirmada = await LugarAsync("CEDI Envigado", "confirmada", LAT, LNG, 800);
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);

        Assert.Equal(HttpStatusCode.Conflict, (await Como(inspector).PostAsync($"/api/lugares/{pendiente}/ubicacion", Cuerpo(new { lat = 7.0, lng = -75.0, precision = 10 }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Como(inspector).PostAsync($"/api/lugares/{confirmada}/ubicacion", Cuerpo(new { lat = 7.0, lng = -75.0, precision = 10 }))).StatusCode);
        Assert.Equal(("confirmada", LAT, 800), await EstadoDelLugarAsync(confirmada));
    }

    [Fact]
    public async Task Fijar_aqui_valida_las_coordenadas_la_precision_y_que_el_lugar_exista_y_no_lo_hace_quien_no_opera()
    {
        var lugar = await LugarAsync("CEDI Envigado");
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        var comercial = await SesionAsync("ventas@ejemplo.com", clsRol.COMERCIAL);

        foreach (var invalido in new object[] { new { lng = LNG }, new { lat = LAT }, new { lat = 95, lng = LNG }, new { lat = LAT, lng = 200 }, new { lat = LAT, lng = LNG, precision = -1 } })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Como(inspector).PostAsync($"/api/lugares/{lugar}/ubicacion", Cuerpo(invalido))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await Como(inspector).PostAsync($"/api/lugares/{Guid.NewGuid()}/ubicacion", Cuerpo(new { lat = LAT, lng = LNG }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(comercial).PostAsync($"/api/lugares/{lugar}/ubicacion", Cuerpo(new { lat = LAT, lng = LNG }))).StatusCode);
        Assert.Equal(("sin_ubicacion", null, null), await EstadoDelLugarAsync(lugar));
    }

    // ------------------------------------------------------------------------------------------------ autocalibracion

    private int _visitas;

    /// <summary>Una visita al lugar: un viaje con ese lugar de cargue y una inspeccion de Cargue con una foto sellada a esa distancia al norte del centro y esa precision.</summary>
    private async Task VisitaAsync(Sesion inspector, Guid lugar, double metros, double precision = 20)
    {
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync($"9FBBR8A04PM{Interlocked.Increment(ref _visitas):000000}", inspectorId: inspector.UsuarioId);
        var viaje = await Sembrador.ViajeDeEtapaAsync(etapas[0]);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET LugarCargueId = @l WHERE Id = @v", ("@l", lugar), ("@v", viaje));
        var inspeccion = await Sembrador.InformeAsync(etapas[0], inspector.UsuarioId, cerrada: false, momento: "llegada");
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, Precision) VALUES (NEWID(), @i, N'foto.jpg', SYSDATETIMEOFFSET(), @lat, @lng, @p)",
            ("@i", inspeccion), ("@lat", AlNorte(metros)), ("@lng", LNG), ("@p", precision));
    }

    private Task<int> PropuestasAsync(Guid lugar, string? estado = null) =>
        Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblPropuestasUbicacion WHERE LugarId = @l AND (@e IS NULL OR Estado = @e)", ("@l", lugar), ("@e", (object?)estado ?? DBNull.Value));

    private Task<int> AvisosAsync(Guid? usuario = null) =>
        Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'propuesta_ubicacion' AND Canal = 'bandeja' AND (@u IS NULL OR UsuarioId = @u)", ("@u", (object?)usuario ?? DBNull.Value));

    [Fact]
    public async Task Con_cinco_visitas_la_autocalibracion_propone_centro_y_radio_avisa_a_la_coordinadora_y_no_cambia_el_lugar()
    {
        var lugar = await LugarAsync("CEDI Envigado");
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        foreach (var metros in new double[] { 0, 30, 60, 90, 120 })
        {
            await VisitaAsync(inspector, lugar, metros);
        }

        var avisos = await ConProcesosAsync(p => p.EjecutarAutocalibracionAsync());

        Assert.Equal(1, avisos);
        Assert.Equal(1, await PropuestasAsync(lugar, "pendiente"));
        Assert.Equal(1, await AvisosAsync(diana.UsuarioId));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'propuesta_ubicacion' AND Canal = 'push' AND UsuarioId = @u", ("@u", diana.UsuarioId)));
        Assert.Equal(("sin_ubicacion", null, null), await EstadoDelLugarAsync(lugar));

        var propuesta = (await JsonAsync(await Como(diana).GetAsync("/api/lugares/propuestas?estado=pendiente"))).EnumerateArray().Single();
        Assert.Equal("CEDI Envigado", propuesta.GetProperty("lugar").GetString());
        Assert.Equal(AlNorte(60), propuesta.GetProperty("centro_lat").GetDouble(), 7);
        Assert.Equal(LNG, propuesta.GetProperty("centro_lng").GetDouble(), 7);
        Assert.Equal(100, propuesta.GetProperty("radio_metros").GetInt32());
        Assert.Equal(5, propuesta.GetProperty("fotos").GetInt32());
        Assert.Equal(5, propuesta.GetProperty("visitas").GetInt32());
        Assert.Equal("pendiente", propuesta.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task La_autocalibracion_no_repite_la_propuesta_pendiente_ni_propone_con_menos_de_tres_visitas()
    {
        var lugarConTres = await LugarAsync("CEDI Envigado");
        var lugarConDos = await LugarAsync("Patio Bello");
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        foreach (var metros in new double[] { 0, 40, 80 })
        {
            await VisitaAsync(inspector, lugarConTres, metros);
        }

        await VisitaAsync(inspector, lugarConDos, 0);
        await VisitaAsync(inspector, lugarConDos, 40);

        var primera = await ConProcesosAsync(p => p.EjecutarAutocalibracionAsync());
        var segunda = await ConProcesosAsync(p => p.EjecutarAutocalibracionAsync());

        Assert.Equal(1, primera);
        Assert.Equal(0, segunda);
        Assert.Equal(1, await PropuestasAsync(lugarConTres));
        Assert.Equal(0, await PropuestasAsync(lugarConDos));
        Assert.Equal(1, await AvisosAsync());
    }

    [Fact]
    public async Task La_autocalibracion_ignora_las_fotos_imprecisas_sin_ubicacion_y_los_lugares_ya_confirmados()
    {
        var sinConfirmar = await LugarAsync("CEDI Envigado");
        var confirmado = await LugarAsync("Patio Bello", "confirmada", LAT, LNG, 500);
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        await VisitaAsync(inspector, sinConfirmar, 0);
        await VisitaAsync(inspector, sinConfirmar, 20);
        await VisitaAsync(inspector, sinConfirmar, 40, precision: 900);
        foreach (var metros in new double[] { 0, 20, 40 })
        {
            await VisitaAsync(inspector, confirmado, metros);
        }

        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAutocalibracionAsync()));
        Assert.Equal(0, await PropuestasAsync(sinConfirmar));
        Assert.Equal(0, await PropuestasAsync(confirmado));
    }

    [Fact]
    public async Task La_coordinadora_confirma_la_propuesta_y_el_lugar_queda_con_ese_centro_y_radio_confirmados()
    {
        var lugar = await LugarAsync("CEDI Envigado");
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        foreach (var metros in new double[] { 0, 200, 400 })
        {
            await VisitaAsync(inspector, lugar, metros);
        }

        await ConProcesosAsync(p => p.EjecutarAutocalibracionAsync());
        var propuestaId = (await JsonAsync(await Como(diana).GetAsync("/api/lugares/propuestas"))).EnumerateArray().Single().GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await Como(inspector).PostAsync($"/api/lugares/propuestas/{propuestaId}/confirmar", null)).StatusCode);
        var confirmada = await Como(diana).PostAsync($"/api/lugares/propuestas/{propuestaId}/confirmar", null);

        Assert.Equal(HttpStatusCode.OK, confirmada.StatusCode);
        var cuerpo = await JsonAsync(confirmada);
        Assert.Equal("confirmada", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(diana.UsuarioId, cuerpo.GetProperty("decidida_por").GetGuid());
        var (estado, lat, radio) = await EstadoDelLugarAsync(lugar);
        Assert.Equal("confirmada", estado);
        Assert.Equal(AlNorte(200), lat!.Value, 5);
        Assert.InRange(radio!.Value, 200, 210);
        var delCatalogo = await LugarDelCatalogoAsync(diana, lugar);
        Assert.Equal("confirmada", delCatalogo.GetProperty("estado_ubicacion").GetString());
        Assert.Equal(diana.UsuarioId, delCatalogo.GetProperty("ubicacion_fijada_por").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'lugar.confirmar_propuesta' AND UsuarioId = @u", ("@u", diana.UsuarioId)));
        Assert.Equal(HttpStatusCode.Conflict, (await Como(diana).PostAsync($"/api/lugares/propuestas/{propuestaId}/confirmar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Como(diana).PostAsync($"/api/lugares/propuestas/{propuestaId}/descartar", null)).StatusCode);
    }

    [Fact]
    public async Task La_coordinadora_descarta_la_propuesta_y_el_lugar_no_cambia_ni_se_vuelve_a_proponer_sin_fotos_nuevas()
    {
        var lugar = await LugarAsync("CEDI Envigado");
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        foreach (var metros in new double[] { 0, 30, 60 })
        {
            await VisitaAsync(inspector, lugar, metros);
        }

        await ConProcesosAsync(p => p.EjecutarAutocalibracionAsync());
        var propuestaId = (await JsonAsync(await Como(diana).GetAsync("/api/lugares/propuestas"))).EnumerateArray().Single().GetProperty("id").GetGuid();

        var descartada = await Como(diana).PostAsync($"/api/lugares/propuestas/{propuestaId}/descartar", null);

        Assert.Equal(HttpStatusCode.OK, descartada.StatusCode);
        Assert.Equal("descartada", (await JsonAsync(descartada)).GetProperty("estado").GetString());
        Assert.Equal(("sin_ubicacion", null, null), await EstadoDelLugarAsync(lugar));
        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarAutocalibracionAsync()));
        Assert.Equal(1, await PropuestasAsync(lugar));

        await VisitaAsync(inspector, lugar, 90);
        Assert.Equal(1, await ConProcesosAsync(p => p.EjecutarAutocalibracionAsync()));
        Assert.Equal(2, await PropuestasAsync(lugar));
        Assert.Equal(1, await PropuestasAsync(lugar, "pendiente"));
        Assert.Empty((await JsonAsync(await Como(diana).GetAsync("/api/lugares/propuestas?estado=confirmada"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await Como(diana).GetAsync("/api/lugares/propuestas?estado=rara")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Como(diana).PostAsync($"/api/lugares/propuestas/{Guid.NewGuid()}/descartar", null)).StatusCode);
    }

    [Fact]
    public async Task Una_propuesta_decidida_no_se_modifica_ni_se_borra_en_la_base()
    {
        var lugar = await LugarAsync("CEDI Envigado");
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        foreach (var metros in new double[] { 0, 30, 60 })
        {
            await VisitaAsync(inspector, lugar, metros);
        }

        await ConProcesosAsync(p => p.EjecutarAutocalibracionAsync());
        var propuestaId = (await JsonAsync(await Como(diana).GetAsync("/api/lugares/propuestas"))).EnumerateArray().Single().GetProperty("id").GetGuid();
        await Como(diana).PostAsync($"/api/lugares/propuestas/{propuestaId}/descartar", null);

        await Assert.ThrowsAnyAsync<Exception>(() => Sembrador.EjecutarAsync("UPDATE dbo.tblPropuestasUbicacion SET RadioMetros = 999 WHERE Id = @i", ("@i", propuestaId)));
        await Assert.ThrowsAnyAsync<Exception>(() => Sembrador.EjecutarAsync("DELETE dbo.tblPropuestasUbicacion WHERE Id = @i", ("@i", propuestaId)));
    }

    // ------------------------------------------------------------------------------------------------ cargue sin reclamar

    private async Task<Guid> ViajeProgramadoAsync(string vin, int minutosPasadaLaHora)
    {
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync(vin);
        var viaje = await Sembrador.ViajeDeEtapaAsync(etapas[0]);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET FechaProgramada = DATEADD(MINUTE, -@m, SYSDATETIMEOFFSET()) WHERE Id = @v", ("@m", minutosPasadaLaHora), ("@v", viaje));
        return viaje;
    }

    private Task<int> AvisosCargueAsync(Guid? viaje = null) =>
        Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'cargue_sin_reclamar' AND Canal = 'bandeja' AND (@v IS NULL OR ViajeId = @v)", ("@v", (object?)viaje ?? DBNull.Value));

    [Fact]
    public async Task Un_cargue_programado_que_nadie_reclama_pasados_treinta_minutos_avisa_a_la_coordinadora_una_sola_vez()
    {
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var tarde = await ViajeProgramadoAsync("VIN-CSR-0001", 40);
        await ViajeProgramadoAsync("VIN-CSR-0002", 10);

        var primera = await ConProcesosAsync(p => p.EjecutarCarguesSinReclamarAsync());
        var segunda = await ConProcesosAsync(p => p.EjecutarCarguesSinReclamarAsync()); // intenta avisar otra vez: la clave de dedupe lo impide

        Assert.Equal(1, primera);
        Assert.Equal(1, segunda);
        Assert.Equal(1, await AvisosCargueAsync());
        Assert.Equal(1, await AvisosCargueAsync(tarde));
        Assert.Equal(diana.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT UsuarioId FROM dbo.tblNotificaciones WHERE Tipo = 'cargue_sin_reclamar' AND Canal = 'bandeja'"))!);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'cargue_sin_reclamar' AND Canal = 'push'"));
    }

    [Fact]
    public async Task Un_cargue_reclamado_o_con_inspeccion_iniciada_no_avisa()
    {
        await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        var reclamado = await ViajeProgramadoAsync("VIN-CSR-0003", 90);
        var iniciado = await ViajeProgramadoAsync("VIN-CSR-0004", 90);
        await Sembrador.EjecutarAsync("INSERT dbo.tblReclamos (Id, ViajeId, InspectorId, Proceso) VALUES (NEWID(), @v, @i, 'cargue')", ("@v", reclamado), ("@i", inspector.UsuarioId));
        var etapa = (Guid)(await Sembrador.EscalarAsync("SELECT TOP 1 Id FROM dbo.tblEtapas WHERE ViajeId = @v ORDER BY Orden", ("@v", iniciado)))!;
        await Sembrador.InformeAsync(etapa, inspector.UsuarioId, cerrada: false, momento: "llegada");

        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarCarguesSinReclamarAsync()));
        Assert.Equal(0, await AvisosCargueAsync());
    }

    [Fact]
    public async Task El_tiempo_de_espera_del_cargue_sin_reclamar_se_ajusta_por_parametro_sin_desplegar()
    {
        await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        await ViajeProgramadoAsync("VIN-CSR-0005", 40);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblParametros SET Valor = N'60' WHERE Clave = 'cargue_sin_reclamar_minutos'");
        using (var alcance = Fabrica.Services.CreateScope())
        {
            alcance.ServiceProvider.GetRequiredService<IAAutos.Aplicacion.Parametros.IParametros>().Invalidar();
        }

        Assert.Equal(0, await ConProcesosAsync(p => p.EjecutarCarguesSinReclamarAsync()));

        await Sembrador.EjecutarAsync("UPDATE dbo.tblParametros SET Valor = N'30' WHERE Clave = 'cargue_sin_reclamar_minutos'");
        using (var alcance = Fabrica.Services.CreateScope())
        {
            alcance.ServiceProvider.GetRequiredService<IAAutos.Aplicacion.Parametros.IParametros>().Invalidar();
        }

        Assert.Equal(1, await ConProcesosAsync(p => p.EjecutarCarguesSinReclamarAsync()));
    }
}
