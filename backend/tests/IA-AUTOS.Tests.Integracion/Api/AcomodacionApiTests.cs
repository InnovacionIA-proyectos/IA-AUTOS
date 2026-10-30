using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La segunda inspeccion del cargue documenta la acomodacion en la ninera: cada unidad del viaje queda en una posicion de 1 a 7, unica dentro del viaje (ESC-01 a ESC-03); el cierre exige la posicion de cada
/// unidad activa y las fotos de la acomodacion, y deja de exigir las fotos de las vistas de estado (ESC-04); una unidad que no sale no exige posicion y la libera (ESC-05).
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class AcomodacionApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-11-03T06:00:00-05:00";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly string[] VINES = ["9FB45RFL8NM201001", "9FB45RFL8NM201002", "9FB45RFL8NM201003"];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>El viaje M-48213 con tres unidades activas, su inspector y su segunda inspeccion del cargue (la salida de la etapa Cargue de la primera unidad) en borrador.</summary>
    private sealed record Escenario(Guid Viaje, Guid[] Vehiculos, Guid[][] Etapas, Sesion Inspector, Sesion Coordinador, Sesion Conductor, Guid Inspeccion);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private async Task<Escenario> EscenarioAsync(bool conSalidaEnCurso = true)
    {
        var inspector = await SesionAsync("camilo@ejemplo.com", clsRol.INSPECTOR);
        var coordinador = await SesionAsync("luisa@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var viaje = await Sembrador.ViajeAsync(conductor.UsuarioId, inspector.UsuarioId, codigoMtrack: "M-48213");
        var vehiculos = new List<Guid>();
        var etapas = new List<Guid[]>();
        foreach (var vin in VINES)
        {
            var (vehiculo, ruta) = await Sembrador.VehiculoConRutaAsync(vin, viajeId: viaje);
            vehiculos.Add(vehiculo);
            etapas.Add(ruta);
        }

        var inspeccion = Guid.Empty;
        if (conSalidaEnCurso)
        {
            var r = await Como(inspector).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = etapas[0][0], momento = "salida", fecha = FECHA, lat = 6.2, lng = -75.5 }));
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            inspeccion = (await JsonAsync(r)).GetProperty("id").GetGuid();
        }

        return new Escenario(viaje, [.. vehiculos], [.. etapas], inspector, coordinador, conductor, inspeccion);
    }

    private Task<HttpResponseMessage> PosicionAsync(Sesion quien, Guid viaje, string vin, int? posicion) =>
        Como(quien).PutAsync($"/api/viajes/{viaje}/vehiculos/{vin}/posicion", Cuerpo(new { posicion }));

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien)
    {
        var bytes = FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var r = await Como(quien).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await JsonAsync(r);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), cuerpo.GetProperty("hash_sha256").GetString());
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    /// <summary>Una foto de la acomodacion: sellada con fecha y ubicacion, sin vista ni hallazgo.</summary>
    private async Task<HttpResponseMessage> FotoDeAcomodacionAsync(Sesion quien, Guid inspeccion)
    {
        var (ruta, hash) = await SubirAsync(quien);
        return await Como(quien).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccion}/evidencias",
            new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 6.2, sello_lng = -75.5, precision = 4.5, dispositivo_id = "tablet-1", hash_sha256 = hash });
    }

    /// <summary>Cambia un parametro por la API (que renueva su cache): un UPDATE directo en la base no se veria hasta que venza.</summary>
    private async Task CambiarParametroAsync(string clave, string valor)
    {
        var admin = await SesionAsync($"admin-{Guid.NewGuid():N}@ejemplo.com", clsRol.ADMIN);
        Assert.Equal(HttpStatusCode.OK, (await Como(admin).PatchAsync($"/api/parametros/{clave}", Cuerpo(new { valor }))).StatusCode);
    }

    private Task<HttpResponseMessage> CerrarAsync(Sesion quien, Guid inspeccion) => Como(quien).PostAsync($"/api/inspecciones/{inspeccion}/cerrar", null);

    private static List<string?> Detalles(JsonElement cuerpo) => cuerpo.GetProperty("details").EnumerateArray().Select(d => d.GetString()).ToList();

    private async Task<object?> PosicionDeAsync(Guid viaje, string vin) =>
        await Sembrador.EscalarAsync("SELECT vv.Posicion FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos v ON v.Id = vv.VehiculoId WHERE vv.ViajeId = @v AND v.Vin = @vin", ("@v", viaje), ("@vin", vin));

    // ------------------------------------------------------------------------------------------ ESC-01: la acomodacion completa y el cierre

    [Fact]
    public async Task El_inspector_registra_la_acomodacion_completa_con_fotos_selladas_y_cierra_sin_las_fotos_de_las_vistas_de_estado()
    {
        var e = await EscenarioAsync();

        var posiciones = new[] { 4, 3, 7 };
        for (var i = 0; i < VINES.Length; i++)
        {
            var r = await PosicionAsync(e.Inspector, e.Viaje, VINES[i], posiciones[i]);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        var viaje = await JsonAsync(await Como(e.Inspector).GetAsync($"/api/viajes/{e.Viaje}"));
        Assert.Equal(posiciones, viaje.GetProperty("vehiculos").EnumerateArray().OrderBy(v => v.GetProperty("vin").GetString()).Select(v => v.GetProperty("posicion").GetInt32()).ToArray());
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.posicion_unidad' AND UsuarioId = @u AND ViajeId = @v", ("@u", e.Inspector.UsuarioId), ("@v", e.Viaje)));

        // Dos fotos de la acomodacion y ninguna de las vistas de estado: la inspeccion cierra.
        Assert.Equal(HttpStatusCode.Created, (await FotoDeAcomodacionAsync(e.Inspector, e.Inspeccion)).StatusCode);
        var segunda = await FotoDeAcomodacionAsync(e.Inspector, e.Inspeccion);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        var evidencia = await JsonAsync(segunda);
        Assert.Equal(JsonValueKind.Null, evidencia.GetProperty("vista_id").ValueKind);

        var cierre = await CerrarAsync(e.Inspector, e.Inspeccion);

        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal("cerrada", (await JsonAsync(cierre)).GetProperty("inspeccion").GetProperty("estado").GetString());
        // Las fotos quedan selladas (fecha y ubicacion, hash) e inmutables tras el cierre.
        Assert.Equal(2, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblEvidencias WHERE InspeccionId = @i AND VistaId IS NULL AND HallazgoId IS NULL AND SelloLat = 6.2 AND SelloLng = -75.5 AND HashSha256 IS NOT NULL", ("@i", e.Inspeccion)));
        Assert.Equal(HttpStatusCode.Conflict, (await FotoDeAcomodacionAsync(e.Inspector, e.Inspeccion)).StatusCode);
        // La inspeccion de estado (la llegada del cargue) sigue exigiendo sus vistas: la nueva regla es solo de la salida.
        var llegada = await Como(e.Inspector).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = e.Etapas[1][0], momento = "llegada", fecha = FECHA, lat = 6.2, lng = -75.5 }));
        Assert.Equal(HttpStatusCode.Created, llegada.StatusCode);
        var cierreLlegada = await CerrarAsync(e.Inspector, (await JsonAsync(llegada)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, cierreLlegada.StatusCode);
        Assert.StartsWith("Falta la foto de la vista", Detalles(await JsonAsync(cierreLlegada))[0]);
    }

    // ------------------------------------------------------------------------------------- ESC-02 y ESC-03: ocupada o fuera de rango

    [Fact]
    public async Task Una_posicion_ocupada_se_rechaza_con_409_y_la_unidad_queda_sin_posicion()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[0], 4)).StatusCode);

        var r = await PosicionAsync(e.Inspector, e.Viaje, VINES[1], 4);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("La posición 4 ya está ocupada por otra unidad de este viaje", (await JsonAsync(r)).GetProperty("message").GetString());
        Assert.Null(await PosicionDeAsync(e.Viaje, VINES[1]));
        // Repetir la propia posicion no es un conflicto, y liberarla deja la 4 disponible.
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[0], 4)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[0], null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[1], 4)).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(-3)]
    public async Task Una_posicion_fuera_de_1_a_7_responde_400_con_su_mensaje(int posicion)
    {
        var e = await EscenarioAsync();

        var r = await PosicionAsync(e.Inspector, e.Viaje, VINES[2], posicion);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("La posición debe estar entre 1 y 7", (await JsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").GetProperty("posicion")[0].GetString());
        Assert.Null(await PosicionDeAsync(e.Viaje, VINES[2]));
    }

    [Fact]
    public async Task Si_varias_solicitudes_piden_la_misma_posicion_a_la_vez_gana_exactamente_una()
    {
        var e = await EscenarioAsync();
        var extras = new List<string>();
        for (var i = 4; i <= 6; i++)
        {
            var vin = $"9FB45RFL8NM20100{i}";
            await Sembrador.VehiculoConRutaAsync(vin, viajeId: e.Viaje);
            extras.Add(vin);
        }

        var aspirantes = VINES.Concat(extras).ToList();
        var respuestas = await Task.WhenAll(aspirantes.Select(vin => Task.Run(() => PosicionAsync(e.Inspector, e.Viaje, vin, 5))));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(aspirantes.Count - 1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var perdedora in respuestas.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Contains("ya está ocupada por otra unidad de este viaje", (await JsonAsync(perdedora)).GetProperty("message").GetString());
        }

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos WHERE ViajeId = @v AND Posicion = 5", ("@v", e.Viaje)));
    }

    [Fact]
    public async Task La_posicion_exige_la_segunda_inspeccion_en_curso_una_unidad_del_viaje_y_quien_participa()
    {
        var sinSalida = await EscenarioAsync(conSalidaEnCurso: false);
        var otroInspector = await SesionAsync("otro@ejemplo.com", clsRol.INSPECTOR);

        var sinInspeccion = await PosicionAsync(sinSalida.Inspector, sinSalida.Viaje, VINES[0], 2);
        var comoConductor = await PosicionAsync(sinSalida.Conductor, sinSalida.Viaje, VINES[0], 2);
        var comoOtro = await PosicionAsync(otroInspector, sinSalida.Viaje, VINES[0], 2);
        var inexistente = await PosicionAsync(sinSalida.Inspector, sinSalida.Viaje, "VIN-QUE-NO-EXISTE", 2);
        await Sembrador.VehiculoAsync("VIN-AJENO-0001");
        var ajena = await PosicionAsync(sinSalida.Inspector, sinSalida.Viaje, "VIN-AJENO-0001", 2);
        var liberar = await PosicionAsync(sinSalida.Inspector, sinSalida.Viaje, VINES[0], null);

        Assert.Equal(HttpStatusCode.Conflict, sinInspeccion.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comoConductor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comoOtro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.OK, liberar.StatusCode);
    }

    [Fact]
    public async Task El_coordinador_y_el_administrador_tambien_registran_la_posicion_y_un_comercial_no()
    {
        var e = await EscenarioAsync();
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var comercial = await SesionAsync("comercial@ejemplo.com", clsRol.COMERCIAL);

        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Coordinador, e.Viaje, VINES[0], 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(admin, e.Viaje, VINES[1], 2)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PosicionAsync(comercial, e.Viaje, VINES[2], 3)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PutAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[2]}/posicion", Cuerpo(new { posicion = 3 }))).StatusCode);
        Assert.Null(await PosicionDeAsync(e.Viaje, VINES[2]));
    }

    [Fact]
    public async Task Una_foto_sin_vista_ni_hallazgo_solo_se_admite_en_la_segunda_inspeccion_del_cargue()
    {
        var e = await EscenarioAsync();
        var llegada = await Como(e.Inspector).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = e.Etapas[0][0], momento = "llegada", fecha = FECHA, lat = 6.2, lng = -75.5 }));
        Assert.Equal(HttpStatusCode.Created, llegada.StatusCode);

        var enLaPrimera = await FotoDeAcomodacionAsync(e.Inspector, (await JsonAsync(llegada)).GetProperty("id").GetGuid());
        var enLaAcomodacion = await FotoDeAcomodacionAsync(e.Inspector, e.Inspeccion);

        Assert.Equal(HttpStatusCode.BadRequest, enLaPrimera.StatusCode);
        Assert.True((await JsonAsync(enLaPrimera)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("vista_id", out _));
        Assert.Equal(HttpStatusCode.Created, enLaAcomodacion.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias"));
    }

    // ----------------------------------------------------------------------------------------------- ESC-04: el cierre incompleto

    [Fact]
    public async Task El_cierre_se_rechaza_si_falta_la_posicion_de_una_unidad_activa_o_las_fotos_de_la_acomodacion()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[0], 4)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[1], 3)).StatusCode);

        var sinTodo = await CerrarAsync(e.Inspector, e.Inspeccion);

        Assert.Equal(HttpStatusCode.BadRequest, sinTodo.StatusCode);
        var detalles = Detalles(await JsonAsync(sinTodo));
        Assert.Contains("Falta la posición de la unidad 9FB45RFL8NM201003", detalles);
        Assert.DoesNotContain(detalles, d => d!.StartsWith("Falta la foto de la vista"));
        Assert.Contains(detalles, d => d!.StartsWith("Faltan fotos de la acomodación"));
        Assert.Equal(HttpStatusCode.Created, (await FotoDeAcomodacionAsync(e.Inspector, e.Inspeccion)).StatusCode);

        var sinUnaPosicion = await CerrarAsync(e.Inspector, e.Inspeccion);
        Assert.Equal(HttpStatusCode.BadRequest, sinUnaPosicion.StatusCode);
        Assert.Equal(["Falta la posición de la unidad 9FB45RFL8NM201003"], Detalles(await JsonAsync(sinUnaPosicion)));

        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[2], 7)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(e.Inspector, e.Inspeccion)).StatusCode);
    }

    [Fact]
    public async Task El_minimo_de_fotos_de_la_acomodacion_es_un_parametro()
    {
        var e = await EscenarioAsync();
        for (var i = 0; i < VINES.Length; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[i], i + 1)).StatusCode);
        }

        await CambiarParametroAsync("acomodacion_fotos_minimas", "2");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeAcomodacionAsync(e.Inspector, e.Inspeccion)).StatusCode);

        var conUna = await CerrarAsync(e.Inspector, e.Inspeccion);
        Assert.Equal(HttpStatusCode.BadRequest, conUna.StatusCode);
        Assert.Equal(["Faltan fotos de la acomodación: se exige al menos 2 y hay 1"], Detalles(await JsonAsync(conUna)));

        Assert.Equal(HttpStatusCode.Created, (await FotoDeAcomodacionAsync(e.Inspector, e.Inspeccion)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(e.Inspector, e.Inspeccion)).StatusCode);
    }

    // ------------------------------------------------------------------------------------- ESC-05: la unidad que no sale

    [Fact]
    public async Task Una_unidad_que_no_sale_no_exige_posicion_ni_admite_una_y_el_cierre_procede_con_las_activas()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[0], 4)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[1], 3)).StatusCode);
        // La tercera unidad quedo marcada «no sale» por un hallazgo V1+ de su inspeccion cerrada.
        var (_, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2][2], e.Inspector.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!, nivel: "v1_plus");
        await Sembrador.UnidadNoSaleAsync(e.Viaje, e.Vehiculos[2], hallazgo, e.Coordinador.UsuarioId, "Golpe en el capó");
        Assert.Equal(HttpStatusCode.Created, (await FotoDeAcomodacionAsync(e.Inspector, e.Inspeccion)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await FotoDeAcomodacionAsync(e.Inspector, e.Inspeccion)).StatusCode);

        var conPosicion = await PosicionAsync(e.Inspector, e.Viaje, VINES[2], 7);
        var cierre = await CerrarAsync(e.Inspector, e.Inspeccion);

        Assert.Equal(HttpStatusCode.Conflict, conPosicion.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Null(await PosicionDeAsync(e.Viaje, VINES[2]));
    }

    [Fact]
    public async Task Marcar_la_unidad_que_no_sale_por_la_api_libera_su_posicion()
    {
        var e = await EscenarioAsync();
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[2], 6)).StatusCode);
        var (_, hallazgo) = await Sembrador.InformeCerradoConHallazgoAsync(e.Etapas[2][2], e.Inspector.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!, nivel: "v1_plus");

        var r = await Como(e.Coordinador).PostAsync($"/api/viajes/{e.Viaje}/vehiculos/{VINES[2]}/no-sale", Cuerpo(new { motivo = "Golpe en el capó", hallazgo_id = hallazgo }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Null(await PosicionDeAsync(e.Viaje, VINES[2]));
        var unidad = (await JsonAsync(r)).GetProperty("vehiculos").EnumerateArray().Single(v => v.GetProperty("vin").GetString() == VINES[2]);
        Assert.Equal(JsonValueKind.Null, unidad.GetProperty("posicion").ValueKind);
        // La posicion que dejo libre la puede tomar otra unidad.
        Assert.Equal(HttpStatusCode.OK, (await PosicionAsync(e.Inspector, e.Viaje, VINES[0], 6)).StatusCode);
    }
}
