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
[Collection(clsColecciones.BASE_DE_DATOS_1)]
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

    /// <summary>Crea la inspeccion. La salida del cargue que hace el conductor lleva el motivo de la excepcion «No hay inspector» (<paramref name="motivo"/>).</summary>
    private async Task<Guid> CrearInspeccionAsync(Sesion sesion, Guid etapaId, string momento = "salida", bool conGps = true, string? motivo = null)
    {
        var r = await Cliente(sesion.Token).PostAsJsonAsync(
            "/api/inspecciones",
            new Dictionary<string, object?>
            {
                ["etapa_id"] = etapaId, ["momento"] = momento, ["fecha"] = FECHA, ["lat"] = conGps ? 4.6 : null, ["lng"] = conGps ? -74.1 : null, ["motivo_sin_inspector"] = motivo,
            });
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
                "created_at", "updated_at", "iniciada_at", "cerrada_at", "version", "corrige_inspeccion_id", "momento_nombre", "motivo_sin_inspector", "novedades_proceso",
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

    /// <summary>Cuerpo de un hallazgo con su foto ya subida; <c>zona</c> trae los campos de la zona (cuadrante + lado, o zona provisional).</summary>
    private static Dictionary<string, object?> CuerpoHallazgo(string ruta, object zona, string clase = "golpe", string? nivel = "v2", string? hash = null)
    {
        var cuerpo = new Dictionary<string, object?>();
        foreach (var propiedad in zona.GetType().GetProperties())
        {
            cuerpo[propiedad.Name] = propiedad.GetValue(zona);
        }

        cuerpo["clase_dano"] = clase;
        cuerpo["tamano_dano"] = "5_10cm";
        if (nivel is not null)
        {
            cuerpo["nivel_severidad"] = nivel;
        }

        cuerpo["evidencia"] = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, hash_sha256 = hash };
        return cuerpo;
    }

    private static readonly object ZONA_IZQUIERDO_7 = new { cuadrante_numero = 7, lado = "izquierdo" };

    private async Task<HttpResponseMessage> PostHallazgoAsync(Sesion sesion, Guid inspeccionId, Dictionary<string, object?> cuerpo) =>
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{inspeccionId}/hallazgos", cuerpo);

    private static async Task<string?> ErrorDeCampoAsync(HttpResponseMessage r, string campo)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errores = (await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors");
        return errores.TryGetProperty(campo, out var mensajes) ? mensajes[0].GetString() : null;
    }

    [Fact]
    public async Task Un_hallazgo_exige_su_foto_propia_y_se_guarda_junto_con_ella_con_su_zona_y_su_nivel()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await Sembrador.CuadranteAsync(7, "Puerta delantera");
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, hash) = await SubirAsync(sesion);

        var sinFoto = await Cliente(sesion.Token).PostAsJsonAsync(
            $"/api/inspecciones/{id}/hallazgos", new { cuadrante_numero = 7, lado = "izquierdo", clase_dano = "golpe", tamano_dano = "1_5cm", nivel_severidad = "v2" });
        var hallazgosSinFoto = await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos");
        var cuerpo = CuerpoHallazgo(ruta, ZONA_IZQUIERDO_7, hash: hash);
        cuerpo["nota"] = "  Rayon largo  ";
        var ok = await PostHallazgoAsync(sesion, id, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, sinFoto.StatusCode);
        Assert.True((await ClonarAsync(sinFoto)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("evidencia", out _));
        Assert.Equal(0, hallazgosSinFoto);

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var h = await ClonarAsync(ok);
        Assert.Equal(id, h.GetProperty("inspeccion_id").GetGuid());
        Assert.Equal(7, h.GetProperty("cuadrante_numero").GetInt32());
        Assert.Equal("izquierdo", h.GetProperty("lado").GetString());
        Assert.Equal(JsonValueKind.Null, h.GetProperty("zona_provisional").ValueKind);
        Assert.Equal("Puerta delantera", h.GetProperty("cuadrante_nombre").GetString());
        Assert.Equal("Izquierdo 7 · Puerta delantera", h.GetProperty("zona").GetString());
        Assert.Equal("golpe", h.GetProperty("clase_dano").GetString());
        Assert.Equal("5_10cm", h.GetProperty("tamano_dano").GetString());
        Assert.Equal("v2", h.GetProperty("nivel_severidad").GetString());
        Assert.Equal("V2", h.GetProperty("nivel_nombre").GetString());
        Assert.Equal("Validación por Sofasa", h.GetProperty("nivel_accion").GetString());
        Assert.Equal("inspector", h.GetProperty("origen").GetString());
        Assert.Equal("Rayon largo", h.GetProperty("nota").GetString());
        Assert.False(h.GetProperty("es_preexistente").GetBoolean());
        Assert.Equal(JsonValueKind.Null, h.GetProperty("hallazgo_origen_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("confirmado_por").ValueKind);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("tipo_novedad").ValueKind);
        Assert.Equal("pendiente", h.GetProperty("estado_seguimiento").GetString());
        Assert.False(h.GetProperty("evidencia_completa").GetBoolean());
        // Tiene su foto propia; le falta que se cierre la transferencia de su inspeccion.
        Assert.Equal(["transferencia_sin_cierre"], h.GetProperty("evidencia_faltante").EnumerateArray().Select(x => x.GetString()).ToList());
        Assert.Equal(JsonValueKind.Null, h.GetProperty("revisado_por").ValueKind);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("revisado_at").ValueKind);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("parte_vehiculo").ValueKind);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("coords_diagrama").ValueKind);
        var foto = h.GetProperty("evidencia");
        Assert.Equal(h.GetProperty("id").GetGuid(), foto.GetProperty("hallazgo_id").GetGuid());
        Assert.Equal(hash, foto.GetProperty("hash_sha256").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE HallazgoId IS NOT NULL"));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblHallazgos h JOIN dbo.tblZonasDano z ON z.Id = h.ZonaId WHERE z.Numero = 7 AND z.Lado = 'izquierdo' AND h.NivelSeveridad = 'v2' AND h.Origen = 'inspector'"));
    }

    [Fact]
    public async Task Un_hallazgo_en_una_zona_provisional_se_guarda_con_su_nombre_y_sin_cuadrante()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);

        var r = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { zona_provisional = "llanta_trasera_derecha" }, nivel: "v1"));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var h = await ClonarAsync(r);
        Assert.Equal("llanta_trasera_derecha", h.GetProperty("zona_provisional").GetString());
        Assert.Equal("Llanta trasera derecha", h.GetProperty("zona").GetString());
        Assert.Equal(JsonValueKind.Null, h.GetProperty("cuadrante_numero").ValueKind);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("lado").ValueKind);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("cuadrante_nombre").ValueKind);
        Assert.Equal("Se rechaza el vehículo", h.GetProperty("nivel_accion").GetString());
    }

    [Fact]
    public async Task Un_hallazgo_con_datos_invalidos_responde_400_por_campo_y_no_guarda_nada()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await Sembrador.CuadranteAsync(7, "Puerta delantera");
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);
        var cuerpo = CuerpoHallazgo(ruta, new { cuadrante_numero = 7 }, clase: "inventada", nivel: "v9");
        cuerpo["tamano_dano"] = "enorme";

        var malo = await PostHallazgoAsync(sesion, id, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, malo.StatusCode);
        var campos = (await ClonarAsync(malo)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        // El orden de los errores no importa: llegan los del cuerpo y los del catalogo juntos.
        Assert.Equal(["clase_dano", "lado", "nivel_severidad", "tamano_dano"], campos.Order().ToList());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias"));
    }

    [Fact]
    public async Task El_lado_es_obligatorio_del_1_al_25_y_no_se_admite_del_26_en_adelante()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await Sembrador.CuadranteAsync(7, "Puerta delantera");
        await Sembrador.CuadranteAsync(30, "Techo");
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);

        var sinLado = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { cuadrante_numero = 7 }));
        var ladoInvalido = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { cuadrante_numero = 7, lado = "arriba" }));
        var conLadoEn30 = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { cuadrante_numero = 30, lado = "derecho" }));
        var sin30 = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { cuadrante_numero = 30 }));

        Assert.Contains("lado es requerido", await ErrorDeCampoAsync(sinLado, "lado"));
        Assert.NotNull(await ErrorDeCampoAsync(ladoInvalido, "lado"));
        Assert.Contains("26 en adelante", await ErrorDeCampoAsync(conLadoEn30, "lado"));
        Assert.Equal(HttpStatusCode.Created, sin30.StatusCode);
        var h = await ClonarAsync(sin30);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("lado").ValueKind);
        Assert.Equal("Techo 30", h.GetProperty("zona").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task La_zona_es_un_cuadrante_o_una_zona_provisional_exactamente_una_de_las_dos()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await Sembrador.CuadranteAsync(7, "Puerta delantera");
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);

        var ninguna = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { }));
        var ambas = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { cuadrante_numero = 7, lado = "izquierdo", zona_provisional = "frente" }));
        var provisionalConLado = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { zona_provisional = "frente", lado = "izquierdo" }));
        var provisionalInvalida = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { zona_provisional = "techo" }));
        var cuadranteInexistente = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { cuadrante_numero = 9, lado = "izquierdo" }));
        var numeroInvalido = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { cuadrante_numero = 0 }));

        Assert.Contains("no ambos ni ninguno", await ErrorDeCampoAsync(ninguna, "cuadrante_numero"));
        Assert.Contains("no ambos ni ninguno", await ErrorDeCampoAsync(ambas, "cuadrante_numero"));
        Assert.Contains("no lleva lado", await ErrorDeCampoAsync(provisionalConLado, "lado"));
        Assert.Equal("Zona provisional inválida", await ErrorDeCampoAsync(provisionalInvalida, "zona_provisional"));
        Assert.Contains("no existe o está inactivo", await ErrorDeCampoAsync(cuadranteInexistente, "cuadrante_numero"));
        Assert.NotNull(await ErrorDeCampoAsync(numeroInvalido, "cuadrante_numero"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task Un_cuadrante_inactivo_ya_no_se_ofrece_para_hallazgos_nuevos()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var cuadranteId = await Sembrador.CuadranteAsync(7, "Puerta delantera");
        await Sembrador.EjecutarAsync("UPDATE dbo.tblCuadrantes SET Activo = 0 WHERE Id = @c", ("@c", cuadranteId));
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);

        var r = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, ZONA_IZQUIERDO_7));

        Assert.Contains("no existe o está inactivo", await ErrorDeCampoAsync(r, "cuadrante_numero"));
    }

    [Fact]
    public async Task El_cuadrante_del_tipo_de_vehiculo_gana_sobre_el_generico_y_uno_de_otro_tipo_no_aplica()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET TipoVehiculo = 'sedan' WHERE Vin = 'VIN-INSP-0001'");
        await Sembrador.CuadranteAsync(7, "Puerta generica");
        await Sembrador.CuadranteAsync(7, "Puerta sedan", "sedan");
        await Sembrador.CuadranteAsync(8, "Puerta de camioneta", "camioneta");
        await Sembrador.CuadranteAsync(9, "Puerta generica 9");
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);

        var propio = await ClonarAsync(await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, ZONA_IZQUIERDO_7)));
        var generico = await ClonarAsync(await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { cuadrante_numero = 9, lado = "derecho" })));
        var ajeno = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { cuadrante_numero = 8, lado = "derecho" }));

        Assert.Equal("Puerta sedan", propio.GetProperty("cuadrante_nombre").GetString());
        Assert.Equal("Puerta generica 9", generico.GetProperty("cuadrante_nombre").GetString());
        Assert.Contains("no existe o está inactivo", await ErrorDeCampoAsync(ajeno, "cuadrante_numero"));
    }

    [Fact]
    public async Task El_nivel_es_obligatorio_para_el_inspector_y_el_administrador_y_debe_ser_un_codigo_activo()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var idAdmin = await CrearInspeccionAsync(admin, etapas[0], "llegada");
        var (ruta, _) = await SubirAsync(sesion);

        var inspectorSinNivel = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { zona_provisional = "frente" }, nivel: null));
        var adminSinNivel = await PostHallazgoAsync(admin, idAdmin, CuerpoHallazgo(ruta, new { zona_provisional = "frente" }, nivel: null));
        var nivelInexistente = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { zona_provisional = "frente" }, nivel: "v9"));
        var niveles = await ClonarAsync(await Cliente(admin.Token).GetAsync("/api/catalogos/niveles-severidad"));
        var nivelV3 = niveles.GetProperty("items").EnumerateArray().Single(n => n.GetProperty("codigo").GetString() == "v3").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Cliente(admin.Token).PatchAsync($"/api/catalogos/niveles-severidad/{nivelV3}", JsonContent.Create(new { activo = false }))).StatusCode);
        var nivelInactivo = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { zona_provisional = "frente" }, nivel: "v3"));
        var adminConNivel = await PostHallazgoAsync(admin, idAdmin, CuerpoHallazgo(ruta, new { zona_provisional = "frente" }, nivel: "v1_plus"));

        Assert.Equal("El nivel de severidad es requerido", await ErrorDeCampoAsync(inspectorSinNivel, "nivel_severidad"));
        Assert.Equal("El nivel de severidad es requerido", await ErrorDeCampoAsync(adminSinNivel, "nivel_severidad"));
        Assert.Equal("Nivel de severidad inválido", await ErrorDeCampoAsync(nivelInexistente, "nivel_severidad"));
        Assert.Equal("Nivel de severidad inválido", await ErrorDeCampoAsync(nivelInactivo, "nivel_severidad"));
        Assert.Equal(HttpStatusCode.Created, adminConNivel.StatusCode);
        Assert.Equal("inspector", (await ClonarAsync(adminConNivel)).GetProperty("origen").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task El_conductor_registra_su_novedad_sin_nivel_y_no_puede_enviarlo()
    {
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-COND-0002", conductorId: conductor.UsuarioId);
        var id = await CrearInspeccionAsync(conductor, etapas[0], motivo: "inspector_no_llego");
        var (ruta, _) = await SubirAsync(conductor);

        var conNivel = await PostHallazgoAsync(conductor, id, CuerpoHallazgo(ruta, new { zona_provisional = "frente" }, nivel: "v2"));
        var sinNivel = await PostHallazgoAsync(conductor, id, CuerpoHallazgo(ruta, new { zona_provisional = "frente" }, nivel: null));

        Assert.Contains("lo asigna el inspector", await ErrorDeCampoAsync(conNivel, "nivel_severidad"));
        Assert.Equal(HttpStatusCode.Created, sinNivel.StatusCode);
        var h = await ClonarAsync(sinNivel);
        Assert.Equal("conductor", h.GetProperty("origen").GetString());
        Assert.Equal(JsonValueKind.Null, h.GetProperty("nivel_severidad").ValueKind);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("nivel_nombre").ValueKind);
        Assert.Equal(JsonValueKind.Null, h.GetProperty("nivel_accion").ValueKind);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos WHERE Origen = 'conductor' AND NivelSeveridad IS NULL"));
    }

    [Fact]
    public async Task Los_campos_del_modelo_anterior_ya_no_se_admiten()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);
        var cuerpo = CuerpoHallazgo(ruta, new { zona_provisional = "frente" });
        cuerpo["parte_vehiculo"] = "Capot";
        cuerpo["coords_diagrama"] = new { x = 0.25, y = 0.75 };

        var r = await PostHallazgoAsync(sesion, id, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errores = (await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors");
        Assert.Equal("Campo no admitido", errores.GetProperty("parte_vehiculo")[0].GetString());
        Assert.Equal("Campo no admitido", errores.GetProperty("coords_diagrama")[0].GetString());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task Un_hallazgo_preexistente_apunta_al_hallazgo_previo_del_mismo_vin_con_la_misma_zona_y_clase()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await Sembrador.CuadranteAsync(7, "Puerta delantera");
        // El hallazgo previo es de una inspeccion de salida ya cerrada; la de llegada se abre despues.
        var (_, origen) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], sesion.UsuarioId, await Sembrador.ZonaAsync(7, "izquierdo"));
        var llegada = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
        var (ruta, _) = await SubirAsync(sesion);
        Dictionary<string, object?> Preexistente(object zona, string clase = "golpe", Guid? hallazgoOrigen = null)
        {
            var cuerpo = CuerpoHallazgo(ruta, zona, clase);
            cuerpo["es_preexistente"] = true;
            cuerpo["hallazgo_origen_id"] = hallazgoOrigen ?? origen;
            return cuerpo;
        }

        var otraZona = await PostHallazgoAsync(sesion, llegada, Preexistente(new { cuadrante_numero = 7, lado = "derecho" }));
        var otraClase = await PostHallazgoAsync(sesion, llegada, Preexistente(ZONA_IZQUIERDO_7, clase: "rotura"));
        var inexistente = await PostHallazgoAsync(sesion, llegada, Preexistente(ZONA_IZQUIERDO_7, hallazgoOrigen: Guid.NewGuid()));
        var abierto = (await ClonarAsync(await PostHallazgoAsync(sesion, llegada, CuerpoHallazgo(ruta, new { zona_provisional = "frente" }, nivel: "v1")))).GetProperty("id").GetGuid();
        var deInspeccionAbierta = await PostHallazgoAsync(sesion, llegada, Preexistente(new { zona_provisional = "frente" }, hallazgoOrigen: abierto));
        var ok = await PostHallazgoAsync(sesion, llegada, Preexistente(ZONA_IZQUIERDO_7));

        Assert.Contains("mismo vehículo", await ErrorDeCampoAsync(otraZona, "hallazgo_origen_id"));
        Assert.Contains("misma zona", await ErrorDeCampoAsync(otraClase, "hallazgo_origen_id"));
        Assert.Contains("mismo vehículo", await ErrorDeCampoAsync(inexistente, "hallazgo_origen_id"));
        Assert.Contains("inspección cerrada anterior", await ErrorDeCampoAsync(deInspeccionAbierta, "hallazgo_origen_id"));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var h = await ClonarAsync(ok);
        Assert.True(h.GetProperty("es_preexistente").GetBoolean());
        Assert.Equal(origen, h.GetProperty("hallazgo_origen_id").GetGuid());
        Assert.Equal(sesion.UsuarioId, h.GetProperty("confirmado_por").GetGuid());
        // El previo sembrado, el hallazgo abierto y el preexistente recien guardado.
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task Un_hallazgo_preexistente_de_otro_vin_se_rechaza_y_es_preexistente_exige_su_origen()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var zonaFrente = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!;
        var (_, etapasAjenas) = await Sembrador.VehiculoConRutaAsync("VIN-INSP-0002", inspectorId: sesion.UsuarioId);
        var ajena = await Sembrador.InformeAsync(etapasAjenas[0], sesion.UsuarioId, cerrada: false);
        var hallazgoAjeno = Guid.NewGuid();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (@h, @i, @z, 'golpe', '5_10cm', 'v2')",
            ("@h", hallazgoAjeno), ("@i", ajena), ("@z", zonaFrente));
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);

        var otroVin = CuerpoHallazgo(ruta, new { zona_provisional = "frente" });
        otroVin["es_preexistente"] = true;
        otroVin["hallazgo_origen_id"] = hallazgoAjeno;
        var sinOrigen = CuerpoHallazgo(ruta, new { zona_provisional = "frente" });
        sinOrigen["es_preexistente"] = true;
        var origenSinMarca = CuerpoHallazgo(ruta, new { zona_provisional = "frente" });
        origenSinMarca["hallazgo_origen_id"] = hallazgoAjeno;

        Assert.Contains("mismo vehículo", await ErrorDeCampoAsync(await PostHallazgoAsync(sesion, id, otroVin), "hallazgo_origen_id"));
        Assert.Contains("exige hallazgo_origen_id", await ErrorDeCampoAsync(await PostHallazgoAsync(sesion, id, sinOrigen), "hallazgo_origen_id"));
        Assert.Contains("solo se envía con es_preexistente", await ErrorDeCampoAsync(await PostHallazgoAsync(sesion, id, origenSinMarca), "hallazgo_origen_id"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task La_foto_de_un_hallazgo_debe_haberse_subido_y_su_hash_debe_coincidir()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0]);
        var (ruta, _) = await SubirAsync(sesion);
        var zona = new { zona_provisional = "frente" };

        var noSubida = await PostHallazgoAsync(sesion, id, CuerpoHallazgo("3f2b6c1e-0000-4000-8000-123456789abc.png", zona));
        var hashAjeno = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, zona, hash: new string('a', 64)));

        Assert.NotNull(await ErrorDeCampoAsync(noSubida, "storage_path"));
        Assert.NotNull(await ErrorDeCampoAsync(hashAjeno, "hash_sha256"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblHallazgos"));
    }

    [Fact]
    public async Task Agregar_a_una_inspeccion_inexistente_responde_404()
    {
        var (sesion, _) = await EscenarioAsync();
        var id = Guid.NewGuid();
        var (ruta, _) = await SubirAsync(sesion);

        var r = await PostHallazgoAsync(sesion, id, CuerpoHallazgo(ruta, new { zona_provisional = "frente" }));

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal($"Inspección {id} no encontrada", (await ClonarAsync(r)).GetProperty("message").GetString());
    }

    // ---------------------------------------------------------------- hallazgos previos del VIN

    [Fact]
    public async Task Los_hallazgos_previos_traen_los_de_las_inspecciones_cerradas_del_vin_del_mas_reciente_al_mas_antiguo_con_su_contexto()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await Sembrador.CuadranteAsync(7, "Puerta delantera");
        var izquierdo7 = await Sembrador.ZonaAsync(7, "izquierdo");
        var frente = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!;
        var vehiculo = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblVehiculos WHERE Vin = 'VIN-INSP-0001'"))!;

        // Un viaje anterior del que la unidad no salio, con su hallazgo grave.
        var viajeAnterior = await Sembrador.ViajeAsync(inspectorId: sesion.UsuarioId, estado: "cancelado", codigoMtrack: "MTK-ANTERIOR");
        var etapaAnterior = Guid.NewGuid();
        await Sembrador.EjecutarAsync("INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @v, @veh)", ("@v", viajeAnterior), ("@veh", vehiculo));
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e, @veh, @v, @p, 1)",
            ("@e", etapaAnterior), ("@veh", vehiculo), ("@v", viajeAnterior), ("@p", (await Sembrador.EtapasRutaAsync())[0]));
        var (_, hallazgoNoSale) = await Sembrador.InformeCerradoConHallazgoAsync(etapaAnterior, sesion.UsuarioId, frente, clase: "rotura");
        await Sembrador.UnidadNoSaleAsync(viajeAnterior, vehiculo, hallazgoNoSale, sesion.UsuarioId, "Rotura en el frente");
        await Task.Delay(30);

        // Las inspecciones del viaje actual: una salida cerrada y una llegada cerrada despues.
        var (inspeccionSalida, hallazgoSalida) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], sesion.UsuarioId, izquierdo7);
        await Task.Delay(30);
        var (inspeccionLlegada, hallazgoLlegada) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], sesion.UsuarioId, frente, momento: "llegada");
        // Lo que no cuenta: un hallazgo de una inspeccion abierta y uno de otro VIN.
        await Sembrador.InformeAsync(etapas[1], sesion.UsuarioId, cerrada: false, hallazgos: 1, momento: "llegada");
        var (_, etapasAjenas) = await Sembrador.VehiculoConRutaAsync("VIN-INSP-0099", inspectorId: sesion.UsuarioId);
        await Sembrador.InformeCerradoConHallazgoAsync(etapasAjenas[0], sesion.UsuarioId, frente);
        await Task.Delay(30);
        var actual = await CrearInspeccionAsync(sesion, etapas[1]);

        var r = await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{actual}/hallazgos-previos");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var lista = (await ClonarAsync(r)).EnumerateArray().ToList();
        Assert.Equal([hallazgoLlegada, hallazgoSalida, hallazgoNoSale], lista.Select(h => h.GetProperty("id").GetGuid()).ToList());
        var llegada = lista[0];
        Assert.Equal(inspeccionLlegada, llegada.GetProperty("inspeccion_id").GetGuid());
        Assert.Equal(await Sembrador.ViajeDeEtapaAsync(etapas[0]), llegada.GetProperty("viaje_id").GetGuid());
        Assert.StartsWith("MTK-S", llegada.GetProperty("codigo_mtrack").GetString());
        Assert.Equal("cargue", llegada.GetProperty("proceso").GetString());
        Assert.Equal("llegada", llegada.GetProperty("momento").GetString());
        Assert.Equal(JsonValueKind.String, llegada.GetProperty("cerrada_at").ValueKind);
        Assert.Equal(JsonValueKind.Array, llegada.GetProperty("evidencias").ValueKind);
        Assert.Equal("Frente", llegada.GetProperty("zona").GetString());
        Assert.Equal("v2", llegada.GetProperty("nivel_severidad").GetString());
        Assert.Equal("Izquierdo 7 · Puerta delantera", lista[1].GetProperty("zona").GetString());
        Assert.Equal(inspeccionSalida, lista[1].GetProperty("inspeccion_id").GetGuid());
        // El del viaje anterior del que la unidad no salio tambien llega, con el codigo de su viaje.
        Assert.Equal(viajeAnterior, lista[2].GetProperty("viaje_id").GetGuid());
        Assert.Equal("MTK-ANTERIOR", lista[2].GetProperty("codigo_mtrack").GetString());
        Assert.Equal("rotura", lista[2].GetProperty("clase_dano").GetString());
    }

    [Fact]
    public async Task Los_hallazgos_previos_traen_las_fotos_del_hallazgo_con_su_url()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var frente = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'"))!;
        var inspeccion = await Sembrador.InformeAsync(etapas[0], sesion.UsuarioId, cerrada: false);
        var hallazgo = Guid.NewGuid();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (@h, @i, @z, 'golpe', '5_10cm', 'v2')",
            ("@h", hallazgo), ("@i", inspeccion), ("@z", frente));
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblEvidencias (Id, InspeccionId, HallazgoId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), @i, @h, 'hallazgo-previo.png', SYSDATETIMEOFFSET(), 4.6, -74.1)",
            ("@i", inspeccion), ("@h", hallazgo));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i", ("@i", inspeccion));
        await Task.Delay(30);
        var actual = await CrearInspeccionAsync(sesion, etapas[0], "llegada");

        var lista = (await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{actual}/hallazgos-previos"))).EnumerateArray().ToList();

        var foto = Assert.Single(lista).GetProperty("evidencias").EnumerateArray().Single();
        Assert.StartsWith("http://localhost/api/uploads/hallazgo-previo.png?exp=", foto.GetProperty("url").GetString());
        Assert.Contains("&firma=", foto.GetProperty("url").GetString());
        Assert.NotEqual(JsonValueKind.Null, foto.GetProperty("url_expira_at").ValueKind);
        Assert.Equal(hallazgo, foto.GetProperty("hallazgo_id").GetGuid());
        Assert.False(foto.TryGetProperty("storage_path", out _));
    }

    [Fact]
    public async Task Una_inspeccion_corregida_aporta_los_hallazgos_de_su_ultima_version_cerrada()
    {
        var (sesion, etapas) = await EscenarioAsync();
        await Sembrador.CuadranteAsync(7, "Puerta delantera");
        var (original, hallazgoOriginal) = await Sembrador.InformeCerradoConHallazgoAsync(etapas[0], sesion.UsuarioId, await Sembrador.ZonaAsync(7, "izquierdo"));
        var correccion = Guid.NewGuid();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Estado, Version, CorrigeInspeccionId) SELECT @c, EtapaId, ViajeId, UsuarioId, Momento, Fecha, 'borrador', 2, @o FROM dbo.tblInspecciones WHERE Id = @o",
            ("@c", correccion), ("@o", original));
        var hallazgoCorregido = Guid.NewGuid();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (@h, @i, @z, 'golpe', '1_5cm', 'v1')",
            ("@h", hallazgoCorregido), ("@i", correccion), ("@z", await Sembrador.ZonaAsync(7, "derecho")));
        await Sembrador.EjecutarAsync("UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i", ("@i", correccion));
        await Task.Delay(30);
        var actual = await CrearInspeccionAsync(sesion, etapas[0], "llegada");

        var lista = (await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{actual}/hallazgos-previos"))).EnumerateArray().ToList();

        Assert.Equal([hallazgoCorregido], lista.Select(h => h.GetProperty("id").GetGuid()).ToList());
        Assert.DoesNotContain(lista, h => h.GetProperty("id").GetGuid() == hallazgoOriginal);
    }

    [Fact]
    public async Task Sin_hallazgos_previos_la_lista_va_vacia()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var actual = await CrearInspeccionAsync(sesion, etapas[0]);

        var r = await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{actual}/hallazgos-previos");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(0, (await ClonarAsync(r)).GetArrayLength());
    }

    [Fact]
    public async Task Los_hallazgos_previos_son_de_inspector_y_administrador_el_conductor_y_un_viaje_ajeno_son_403()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var ajeno = await SesionAsync("ajeno@ejemplo.com");
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var actual = await CrearInspeccionAsync(inspector, etapas[0]);
        var ruta = $"/api/inspecciones/{actual}/hallazgos-previos";

        Assert.Equal(HttpStatusCode.OK, (await Cliente(inspector.Token).GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(admin.Token).GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(superAdmin.Token).GetAsync(ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(conductor.Token).GetAsync(ruta)).StatusCode);
        var viajeAjeno = await Cliente(ajeno.Token).GetAsync(ruta);
        Assert.Equal(HttpStatusCode.Forbidden, viajeAjeno.StatusCode);
        Assert.Contains("no está asignado", (await ClonarAsync(viajeAjeno)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync(ruta)).StatusCode);
    }

    [Fact]
    public async Task Los_hallazgos_previos_de_una_inspeccion_inexistente_responden_404()
    {
        var (sesion, _) = await EscenarioAsync();
        var id = Guid.NewGuid();

        var r = await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{id}/hallazgos-previos");

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
                "id", "inspeccion_id", "hallazgo_id", "sello_fecha", "sello_lat", "sello_lng", "url", "url_expira_at", "created_at", "updated_at", "vista_id", "hash_sha256",
                "sello_servidor_at", "precision", "dispositivo_id", "nota", "sin_ubicacion", "es_constancia", "es_sello",
            ],
            e.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(vista, e.GetProperty("vista_id").GetGuid());
        Assert.Equal(FECHA, e.GetProperty("sello_fecha").GetString());
        Assert.Equal(4.60971, e.GetProperty("sello_lat").GetDouble());
        Assert.Equal(4.5, e.GetProperty("precision").GetDouble());
        Assert.Equal("tablet-1", e.GetProperty("dispositivo_id").GetString());
        Assert.Equal(64, e.GetProperty("hash_sha256").GetString()!.Length);
        Assert.False(e.GetProperty("sin_ubicacion").GetBoolean());
        Assert.StartsWith("http://localhost/api/uploads/", e.GetProperty("url").GetString());
        Assert.True(DateTimeOffset.Parse(e.GetProperty("url_expira_at").GetString()!) > DateTimeOffset.UtcNow.AddMinutes(14));
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
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
            zona_provisional = "frente",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            nivel_severidad = "v2",
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");

        var r = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(7, (await ClonarAsync(r)).GetProperty("details").GetArrayLength());
    }

    [Fact]
    public async Task Con_las_7_vistas_se_cierra_con_su_hora_de_cierre_y_las_adicionales_no_bloquean()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
        foreach (var (vistaId, _) in vistas.Skip(1))
        {
            await FotoDeVistaAsync(sesion, id, vistaId);
        }

        var sinTipo = await CerrarAsync(sesion, id);
        Assert.Equal(HttpStatusCode.OK, sinTipo.StatusCode);

        var (_, etapasCamion) = await Sembrador.VehiculoConRutaAsync("VIN-CAMION-001", inspectorId: sesion.UsuarioId);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET TipoVehiculo = 'camion' WHERE Vin = 'VIN-CAMION-001'");
        var idCamion = await CrearInspeccionAsync(sesion, etapasCamion[0], "llegada");
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
        await TomarVistasObligatoriasAsync(sesion, id);
        var (ruta, _) = await SubirAsync(sesion);
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            zona_provisional = "frente",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            nivel_severidad = "v2",
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
        await TomarVistasObligatoriasAsync(sesion, id);
        var cierre = await CerrarAsync(sesion, id);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        var vista = (await Sembrador.VistasAsync(obligatoria: true))[0].Id;
        var (ruta, _) = await SubirAsync(sesion);

        var otraVez = await CerrarAsync(sesion, id);
        var hallazgo = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            zona_provisional = "frente",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            nivel_severidad = "v2",
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
            zona_provisional = "frente",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            nivel_severidad = "v2",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        });

        var cierre = await CerrarAsync(sesion, id);

        Assert.Equal(HttpStatusCode.Created, novedad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cierre.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e AND Estado = 'cerrada'", ("@e", etapas[1])));
    }

    // ---------------------------------------------------------------- cargue sin personal de apoyo

    [Fact]
    public async Task El_conductor_no_crea_la_salida_del_cargue_sin_la_excepcion_y_el_inspector_si()
    {
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("insp@ejemplo.com");
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-COND-0002", conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);

        var delConductor = await Cliente(conductor.Token).PostAsJsonAsync(
            "/api/inspecciones", new { etapa_id = etapas[0], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1 });
        var delInspector = await Cliente(inspector.Token).PostAsJsonAsync(
            "/api/inspecciones", new { etapa_id = etapas[0], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1 });

        Assert.Equal(HttpStatusCode.Forbidden, delConductor.StatusCode);
        var cuerpo = await ClonarAsync(delConductor);
        Assert.Equal("FORBIDDEN", cuerpo.GetProperty("code").GetString());
        Assert.Equal("La inspección de salida del cargue la hace un inspector; si no hay inspector, active la excepción", cuerpo.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Created, delInspector.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }

    [Fact]
    public async Task El_conductor_crea_la_salida_de_transporte_y_de_descargue_aunque_el_cargue_tenga_personal_de_apoyo()
    {
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-COND-0003", conductorId: conductor.UsuarioId);
        await LlegadaCerradaAsync(etapas[0], conductor.UsuarioId);
        var transporte = await Cliente(conductor.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[1], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1 });
        await LlegadaCerradaAsync(etapas[1], conductor.UsuarioId);

        var descargue = await Cliente(conductor.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[2], momento = "salida", fecha = FECHA, lat = 4.6, lng = -74.1 });
        var llegadaDelCargue = await Cliente(conductor.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = etapas[0], momento = "llegada", fecha = FECHA, lat = 4.6, lng = -74.1 });

        Assert.Equal(HttpStatusCode.Created, transporte.StatusCode);
        Assert.Equal(HttpStatusCode.Created, descargue.StatusCode);
        // El bloqueo es de la salida del Cargue: lo demas del Cargue (por ejemplo la llegada) no cambia.
        Assert.NotEqual(HttpStatusCode.Forbidden, llegadaDelCargue.StatusCode);
    }

    [Fact]
    public async Task Con_la_excepcion_el_conductor_hace_la_salida_del_cargue_con_las_reglas_de_la_acomodacion()
    {
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-COND-0001", conductorId: conductor.UsuarioId);
        var id = await CrearInspeccionAsync(conductor, etapas[0], motivo: "inspector_no_llego");

        var incompleta = await CerrarAsync(conductor, id);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblViajeVehiculos SET Posicion = 3 WHERE ViajeId = @v", ("@v", await Sembrador.ViajeDeEtapaAsync(etapas[0])));
        var (ruta, hash) = await SubirAsync(conductor);
        var foto = await Cliente(conductor.Token).PostAsJsonAsync(
            $"/api/inspecciones/{id}/evidencias", new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, precision = 4.5, dispositivo_id = "tablet-1", hash_sha256 = hash });
        var completa = await CerrarAsync(conductor, id);

        Assert.Equal(HttpStatusCode.BadRequest, incompleta.StatusCode);
        var faltantes = (await ClonarAsync(incompleta)).GetProperty("details").EnumerateArray().Select(d => d.GetString()).ToList();
        Assert.Equal(["Falta la posición de la unidad VIN-COND-0001", "Faltan fotos de la acomodación: se exige al menos 1 y hay 0"], faltantes);
        Assert.Equal(HttpStatusCode.Created, foto.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completa.StatusCode);
    }

    // ---------------------------------------------------------------- correccion por version

    [Fact]
    public async Task Corregir_crea_una_version_nueva_que_referencia_a_la_anterior_y_la_anterior_no_cambia()
    {
        var (sesion, etapas) = await EscenarioAsync();
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
        await TomarVistasObligatoriasAsync(sesion, id);
        await CerrarAsync(sesion, id);
        var antes = await ClonarAsync(await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{id}"));

        var r = await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/corregir", new { notas = "Se repiten las fotos", lat = 5.0, lng = -75.0 });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var v2 = await ClonarAsync(r);
        Assert.Equal(2, v2.GetProperty("version").GetInt32());
        Assert.Equal(id, v2.GetProperty("corrige_inspeccion_id").GetGuid());
        Assert.Equal("borrador", v2.GetProperty("estado").GetString());
        Assert.Equal("llegada", v2.GetProperty("momento").GetString());
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
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
        var id = await CrearInspeccionAsync(sesion, etapas[0], "llegada");

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

    /// <summary>Viaje con inspector y conductor: son los dos firmantes posibles de la transferencia.</summary>
    private async Task<(Sesion Inspector, Sesion Conductor, Guid[] Etapas)> EscenarioConConductorAsync()
    {
        var inspector = await SesionAsync("insp@ejemplo.com");
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-INSP-0001", conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        return (inspector, conductor, etapas);
    }

    private Task<HttpResponseMessage> FirmarAsync(Sesion quien, Guid inspeccionId, Sesion entrega, Sesion recibe, string firma) =>
        Cliente(quien.Token).PostAsJsonAsync($"/api/inspecciones/{inspeccionId}/transferencia",
            new { entrega_usuario_id = entrega.UsuarioId, recibe_usuario_id = recibe.UsuarioId, firma });

    private Task<HttpResponseMessage> FirmarSoloConFirmaAsync(Sesion quien, Guid inspeccionId, string firma) =>
        Cliente(quien.Token).PostAsJsonAsync($"/api/inspecciones/{inspeccionId}/transferencia", new { firma });

    private async Task<object> FotoConstanciaAsync(Sesion quien)
    {
        var (ruta, hash) = await SubirAsync(quien);
        return new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1, precision = 4.5, dispositivo_id = "tablet-1", hash_sha256 = hash };
    }

    [Fact]
    public async Task Cada_firma_la_envia_su_titular_y_guarda_su_propia_fecha_y_el_cierre_se_fija_con_la_segunda()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var id = await CrearInspeccionAsync(inspector, etapas[0]);

        var primera = await FirmarAsync(inspector, id, inspector, conductor, "M0 0 L10 10");
        var t1 = await ClonarAsync(primera);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE FirmaEntregaAt IS NOT NULL AND FirmaRecibeAt IS NULL AND TipoCierre IS NULL"));
        var segunda = await FirmarSoloConFirmaAsync(conductor, id, "M5 5 L20 20");
        var t2 = await ClonarAsync(segunda);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(
            [
                "id", "inspeccion_id", "entrega_usuario_id", "recibe_usuario_id", "firmada", "firmada_at", "created_at", "updated_at", "tipo_cierre",
                "entrega_firmada_at", "recibe_firmada_at", "reservas_texto", "no_firma_parte", "motivo_no_firma", "testigo_usuario_id", "evidencia_constancia_id",
            ],
            t1.EnumerateObject().Select(p => p.Name).ToList());
        Assert.False(t1.GetProperty("firmada").GetBoolean());
        Assert.Equal(JsonValueKind.Null, t1.GetProperty("firmada_at").ValueKind);
        Assert.Equal(JsonValueKind.Null, t1.GetProperty("tipo_cierre").ValueKind);
        Assert.Equal(JsonValueKind.String, t1.GetProperty("entrega_firmada_at").ValueKind);
        Assert.Equal(JsonValueKind.Null, t1.GetProperty("recibe_firmada_at").ValueKind);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.True(t2.GetProperty("firmada").GetBoolean());
        Assert.Equal("firmada", t2.GetProperty("tipo_cierre").GetString());
        Assert.Equal(JsonValueKind.String, t2.GetProperty("firmada_at").ValueKind);
        Assert.Equal(JsonValueKind.String, t2.GetProperty("recibe_firmada_at").ValueKind);
        Assert.Equal(t1.GetProperty("id").GetGuid(), t2.GetProperty("id").GetGuid());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal("M0 0 L10 10", (string?)await Sembrador.EscalarAsync("SELECT FirmaEntrega FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal("M5 5 L20 20", (string?)await Sembrador.EscalarAsync("SELECT FirmaRecibe FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'transferencia.firmar'"));
    }

    [Fact]
    public async Task Nadie_firma_por_otro_ni_crea_una_transferencia_ajena()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var intruso = await SesionAsync("intruso@ejemplo.com");
        var id = await CrearInspeccionAsync(inspector, etapas[0]);

        var creaAjena = await FirmarAsync(intruso, id, inspector, conductor, "FIRMA-FALSA");
        Assert.Equal(HttpStatusCode.Forbidden, creaAjena.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));

        Assert.Equal(HttpStatusCode.Created, (await FirmarAsync(inspector, id, inspector, conductor, "FIRMA-INSPECTOR")).StatusCode);
        var firmaAjena = await FirmarSoloConFirmaAsync(intruso, id, "FIRMA-FALSA");
        var reservasAjenas = await Cliente(intruso.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia/reservas", new { firma = "X", reservas = "Observación" });

        Assert.Equal(HttpStatusCode.Forbidden, firmaAjena.StatusCode);
        Assert.Equal("FORBIDDEN", (await ClonarAsync(firmaAjena)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, reservasAjenas.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE FirmaEntrega = N'FIRMA-INSPECTOR' AND FirmaRecibe IS NULL AND TipoCierre IS NULL"));
    }

    [Fact]
    public async Task Cada_uno_firma_su_parte_aunque_envie_los_dos_ids_y_los_firmantes_no_se_pueden_cambiar()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var id = await CrearInspeccionAsync(inspector, etapas[0]);
        await FirmarAsync(inspector, id, inspector, conductor, "A");

        // El conductor envia los dos ids y su firma: se guarda como el recibo, nunca como la entrega.
        var r = await FirmarAsync(conductor, id, inspector, conductor, "B");
        var cambio = await FirmarAsync(inspector, id, conductor, inspector, "C");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal("A", (string?)await Sembrador.EscalarAsync("SELECT FirmaEntrega FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal("B", (string?)await Sembrador.EscalarAsync("SELECT FirmaRecibe FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(HttpStatusCode.Conflict, cambio.StatusCode);
        Assert.Equal("Los firmantes de la transferencia no se pueden cambiar", (await ClonarAsync(cambio)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Una_transferencia_firmada_es_inmutable_pero_acepta_el_reintento_identico()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var id = await CrearInspeccionAsync(inspector, etapas[0], "llegada");
        await TomarVistasObligatoriasAsync(inspector, id);
        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(inspector, id)).StatusCode);

        var firma = await FirmarAsync(inspector, id, inspector, conductor, "A");
        await FirmarSoloConFirmaAsync(conductor, id, "B");
        var repetida = await FirmarSoloConFirmaAsync(inspector, id, "A");
        var alterada = await FirmarSoloConFirmaAsync(inspector, id, "OTRA");

        Assert.Equal(HttpStatusCode.Created, firma.StatusCode);
        Assert.Equal(HttpStatusCode.Created, repetida.StatusCode);
        Assert.True((await ClonarAsync(repetida)).GetProperty("firmada").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, alterada.StatusCode);
        Assert.Equal("La transferencia ya está firmada por ambas partes y es inmutable", (await ClonarAsync(alterada)).GetProperty("message").GetString());
        Assert.Equal("A", (string?)await Sembrador.EscalarAsync("SELECT FirmaEntrega FROM dbo.tblTransferenciasCustodia"));
    }

    [Fact]
    public async Task La_transferencia_valida_los_ids_y_que_los_firmantes_sean_del_viaje()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var ajeno = await SesionAsync("ajeno@ejemplo.com", clsRol.CONDUCTOR);
        var id = await CrearInspeccionAsync(inspector, etapas[0]);

        var mismo = await Cliente(inspector.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = inspector.UsuarioId, recibe_usuario_id = inspector.UsuarioId, firma = "A" });
        var inexistente = await Cliente(inspector.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = inspector.UsuarioId, recibe_usuario_id = Guid.NewGuid(), firma = "A" });
        var fueraDelViaje = await FirmarAsync(inspector, id, inspector, ajeno, "A");
        var sinIds = await FirmarSoloConFirmaAsync(inspector, id, "A");
        var sinFirma = await Cliente(inspector.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = inspector.UsuarioId, recibe_usuario_id = conductor.UsuarioId });
        var inspeccionAjena = await FirmarAsync(inspector, Guid.NewGuid(), inspector, conductor, "A");

        Assert.Equal(HttpStatusCode.BadRequest, mismo.StatusCode);
        Assert.Equal(
            ["Quien entrega y quien recibe deben ser usuarios distintos"],
            (await ClonarAsync(mismo)).GetProperty("details").GetProperty("fieldErrors").GetProperty("recibe_usuario_id").EnumerateArray().Select(m => m.GetString()).ToList());
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.Equal(["Quien recibe debe ser un usuario del viaje"], MensajesDe(await ClonarAsync(inexistente), "recibe_usuario_id"));
        Assert.Equal(HttpStatusCode.BadRequest, fueraDelViaje.StatusCode);
        Assert.Equal(["Quien recibe debe ser un usuario del viaje"], MensajesDe(await ClonarAsync(fueraDelViaje), "recibe_usuario_id"));
        Assert.Equal(HttpStatusCode.BadRequest, sinIds.StatusCode);
        Assert.Equal(["Indique quién entrega: la primera firma crea la transferencia"], MensajesDe(await ClonarAsync(sinIds), "entrega_usuario_id"));
        Assert.Equal(HttpStatusCode.BadRequest, sinFirma.StatusCode);
        Assert.Equal(["La firma es obligatoria"], MensajesDe(await ClonarAsync(sinFirma), "firma"));
        Assert.Equal(HttpStatusCode.NotFound, inspeccionAjena.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia"));
    }

    private static List<string?> MensajesDe(JsonElement error, string campo) =>
        error.GetProperty("details").GetProperty("fieldErrors").GetProperty(campo).EnumerateArray().Select(m => m.GetString()).ToList();

    [Fact]
    public async Task Recibir_con_reservas_exige_las_observaciones_y_solo_lo_hace_quien_recibe()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var id = await CrearInspeccionAsync(inspector, etapas[0]);
        await FirmarAsync(inspector, id, inspector, conductor, "A");

        var sinTexto = await Cliente(conductor.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia/reservas", new { firma = "B", reservas = "   " });
        var quienEntrega = await Cliente(inspector.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia/reservas", new { firma = "A", reservas = "Algo" });
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre IS NOT NULL OR FirmaRecibe IS NOT NULL"));
        var ok = await Cliente(conductor.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia/reservas", new { firma = "B", reservas = "  El parachoques tiene un rayón  " });
        var cuerpo = await ClonarAsync(ok);

        Assert.Equal(HttpStatusCode.BadRequest, sinTexto.StatusCode);
        Assert.Equal(["Escriba las observaciones: no se puede recibir con reservas sin ellas"], MensajesDe(await ClonarAsync(sinTexto), "reservas"));
        Assert.Equal(HttpStatusCode.Forbidden, quienEntrega.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal("con_reservas", cuerpo.GetProperty("tipo_cierre").GetString());
        Assert.Equal("El parachoques tiene un rayón", cuerpo.GetProperty("reservas_texto").GetString());
        Assert.True(cuerpo.GetProperty("firmada").GetBoolean());
        Assert.Equal("El parachoques tiene un rayón", (string?)await Sembrador.EscalarAsync("SELECT ReservasTexto FROM dbo.tblTransferenciasCustodia"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'transferencia.reservas'"));
        Assert.Equal(HttpStatusCode.Conflict, (await FirmarSoloConFirmaAsync(conductor, id, "OTRA")).StatusCode);
    }

    [Fact]
    public async Task La_constancia_de_no_firma_exige_motivo_testigo_con_usuario_y_foto_y_cierra_la_transferencia()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var testigo = await SesionAsync("testigo@ejemplo.com");
        var id = await CrearInspeccionAsync(inspector, etapas[0]);
        await FirmarAsync(inspector, id, inspector, conductor, "A");
        var foto = await FotoConstanciaAsync(inspector);
        var ruta = $"/api/inspecciones/{id}/transferencia/no-firma";

        var sinMotivo = await Cliente(inspector.Token).PostAsJsonAsync(ruta, new { parte = "recibe", testigo_usuario_id = testigo.UsuarioId, evidencia = foto });
        var sinTestigo = await Cliente(inspector.Token).PostAsJsonAsync(ruta, new { parte = "recibe", motivo = "Se niega a firmar", evidencia = foto });
        var sinFoto = await Cliente(inspector.Token).PostAsJsonAsync(ruta, new { parte = "recibe", motivo = "Se niega a firmar", testigo_usuario_id = testigo.UsuarioId });
        var testigoInexistente = await Cliente(inspector.Token).PostAsJsonAsync(ruta, new { parte = "recibe", motivo = "Se niega a firmar", testigo_usuario_id = Guid.NewGuid(), evidencia = foto });
        var testigoEsUnaParte = await Cliente(inspector.Token).PostAsJsonAsync(ruta, new { parte = "recibe", motivo = "Se niega a firmar", testigo_usuario_id = conductor.UsuarioId, evidencia = foto });
        var unoMismo = await Cliente(inspector.Token).PostAsJsonAsync(ruta, new { parte = "entrega", motivo = "Me niego", testigo_usuario_id = testigo.UsuarioId, evidencia = foto });
        var porElQueSeNiega = await Cliente(conductor.Token).PostAsJsonAsync(ruta, new { parte = "recibe", motivo = "No quiero", testigo_usuario_id = testigo.UsuarioId, evidencia = await FotoConstanciaAsync(conductor) });
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblTransferenciasCustodia WHERE TipoCierre IS NOT NULL"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias"));

        var ok = await Cliente(inspector.Token).PostAsJsonAsync(ruta, new { parte = "recibe", motivo = "  Se niega a firmar el recibo  ", testigo_usuario_id = testigo.UsuarioId, evidencia = foto });
        var cuerpo = await ClonarAsync(ok);

        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Equal(["El motivo de la no firma es obligatorio"], MensajesDe(await ClonarAsync(sinMotivo), "motivo"));
        Assert.Equal(HttpStatusCode.BadRequest, sinTestigo.StatusCode);
        Assert.Equal(["La constancia exige un testigo con usuario"], MensajesDe(await ClonarAsync(sinTestigo), "testigo_usuario_id"));
        Assert.Equal(HttpStatusCode.BadRequest, sinFoto.StatusCode);
        Assert.Equal(["La constancia exige una foto"], MensajesDe(await ClonarAsync(sinFoto), "evidencia"));
        Assert.Equal(HttpStatusCode.BadRequest, testigoInexistente.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, testigoEsUnaParte.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unoMismo.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, porElQueSeNiega.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal("no_firma", cuerpo.GetProperty("tipo_cierre").GetString());
        Assert.Equal("recibe", cuerpo.GetProperty("no_firma_parte").GetString());
        Assert.Equal("Se niega a firmar el recibo", cuerpo.GetProperty("motivo_no_firma").GetString());
        Assert.Equal(testigo.UsuarioId, cuerpo.GetProperty("testigo_usuario_id").GetGuid());
        Assert.Equal(JsonValueKind.String, cuerpo.GetProperty("evidencia_constancia_id").ValueKind);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias e JOIN dbo.tblTransferenciasCustodia t ON t.EvidenciaConstanciaId = e.Id WHERE e.EsConstancia = 1 AND e.VistaId IS NULL AND e.HallazgoId IS NULL"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'transferencia.no_firma'"));
        Assert.Equal(HttpStatusCode.Conflict, (await FirmarSoloConFirmaAsync(conductor, id, "TARDE")).StatusCode);
    }

    [Fact]
    public async Task La_foto_de_la_constancia_se_admite_aunque_la_inspeccion_ya_este_cerrada_y_no_cuenta_como_foto_de_vista()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var testigo = await SesionAsync("testigo@ejemplo.com");
        var id = await CrearInspeccionAsync(inspector, etapas[0], "llegada");
        await TomarVistasObligatoriasAsync(inspector, id);
        Assert.Equal(HttpStatusCode.OK, (await CerrarAsync(inspector, id)).StatusCode);

        var r = await Cliente(inspector.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia/no-firma", new
        {
            entrega_usuario_id = inspector.UsuarioId,
            recibe_usuario_id = conductor.UsuarioId,
            parte = "recibe",
            motivo = "Se niega a firmar",
            testigo_usuario_id = testigo.UsuarioId,
            evidencia = await FotoConstanciaAsync(inspector),
        });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(7, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsConstancia = 0"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEvidencias WHERE EsConstancia = 1"));
    }

    [Fact]
    public async Task Una_transferencia_sin_cierre_bloquea_la_salida_de_la_etapa_siguiente_hasta_que_se_cierra()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        await LlegadaCerradaAsync(etapas[0], inspector.UsuarioId);
        var llegada = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblInspecciones WHERE EtapaId = @e", ("@e", etapas[0])))!;
        await FirmarAsync(inspector, llegada, inspector, conductor, "A");
        var salida = new { etapa_id = etapas[1], momento = "salida", fecha = FECHA };

        var bloqueada = await Cliente(inspector.Token).PostAsJsonAsync("/api/inspecciones", salida);
        var error = await ClonarAsync(bloqueada);
        Assert.Equal(HttpStatusCode.Conflict, bloqueada.StatusCode);
        Assert.Equal("ETAPA_PREVIA_SIN_CIERRE", error.GetProperty("code").GetString());
        Assert.Equal("transferencia_sin_cierre", error.GetProperty("details").GetProperty("motivo").GetString());
        Assert.Equal(etapas[0], error.GetProperty("details").GetProperty("etapa_previa_id").GetGuid());

        Assert.Equal(HttpStatusCode.Created, (await Cliente(conductor.Token).PostAsJsonAsync($"/api/inspecciones/{llegada}/transferencia/reservas", new { firma = "B", reservas = "Rayón" })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await Cliente(inspector.Token).PostAsJsonAsync("/api/inspecciones", salida)).StatusCode);
    }

    [Fact]
    public async Task El_conductor_recibe_en_el_cargue_sin_que_la_respuesta_le_muestre_los_hallazgos()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var id = await CrearInspeccionAsync(inspector, etapas[0]);
        var (ruta, _) = await SubirAsync(inspector);
        Assert.Equal(HttpStatusCode.Created, (await Cliente(inspector.Token).PostAsJsonAsync($"/api/inspecciones/{id}/hallazgos", new
        {
            zona_provisional = "frente",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            nivel_severidad = "v2",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        })).StatusCode);
        await FirmarAsync(inspector, id, inspector, conductor, "A");

        var recibo = await FirmarSoloConFirmaAsync(conductor, id, "B");
        var cuerpo = await ClonarAsync(recibo);

        Assert.Equal(HttpStatusCode.Created, recibo.StatusCode);
        Assert.False(cuerpo.TryGetProperty("hallazgos", out _));
        Assert.Equal("firmada", cuerpo.GetProperty("tipo_cierre").GetString());
    }

    [Fact]
    public async Task La_constancia_en_pdf_la_descargan_los_del_viaje_y_no_un_inspector_ajeno()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var ajeno = await SesionAsync("ajeno@ejemplo.com");
        var testigo = await SesionAsync("testigo@ejemplo.com");
        var id = await CrearInspeccionAsync(inspector, etapas[0]);
        var ruta = $"/api/inspecciones/{id}/transferencia/constancia/pdf";
        Assert.Equal(HttpStatusCode.NotFound, (await Cliente(inspector.Token).GetAsync(ruta)).StatusCode);
        await FirmarAsync(inspector, id, inspector, conductor, "M0 0 L10 10 L20 0");
        await Cliente(inspector.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia/no-firma", new
        {
            parte = "recibe",
            motivo = "Se niega a firmar (sin motivo adicional)",
            testigo_usuario_id = testigo.UsuarioId,
            evidencia = await FotoConstanciaAsync(inspector),
        });

        var delInspector = await Cliente(inspector.Token).GetAsync(ruta);
        var delConductor = await Cliente(conductor.Token).GetAsync(ruta);
        var delAjeno = await Cliente(ajeno.Token).GetAsync(ruta);
        var inexistente = await Cliente(inspector.Token).GetAsync($"/api/inspecciones/{Guid.NewGuid()}/transferencia/constancia/pdf");

        Assert.Equal(HttpStatusCode.OK, delInspector.StatusCode);
        Assert.Equal("application/pdf", delInspector.Content.Headers.ContentType?.MediaType);
        var bytes = await delInspector.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Contains("%%EOF", System.Text.Encoding.ASCII.GetString(bytes[^16..]));
        Assert.Equal(HttpStatusCode.OK, delConductor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Fact]
    public async Task Los_firmantes_del_viaje_son_su_inspector_y_su_conductor_y_un_ajeno_no_los_ve()
    {
        var (inspector, conductor, etapas) = await EscenarioConConductorAsync();
        var ajeno = await SesionAsync("ajeno@ejemplo.com");
        var viajeId = await Sembrador.ViajeDeEtapaAsync(etapas[0]);

        var r = await Cliente(inspector.Token).GetAsync($"/api/viajes/{viajeId}/firmantes");
        var delConductor = await Cliente(conductor.Token).GetAsync($"/api/viajes/{viajeId}/firmantes");
        var delAjeno = await Cliente(ajeno.Token).GetAsync($"/api/viajes/{viajeId}/firmantes");
        var inexistente = await Cliente(inspector.Token).GetAsync($"/api/viajes/{Guid.NewGuid()}/firmantes");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var firmantes = await ClonarAsync(r);
        Assert.Equal(2, firmantes.GetArrayLength());
        Assert.Equal(["id", "nombre", "rol"], firmantes[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(
            new[] { inspector.UsuarioId, conductor.UsuarioId }.Order(),
            firmantes.EnumerateArray().Select(f => f.GetProperty("id").GetGuid()).Order());
        Assert.Equal(HttpStatusCode.OK, delConductor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
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
            zona_provisional = "frente",
            clase_dano = "golpe",
            tamano_dano = "1_5cm",
            nivel_severidad = "v2",
            evidencia = new { storage_path = ruta, sello_fecha = FECHA, sello_lat = 4.6, sello_lng = -74.1 },
        });
        await Cliente(sesion.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { entrega_usuario_id = sesion.UsuarioId, recibe_usuario_id = receptor.UsuarioId, firma = "TRAZO-A" });
        await Cliente(receptor.Token).PostAsJsonAsync($"/api/inspecciones/{id}/transferencia", new { firma = "TRAZO-B" });

        // Lo lee quien creo el informe: el conductor que recibe no ve los hallazgos que registro el inspector.
        var r = await Cliente(sesion.Token).GetAsync($"/api/inspecciones/{id}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var inf = await ClonarAsync(r);
        Assert.Equal("VIN-INSP-0001", inf.GetProperty("vin").GetString());
        Assert.Equal("Renault", inf.GetProperty("marca").GetString());
        Assert.Equal("cargue", inf.GetProperty("proceso").GetString());
        Assert.True(inf.GetProperty("requiere_inspeccion").GetBoolean());
        Assert.Equal("Nombre insp@ejemplo.com", inf.GetProperty("inspector_nombre").GetString());
        Assert.Equal(1, inf.GetProperty("hallazgos").GetArrayLength());
        Assert.Equal("Frente", inf.GetProperty("hallazgos")[0].GetProperty("zona").GetString());
        Assert.Equal("v2", inf.GetProperty("hallazgos")[0].GetProperty("nivel_severidad").GetString());
        Assert.StartsWith($"http://localhost/api/uploads/{ruta}?exp=", inf.GetProperty("evidencias")[0].GetProperty("url").GetString());
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
        var a = await CrearInspeccionAsync(sesion, etapas[0], "llegada");
        await TomarVistasObligatoriasAsync(sesion, a);
        await CerrarAsync(sesion, a);
        await Task.Delay(20);
        var b = await CrearInspeccionAsync(otra, etapas2[1]);

        var todas = Items(await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones")));
        var porUsuario = Items(await ClonarAsync(await Cliente(coordinador.Token).GetAsync($"/api/inspecciones?usuario_id={otra.UsuarioId}")));
        var porEstado = Items(await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?estado=cerrada")));
        var porMomento = Items(await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?momento=llegada")));
        var porProceso = Items(await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?proceso=transporte")));
        var porVin = Items(await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?vin=9fbhsr4e8pm000999")));
        var porFecha = Items(await ClonarAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones?desde=2026-07-01T00:00:00Z&hasta=2026-07-31T00:00:00Z")));

        Assert.Equal(3, todas.GetArrayLength());
        Assert.Equal(["iniciada_at", "cerrada_at", "version", "corrige_inspeccion_id", "momento_nombre"], todas[0].EnumerateObject().Select(p => p.Name).TakeLast(5).ToList());
        Assert.Equal(["vin", "proceso"], todas[0].EnumerateObject().Select(p => p.Name).Skip(15).Take(2).ToList());
        Assert.Equal(2, porUsuario.GetArrayLength());
        Assert.Contains(porUsuario.EnumerateArray(), i => i.GetProperty("id").GetGuid() == b);
        Assert.Contains(porEstado.EnumerateArray(), i => i.GetProperty("id").GetGuid() == a);
        Assert.Equal(2, porMomento.EnumerateArray().Count(i => i.GetProperty("momento_nombre").GetString() == "Inspección de llegada"));
        Assert.Equal("transporte", porProceso[0].GetProperty("proceso").GetString());
        Assert.Equal("9FBHSR4E8PM000999", porVin[0].GetProperty("vin").GetString());
        // Solo las dos creadas por la API llevan la fecha del dispositivo (julio de 2026); la llegada sembrada lleva la de hoy.
        Assert.Equal(2, porFecha.GetArrayLength());
    }

    [Fact]
    public async Task El_listado_valida_los_filtros()
    {
        var (sesion, _) = await EscenarioAsync();

        var r = await Cliente(sesion.Token).GetAsync("/api/inspecciones?vin=corto&usuario_id=no&momento=x&estado=y&proceso=z&desde=ayer");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var campos = (await ClonarAsync(r)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["vin", "usuario_id", "momento", "estado", "proceso", "desde"], campos);
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
