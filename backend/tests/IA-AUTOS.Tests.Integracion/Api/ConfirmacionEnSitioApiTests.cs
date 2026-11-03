using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La confirmacion «en sitio» de la evidencia sellada: con la primera foto con ubicacion de una inspeccion de Cargue o Descargue se calcula la distancia al lugar de la etapa y se guarda el resultado en la
/// inspeccion (confirmado en sitio, fuera del rango o sin confirmacion posible); solo «fuera del rango» avisa, una vez, al coordinador de calidad. Nada de esto bloquea ni cambia la captura.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class ConfirmacionEnSitioApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-11-03T06:00:00-05:00";
    private const string VIN = "9FBBR8A04PM123456";
    private const double LAT = 6.2;
    private const double LNG = -75.5;

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>El VIN con su ruta, el lugar real de cargue («CEDI Envigado») en el viaje y el destino «Concesionario Caminos Cali» en la unidad, el inspector y la coordinadora Diana.</summary>
    private sealed record Escenario(Guid Viaje, Guid Vehiculo, Guid[] Etapas, Guid Lugar, Guid Destino, Sesion Inspector, Sesion Diana);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private HttpClient Como(Sesion quien) => Cliente(quien.Token);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    /// <summary>La latitud de un punto al norte del centro del lugar, a esa distancia en metros.</summary>
    private static double AlNorte(double metros) => LAT + (metros / clsConfirmacionSitio.RADIO_TIERRA_M * 180 / Math.PI);

    private async Task<Guid> LugarAsync(string nombre, string tipo, double? lat, double? lng, int? radio, string estado)
    {
        var id = Guid.NewGuid();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblLugares (Id, Nombre, TipoId, Ciudad, Latitud, Longitud, RadioMetros, EstadoUbicacion) SELECT @id, @n, Id, N'Medellin', @lat, @lng, @r, @e FROM dbo.tblPuntosCustodia WHERE Codigo = @t",
            ("@id", id), ("@n", nombre), ("@t", tipo), ("@lat", (object?)lat ?? DBNull.Value), ("@lng", (object?)lng ?? DBNull.Value), ("@r", (object?)radio ?? DBNull.Value), ("@e", estado));
        return id;
    }

    private async Task<Escenario> EscenarioAsync(int? radioDelLugar = 1000, string estadoDelLugar = "confirmada", bool conCoordenadas = true)
    {
        // Los lugares primero: la API guarda en cache todos los catalogos la primera vez que se consultan (al crear el primer usuario) y no veria los que se siembran despues.
        var lugar = await LugarAsync("CEDI Envigado", "cedi_1", conCoordenadas ? LAT : null, conCoordenadas ? LNG : null, radioDelLugar, estadoDelLugar);
        var destino = await LugarAsync("Concesionario Caminos Cali", "concesionario", LAT, LNG, null, "confirmada");
        var inspector = await SesionAsync("marcos@ejemplo.com", clsRol.INSPECTOR);
        var diana = await SesionAsync("diana@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var (vehiculo, etapas) = await Sembrador.VehiculoConRutaAsync(VIN, inspectorId: inspector.UsuarioId);
        var viaje = await Sembrador.ViajeDeEtapaAsync(etapas[0]);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET LugarCargueId = @l WHERE Id = @v", ("@l", lugar), ("@v", viaje));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajeVehiculos SET LugarDestinoId = @l WHERE ViajeId = @v AND VehiculoId = @veh", ("@l", destino), ("@v", viaje), ("@veh", vehiculo));
        return new Escenario(viaje, vehiculo, etapas, lugar, destino, inspector, diana);
    }

    private async Task<Guid> InspeccionAsync(Sesion quien, Guid etapa, string momento = "llegada")
    {
        var r = await Como(quien).PostAsync("/api/inspecciones", Cuerpo(new { etapa_id = etapa, momento, fecha = FECHA, lat = LAT, lng = LNG }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await JsonAsync(r)).GetProperty("id").GetGuid();
    }

    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion quien, clsFabricaApi? fabrica = null)
    {
        var bytes = FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var r = await Cliente(quien.Token, fabrica).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await JsonAsync(r);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), cuerpo.GetProperty("hash_sha256").GetString());
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    /// <summary>Una foto de una vista tomada a esa distancia del centro (o sin ubicacion, con <paramref name="metros"/> nulo) y con esa precision de GPS.</summary>
    private async Task<HttpResponseMessage> FotoAsync(Sesion quien, Guid inspeccion, double? metros, double? precision, int vista = 0)
    {
        var (ruta, hash) = await SubirAsync(quien);
        var vistaId = (await Sembrador.VistasAsync(obligatoria: true))[vista].Id;
        return await Como(quien).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccion}/evidencias",
            new { vista_id = vistaId, storage_path = ruta, sello_fecha = FECHA, sello_lat = metros is { } m ? AlNorte(m) : (double?)null, sello_lng = metros is null ? (double?)null : LNG, precision, dispositivo_id = "tablet-1", hash_sha256 = hash });
    }

    private async Task<JsonElement?> ConfirmacionAsync(Sesion quien, Guid inspeccion)
    {
        var informe = await JsonAsync(await Como(quien).GetAsync($"/api/inspecciones/{inspeccion}"));
        return informe.GetProperty("confirmacion_sitio").ValueKind == JsonValueKind.Null ? null : informe.GetProperty("confirmacion_sitio");
    }

    private Task<int> AvisosAsync() => Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Canal = 'bandeja'");

    private Task<int> NotificacionesAsync() => Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones");

    // ------------------------------------------------------------------------------------------------ ESC-01 a ESC-03

    [Fact]
    public async Task Una_foto_dentro_del_rango_confirma_en_sitio_con_su_distancia_y_nadie_recibe_avisos()
    {
        var e = await EscenarioAsync();
        var inspeccion = await InspeccionAsync(e.Inspector, e.Etapas[0]);

        var foto = await FotoAsync(e.Inspector, inspeccion, 400, 25);

        Assert.Equal(HttpStatusCode.Created, foto.StatusCode);
        var confirmacion = (await ConfirmacionAsync(e.Diana, inspeccion))!.Value;
        Assert.Equal(["resultado", "nombre", "distancia_m", "evidencia_id"], confirmacion.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("confirmado_en_sitio", confirmacion.GetProperty("resultado").GetString());
        Assert.Equal("Confirmado en sitio", confirmacion.GetProperty("nombre").GetString());
        Assert.Equal(400, confirmacion.GetProperty("distancia_m").GetDouble(), 1);
        Assert.Equal((await JsonAsync(foto)).GetProperty("id").GetGuid(), confirmacion.GetProperty("evidencia_id").GetGuid());
        Assert.Equal(0, await NotificacionesAsync());
    }

    [Fact]
    public async Task La_duda_del_GPS_juega_a_favor_en_el_limite_exacto_y_un_metro_mas_queda_fuera_del_rango()
    {
        var e = await EscenarioAsync();
        var justa = await InspeccionAsync(e.Inspector, e.Etapas[0]);
        var pasada = await InspeccionAsync(e.Inspector, e.Etapas[2]);

        await FotoAsync(e.Inspector, justa, 1200, 200);
        await FotoAsync(e.Inspector, pasada, 1201, 200);

        Assert.Equal("confirmado_en_sitio", (await ConfirmacionAsync(e.Diana, justa))!.Value.GetProperty("resultado").GetString());
        // La segunda inspeccion es de Descargue (radio de 500 m del destino): a 1.201 m queda fuera igual.
        Assert.Equal("fuera_de_rango", (await ConfirmacionAsync(e.Diana, pasada))!.Value.GetProperty("resultado").GetString());
    }

    [Fact]
    public async Task Fuera_del_rango_no_bloquea_marca_y_avisa_una_sola_vez_a_la_coordinadora_con_el_viaje_el_lugar_y_la_distancia()
    {
        var e = await EscenarioAsync();
        var inspeccion = await InspeccionAsync(e.Inspector, e.Etapas[0]);

        var primera = await FotoAsync(e.Inspector, inspeccion, 2500, 30);
        // Mas fotos, cerca y lejos: la inspeccion sigue editable y la marca y el aviso no se repiten.
        var vistas = await Sembrador.VistasAsync(obligatoria: true);
        for (var i = 1; i < vistas.Count; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await FotoAsync(e.Inspector, inspeccion, i % 2 == 0 ? 50 : 3000, 10, vista: i)).StatusCode);
        }

        var cierre = await Como(e.Inspector).PostAsync($"/api/inspecciones/{inspeccion}/cerrar", null);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        var confirmacion = (await ConfirmacionAsync(e.Diana, inspeccion))!.Value;
        Assert.Equal("fuera_de_rango", confirmacion.GetProperty("resultado").GetString());
        Assert.Equal(2500, confirmacion.GetProperty("distancia_m").GetDouble(), 1);
        Assert.Equal((await JsonAsync(primera)).GetProperty("id").GetGuid(), confirmacion.GetProperty("evidencia_id").GetGuid());
        Assert.Equal(1, await AvisosAsync());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Canal = 'push'"));
        var aviso = (string)(await Sembrador.EscalarAsync("SELECT Mensaje FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Canal = 'bandeja'"))!;
        Assert.Contains("MTK-S", aviso);
        Assert.Contains("CEDI Envigado", aviso);
        Assert.Contains("2500 m", aviso);
        Assert.Contains(VIN, aviso);
        Assert.Equal(e.Diana.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT UsuarioId FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Canal = 'bandeja'"))!);
        Assert.Equal(e.Viaje, (Guid)(await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Canal = 'bandeja'"))!);
        Assert.Equal(inspeccion, (Guid)(await Sembrador.EscalarAsync("SELECT InspeccionId FROM dbo.tblNotificaciones WHERE Tipo = 'fuera_de_rango' AND Canal = 'bandeja'"))!);
    }

    // ------------------------------------------------------------------------------------------------ ESC-04 a ESC-07

    [Theory]
    [InlineData(800.0)]
    [InlineData(null)]
    public async Task Con_una_precision_de_800_m_o_sin_precision_reportada_no_hay_confirmacion_posible_ni_aviso(double? precision)
    {
        var e = await EscenarioAsync();
        var inspeccion = await InspeccionAsync(e.Inspector, e.Etapas[0]);

        await FotoAsync(e.Inspector, inspeccion, 2500, precision);

        var confirmacion = (await ConfirmacionAsync(e.Diana, inspeccion))!.Value;
        Assert.Equal("sin_confirmacion_posible", confirmacion.GetProperty("resultado").GetString());
        Assert.Equal(JsonValueKind.Null, confirmacion.GetProperty("distancia_m").ValueKind);
        Assert.Equal(0, await NotificacionesAsync());
    }

    [Fact]
    public async Task Una_foto_sin_ubicacion_es_neutra_la_regla_de_repetirla_sigue_igual_y_una_foto_con_ubicacion_despues_si_confirma()
    {
        var e = await EscenarioAsync();
        var inspeccion = await InspeccionAsync(e.Inspector, e.Etapas[0]);

        var sinGps = await FotoAsync(e.Inspector, inspeccion, null, null);
        var cierre = await Como(e.Inspector).PostAsync($"/api/inspecciones/{inspeccion}/cerrar", null);
        var enSitio = await Como(e.Diana).GetAsync($"/api/inspecciones/{inspeccion}");

        Assert.Equal(HttpStatusCode.Created, sinGps.StatusCode);
        Assert.True((await JsonAsync(sinGps)).GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal("sin_confirmacion_posible", (await ConfirmacionAsync(e.Diana, inspeccion))!.Value.GetProperty("resultado").GetString());
        // La regla existente del cierre no cambia: faltan las demas vistas y la foto sin ubicacion hay que repetirla.
        Assert.Equal(HttpStatusCode.BadRequest, cierre.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enSitio.StatusCode);
        Assert.Equal(0, await NotificacionesAsync());
        // Repetida la foto con su ubicacion, la confirmacion se determina con ella.
        await FotoAsync(e.Inspector, inspeccion, 300, 20);
        Assert.Equal("confirmado_en_sitio", (await ConfirmacionAsync(e.Diana, inspeccion))!.Value.GetProperty("resultado").GetString());
    }

    [Theory]
    [InlineData("sin_ubicacion", false)]
    [InlineData("pendiente_confirmar", true)]
    public async Task Un_lugar_sin_ubicacion_o_con_la_ubicacion_pendiente_de_confirmar_no_confirma_ni_acusa(string estado, bool conCoordenadas)
    {
        var e = await EscenarioAsync(estadoDelLugar: estado, conCoordenadas: conCoordenadas);
        var inspeccion = await InspeccionAsync(e.Inspector, e.Etapas[0]);

        await FotoAsync(e.Inspector, inspeccion, 2500, 20);

        Assert.Equal("sin_confirmacion_posible", (await ConfirmacionAsync(e.Diana, inspeccion))!.Value.GetProperty("resultado").GetString());
        Assert.Equal(0, await NotificacionesAsync());
    }

    [Fact]
    public async Task Un_viaje_sin_lugar_de_cargue_tampoco_confirma_ni_acusa()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET LugarCargueId = NULL WHERE Id = @v", ("@v", e.Viaje));
        var inspeccion = await InspeccionAsync(e.Inspector, e.Etapas[0]);

        await FotoAsync(e.Inspector, inspeccion, 2500, 20);

        Assert.Equal("sin_confirmacion_posible", (await ConfirmacionAsync(e.Diana, inspeccion))!.Value.GetProperty("resultado").GetString());
        Assert.Equal(0, await NotificacionesAsync());
    }

    // ------------------------------------------------------------------------------------------------ radios, descargue y transporte

    [Fact]
    public async Task Sin_radio_en_el_lugar_se_usa_el_parametro_del_cargue_o_del_destino_leido_en_cada_evaluacion()
    {
        var e = await EscenarioAsync(radioDelLugar: null);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var cargue = await InspeccionAsync(e.Inspector, e.Etapas[0]);
        var descargue = await InspeccionAsync(e.Inspector, e.Etapas[2]);

        await FotoAsync(e.Inspector, cargue, 900, 20);
        await FotoAsync(e.Inspector, descargue, 600, 20);
        Assert.Equal("confirmado_en_sitio", (await ConfirmacionAsync(e.Diana, cargue))!.Value.GetProperty("resultado").GetString());
        // El destino usa su propio parametro (500 m): a 600 m queda fuera del rango.
        Assert.Equal("fuera_de_rango", (await ConfirmacionAsync(e.Diana, descargue))!.Value.GetProperty("resultado").GetString());

        // Cambiado el parametro del cargue, la siguiente evaluacion lo usa sin reiniciar nada.
        Assert.Equal(HttpStatusCode.OK, (await Como(admin).PatchAsync("/api/parametros/radio_lugar_cargue_m", Cuerpo(new { valor = "300" }))).StatusCode);
        var otra = await InspeccionAsync(e.Inspector, e.Etapas[1], "llegada");
        var cargue2 = await InspeccionAsync(e.Inspector, e.Etapas[0], "salida");
        await FotoAsync(e.Inspector, cargue2, 400, 20);
        Assert.Equal("fuera_de_rango", (await ConfirmacionAsync(e.Diana, cargue2))!.Value.GetProperty("resultado").GetString());
        Assert.Null(await ConfirmacionAsync(e.Diana, otra));
    }

    [Fact]
    public async Task El_transporte_no_se_confirma_en_sitio_y_las_fotos_de_un_hallazgo_tambien_confirman()
    {
        var e = await EscenarioAsync();
        await Sembrador.InformeAsync(e.Etapas[0], e.Inspector.UsuarioId, cerrada: true, momento: "llegada");
        var transporte = await InspeccionAsync(e.Inspector, e.Etapas[1], "salida");
        await FotoAsync(e.Inspector, transporte, 5000, 10);
        var cargue = await InspeccionAsync(e.Inspector, e.Etapas[0], "salida");
        await Sembrador.CuadranteAsync(7, "Puerta delantera");

        var (ruta, hash) = await SubirAsync(e.Inspector);
        var hallazgo = await Como(e.Inspector).PostAsJsonAsync(
            "/api/inspecciones/" + cargue + "/hallazgos",
            new
            {
                cuadrante_numero = 7, lado = "izquierdo", clase_dano = "golpe", tamano_dano = "5_10cm", nivel_severidad = "v2",
                evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = AlNorte(100), sello_lng = LNG, precision = 15, hash_sha256 = hash },
            });

        Assert.Null(await ConfirmacionAsync(e.Diana, transporte));
        Assert.True(hallazgo.StatusCode == HttpStatusCode.Created, await hallazgo.Content.ReadAsStringAsync());
        Assert.Equal("confirmado_en_sitio", (await ConfirmacionAsync(e.Diana, cargue))!.Value.GetProperty("resultado").GetString());
    }

    [Fact]
    public async Task Una_vez_determinado_el_resultado_no_cambia_con_las_fotos_siguientes()
    {
        var e = await EscenarioAsync();
        var inspeccion = await InspeccionAsync(e.Inspector, e.Etapas[0]);

        await FotoAsync(e.Inspector, inspeccion, 100, 20);
        await FotoAsync(e.Inspector, inspeccion, 4000, 20, vista: 1);

        var confirmacion = (await ConfirmacionAsync(e.Diana, inspeccion))!.Value;
        Assert.Equal("confirmado_en_sitio", confirmacion.GetProperty("resultado").GetString());
        Assert.Equal(100, confirmacion.GetProperty("distancia_m").GetDouble(), 1);
        Assert.Equal(0, await NotificacionesAsync());
    }

    // ------------------------------------------------------------------------------------------------ CA-6: el calculo falla

    /// <summary>Los parametros reales, salvo los del rango y la valvula, que fallan: lo unico que se rompe es el calculo de la confirmacion (el resto de la peticion, como la firma de las URL, sigue igual).</summary>
    private sealed class ParametrosConFallaEnElRango(IParametros real) : IParametros
    {
        private static readonly string[] DEL_RANGO = ["radio_lugar_cargue_m", "radio_destino_m", "valvula_precision_m"];

        public Task<string?> ValorAsync(string clave, CancellationToken ct = default) => DEL_RANGO.Contains(clave) ? throw Falla() : real.ValorAsync(clave, ct);

        public Task<int> EnteroAsync(string clave, int porDefecto, CancellationToken ct = default) => DEL_RANGO.Contains(clave) ? throw Falla() : real.EnteroAsync(clave, porDefecto, ct);

        public Task<IReadOnlyList<string>> ListaAsync(string clave, IReadOnlyList<string> porDefecto, CancellationToken ct = default) => real.ListaAsync(clave, porDefecto, ct);

        public void Invalidar() => real.Invalidar();

        private static InvalidOperationException Falla() => new("Falla simulada de los parametros del rango");
    }

    [Fact]
    public async Task Si_el_calculo_falla_la_foto_queda_guardada_y_sellada_igual_y_el_resultado_es_sin_confirmacion_posible()
    {
        var e = await EscenarioAsync();
        var inspeccion = await InspeccionAsync(e.Inspector, e.Etapas[0]);
        var fabrica = CrearFabrica(servicios: s =>
        {
            var original = s.Last(d => d.ServiceType == typeof(IParametros));
            s.Remove(original);
            s.AddScoped<IParametros>(p => new ParametrosConFallaEnElRango((IParametros)ActivatorUtilities.CreateInstance(p, original.ImplementationType!)));
        });
        // La carpeta de archivos es de cada instancia de la API: la foto se sube por la misma que la recibe.
        var (ruta, hash) = await SubirAsync(e.Inspector, fabrica);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;

        var foto = await Cliente(e.Inspector.Token, fabrica).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccion}/evidencias", new { vista_id = vista, storage_path = ruta, sello_fecha = FECHA, sello_lat = AlNorte(2500), sello_lng = LNG, precision = 30, hash_sha256 = hash });

        Assert.Equal(HttpStatusCode.Created, foto.StatusCode);
        var dto = await JsonAsync(foto);
        Assert.Equal(hash, dto.GetProperty("hash_sha256").GetString());
        Assert.Equal(JsonValueKind.String, dto.GetProperty("sello_servidor_at").ValueKind);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE InspeccionId = @i", ("@i", inspeccion)));
        Assert.Equal("sin_confirmacion_posible", (await ConfirmacionAsync(e.Diana, inspeccion))!.Value.GetProperty("resultado").GetString());
        Assert.Equal(0, await NotificacionesAsync());
    }
}
