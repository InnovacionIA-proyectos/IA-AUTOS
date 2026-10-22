using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Una foto solo se ve con una URL firmada por el sistema y vigente: sin firma, con una firma que no es la suya o vencida responde 403 URL_FIRMADA_INVALIDA; con una firma valida se ve sin
/// encabezado de autenticacion; la vigencia es el parametro <c>url_firmada_minutos</c>.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class UrlsFirmadasApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private static readonly byte[] JPEG = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00];

    /// <summary>El reloj de las pruebas: la hora real mas lo que la prueba adelante, para vencer una URL sin esperar.</summary>
    private sealed class RelojAjustable : IReloj
    {
        public TimeSpan Adelanto { get; set; }

        public DateTimeOffset Ahora => DateTimeOffset.UtcNow + Adelanto;
    }

    private readonly RelojAjustable _reloj = new();
    private clsFabricaApi _api = null!;

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _api = CrearFabrica(servicios: s => s.AddSingleton<IReloj>(_reloj));
    }

    private async Task<string> TokenAsync(string email = "foto@ejemplo.com", string rol = clsRol.INSPECTOR)
    {
        await _api.SembrarUsuarioAsync(email, "Clave-Segura-1", rol);
        return await LoginAsync(email, "Clave-Segura-1", _api);
    }

    private static async Task<JsonElement> ComoJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<JsonElement> SubirAsync(string token, byte[]? contenido = null) =>
        await ComoJsonAsync(await Cliente(token, _api).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(contenido ?? JPEG.Concat(Guid.NewGuid().ToByteArray()).ToArray()) }));

    /// <summary>Parte una URL firmada en su ruta y sus parametros para alterarlos.</summary>
    private static (string Ruta, string Exp, string Firma) Partes(string url)
    {
        var uri = new Uri(url);
        var consulta = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return (uri.AbsolutePath, consulta["exp"]!, consulta["firma"]!);
    }

    [Fact]
    public async Task Una_url_firmada_se_abre_sin_token_y_dice_cuando_vence()
    {
        var token = await TokenAsync();

        var subida = await SubirAsync(token);

        var url = subida.GetProperty("url").GetString()!;
        Assert.StartsWith("http://localhost/api/uploads/", url);
        var expira = DateTimeOffset.Parse(subida.GetProperty("url_expira_at").GetString()!);
        Assert.InRange((expira - DateTimeOffset.UtcNow).TotalMinutes, 14, 15.1);
        var (_, exp, _) = Partes(url);
        Assert.Equal(expira.ToUnixTimeSeconds(), long.Parse(exp));
        var lectura = await Cliente(fabrica: _api).GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
        Assert.Equal("image/jpeg", lectura.Content.Headers.ContentType!.MediaType);
        Assert.Equal("private", lectura.Headers.CacheControl!.Private ? "private" : "publica");
        Assert.InRange(lectura.Headers.CacheControl!.MaxAge!.Value.TotalSeconds, 1, 900);
    }

    [Fact]
    public async Task Sin_firma_o_con_una_firma_incompleta_responde_403_url_firmada_invalida()
    {
        var token = await TokenAsync();
        var subida = await SubirAsync(token);
        var nombre = subida.GetProperty("storage_path").GetString()!;
        var (ruta, exp, _) = Partes(subida.GetProperty("url").GetString()!);

        var sinNada = await Cliente(fabrica: _api).GetAsync($"/api/uploads/{nombre}");
        var sinFirma = await Cliente(fabrica: _api).GetAsync($"{ruta}?exp={exp}");
        var sinVencimiento = await Cliente(fabrica: _api).GetAsync($"{ruta}?firma=abc");
        // Tener un token tampoco reemplaza la firma.
        var conToken = await Cliente(token, _api).GetAsync($"/api/uploads/{nombre}");

        foreach (var r in new[] { sinNada, sinFirma, sinVencimiento, conToken })
        {
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("URL_FIRMADA_INVALIDA", (await ComoJsonAsync(r)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Una_firma_alterada_un_vencimiento_cambiado_o_la_firma_de_otro_archivo_responden_403()
    {
        var token = await TokenAsync();
        var uno = await SubirAsync(token);
        var otro = await SubirAsync(token);
        var (ruta, exp, firma) = Partes(uno.GetProperty("url").GetString()!);
        var (rutaOtro, _, firmaOtro) = Partes(otro.GetProperty("url").GetString()!);
        var alterada = (firma[0] == 'a' ? 'b' : 'a') + firma[1..];

        var respuestas = new[]
        {
            await Cliente(fabrica: _api).GetAsync($"{ruta}?exp={exp}&firma={alterada}"),
            await Cliente(fabrica: _api).GetAsync($"{ruta}?exp={long.Parse(exp) + 3600}&firma={firma}"),
            await Cliente(fabrica: _api).GetAsync($"{ruta}?exp={exp}&firma={firmaOtro}"),
            await Cliente(fabrica: _api).GetAsync($"{rutaOtro}?exp={exp}&firma={firma}"),
            await Cliente(fabrica: _api).GetAsync($"{ruta}?exp=no-es-numero&firma={firma}"),
        };

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
        Assert.Equal(HttpStatusCode.OK, (await Cliente(fabrica: _api).GetAsync($"{ruta}?exp={exp}&firma={firma}")).StatusCode);
    }

    [Fact]
    public async Task Una_url_vencida_responde_403_y_vigente_hasta_el_ultimo_minuto()
    {
        var token = await TokenAsync();
        var url = (await SubirAsync(token)).GetProperty("url").GetString()!;

        _reloj.Adelanto = TimeSpan.FromMinutes(14);
        var vigente = await Cliente(fabrica: _api).GetAsync(url);
        _reloj.Adelanto = TimeSpan.FromMinutes(16);
        var vencida = await Cliente(fabrica: _api).GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, vigente.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, vencida.StatusCode);
        Assert.Equal("URL_FIRMADA_INVALIDA", (await ComoJsonAsync(vencida)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Una_firma_valida_de_un_archivo_que_no_existe_responde_404()
    {
        var inexistente = UrlFirmada("3f2b6c1e-0000-4000-8000-123456789abc.jpg", _api);

        var r = await Cliente(fabrica: _api).GetAsync(inexistente);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task La_vigencia_es_el_parametro_url_firmada_minutos()
    {
        await Sembrador.EjecutarAsync("UPDATE dbo.tblParametros SET Valor = N'5' WHERE Clave = 'url_firmada_minutos'");
        var conCinco = CrearFabrica(servicios: s => s.AddSingleton<IReloj>(_reloj));
        await conCinco.SembrarUsuarioAsync("foto@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        var token = await LoginAsync("foto@ejemplo.com", "Clave-Segura-1", conCinco);

        var subida = await ComoJsonAsync(await Cliente(token, conCinco).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG) }));

        var expira = DateTimeOffset.Parse(subida.GetProperty("url_expira_at").GetString()!);
        Assert.InRange((expira - DateTimeOffset.UtcNow).TotalMinutes, 4, 5.1);
        _reloj.Adelanto = TimeSpan.FromMinutes(6);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(fabrica: conCinco).GetAsync(subida.GetProperty("url").GetString())).StatusCode);
    }

    [Fact]
    public async Task Las_evidencias_de_un_informe_traen_su_url_firmada_y_se_ven_sin_token()
    {
        var inspector = await _api.SembrarUsuarioAsync("insp@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        var token = await LoginAsync("insp@ejemplo.com", "Clave-Segura-1", _api);
        var subida = await SubirAsync(token);
        var nombre = subida.GetProperty("storage_path").GetString()!;
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-URL-0001", inspectorId: inspector.Id);
        var inspeccion = await Sembrador.InformeAsync(etapas[0], inspector.Id, cerrada: false);
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), @i, @p, SYSDATETIMEOFFSET(), 4.6, -74.1)", ("@i", inspeccion), ("@p", nombre));

        var informe = await ComoJsonAsync(await Cliente(token, _api).GetAsync($"/api/inspecciones/{inspeccion}"));
        var inventario = await ComoJsonAsync(await Cliente(token, _api).GetAsync("/api/vehiculos/VIN-URL-0001/inventario"));

        var evidencia = informe.GetProperty("evidencias")[0];
        Assert.StartsWith($"http://localhost/api/uploads/{nombre}?exp=", evidencia.GetProperty("url").GetString());
        Assert.NotEqual(JsonValueKind.Null, evidencia.GetProperty("url_expira_at").ValueKind);
        Assert.False(evidencia.TryGetProperty("ruta_archivo", out _));
        Assert.False(evidencia.TryGetProperty("storage_path", out _));
        Assert.Equal(HttpStatusCode.OK, (await Cliente(fabrica: _api).GetAsync(evidencia.GetProperty("url").GetString())).StatusCode);
        var deInventario = inventario.GetProperty("etapas").EnumerateArray().SelectMany(e => e.GetProperty("inspecciones").EnumerateArray()).SelectMany(i => i.GetProperty("evidencias").EnumerateArray()).Single();
        Assert.StartsWith($"http://localhost/api/uploads/{nombre}?exp=", deInventario.GetProperty("url").GetString());
    }

    [Fact]
    public async Task La_url_y_su_vencimiento_de_una_evidencia_salen_de_la_misma_firma()
    {
        var token = await TokenAsync();
        var subida = await SubirAsync(token);

        var (_, exp, _) = Partes(subida.GetProperty("url").GetString()!);

        Assert.Equal(DateTimeOffset.Parse(subida.GetProperty("url_expira_at").GetString()!).ToUnixTimeSeconds(), long.Parse(exp));
    }

    [Fact]
    public async Task La_firma_dibujada_de_la_transferencia_solo_es_url_cuando_es_una_ruta()
    {
        var inspector = await _api.SembrarUsuarioAsync("insp@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        var conductor = await _api.SembrarUsuarioAsync("cond@ejemplo.com", "Clave-Segura-1", clsRol.CONDUCTOR);
        var token = await LoginAsync("insp@ejemplo.com", "Clave-Segura-1", _api);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-URL-0002", conductorId: conductor.Id, inspectorId: inspector.Id);
        var inspeccion = await Sembrador.InformeAsync(etapas[0], inspector.Id, cerrada: true, momento: "llegada");
        await Sembrador.TransferenciaAsync(inspeccion, inspector.Id, conductor.Id, "firmada");
        await Sembrador.EjecutarAsync(
            "ALTER TABLE dbo.tblTransferenciasCustodia DISABLE TRIGGER ALL; UPDATE dbo.tblTransferenciasCustodia SET FirmaEntrega = N'firma-entrega.png', FirmaRecibe = N'<svg><path d=\"M0 0\"/></svg>'; "
            + "ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL");

        var informe = await ComoJsonAsync(await Cliente(token, _api).GetAsync($"/api/inspecciones/{inspeccion}"));

        var transferencia = informe.GetProperty("transferencia");
        Assert.StartsWith("http://localhost/api/uploads/firma-entrega.png?exp=", transferencia.GetProperty("firma_entrega").GetString());
        Assert.Equal("<svg><path d=\"M0 0\"/></svg>", transferencia.GetProperty("firma_recibe").GetString());
    }

    [Fact]
    public async Task Sin_secreto_propio_las_firmas_siguen_funcionando_con_la_clave_derivada_y_uno_corto_no_arranca()
    {
        var derivada = CrearFabrica(new Dictionary<string, string?> { ["Storage:FirmaSecreto"] = string.Empty });
        await derivada.SembrarUsuarioAsync("foto@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        var token = await LoginAsync("foto@ejemplo.com", "Clave-Segura-1", derivada);
        var subida = await ComoJsonAsync(await Cliente(token, derivada).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG) }));

        var lectura = await Cliente(fabrica: derivada).GetAsync(subida.GetProperty("url").GetString());

        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
        var corto = CrearFabrica(new Dictionary<string, string?> { ["Storage:FirmaSecreto"] = "corto" });
        Assert.ThrowsAny<Exception>(() => corto.CreateClient());
    }

    [Fact]
    public async Task Otro_secreto_no_valida_las_firmas_de_este()
    {
        var token = await TokenAsync();
        var url = (await SubirAsync(token)).GetProperty("url").GetString()!;
        var otroSecreto = CrearFabrica(new Dictionary<string, string?> { ["Storage:FirmaSecreto"] = "otro-secreto-de-firmas-con-mas-de-32-caracteres" });
        var (ruta, exp, firma) = Partes(url);

        var r = await Cliente(fabrica: otroSecreto).GetAsync($"{ruta}?exp={exp}&firma={firma}");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
