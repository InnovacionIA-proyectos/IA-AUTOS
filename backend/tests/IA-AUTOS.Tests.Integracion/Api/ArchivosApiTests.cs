using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Almacen S3 compatible en contenedor (adobe/s3mock) para probar el adaptador de extremo a extremo.
/// La imagen oficial de MinIO ya no se publica en los registros publicos; el adaptador usa el protocolo S3 estandar.
/// </summary>
public sealed class clsAlmacenS3Fixture : IAsyncLifetime
{
    private const int PUERTO = 9090;

    private readonly IContainer _contenedor = new ContainerBuilder("adobe/s3mock:5.2.3")
        .WithPortBinding(PUERTO, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Started S3MockApplication"))
        .Build();

    public string Endpoint => $"http://{_contenedor.Hostname}:{_contenedor.GetMappedPublicPort(PUERTO)}";

    public string AccessKey => "acceso-de-pruebas";

    public string SecretKey => "secreto-de-pruebas";

    public Task InitializeAsync() => _contenedor.StartAsync();

    public async Task DisposeAsync() => await _contenedor.DisposeAsync();
}

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class ArchivosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private static readonly byte[] JPEG = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. Enumerable.Range(0, 200).Select(i => (byte)i)];
    private static readonly byte[] PNG = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Range(0, 200).Select(i => (byte)(255 - i))];

    private async Task<string> TokenAsync(string rol = clsRol.INSPECTOR, string email = "foto@ejemplo.com")
    {
        await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol);
        return await LoginAsync(email, "Clave-Segura-1");
    }

    private static async Task<JsonElement> ClonarAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    [Fact]
    public async Task Sube_una_foto_jpeg_y_se_lee_sin_token_con_cache_publica()
    {
        var token = await TokenAsync();

        var r = await Cliente(token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG), ext = "jpg" });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var subida = await ClonarAsync(r);
        Assert.Equal(["storage_path", "url", "hash_sha256"], subida.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JPEG)).ToLowerInvariant(), subida.GetProperty("hash_sha256").GetString());
        var nombre = subida.GetProperty("storage_path").GetString()!;
        Assert.Matches(@"^[0-9a-f-]{36}\.jpg$", nombre);
        Assert.Equal("/api/uploads/" + nombre, subida.GetProperty("url").GetString());

        var lectura = await Cliente().GetAsync(subida.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
        Assert.Equal("image/jpeg", lectura.Content.Headers.ContentType!.MediaType);
        Assert.Equal("public, max-age=31536000", lectura.Headers.CacheControl!.ToString());
        Assert.Equal(JPEG, await lectura.Content.ReadAsByteArrayAsync());
    }

    private static MultipartFormDataContent Formulario(byte[] contenido, string campo = "archivo", string nombre = "foto.jpg")
    {
        var formulario = new MultipartFormDataContent();
        formulario.Add(new ByteArrayContent(contenido), campo, nombre);
        return formulario;
    }

    [Fact]
    public async Task Sube_una_foto_por_multipart_con_las_mismas_reglas_y_devuelve_su_hash()
    {
        var token = await TokenAsync();

        var jpeg = await Cliente(token).PostAsync("/api/uploads", Formulario(JPEG));
        var png = await Cliente(token).PostAsync("/api/uploads", Formulario(PNG, nombre: "otra.png"));

        Assert.Equal(HttpStatusCode.Created, jpeg.StatusCode);
        var subida = await ClonarAsync(jpeg);
        Assert.Equal(["storage_path", "url", "hash_sha256"], subida.EnumerateObject().Select(p => p.Name).ToList());
        Assert.EndsWith(".jpg", subida.GetProperty("storage_path").GetString());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JPEG)).ToLowerInvariant(), subida.GetProperty("hash_sha256").GetString());
        var lectura = await Cliente().GetAsync(subida.GetProperty("url").GetString());
        Assert.Equal(JPEG, await lectura.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Created, png.StatusCode);
        Assert.EndsWith(".png", (await ClonarAsync(png)).GetProperty("storage_path").GetString());
    }

    [Fact]
    public async Task Multipart_rechaza_lo_que_no_es_imagen_el_archivo_ausente_el_vacio_y_el_exceso_de_tamano()
    {
        var token = await TokenAsync();
        var limitada = CrearFabrica(new Dictionary<string, string?> { ["Storage:MaximoBytes"] = "100" });
        await limitada.SembrarUsuarioAsync("foto2@ejemplo.com", "Clave-Segura-1");
        var tokenLimitada = await LoginAsync("foto2@ejemplo.com", "Clave-Segura-1", limitada);

        var texto = await Cliente(token).PostAsync("/api/uploads", Formulario("esto no es una imagen"u8.ToArray()));
        var vacio = await Cliente(token).PostAsync("/api/uploads", Formulario([]));
        var sinArchivo = await Cliente(token).PostAsync("/api/uploads", new MultipartFormDataContent { { new StringContent("hola"), "otro" } });
        var grande = await Cliente(tokenLimitada, limitada).PostAsync("/api/uploads", Formulario(JPEG));
        var sinToken = await Cliente().PostAsync("/api/uploads", Formulario(JPEG));

        Assert.Equal(HttpStatusCode.BadRequest, texto.StatusCode);
        Assert.Equal("El contenido no es una imagen JPG o PNG válida", (await ClonarAsync(texto)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinArchivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, grande.StatusCode);
        Assert.Contains("tamaño máximo", (await ClonarAsync(grande)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);
    }

    [Fact]
    public async Task Acepta_el_prefijo_data_uri_y_decide_el_tipo_por_el_contenido_no_por_la_extension_declarada()
    {
        var token = await TokenAsync();

        var png = await Cliente(token).PostAsJsonAsync("/api/uploads", new { data = "data:image/jpeg;base64," + Convert.ToBase64String(PNG), ext = "jpg" });
        var sinExt = await Cliente(token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG) });

        Assert.Equal(HttpStatusCode.Created, png.StatusCode);
        var nombrePng = (await ClonarAsync(png)).GetProperty("storage_path").GetString()!;
        Assert.EndsWith(".png", nombrePng);
        var lectura = await Cliente().GetAsync("/api/uploads/" + nombrePng);
        Assert.Equal("image/png", lectura.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".jpg", (await ClonarAsync(sinExt)).GetProperty("storage_path").GetString());
    }

    [Theory]
    [InlineData("contenido de texto que no es una imagen", "El contenido no es una imagen JPG o PNG válida")]
    [InlineData("%%%no-es-base64%%%", "La imagen no es un base64 válido")]
    public async Task Rechaza_contenido_que_no_es_jpeg_ni_png_o_que_no_es_base64(string data, string mensaje)
    {
        var token = await TokenAsync();
        var cuerpo = data.StartsWith('%') ? data : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(data));

        var r = await Cliente(token).PostAsJsonAsync("/api/uploads", new { data = cuerpo, ext = "jpg" });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(mensaje, (await ClonarAsync(r)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Valida_la_solicitud_de_subida()
    {
        var token = await TokenAsync();

        var sinData = await Cliente(token).PostAsJsonAsync("/api/uploads", new { ext = "jpg" });
        var extMala = await Cliente(token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG), ext = "gif" });

        Assert.Equal(HttpStatusCode.BadRequest, sinData.StatusCode);
        Assert.True((await ClonarAsync(sinData)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("data", out _));
        Assert.Equal(HttpStatusCode.BadRequest, extMala.StatusCode);
        Assert.True((await ClonarAsync(extMala)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("ext", out _));
    }

    [Fact]
    public async Task Rechaza_imagenes_que_exceden_el_tamano_maximo_configurado()
    {
        var limitada = CrearFabrica(new Dictionary<string, string?> { ["Storage:MaximoBytes"] = "1000" });
        await limitada.SembrarUsuarioAsync("foto@ejemplo.com", "Clave-Segura-1");
        var token = await LoginAsync("foto@ejemplo.com", "Clave-Segura-1", limitada);
        var grande = JPEG.Concat(new byte[5000]).ToArray();

        var excede = await Cliente(token, limitada).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(grande) });
        var cabe = await Cliente(token, limitada).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG) });

        Assert.Equal(HttpStatusCode.BadRequest, excede.StatusCode);
        Assert.Contains("excede el tamaño máximo", (await ClonarAsync(excede)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Created, cabe.StatusCode);
    }

    [Theory]
    [InlineData("no-es-un-uuid.jpg")]
    [InlineData("3f2b6c1e-0000-4000-8000-123456789abc.gif")]
    [InlineData("3f2b6c1e-0000-4000-8000-123456789abc")]
    public async Task Los_nombres_invalidos_responden_404_archivo_invalido(string nombre)
    {
        var r = await Cliente().GetAsync("/api/uploads/" + nombre);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("Archivo inválido", (await ClonarAsync(r)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Un_archivo_inexistente_responde_404()
    {
        var r = await Cliente().GetAsync("/api/uploads/3f2b6c1e-0000-4000-8000-123456789abc.jpg");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("Archivo no encontrado", (await ClonarAsync(r)).GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("..%2F..%2Fappsettings.json")]
    [InlineData("%2e%2e%2f%2e%2e%2fetc%2fpasswd")]
    public async Task Los_intentos_de_recorrido_de_ruta_no_devuelven_archivos(string nombre)
    {
        var r = await Cliente().GetAsync("/api/uploads/" + nombre);

        Assert.True(r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(clsRol.SUPER_ADMIN, HttpStatusCode.Forbidden)]
    [InlineData(clsRol.INSPECTOR, HttpStatusCode.Created)]
    [InlineData(clsRol.CONDUCTOR, HttpStatusCode.Created)]
    [InlineData(clsRol.ADMIN, HttpStatusCode.Created)]
    public async Task La_subida_es_de_los_roles_de_campo(string rol, HttpStatusCode esperado)
    {
        var token = await TokenAsync(rol, "rol@ejemplo.com");

        var r = await Cliente(token).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG) });

        Assert.Equal(esperado, r.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsJsonAsync("/api/uploads", new { data = "x" })).StatusCode);
    }

    [Fact]
    public async Task Con_lectura_privada_las_imagenes_exigen_token()
    {
        var privada = CrearFabrica(new Dictionary<string, string?> { ["Storage:LecturaPublica"] = "false" });
        await privada.SembrarUsuarioAsync("foto@ejemplo.com", "Clave-Segura-1");
        var token = await LoginAsync("foto@ejemplo.com", "Clave-Segura-1", privada);
        var url = (await ClonarAsync(await Cliente(token, privada).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG) }))).GetProperty("url").GetString()!;

        var sinToken = await Cliente(fabrica: privada).GetAsync(url);
        var conToken = await Cliente(token, privada).GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, conToken.StatusCode);
        Assert.Contains("private", conToken.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task En_disco_el_archivo_queda_en_la_carpeta_configurada()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "iaautos-disco-" + Guid.NewGuid().ToString("N"));
        var enDisco = CrearFabrica(new Dictionary<string, string?> { ["Storage:LocalPath"] = carpeta });
        await enDisco.SembrarUsuarioAsync("foto@ejemplo.com", "Clave-Segura-1");
        var token = await LoginAsync("foto@ejemplo.com", "Clave-Segura-1", enDisco);

        var r = await Cliente(token, enDisco).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG) });

        var nombre = (await ClonarAsync(r)).GetProperty("storage_path").GetString()!;
        Assert.True(File.Exists(Path.Combine(carpeta, nombre)));
        Assert.Equal(JPEG, await File.ReadAllBytesAsync(Path.Combine(carpeta, nombre)));
        Directory.Delete(carpeta, true);
    }
}

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class ArchivosS3ApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private readonly clsAlmacenS3Fixture _almacen = new();
    private static readonly byte[] JPEG = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. Enumerable.Range(0, 300).Select(i => (byte)i)];

    public override async Task InitializeAsync()
    {
        await _almacen.InitializeAsync();
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _almacen.DisposeAsync();
    }

    [Fact]
    public async Task Con_s3_configurado_crea_el_bucket_guarda_en_almacen_y_sirve_la_imagen_por_la_api()
    {
        const string BUCKET = "iaautos-pruebas";
        var s3 = CrearFabrica(new Dictionary<string, string?>
        {
            ["Storage:S3:Endpoint"] = _almacen.Endpoint,
            ["Storage:S3:Bucket"] = BUCKET,
            ["Storage:S3:Region"] = "us-east-1",
            ["Storage:S3:AccessKey"] = _almacen.AccessKey,
            ["Storage:S3:SecretKey"] = _almacen.SecretKey,
        });
        await s3.SembrarUsuarioAsync("foto@ejemplo.com", "Clave-Segura-1");
        var token = await LoginAsync("foto@ejemplo.com", "Clave-Segura-1", s3);

        var subida = await Cliente(token, s3).PostAsJsonAsync("/api/uploads", new { data = Convert.ToBase64String(JPEG) });

        Assert.Equal(HttpStatusCode.Created, subida.StatusCode);
        var nombre = (await subida.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("storage_path").GetString()!;

        using var cliente = new AmazonS3Client(
            new BasicAWSCredentials(_almacen.AccessKey, _almacen.SecretKey),
            new AmazonS3Config { ServiceURL = _almacen.Endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        using var objeto = await cliente.GetObjectAsync(BUCKET, nombre);
        using var memoria = new MemoryStream();
        await objeto.ResponseStream.CopyToAsync(memoria);
        Assert.Equal(JPEG, memoria.ToArray());
        Assert.Equal("image/jpeg", objeto.Headers.ContentType);

        var lectura = await Cliente(fabrica: s3).GetAsync("/api/uploads/" + nombre);
        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
        Assert.Equal(JPEG, await lectura.Content.ReadAsByteArrayAsync());

        var inexistente = await Cliente(fabrica: s3).GetAsync("/api/uploads/3f2b6c1e-0000-4000-8000-123456789abc.jpg");
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }
}
