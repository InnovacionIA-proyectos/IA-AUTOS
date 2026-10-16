using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Inspeccion de salida y de llegada: las 7 vistas obligatorias con su sello, las fotos adicionales, la foto propia de cada hallazgo, la
/// etapa anterior cerrada y las correcciones por version. Las etapas del escenario son Cargue (0), Transporte (1) y Descargue (2).
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class InspeccionesApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-07-17T12:30:15.123Z";

    private static readonly byte[] FIRMA_PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol = clsRol.INSPECTOR)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> ClonarAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Guid> CrearInspeccionAsync(Sesion sesion, Guid etapaId, string momento = "salida", bool conGps = true)
    {
        var r = await Cliente(sesion.Token).PostAsJsonAsync(
            "/api/inspecciones",
            new { etapa_id = etapaId, momento, fecha = FECHA, lat = conGps ? 4.6 : (double?)null, lng = conGps ? -74.1 : (double?)null });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await ClonarAsync(r)).GetProperty("id").GetGuid();
    }

    private async Task<(Sesion Sesion, Guid[] Etapas)> EscenarioAsync(string email = "insp@ejemplo.com")
    {
        var sesion = await SesionAsync(email);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-INSP-0001", inspectorId: sesion.UsuarioId);
        return (sesion, etapas);
    }

    /// <summary>Sube una foto (PNG minimo y distinto cada vez) y devuelve su nombre y el hash que calculo el servidor.</summary>
    private async Task<(string Ruta, string Hash)> SubirAsync(Sesion sesion)
    {
        var bytes = FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(bytes), ext = "png" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await ClonarAsync(r);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), cuerpo.GetProperty("hash_sha256").GetString());
        return (cuerpo.GetProperty("storage_path").GetString()!, cuerpo.GetProperty("hash_sha256").GetString()!);
    }

    private async Task<HttpResponseMessage> FotoDeVistaAsync(Sesion sesion, Guid inspeccionId, Guid vistaId, double? lat = 4.6, double? lng = -74.1, string? nota = null)
    {
        var (ruta, hash) = await SubirAsync(sesion);
        return await Cliente(sesion.Token).PostAsJsonAsync(
            $"/api/inspecciones/{inspeccionId}/evidencias",
            new { vista_id = vistaId, storage_path = ruta, sello_fecha = FECHA, sello_lat = lat, sello_lng = lng, precision = 4.5, dispositivo_id = "tablet-1", nota, hash_sha256 = hash });
    }

    /// <summary>Toma la foto de cada vista obligatoria, salvo las indicadas.</summary>
    private async Task TomarVistasObligatoriasAsync(Sesion sesion, Guid inspeccionId, int salvo = 0)
    {
        var vistas = await Sembrador.VistasAsync(obligatoria: true);
        Assert.Equal(7, vistas.Count);
        foreach (var (id, _) in vistas.Take(vistas.Count - salvo))
        {
            Assert.Equal(HttpStatusCode.Created, (await FotoDeVistaAsync(sesion, inspeccionId, id)).StatusCode);
        }
    }

    /// <summary>Cambia una vista del catalogo por la API (que renueva el cache de la aplicacion): un UPDATE directo en la base no se veria hasta que venza.</summary>
    private async Task CambiarVistaAsync(Guid vistaId, object cambios)
    {
        var admin = await SesionAsync($"admin-{Guid.NewGuid():N}@ejemplo.com", clsRol.ADMIN);
        var r = await Cliente(admin.Token).PatchAsync($"/api/catalogos/vistas-foto/{vistaId}", JsonContent.Create(cambios));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    private Task<Guid> LlegadaCerradaAsync(Guid etapaId, Guid usuarioId) => Sembrador.InformeAsync(etapaId, usuarioId, cerrada: true, momento: "llegada");

    private Task<HttpResponseMessage> CerrarAsync(Sesion sesion, Guid id) => Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/cerrar", null);

    // ---------------------------------------------------------------- crear

    [Fact]
    public async Task Crear_toma_el_usuario_del_token_y_la_inspeccion_nace_en_borrador_con_su_nombre_en_pantalla()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var otro = await Fabrica.SembrarUsuarioAsync("otro@ejemplo.com", "x");

        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new
        {
            etapa_id = etapas[0],
            usuario_id = otro.Id,
            momento = "salida",
            fecha = FECHA,
            lat = 4.60971,
            lng = -74.08175,
            notas = "línea 1\nlínea 2",
        });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var i = await ClonarAsync(r);
        Assert.Equal(
            [
                "id", "etapa_id", "viaje_id", "usuario_id", "momento", "fecha", "lat", "lng", "estado", "conductor_nombre", "placa", "trailer", "notas",
                "created_at", "updated_at", "iniciada_at", "cerrada_at", "version", "corrige_inspeccion_id", "momento_nombre",
            ],
            i.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(sesion.UsuarioId, i.GetProperty("usuario_id").GetGuid());
        Assert.Equal("borrador", i.GetProperty("estado").GetString());
        Assert.Equal("salida", i.GetProperty("momento").GetString());
        Assert.Equal("Inspección de salida", i.GetProperty("momento_nombre").GetString());
        Assert.Equal(1, i.GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, i.GetProperty("corrige_inspeccion_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, i.GetProperty("cerrada_at").ValueKind);
        Assert.Equal(JsonValueKind.String, i.GetProperty("iniciada_at").ValueKind);
        Assert.Equal(FECHA, i.GetProperty("fecha").GetString());
        Assert.Equal(await Sembrador.ViajeDeEtapaAsync(etapas[0]), i.GetProperty("viaje_id").GetGuid());
        Assert.Equal(JsonValueKind.Null, i.GetProperty("conductor_nombre").ValueKind);
        Assert.Equal(await Sembrador.EscalarAsync("SELECT Placa FROM dbo.tblViajes v JOIN dbo.tblEtapas e ON e.ViajeId = v.Id WHERE e.Id = @e", ("@e", etapas[0])), i.GetProperty("placa").GetString());
        Assert.Equal(4.60971, i.GetProperty("lat").GetDouble());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.crear'"));
    }

    [Fact]
    public async Task La_llegada_se_nombra_inspeccion_de_llegada()
    {
        var (sesion, etapas) = await EscenarioAsync();

        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "llegada", fecha = FECHA });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("Inspección de llegada", (await ClonarAsync(r)).GetProperty("momento_nombre").GetString());
    }

    [Theory]
    [InlineData("informe")]
    [InlineData("check_in")]
    [InlineData("check_out")]
    [InlineData("otro")]
    public async Task Los_momentos_retirados_ya_no_se_aceptan(string momento)
    {
        var (sesion, etapas) = await EscenarioAsync();

        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento, fecha = FECHA });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("momento", out _));
    }

    [Fact]
    public async Task Crear_con_datos_invalidos_responde_400_con_errores_por_campo()
    {
        var (sesion, etapas) = await EscenarioAsync();

        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = Guid.Empty, momento = "otro", lat = 95, estado = "cerrada" });
        var sinFecha = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "salida" });
        var fechaSinZona = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "salida", fecha = "2026-07-17T12:00:00" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var campos = (await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["etapa_id", "momento", "fecha", "lat", "estado"], campos);
        Assert.Equal(HttpStatusCode.BadRequest, sinFecha.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fechaSinZona.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }

    [Fact]
    public async Task Crear_con_etapa_inexistente_es_404_y_una_segunda_salida_de_la_etapa_es_409()
    {
        var (sesion, etapas) = await EscenarioAsync();

        var inexistente = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = Guid.NewGuid(), momento = "salida", fecha = FECHA });
        await CrearInspeccionAsync(sesion, etapas[0]);
        var repetido = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "salida", fecha = FECHA });
        var llegada = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "llegada", fecha = FECHA });

        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("La etapa indicada no existe", (await ClonarAsync(inexistente)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal("Esta etapa ya tiene una inspección de ese momento: para cambiarla cree una corrección", (await ClonarAsync(repetido)).GetProperty("message").GetString());
        // La salida y la llegada de la misma etapa conviven.
        Assert.Equal(HttpStatusCode.Created, llegada.StatusCode);
    }

    [Theory]
    [InlineData(clsRol.SUPER_ADMIN)]
    [InlineData(clsRol.COMERCIAL)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task Los_roles_sin_escritura_de_campo_no_pueden_registrar_inspecciones(string rol)
    {
        var sesion = await SesionAsync("rol@ejemplo.com", rol);

        var r = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = Guid.NewGuid(), momento = "salida", fecha = FECHA });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ---------------------------------------------------------------- etapa anterior

    [Fact]
    public async Task La_salida_exige_que_la_llegada_de_la_etapa_anterior_este_cerrada()
    {
        var (sesion, etapas) = await EscenarioAsync();

        // La primera etapa no tiene anterior.
        await CrearInspeccionAsync(sesion, etapas[0]);
        // Con la etapa anterior sin llegada (solo hay una salida en borrador), no se puede crear la salida de la siguiente.
        var bloqueada = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[1], momento = "salida", fecha = FECHA });

        Assert.Equal(HttpStatusCode.Conflict, bloqueada.StatusCode);
        var error = await ClonarAsync(bloqueada);
        Assert.Equal("ETAPA_PREVIA_SIN_CIERRE", error.GetProperty("code").GetString());
        Assert.Equal("etapa_previa_sin_cierre", error.GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal(etapas[0], error.GetProperty("details").GetProperty("etapa_previa_id").GetGuid());
        Assert.Equal("Cargue", error.GetProperty("details").GetProperty("etapa_previa").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));

        // Una llegada en borrador tampoco alcanza; cerrada, si.
        await Sembrador.InformeAsync(etapas[0], sesion.UsuarioId, cerrada: false, momento: "llegada");
        Assert.Equal(HttpStatusCode.Conflict, (await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[1], momento = "salida", fecha = FECHA })).StatusCode);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE EtapaId = @e AND Momento = 'llegada'", ("@e", etapas[0]));

        var permitida = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[1], momento = "salida", fecha = FECHA });
        Assert.Equal(HttpStatusCode.Created, permitida.StatusCode);
    }

    [Fact]
    public async Task El_bloqueo_por_etapa_previa_solo_aplica_a_la_salida_no_a_la_llegada()
    {
        var (sesion, etapas) = await EscenarioAsync();

        var llegada = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[2], momento = "llegada", fecha = FECHA });

        Assert.Equal(HttpStatusCode.Created, llegada.StatusCode);
    }

    // ---------------------------------------------------------------- hallazgos

    [Fact]
    public async Task Un_hallazgo_exige_su_foto_propia_y_se_guarda_junto_con_ella()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, hash) = await SubirAsync(sesion);

        var sinFoto = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new { parte_vehiculo = "Capot", clase_dano = "golpe", tamano_dano = "1_5cm" });
        var hallazgosSinFoto = await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos");
        var ok = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            parte_vehiculo = "  Guardafango delantero derecho ",
            clase_dano = "golpe",
            tamano_dano = "5_10cm",
            coords_diagrama = new { x = 0.25, y = 0.75 },
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, precision = 3.0, dispositivo_id = "tablet-1", hash_sha256 = hash },
        });

        Assert.Equal(HttpStatusCode.BadRequest, sinFoto.StatusCode);
        Assert.True((await ClonarAsync(sinFoto)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("evidencia", out _));
        Assert.Equal(0, hallazgosSinFoto);

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var h = await ClonarAsync(ok);
        Assert.Equal("Guardafango delantero derecho", h.GetProperty("parte_vehiculo").GetString());
        Assert.Equal(0.75, h.GetProperty("coords_diagrama").GetProperty("y").GetDouble());
        Assert.Equal(id, h.GetProperty("inspeccion_id").GetGuid());
        var foto = h.GetProperty("evidencia");
        Assert.Equal(h.GetProperty("id").GetGuid(), foto.GetProperty("hallazgo_id").GetGuid());
        Assert.Equal(hash, foto.GetProperty("hash_sha256").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE HallazgoId IS NOT NULL"));
    }

    [Fact]
    public async Task Un_hallazgo_con_datos_invalidos_responde_400_por_campo_y_no_guarda_nada()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);

        var malo = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            parte_vehiculo = " ",
            clase_dano = "inventada",
            tamano_dano = "enorme",
            coords_diagrama = new { x = 2, y = 0.5 },
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        });

        Assert.Equal(HttpStatusCode.BadRequest, malo.StatusCode);
        var campos = (await ClonarAsync(malo)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        // El orden de los errores no importa: llegan los del cuerpo y los del catalogo juntos.
        Assert.Equal(["clase_dano", "coords_diagrama", "parte_vehiculo", "tamano_dano"], campos.Order().ToList());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias"));
    }

    [Fact]
    public async Task La_foto_de_un_hallazgo_debe_haberse_subido_y_su_hash_debe_coincidir()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);
        object Hallazgo(string rutaFoto, string? hash = null) => new
        {
            parte_vehiculo = "Capot",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            evidencia = new { storage_path = rutaFoto, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, hash_sha256 = hash },
        };

        var noSubida = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", Hallazgo("3f2b6c1e-0000-4000-8000-123456789abc.png"));
        var hashAjeno = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", Hallazgo(ruta, new string('a', 64)));

        Assert.Equal(HttpStatusCode.BadRequest, noSubida.StatusCode);
        Assert.True((await ClonarAsync(noSubida)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("storage_path", out _));
        Assert.Equal(HttpStatusCode.BadRequest, hashAjeno.StatusCode);
        Assert.True((await ClonarAsync(hashAjeno)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("hash_sha256", out _));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task Agregar_a_una_inspeccion_inexistente_responde_404()
    {
        var (sesion, _) = await EscenarioAsync();
        var id = Guid.NewGuid();
        var (ruta, _) = await SubirAsync(sesion);

        var r = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            parte_vehiculo = "Capot",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal($"Inspección {id} no encontrada", (await ClonarAsync(r)).GetProperty("message").GetString());
    }

    // ---------------------------------------------------------------- fotos (evidencias)

    [Fact]
    public async Task Cada_foto_guarda_su_propio_sello_el_del_servidor_el_hash_y_la_vista()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;

        var r = await FotoDeVistaAsync(sesion, id, vista, lat: 4.60971, lng: -74.08175);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var e = await ClonarAsync(r);
        Assert.Equal(
            [
                "id", "inspeccion_id", "hallazgo_id", "sello_fecha", "sello_lat", "sello_lng", "url", "created_at", "updated_at", "vista_id", "hash_sha256",
                "sello_servidor_at", "precision", "dispositivo_id", "nota", "sin_ubicacion",
            ],
            e.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(vista, e.GetProperty("vista_id").GetGuid());
        Assert.Equal(FECHA, e.GetProperty("sello_fecha").GetString());
        Assert.Equal(4.60971, e.GetProperty("sello_lat").GetDouble());
        Assert.Equal(4.5, e.GetProperty("precision").GetDouble());
        Assert.Equal("tablet-1", e.GetProperty("dispositivo_id").GetString());
        Assert.Equal(64, e.GetProperty("hash_sha256").GetString()!.Length);
        Assert.False(e.GetProperty("sin_ubicacion").GetBoolean());
        Assert.StartsWith("/api/uploads/", e.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, e.GetProperty("hallazgo_id").ValueKind);
        // El sello del servidor es la hora en que llego la foto (no la del dispositivo, que era de 2026).
        var recibida = DateTimeOffset.Parse(e.GetProperty("sello_servidor_at").GetString()!);
        Assert.InRange(recibida, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task El_informe_muestra_cada_foto_con_su_sello_y_el_del_servidor()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var vistas = await Sembrador.VistasAsync(obligatoria: true);
        await FotoDeVistaAsync(sesion, id, vistas[0].Id, lat: 4.6, lng: -74.1);
        await FotoDeVistaAsync(sesion, id, vistas[1].Id, lat: null, lng: null);

        var informe = await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{id}"));

        var fotos = informe.GetProperty("evidencias").EnumerateArray().ToList();
        Assert.Equal(2, fotos.Count);
        Assert.All(fotos, f =>
        {
            Assert.Equal(FECHA, f.GetProperty("sello_fecha").GetString());
            Assert.Equal(JsonValueKind.String, f.GetProperty("sello_servidor_at").ValueKind);
            Assert.Equal(JsonValueKind.String, f.GetProperty("hash_sha256").ValueKind);
        });
        Assert.False(fotos[0].GetProperty("sin_ubicacion").GetBoolean());
        Assert.True(fotos[1].GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal("Inspección de salida", informe.GetProperty("momento_nombre").GetString());
    }

    [Fact]
    public async Task Una_foto_exige_vista_o_hallazgo_y_no_ambos()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;
        var (ruta, _) = await SubirAsync(sesion);

        var ninguno = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 });
        var ambos = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new { storage_path = ruta, sello_fecha = FECHA, vista_id = vista, hallazgo_id = Guid.NewGuid() });
        var inexistente = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new { storage_path = ruta, sello_fecha = FECHA, vista_id = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, ninguno.StatusCode);
        Assert.True((await ClonarAsync(ninguno)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("vista_id", out _));
        Assert.Equal(HttpStatusCode.BadRequest, ambos.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.Equal("La vista no existe en el catálogo", (await ClonarAsync(inexistente)).GetProperty("details").GetProperty("fieldErrors").GetProperty("vista_id")[0].GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias"));
    }

    [Fact]
    public async Task Una_vista_inactiva_no_admite_fotos_nuevas()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;
        await CambiarVistaAsync(vista, new { activo = false });

        var r = await FotoDeVistaAsync(sesion, id, vista);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("La vista está inactiva", (await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").GetProperty("vista_id")[0].GetString());
    }

    [Theory]
    [InlineData("../secreto.jpg")]
    [InlineData("carpeta/foto.jpg")]
    [InlineData("a..b.jpg")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task La_evidencia_solo_acepta_nombres_planos_de_archivo(string ruta)
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;

        var r = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias", new { storage_path = ruta, sello_fecha = FECHA, vista_id = vista });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("storage_path", out _));
    }

    [Fact]
    public async Task La_foto_debe_haberse_subido_antes_y_su_hash_debe_coincidir_con_el_del_archivo()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;
        var (ruta, _) = await SubirAsync(sesion);

        var noSubida = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias",
            new { storage_path = "3f2b6c1e-0000-4000-8000-123456789abc.png", sello_fecha = FECHA, vista_id = vista, sello_lat = 4.6, sello_lng = -74.1 });
        var hashAjeno = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias",
            new { storage_path = ruta, sello_fecha = FECHA, vista_id = vista, sello_lat = 4.6, sello_lng = -74.1, hash_sha256 = new string('0', 64) });
        var hashMalFormado = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias",
            new { storage_path = ruta, sello_fecha = FECHA, vista_id = vista, hash_sha256 = "no-es-un-hash" });
        var sinHash = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/evidencias",
            new { storage_path = ruta, sello_fecha = FECHA, vista_id = vista, sello_lat = 4.6, sello_lng = -74.1 });

        Assert.Equal(HttpStatusCode.BadRequest, noSubida.StatusCode);
        Assert.True((await ClonarAsync(noSubida)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("storage_path", out _));
        Assert.Equal(HttpStatusCode.BadRequest, hashAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, hashMalFormado.StatusCode);
        // Sin hash del cliente, el servidor guarda el que calculo del archivo.
        Assert.Equal(HttpStatusCode.Created, sinHash.StatusCode);
        Assert.Equal(64, (await ClonarAsync(sinHash)).GetProperty("hash_sha256").GetString()!.Length);
    }

    [Fact]
    public async Task La_evidencia_vinculada_a_un_hallazgo_exige_que_pertenezca_a_la_misma_inspeccion()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var propia = await CrearInspeccionAsync(sesion, etapas[0]);
        var ajena = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
        var (rutaHallazgo, _) = await SubirAsync(sesion);
        var hallazgo = (await ClonarAsync(await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{propia}/hallazgos", new
        {
            parte_vehiculo = "Capot",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            evidencia = new { storage_path = rutaHallazgo, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        }))).GetProperty("id").GetGuid();
        var (ruta1, _) = await SubirAsync(sesion);
        var (ruta2, _) = await SubirAsync(sesion);

        var bien = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{propia}/evidencias", new { storage_path = ruta1, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, hallazgo_id = hallazgo });
        var mal = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{ajena}/evidencias", new { storage_path = ruta2, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, hallazgo_id = hallazgo });

        Assert.Equal(HttpStatusCode.Created, bien.StatusCode);
        Assert.Equal(hallazgo, (await ClonarAsync(bien)).GetProperty("hallazgo_id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, mal.StatusCode);
        Assert.Equal("El hallazgo indicado no pertenece a esta inspección", (await ClonarAsync(mal)).GetProperty("message").GetString());
    }

    // ---------------------------------------------------------------- las 7 vistas y el cierre

    [Fact]
    public async Task No_se_cierra_sin_las_7_vistas_y_el_error_nombra_la_que_falta()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id, salvo: 1);
        var faltante = (await Sembrador.VistasAsync(obligatoria: true)).Last().Nombre;

        var r = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var error = await ClonarAsync(r);
        Assert.Equal("La inspección no cumple los requisitos para cerrarse", error.GetProperty("message").GetString());
        Assert.Equal([$"Falta la foto de la vista «{faltante}»"], error.GetProperty("details").EnumerateArray().Select(d => d.GetString()).ToList());
        Assert.Equal("borrador", (string?)await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblInspecciones WHERE Id = @i", ("@i", id)));
    }

    [Fact]
    public async Task Sin_ninguna_foto_el_cierre_nombra_las_siete_vistas()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var r = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(7, (await ClonarAsync(r)).GetProperty("details").GetArrayLength());
    }

    [Fact]
    public async Task Con_las_7_vistas_se_cierra_con_su_hora_de_cierre_y_las_adicionales_no_bloquean()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id);

        var r = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var c = await ClonarAsync(r);
        Assert.Equal(["inspeccion", "resumen"], c.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("cerrada", c.GetProperty("inspeccion").GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.String, c.GetProperty("inspeccion").GetProperty("cerrada_at").ValueKind);
        Assert.Equal(7, c.GetProperty("resumen").GetProperty("evidencias").GetInt32());
        Assert.Equal(0, c.GetProperty("resumen").GetProperty("hallazgos").GetInt32());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.cerrar'"));
    }

    [Fact]
    public async Task Las_vistas_obligatorias_de_un_tipo_de_vehiculo_solo_se_exigen_a_ese_tipo()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var vistas = await Sembrador.VistasAsync(obligatoria: true);
        // Una vista obligatoria solo de camiones: no se exige a un vehiculo sin tipo, y se exige a un camion.
        await CambiarVistaAsync(vistas[0].Id, new { tipo_vehiculo = "camion" });
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        foreach (var (vistaId, _) in vistas.Skip(1))
        {
            await FotoDeVistaAsync(sesion, id, vistaId);
        }

        var sinTipo = await CerrarAsync(sesion, id);
        Assert.Equal(HttpStatusCode.OK, sinTipo.StatusCode);

        var (_, etapasCamion) = await Sembrador.VehiculoConRutaAsync("VIN-CAMION-001", inspectorId: sesion.UsuarioId);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET TipoVehiculo = 'camion' WHERE Vin = 'VIN-CAMION-001'");
        var idCamion = await CrearInspeccionAsync(sesion, etapasCamion[0]);
        foreach (var (vistaId, _) in vistas.Skip(1))
        {
            await FotoDeVistaAsync(sesion, idCamion, vistaId);
        }

        var camion = await CerrarAsync(sesion, idCamion);
        Assert.Equal(HttpStatusCode.BadRequest, camion.StatusCode);
        Assert.Contains(vistas[0].Nombre, (await ClonarAsync(camion)).GetProperty("details")[0].GetString());
    }

    [Fact]
    public async Task Una_vista_obligatoria_inactiva_ya_no_se_exige()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var vistas = await Sembrador.VistasAsync(obligatoria: true);
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        foreach (var (vistaId, _) in vistas.Skip(1))
        {
            await FotoDeVistaAsync(sesion, id, vistaId);
        }

        await CambiarVistaAsync(vistas[0].Id, new { activo = false });

        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(sesion, id)).StatusCode);
    }

    // ---------------------------------------------------------------- fotos adicionales

    [Fact]
    public async Task Las_fotos_de_baul_y_cojineria_exigen_su_nota_y_quedan_guardadas_con_su_vista()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var adicionales = await Sembrador.VistasAsync(obligatoria: false);
        Assert.Equal(2, adicionales.Count);

        var sinNota = await FotoDeVistaAsync(sesion, id, adicionales[0].Id);
        var conNota = await FotoDeVistaAsync(sesion, id, adicionales[0].Id, nota: "Falta la rueda de repuesto");
        var cojineria = await FotoDeVistaAsync(sesion, id, adicionales[1].Id, nota: "Asiento trasero rasgado");

        Assert.Equal(HttpStatusCode.BadRequest, sinNota.StatusCode);
        Assert.True((await ClonarAsync(sinNota)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("nota", out _));
        Assert.Equal(HttpStatusCode.Created, conNota.StatusCode);
        var foto = await ClonarAsync(conNota);
        Assert.Equal(adicionales[0].Id, foto.GetProperty("vista_id").GetGuid());
        Assert.Equal("Falta la rueda de repuesto", foto.GetProperty("nota").GetString());
        Assert.Equal(FECHA, foto.GetProperty("sello_fecha").GetString());
        Assert.Equal(4.6, foto.GetProperty("sello_lat").GetDouble());
        Assert.Equal(HttpStatusCode.Created, cojineria.StatusCode);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE Nota IS NOT NULL"));
    }

    [Fact]
    public async Task Una_inspeccion_sin_faltantes_ni_danos_en_baul_y_cojineria_se_cierra_sin_esas_fotos()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id);

        var r = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias e JOIN dbo.tblVistasFoto v ON v.Id = e.VistaId WHERE v.Obligatoria = 0"));
    }

    // ---------------------------------------------------------------- foto sin ubicacion

    [Fact]
    public async Task Una_foto_sin_ubicacion_aparece_marcada_y_bloquea_el_cierre_hasta_repetirla()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var vistas = await Sembrador.VistasAsync(obligatoria: true);
        foreach (var (vistaId, _) in vistas.Skip(1))
        {
            await FotoDeVistaAsync(sesion, id, vistaId);
        }

        var sinGps = await FotoDeVistaAsync(sesion, id, vistas[0].Id, lat: null, lng: null);
        var cierre = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.Created, sinGps.StatusCode);
        Assert.True((await ClonarAsync(sinGps)).GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, cierre.StatusCode);
        Assert.Equal("1 foto(s) sin ubicación: repítalas con GPS antes de cerrar", (await ClonarAsync(cierre)).GetProperty("details")[0].GetString());

        // La app la repite con GPS: la nueva reemplaza a la anterior (que queda guardada) y ya se puede cerrar.
        var repetida = await FotoDeVistaAsync(sesion, id, vistas[0].Id);
        Assert.False((await ClonarAsync(repetida)).GetProperty("sin_ubicacion").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(sesion, id)).StatusCode);
        Assert.Equal(8, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE InspeccionId = @i", ("@i", id)));
    }

    // ---------------------------------------------------------------- hallazgos y cierre

    [Fact]
    public async Task Una_novedad_con_su_foto_se_cierra_y_cuenta_en_el_resumen()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id);
        var (ruta, _) = await SubirAsync(sesion);
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            parte_vehiculo = "Capot",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        });

        var r = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var resumen = (await ClonarAsync(r)).GetProperty("resumen");
        Assert.Equal(8, resumen.GetProperty("evidencias").GetInt32());
        Assert.Equal(1, resumen.GetProperty("hallazgos").GetInt32());
    }

    [Fact]
    public async Task Un_hallazgo_historico_sin_foto_bloquea_el_cierre()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id);
        await Sembrador.EjecutarAsync("INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (NEWID(), @i, N'Capot', 'golpe', '1_5cm')", ("@i", id));

        var r = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("hallazgo(s) sin foto propia", (await ClonarAsync(r)).GetProperty("details")[0].GetString());
    }

    [Fact]
    public async Task Una_inspeccion_cerrada_es_inmutable_y_cerrarla_de_nuevo_es_409()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id);
        var cierre = await CerrarAsync(sesion, id);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;
        var (ruta, _) = await SubirAsync(sesion);

        var otraVez = await CerrarAsync(sesion, id);
        var hallazgo = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            parte_vehiculo = "Capot",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        });
        var evidencia = await FotoDeVistaAsync(sesion, id, vista);

        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        Assert.Equal("La inspección ya está cerrada", (await ClonarAsync(otraVez)).GetProperty("message").GetString());
        foreach (var r in new[] { hallazgo, evidencia })
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("La inspección está cerrada y es inmutable; no admite cambios", (await ClonarAsync(r)).GetProperty("message").GetString());
        }
    }

    // ---------------------------------------------------------------- transporte

    [Fact]
    public async Task En_transporte_la_salida_y_la_llegada_se_cierran_sin_las_7_vistas_pero_con_su_gps()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await LlegadaCerradaAsync(etapas[0], sesion.UsuarioId);
        var sinGps = await CrearInspeccionAsync(sesion, etapas[1], conGps: false);
        var conGps = await CrearInspeccionAsync(sesion, etapas[1], "llegada");

        var rechazada = await CerrarAsync(sesion, sinGps);
        var salida = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[2], momento = "llegada", fecha = FECHA, lat = 4.6, lng = -74.1 });
        var cerrada = await CerrarAsync(sesion, conGps);

        Assert.Equal(HttpStatusCode.BadRequest, rechazada.StatusCode);
        Assert.Contains("ubicación", (await ClonarAsync(rechazada)).GetProperty("details")[0].GetString());
        Assert.Equal(HttpStatusCode.OK, cerrada.StatusCode);
        Assert.Equal(0, (await ClonarAsync(cerrada)).GetProperty("resumen").GetProperty("evidencias").GetInt32());
        Assert.Equal(HttpStatusCode.Created, salida.StatusCode);
    }

    [Fact]
    public async Task En_transporte_una_novedad_lleva_su_foto_y_cuenta_para_la_cobertura()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await LlegadaCerradaAsync(etapas[0], sesion.UsuarioId);
        var id = await CrearInspeccionAsync(sesion, etapas[1]);
        var (ruta, _) = await SubirAsync(sesion);
        var novedad = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            parte_vehiculo = "Capot",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        });

        var cierre = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.Created, novedad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Estado = 'cerrada'", ("@e", etapas[1])));
    }

    // ---------------------------------------------------------------- cargue sin personal de apoyo

    [Fact]
    public async Task Sin_personal_de_apoyo_el_conductor_hace_la_salida_con_las_mismas_reglas_del_inspector()
    {
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-COND-0001", conductorId: conductor.UsuarioId);
        var id = await CrearInspeccionAsync(conductor, etapas[0]);
        await TomarVistasObligatoriasAsync(conductor, id, salvo: 2);

        var incompleta = await CerrarAsync(conductor, id);
        var faltantes = (await Sembrador.VistasAsync(obligatoria: true)).TakeLast(2).Select(v => (string?)$"Falta la foto de la vista «{v.Nombre}»").ToList();
        foreach (var (vistaId, _) in (await Sembrador.VistasAsync(obligatoria: true)).TakeLast(2))
        {
            await FotoDeVistaAsync(conductor, id, vistaId);
        }

        var completa = await CerrarAsync(conductor, id);

        Assert.Equal(HttpStatusCode.BadRequest, incompleta.StatusCode);
        Assert.Equal(faltantes, (await ClonarAsync(incompleta)).GetProperty("details").EnumerateArray().Select(d => d.GetString()).ToList());
        Assert.Equal(HttpStatusCode.OK, completa.StatusCode);
    }

    // ---------------------------------------------------------------- correccion por version

    [Fact]
    public async Task Corregir_crea_una_version_nueva_que_referencia_a_la_anterior_y_la_anterior_no_cambia()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id);
        await CerrarAsync(sesion, id);
        var antes = await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{id}"));

        var r = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/corregir", new { notas = "Se repiten las fotos", lat = 5.0, lng = -75.0 });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var v2 = await ClonarAsync(r);
        Assert.Equal(2, v2.GetProperty("version").GetInt32());
        Assert.Equal(id, v2.GetProperty("corrige_inspeccion_id").GetGuid());
        Assert.Equal("borrador", v2.GetProperty("estado").GetString());
        Assert.Equal("salida", v2.GetProperty("momento").GetString());
        Assert.Equal(etapas[0], v2.GetProperty("etapa_id").GetGuid());
        Assert.Equal(JsonValueKind.Null, v2.GetProperty("cerrada_at").ValueKind);
        Assert.Equal("Se repiten las fotos", v2.GetProperty("notas").GetString());
        Assert.NotEqual(id, v2.GetProperty("id").GetGuid());

        // La anterior sigue igual: cerrada, con sus 7 fotos y la misma hora de cierre.
        var despues = await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{id}"));
        Assert.Equal("cerrada", despues.GetProperty("estado").GetString());
        Assert.Equal(antes.GetProperty("cerrada_at").GetString(), despues.GetProperty("cerrada_at").GetString());
        Assert.Equal(antes.GetProperty("updated_at").GetString(), despues.GetProperty("updated_at").GetString());
        Assert.Equal(7, despues.GetProperty("evidencias").GetArrayLength());
        Assert.Equal(1, despues.GetProperty("version").GetInt32());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.corregir'"));
    }

    [Fact]
    public async Task La_correccion_es_una_inspeccion_nueva_con_sus_propias_reglas_de_cierre()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id);
        await CerrarAsync(sesion, id);
        var v2 = (await ClonarAsync(await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/corregir", null))).GetProperty("id").GetGuid();

        var vacia = await CerrarAsync(sesion, v2);
        await TomarVistasObligatoriasAsync(sesion, v2);
        var completa = await CerrarAsync(sesion, v2);
        var v3 = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{v2}/corregir", null);

        Assert.Equal(HttpStatusCode.BadRequest, vacia.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completa.StatusCode);
        Assert.Equal(HttpStatusCode.Created, v3.StatusCode);
        Assert.Equal(3, (await ClonarAsync(v3)).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Una_version_solo_se_corrige_una_vez_y_solo_se_corrige_lo_cerrado()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var enBorrador = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/corregir", null);
        await TomarVistasObligatoriasAsync(sesion, id);
        await CerrarAsync(sesion, id);
        var primera = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/corregir", null);
        var segunda = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/corregir", null);
        var inexistente = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{Guid.NewGuid()}/corregir", null);

        Assert.Equal(HttpStatusCode.Conflict, enBorrador.StatusCode);
        Assert.Contains("Solo se puede corregir una inspección cerrada", (await ClonarAsync(enBorrador)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Contains("ya fue corregida", (await ClonarAsync(segunda)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e", ("@e", etapas[0])));
    }

    [Fact]
    public async Task Quien_no_participa_en_el_viaje_no_corrige_y_un_viaje_cancelado_tampoco_admite_correcciones()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var ajeno = await SesionAsync("ajeno@ejemplo.com");
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id);
        await CerrarAsync(sesion, id);

        var deAjeno = await Cliente(ajeno.Token).PostAsync($"/api/inspecciones/{id}/corregir", null);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET Estado = 'cancelado' WHERE Id = @v", ("@v", await Sembrador.ViajeDeEtapaAsync(etapas[0])));
        // El viaje cancelado ya no es visible para el inspector (403); un administrador si llega, y el viaje no admite correcciones (409).
        var delInspector = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/corregir", null);
        var cancelado = await Cliente(admin.Token).PostAsync($"/api/inspecciones/{id}/corregir", null);

        Assert.Equal(HttpStatusCode.Forbidden, deAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delInspector.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelado.StatusCode);
    }

    [Fact]
    public async Task Corregir_datos_invalidos_responde_400()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, id);
        await CerrarAsync(sesion, id);

        var r = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/corregir", new { lat = 95 });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ---------------------------------------------------------------- propiedad

    [Fact]
    public async Task Solo_el_creador_o_un_administrador_modifica_la_inspeccion()
    {
        var (creador, etapas) = await EscenarioAsync("creador@ejemplo.com");
        var intruso = await SesionAsync("intruso@ejemplo.com");
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var id = await CrearInspeccionAsync(creador, etapas[0]);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;

        var deIntruso = await FotoDeVistaAsync(intruso, id, vista);
        var cierreIntruso = await CerrarAsync(intruso, id);
        var deAdmin = await FotoDeVistaAsync(admin, id, vista);

        Assert.Equal(HttpStatusCode.Forbidden, deIntruso.StatusCode);
        Assert.Equal("No tiene permisos sobre esta inspección", (await ClonarAsync(deIntruso)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, cierreIntruso.StatusCode);
        Assert.Equal(HttpStatusCode.Created, deAdmin.StatusCode);
    }

    [Fact]
    public async Task La_validacion_de_propiedad_se_puede_desactivar_por_configuracion()
    {
        var (creador, etapas) = await EscenarioAsync("creador@ejemplo.com");
        var intruso = await SesionAsync("intruso@ejemplo.com");
        var id = await CrearInspeccionAsync(creador, etapas[0]);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;
        var abierta = CrearFabrica(new Dictionary<string, string?> { ["Seguridad:ValidarPropiedadInspeccion"] = "false" });
        // Cada fabrica tiene su propio almacenamiento: la foto se sube por la misma que recibe la evidencia.
        var subida = await Cliente(intruso.Token, abierta).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(FIRMA_PNG.Concat(Guid.NewGuid().ToByteArray()).ToArray()), ext = "png" });
        var ruta = (await ClonarAsync(subida)).GetProperty("storage_path").GetString();

        var r = await Cliente(intruso.Token, abierta).PostAsJsonAsync(
            $"/api/inspecciones/{id}/evidencias",
            new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, vista_id = vista });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    // ---------------------------------------------------------------- transferencia

    [Fact]
    public async Task La_firma_parcial_se_acumula_y_el_sello_se_fija_al_completarla()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var receptor = await SesionAsync("recibe@ejemplo.com", clsRol.CONDUCTOR);
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var primera = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia",
            new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_entrega = "M0 0 L10 10" });
        var t1 = await ClonarAsync(primera);
        var segunda = await Cliente(receptor.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia",
            new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_recibe = "M5 5 L20 20" });
        var t2 = await ClonarAsync(segunda);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(
            ["id", "inspeccion_id", "entrega_usuario_id", "recibe_usuario_id", "firmada", "firmada_at", "created_at", "updated_at"],
            t1.EnumerateObject().Select(p => p.Name).ToList());
        Assert.False(t1.GetProperty("firmada").GetBoolean());
        Assert.Equal(JsonValueKind.Null, t1.GetProperty("firmada_at").ValueKind);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.True(t2.GetProperty("firmada").GetBoolean());
        Assert.Equal(JsonValueKind.String, t2.GetProperty("firmada_at").ValueKind);
        Assert.Equal(t1.GetProperty("id").GetGuid(), t2.GetProperty("id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal("M0 0 L10 10", (string?)await Sembrador.EscalarAsync("SELECT FirmaEntrega FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task Se_puede_firmar_despues_de_cerrar_con_cualquier_usuario_de_campo_pero_una_transferencia_completa_es_inmutable()
    {
        var (creador, etapas) = await EscenarioAsync("creador@ejemplo.com");
        var tercero = await SesionAsync("tercero@ejemplo.com");
        var receptor = await SesionAsync("recibe@ejemplo.com", clsRol.CONDUCTOR);
        var id = await CrearInspeccionAsync(creador, etapas[0]);
        await TomarVistasObligatoriasAsync(creador, id);
        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(creador, id)).StatusCode);
        var cuerpo = new { entrega_usuario_id = creador.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_entrega = "A", firma_recibe = "B" };

        var firma = await Cliente(tercero.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", cuerpo);
        var repetida = await Cliente(tercero.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", cuerpo);
        var alterada = await Cliente(tercero.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia",
            new { entrega_usuario_id = creador.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_entrega = "OTRA", firma_recibe = "B" });

        Assert.Equal(HttpStatusCode.Created, firma.StatusCode);
        Assert.True((await ClonarAsync(firma)).GetProperty("firmada").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, repetida.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, alterada.StatusCode);
        Assert.Equal("La transferencia ya está firmada por ambas partes y es inmutable", (await ClonarAsync(alterada)).GetProperty("message").GetString());
        Assert.Equal("A", (string?)await Sembrador.EscalarAsync("SELECT FirmaEntrega FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task La_transferencia_valida_usuarios_distintos_y_existentes()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);

        var mismo = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = sesion.UsuarioId });
        var inexistente = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = Guid.NewGuid() });
        var sinIds = await Cliente(sesion.Token).PostAsync($"/api/inspecciones/{id}/transferencia", Json("{}"));
        var inspeccionAjena = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{Guid.NewGuid()}/transferencia", new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, mismo.StatusCode);
        Assert.Equal(
            ["Quien entrega y quien recibe deben ser usuarios distintos"],
            (await ClonarAsync(mismo)).GetProperty("details").GetProperty("fieldErrors").GetProperty("recibe_usuario_id").EnumerateArray().Select(m => m.GetString()).ToList());
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("Algún usuario (entrega/recibe) no existe", (await ClonarAsync(inexistente)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinIds.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inspeccionAjena.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    // ---------------------------------------------------------------- informe y listado

    [Fact]
    public async Task El_informe_consolida_contexto_hallazgos_evidencias_y_transferencia_con_nombres_y_firmas()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var receptor = await SesionAsync("recibe@ejemplo.com", clsRol.CONDUCTOR);
        // El receptor es el conductor del viaje: solo ve los informes de sus viajes.
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajes SET ConductorId = @c WHERE Id = @v", ("@c", receptor.UsuarioId), ("@v", await Sembrador.ViajeDeEtapaAsync(etapas[0])));
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            parte_vehiculo = "Capot",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            coords_diagrama = new { x = 0.1, y = 0.2 },
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        });
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma_entrega = "TRAZO-A", firma_recibe = "TRAZO-B" });

        var r = await Cliente(receptor.Token).GetAsync($"/api/inspecciones/{id}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var inf = await ClonarAsync(r);
        Assert.Equal("VIN-INSP-0001", inf.GetProperty("vin").GetString());
        Assert.Equal("Renault", inf.GetProperty("marca").GetString());
        Assert.Equal("cargue", inf.GetProperty("proceso").GetString());
        Assert.True(inf.GetProperty("requiere_inspeccion").GetBoolean());
        Assert.Equal("Nombre insp@ejemplo.com", inf.GetProperty("inspector_nombre").GetString());
        Assert.Equal(1, inf.GetProperty("hallazgos").GetArrayLength());
        Assert.Equal($"/api/uploads/{ruta}", inf.GetProperty("evidencias")[0].GetProperty("url").GetString());
        Assert.Equal(1, inf.GetProperty("version").GetInt32());
        Assert.Equal("Inspección de salida", inf.GetProperty("momento_nombre").GetString());
        var t = inf.GetProperty("transferencia");
        Assert.Equal("Nombre insp@ejemplo.com", t.GetProperty("entrega_nombre").GetString());
        Assert.Equal("Nombre recibe@ejemplo.com", t.GetProperty("recibe_nombre").GetString());
        Assert.Equal("TRAZO-A", t.GetProperty("firma_entrega").GetString());
        Assert.Equal("TRAZO-B", t.GetProperty("firma_recibe").GetString());
        Assert.True(t.GetProperty("firmada").GetBoolean());
    }

    [Fact]
    public async Task La_inspeccion_toma_la_placa_el_trailer_y_el_conductor_del_viaje_y_lo_capturado_antes_tiene_prioridad()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var conductor = await Fabrica.SembrarUsuarioAsync("cond@ejemplo.com", "x", clsRol.CONDUCTOR, nombre: "Conductor Del Viaje");
        var viajeId = await Sembrador.ViajeDeEtapaAsync(etapas[0]);
        await Sembrador.EjecutarAsync(
            "UPDATE dbo.tblViajes SET Placa = N'NIN-VIAJE', Trailer = N'TRL-VIAJE', ConductorId = @c, CodigoMtrack = 'MTK-INF-1' WHERE Id = @v",
            ("@c", conductor.Id), ("@v", viajeId));
        var nueva = await Cliente(sesion.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "salida", fecha = FECHA });
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Estado, Placa, ConductorNombre) VALUES (NEWID(), @e, @v, @u, 'llegada', SYSDATETIMEOFFSET(), 'borrador', N'PLACA-ANTIGUA', N'Capturado antes de los viajes')",
            ("@e", etapas[0]), ("@v", viajeId), ("@u", sesion.UsuarioId));

        var creada = await ClonarAsync(nueva);
        var informe = await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{creada.GetProperty("id").GetGuid()}"));
        var antiguaId = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblInspecciones WHERE Momento = 'llegada'"))!;
        var antigua = await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{antiguaId}"));

        Assert.Equal("NIN-VIAJE", creada.GetProperty("placa").GetString());
        Assert.Equal("TRL-VIAJE", creada.GetProperty("trailer").GetString());
        Assert.Equal("Conductor Del Viaje", creada.GetProperty("conductor_nombre").GetString());
        Assert.Equal(viajeId, informe.GetProperty("viaje_id").GetGuid());
        Assert.Equal("NIN-VIAJE", informe.GetProperty("ninera").GetString());
        Assert.Equal("NIN-VIAJE", informe.GetProperty("placa").GetString());
        Assert.Equal("Conductor Del Viaje", informe.GetProperty("conductor_nombre").GetString());
        Assert.Equal("MTK-INF-1", informe.GetProperty("codigo_mtrack").GetString());
        Assert.Equal("Niñera", informe.GetProperty("unidad").GetString());
        Assert.Equal("PLACA-ANTIGUA", antigua.GetProperty("placa").GetString());
        Assert.Equal("Capturado antes de los viajes", antigua.GetProperty("conductor_nombre").GetString());
    }

    [Fact]
    public async Task El_informe_de_una_inspeccion_inexistente_responde_404()
    {
        var (sesion, _) = await EscenarioAsync();

        var r = await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task El_listado_filtra_ordena_y_devuelve_vin_proceso_version_y_nombre_en_pantalla()
    {
        var (sesion, etapas) = await EscenarioAsync("uno@ejemplo.com");
        var otra = await SesionAsync("dos@ejemplo.com");
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var (_, etapas2) = await Sembrador.VehiculoConRutaAsync("9FBHSR4E8PM000999", inspectorId: otra.UsuarioId);
        await LlegadaCerradaAsync(etapas2[0], otra.UsuarioId);
        var a = await CrearInspeccionAsync(sesion, etapas[0]);
        await TomarVistasObligatoriasAsync(sesion, a);
        await CerrarAsync(sesion, a);
        await Task.Delay(20);
        var b = await CrearInspeccionAsync(otra, etapas2[1]);

        var todas = await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones"));
        var porUsuario = await ClonarAsync(await Cliente(coordinador.Token).GetAsync($"/api/inspecciones?usuarioId={otra.UsuarioId}"));
        var porEstado = await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?estado=cerrada"));
        var porMomento = await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?momento=llegada"));
        var porProceso = await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?proceso=transporte"));
        var porVin = await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?vin=9fbhsr4e8pm000999"));
        var porFecha = await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?desde=2026-07-01T00:00:00Z&hasta=2026-07-31T00:00:00Z"));

        Assert.Equal(3, todas.GetArrayLength());
        Assert.Equal(["iniciada_at", "cerrada_at", "version", "corrige_inspeccion_id", "momento_nombre"], todas[0].EnumerateObject().Select(p => p.Name).TakeLast(5).ToList());
        Assert.Equal(["vin", "proceso"], todas[0].EnumerateObject().Select(p => p.Name).Skip(15).Take(2).ToList());
        Assert.Equal(2, porUsuario.GetArrayLength());
        Assert.Contains(porUsuario.EnumerateArray(), i => i.GetProperty("id").GetGuid() == b);
        Assert.Equal(a, porEstado.EnumerateArray().Single(i => i.GetProperty("momento").GetString() == "salida").GetProperty("id").GetGuid());
        Assert.Equal(1, porMomento.EnumerateArray().Count(i => i.GetProperty("momento_nombre").GetString() == "Inspección de llegada"));
        Assert.Equal("transporte", porProceso[0].GetProperty("proceso").GetString());
        Assert.Equal("9FBHSR4E8PM000999", porVin[0].GetProperty("vin").GetString());
        // Solo las dos creadas por la API llevan la fecha del dispositivo (julio de 2026); la llegada sembrada lleva la de hoy.
        Assert.Equal(2, porFecha.GetArrayLength());
    }

    [Fact]
    public async Task El_listado_valida_los_filtros()
    {
        var (sesion, _) = await EscenarioAsync();

        var r = await Cliente(sesion.Token).GetAsync("/api/inspecciones?vin=corto&usuarioId=no&momento=x&estado=y&proceso=z&desde=ayer");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var campos = (await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["vin", "usuarioId", "momento", "estado", "proceso", "desde"], campos);
    }

    [Fact]
    public async Task Todos_los_endpoints_de_inspecciones_exigen_autenticacion()
    {
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync("/api/inspecciones")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync($"/api/inspecciones/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsync("/api/inspecciones", Json("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsync($"/api/inspecciones/{id}/cerrar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsync($"/api/inspecciones/{id}/corregir", null)).StatusCode);
    }
}
