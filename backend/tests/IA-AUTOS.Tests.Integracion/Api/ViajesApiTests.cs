using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El viaje como eje: alta, consulta, cambios, asignacion de VIN, estado, carga por Excel, y la visibilidad por asignacion
/// (inspectores y conductores solo ven lo de sus viajes).
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class ViajesApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-07-17T12:30:15.123Z";

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol, string? cedula = null, Guid? clienteId = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, cedula: cedula, clienteId: clienteId);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private Task<Sesion> CoordinadorAsync() => SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> LeerJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Guid> IdAsync(string tabla, string codigo) =>
        (Guid)(await Sembrador.EscalarAsync($"SELECT Id FROM dbo.{tabla} WHERE Codigo = @c", ("@c", codigo)))!;

    private Task<Guid> UnidadAsync(string codigo = "ninera") => IdAsync("tblTiposUnidadTransportadora", codigo);

    private Task<Guid> LugarAsync(string codigo) => IdAsync("tblPuntosCustodia", codigo);

    private async Task<HttpResponseMessage> CrearViajeAsync(
        Sesion quien,
        string codigo,
        string[]? vines = null,
        Guid? conductorId = null,
        Guid? inspectorId = null,
        Guid? origenId = null,
        Guid? destinoId = null,
        bool conPersonalApoyo = false,
        string placa = "ABC123") =>
        await Cliente(quien.Token).PostAsync(
            "/api/viajes",
            Cuerpo(new
            {
                codigo_mtrack = codigo,
                tipo_unidad_id = await UnidadAsync(),
                placa,
                trailer = "T-9",
                conductor_id = conductorId,
                inspector_id = inspectorId,
                origen_id = origenId,
                destino_id = destinoId,
                con_personal_apoyo = conPersonalApoyo,
                vines = vines ?? [],
            }));

    private async Task<Guid> ViajeConVinesAsync(Sesion coordinador, string codigo, string[] vines, Guid? conductorId = null, Guid? inspectorId = null)
    {
        foreach (var vin in vines)
        {
            await Sembrador.VehiculoAsync(vin);
        }

        var r = await CrearViajeAsync(coordinador, codigo, vines, conductorId, inspectorId);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await LeerJsonAsync(r)).GetProperty("id").GetGuid();
    }

    private static List<string?> Vines(JsonElement viaje) =>
        viaje.GetProperty("vehiculos").EnumerateArray().Select(v => v.GetProperty("vin").GetString()).Order().ToList();

    // ---------------------------------------------------------------------------------------------- escenarios

    [Fact]
    public async Task El_conductor_ve_solo_sus_viajes_y_sus_vin()
    {
        var coordinador = await CoordinadorAsync();
        var conductor = await SesionAsync("cond1@ejemplo.com", clsRol.CONDUCTOR);
        var otroConductor = await SesionAsync("cond2@ejemplo.com", clsRol.CONDUCTOR);
        var suyo = await ViajeConVinesAsync(coordinador, "MTK-1", ["VIN-C1-A", "VIN-C1-B", "VIN-C1-C"], conductor.UsuarioId);
        await ViajeConVinesAsync(coordinador, "MTK-2", ["VIN-C2-A"], otroConductor.UsuarioId);

        var r = await Cliente(conductor.Token).GetAsync("/api/viajes/mios");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var viajes = await LeerJsonAsync(r);
        Assert.Equal(1, viajes.GetArrayLength());
        Assert.Equal(suyo, viajes[0].GetProperty("id").GetGuid());
        Assert.Equal("MTK-1", viajes[0].GetProperty("codigo_mtrack").GetString());
        Assert.Equal(["VIN-C1-A", "VIN-C1-B", "VIN-C1-C"], Vines(viajes[0]));
        Assert.Equal(3, viajes[0].GetProperty("vehiculos")[0].GetProperty("etapas").GetInt32());
    }

    [Fact]
    public async Task Un_inspector_no_abre_un_vin_que_no_esta_en_sus_viajes()
    {
        var coordinador = await CoordinadorAsync();
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        await ViajeConVinesAsync(coordinador, "MTK-MIO", ["VIN-MIO-001"], inspectorId: inspector.UsuarioId);
        await ViajeConVinesAsync(coordinador, "MTK-AJENO", ["VIN-AJENO-01"]);

        var ajeno = await Cliente(inspector.Token).GetAsync("/api/vehiculos/VIN-AJENO-01/inventario");
        var ajenoPdf = await Cliente(inspector.Token).GetAsync("/api/vehiculos/VIN-AJENO-01/inventario/pdf");
        var propio = await Cliente(inspector.Token).GetAsync("/api/vehiculos/VIN-MIO-001/inventario");
        var inexistente = await Cliente(inspector.Token).GetAsync("/api/vehiculos/VIN-NO-EXISTE/inventario");
        var delCoordinador = await Cliente(coordinador.Token).GetAsync("/api/vehiculos/VIN-AJENO-01/inventario");

        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal("FORBIDDEN", (await LeerJsonAsync(ajeno)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, ajenoPdf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delCoordinador.StatusCode);
    }

    [Fact]
    public async Task Un_codigo_mtrack_repetido_es_conflicto_y_el_viaje_no_se_crea()
    {
        var coordinador = await CoordinadorAsync();
        Assert.Equal(HttpStatusCode.Created, (await CrearViajeAsync(coordinador, "MTK-UNICO")).StatusCode);

        var repetido = await CrearViajeAsync(coordinador, "MTK-UNICO", placa: "OTRA999");
        var otraMayuscula = await CrearViajeAsync(coordinador, "mtk-unico");

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        var cuerpo = await LeerJsonAsync(repetido);
        Assert.Equal("CONFLICT", cuerpo.GetProperty("code").GetString());
        Assert.Equal("Ya existe un viaje con ese código MTrack", cuerpo.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, otraMayuscula.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
    }

    [Fact]
    public async Task La_carga_por_excel_muestra_el_error_de_cada_fila_antes_de_confirmar_y_solo_guarda_las_validas()
    {
        var coordinador = await CoordinadorAsync();
        await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR, cedula: "1.234.567");
        foreach (var vin in new[] { "VIN-X-01", "VIN-X-02", "VIN-X-03", "VIN-X-04" })
        {
            await Sembrador.VehiculoAsync(vin);
        }

        object[] filas =
        [
            new { fila = 2, vin = "VIN-X-01", placa = "ABC123", trailer = "T-1", cedula_conductor = "1234567", unidad = "Niñera", codigo_mtrack = "MTK-X1" },
            new { fila = 3, vin = "vin-x-02", placa = "ABC123", trailer = "T-1", cedula_conductor = "1.234.567", unidad = "ninera", codigo_mtrack = "MTK-X1" },
            new { fila = 4, vin = "VIN-NO-EXISTE", placa = "DEF456", trailer = "", cedula_conductor = "1234567", unidad = "Niñera", codigo_mtrack = "MTK-X2" },
            new { fila = 5, vin = "VIN-X-03", placa = "GHI789", trailer = "", cedula_conductor = "99999999", unidad = "Niñera", codigo_mtrack = "MTK-X3" },
            new { fila = 6, vin = "VIN-X-04", placa = "JKL012", trailer = "T-4", cedula_conductor = "1234567", unidad = "Nodriza", codigo_mtrack = "MTK-X4" },
        ];

        var vista = await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar/previsualizar", Cuerpo(new { filas }));

        Assert.Equal(HttpStatusCode.OK, vista.StatusCode);
        var previa = await LeerJsonAsync(vista);
        Assert.False(previa.GetProperty("confirmado").GetBoolean());
        Assert.Equal(5, previa.GetProperty("total").GetInt32());
        Assert.Equal(3, previa.GetProperty("validas").GetInt32());
        Assert.Equal(2, previa.GetProperty("invalidas").GetInt32());
        Assert.Equal(2, previa.GetProperty("viajes").GetInt32());
        var porFila = previa.GetProperty("filas").EnumerateArray().ToDictionary(f => f.GetProperty("fila").GetInt32());
        Assert.Equal([2, 3, 4, 5, 6], porFila.Keys.Order());
        Assert.True(porFila[2].GetProperty("valida").GetBoolean());
        Assert.True(porFila[3].GetProperty("valida").GetBoolean());
        Assert.False(porFila[4].GetProperty("valida").GetBoolean());
        Assert.Contains("no existe", porFila[4].GetProperty("errores")[0].GetString());
        Assert.False(porFila[5].GetProperty("valida").GetBoolean());
        Assert.Contains("cédula 99999999", porFila[5].GetProperty("errores")[0].GetString());
        Assert.True(porFila[6].GetProperty("valida").GetBoolean());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));

        var confirmada = await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar", Cuerpo(new { filas }));

        Assert.Equal(HttpStatusCode.Created, confirmada.StatusCode);
        var resultado = await LeerJsonAsync(confirmada);
        Assert.True(resultado.GetProperty("confirmado").GetBoolean());
        Assert.Equal(3, resultado.GetProperty("validas").GetInt32());
        Assert.Equal(2, resultado.GetProperty("invalidas").GetInt32());
        Assert.Equal(2, resultado.GetProperty("viajes").GetInt32());
        var guardadas = resultado.GetProperty("filas").EnumerateArray().Where(f => f.GetProperty("valida").GetBoolean()).ToList();
        Assert.All(guardadas, f => Assert.NotEqual(JsonValueKind.Null, f.GetProperty("viaje_id").ValueKind));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
        Assert.Equal(9, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblViajes v ON v.Id = vv.ViajeId WHERE v.CodigoMtrack = 'MTK-X1'"));
        Assert.Equal("T-4", await Sembrador.EscalarAsync("SELECT Trailer FROM dbo.tblViajes WHERE CodigoMtrack = 'MTK-X4'"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes WHERE CodigoMtrack IN ('MTK-X2', 'MTK-X3')"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes v JOIN dbo.tblTiposUnidadTransportadora t ON t.Id = v.TipoUnidadId WHERE v.CodigoMtrack = 'MTK-X4' AND t.Codigo = 'nodriza'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viajes.cargar'"));
    }

    [Fact]
    public async Task La_carga_por_excel_valida_la_coherencia_del_viaje_los_duplicados_y_lo_que_ya_existe()
    {
        var coordinador = await CoordinadorAsync();
        await ViajeConVinesAsync(coordinador, "MTK-EXISTE", ["VIN-OCUPADO"]);
        foreach (var vin in new[] { "VIN-Y-01", "VIN-Y-02", "VIN-Y-03" })
        {
            await Sembrador.VehiculoAsync(vin);
        }

        object Fila(int n, string? vin, string? codigo, string? placa = "ABC123", string? unidad = "Niñera", string? cedula = null) =>
            new { fila = n, vin, placa, trailer = "", cedula_conductor = cedula, unidad, codigo_mtrack = codigo };

        object[] filas =
        [
            Fila(1, "VIN-Y-01", "MTK-Y1"),
            Fila(2, "VIN-Y-02", "MTK-Y1", placa: "OTRA999"),
            Fila(3, "VIN-Y-01", "MTK-Y1"),
            Fila(4, "VIN-Y-01", "MTK-Y2"),
            Fila(5, "VIN-Y-03", "MTK-EXISTE"),
            Fila(6, "VIN-OCUPADO", "MTK-Y3"),
            Fila(7, "VIN-Y-03", null),
            Fila(8, "VIN-Y-03", "MTK-Y4", placa: " "),
            Fila(9, "VIN-Y-03", "MTK-Y5", unidad: "Carreta"),
            Fila(10, null, "MTK-Y6"),
            Fila(11, "VIN-Y-03", "MTK#7"),
        ];

        var vista = await LeerJsonAsync(await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar/previsualizar", Cuerpo(new { filas })));

        var errores = vista.GetProperty("filas").EnumerateArray()
            .ToDictionary(f => f.GetProperty("fila").GetInt32(), f => string.Join(" | ", f.GetProperty("errores").EnumerateArray().Select(e => e.GetString())));
        Assert.Equal(string.Empty, errores[1]);
        Assert.Contains("No coincide con el viaje de la fila 1 (la placa)", errores[2]);
        Assert.Contains("repetido en el viaje (fila 1)", errores[3]);
        Assert.Contains("ya está en el viaje MTK-Y1 de este archivo", errores[4]);
        Assert.Contains("Ya existe un viaje con el código MTrack MTK-EXISTE", errores[5]);
        Assert.Contains("ya está en el viaje activo MTK-EXISTE", errores[6]);
        Assert.Contains("código MTrack es requerido", errores[7]);
        Assert.Contains("placa es requerida", errores[8]);
        Assert.Contains("\"Carreta\" no existe", errores[9]);
        Assert.Contains("VIN es requerido", errores[10]);
        Assert.Contains("código MTrack admite", errores[11]);
        Assert.Equal(1, vista.GetProperty("validas").GetInt32());
    }

    [Fact]
    public async Task Una_carga_vacia_o_demasiado_grande_responde_400()
    {
        var coordinador = await CoordinadorAsync();

        var vacia = await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar", Cuerpo(new { filas = Array.Empty<object>() }));
        var sinLista = await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar/previsualizar", Cuerpo(new { }));
        var enorme = await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar", Cuerpo(new { filas = Enumerable.Range(1, 5001).Select(i => new { vin = "V" + i }).ToArray() }));

        foreach (var r in new[] { vacia, sinLista, enorme })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True((await LeerJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("filas", out _));
        }
    }

    // ------------------------------------------------------------------------------------------------- alta

    [Fact]
    public async Task Crear_un_viaje_crea_las_tres_etapas_de_cada_vin_con_origen_y_destino()
    {
        var coordinador = await CoordinadorAsync();
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        await Sembrador.VehiculoAsync("VIN-V-01");
        await Sembrador.VehiculoAsync("VIN-V-02");
        var origen = await LugarAsync("cedi_1");
        var destino = await LugarAsync("concesionario");

        var r = await CrearViajeAsync(coordinador, "MTK-ALTA", ["VIN-V-01", "vin-v-02"], conductor.UsuarioId, inspector.UsuarioId, origen, destino, conPersonalApoyo: true);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var viaje = await LeerJsonAsync(r);
        Assert.Equal(
            ["id", "codigo_mtrack", "tipo_unidad_id", "unidad", "placa", "trailer", "conductor_id", "conductor_nombre", "inspector_id", "inspector_nombre",
             "origen_id", "origen", "destino_id", "destino", "estado", "con_personal_apoyo", "vehiculos", "clientes", "created_at", "updated_at"],
            viaje.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("MTK-ALTA", viaje.GetProperty("codigo_mtrack").GetString());
        Assert.Equal("Niñera", viaje.GetProperty("unidad").GetString());
        Assert.Equal("programado", viaje.GetProperty("estado").GetString());
        Assert.Equal("Nombre cond@ejemplo.com", viaje.GetProperty("conductor_nombre").GetString());
        Assert.Equal("CEDI 1", viaje.GetProperty("origen").GetString());
        Assert.Equal("Concesionario", viaje.GetProperty("destino").GetString());
        Assert.True(viaje.GetProperty("con_personal_apoyo").GetBoolean());
        Assert.Equal(["VIN-V-01", "VIN-V-02"], Vines(viaje));

        Assert.Equal(6, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
        var etapas = new Dictionary<string, (object? Lugar, bool Apoyo)>();
        foreach (var codigo in new[] { "cargue", "transporte", "descargue" })
        {
            var lugar = await Sembrador.EscalarAsync("SELECT TOP 1 e.PuntoCustodiaId FROM dbo.tblEtapas e JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId WHERE c.Codigo = @c", ("@c", codigo));
            var apoyo = Convert.ToBoolean(await Sembrador.EscalarAsync("SELECT TOP 1 e.ConPersonalApoyo FROM dbo.tblEtapas e JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId WHERE c.Codigo = @c", ("@c", codigo)));
            etapas[codigo] = (lugar, apoyo);
        }

        Assert.Equal(origen, etapas["cargue"].Lugar);
        Assert.Null(etapas["transporte"].Lugar);
        Assert.Equal(destino, etapas["descargue"].Lugar);
        Assert.Equal([true, false, false], new[] { etapas["cargue"].Apoyo, etapas["transporte"].Apoyo, etapas["descargue"].Apoyo });
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.crear'"));
        Assert.Equal(coordinador.UsuarioId, await Sembrador.EscalarAsync("SELECT CreadoPor FROM dbo.tblViajes WHERE CodigoMtrack = 'MTK-ALTA'"));
    }

    [Fact]
    public async Task Crear_sin_vin_es_valido_y_los_vin_se_asignan_despues()
    {
        var coordinador = await CoordinadorAsync();

        var r = await CrearViajeAsync(coordinador, "MTK-SIN-VIN");

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(0, (await LeerJsonAsync(r)).GetProperty("vehiculos").GetArrayLength());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
    }

    [Fact]
    public async Task Crear_con_datos_invalidos_responde_400_con_errores_por_campo_y_no_crea_nada()
    {
        var coordinador = await CoordinadorAsync();
        var otroCoordinador = await SesionAsync("coord2@ejemplo.com", clsRol.COORDINADOR_CALIDAD);
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        await Fabrica.SembrarUsuarioAsync("retirado@ejemplo.com", "x", clsRol.CONDUCTOR, activo: false);
        var retirado = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblUsuarios WHERE Email = 'retirado@ejemplo.com'"))!;

        var sinNada = await Cliente(coordinador.Token).PostAsync("/api/viajes", Cuerpo(new { }));
        var referencias = await CrearViajeAsync(coordinador, "MTK-REF", conductorId: otroCoordinador.UsuarioId, inspectorId: inspector.UsuarioId, origenId: Guid.NewGuid());
        var conductorRetirado = await CrearViajeAsync(coordinador, "MTK-RET", conductorId: retirado);
        var inspectorQueEsConductor = await CrearViajeAsync(coordinador, "MTK-INS", inspectorId: otroCoordinador.UsuarioId);
        var vinInexistente = await CrearViajeAsync(coordinador, "MTK-VIN", ["VIN-INEXISTENTE"]);
        var codigoMalo = await CrearViajeAsync(coordinador, "MTK 1");

        Assert.Equal(HttpStatusCode.BadRequest, sinNada.StatusCode);
        var faltan = (await LeerJsonAsync(sinNada)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["codigo_mtrack", "placa", "tipo_unidad_id"], faltan);
        Assert.Equal(HttpStatusCode.BadRequest, referencias.StatusCode);
        var errores = (await LeerJsonAsync(referencias)).GetProperty("details").GetProperty("fieldErrors");
        Assert.Equal("El usuario indicado no es un conductor", errores.GetProperty("conductor_id")[0].GetString());
        Assert.True(errores.TryGetProperty("origen_id", out _));
        Assert.False(errores.TryGetProperty("inspector_id", out _));
        Assert.Equal(HttpStatusCode.BadRequest, conductorRetirado.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inspectorQueEsConductor.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, vinInexistente.StatusCode);
        Assert.Contains("VIN-INEXISTENTE", (await LeerJsonAsync(vinInexistente)).GetProperty("details").GetProperty("fieldErrors").GetProperty("vines")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, codigoMalo.StatusCode);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
    }

    [Fact]
    public async Task Un_vin_que_ya_esta_en_un_viaje_activo_no_entra_a_otro_pero_si_cuando_el_anterior_termino()
    {
        var coordinador = await CoordinadorAsync();
        var primero = await ViajeConVinesAsync(coordinador, "MTK-A", ["VIN-ROTA-01"]);

        var enOtro = await CrearViajeAsync(coordinador, "MTK-B", ["VIN-ROTA-01"]);
        Assert.Equal(HttpStatusCode.Conflict, enOtro.StatusCode);
        Assert.Contains("MTK-A", (await LeerJsonAsync(enOtro)).GetProperty("message").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));

        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{primero}/estado", Cuerpo(new { estado = "en_curso" }));
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{primero}/estado", Cuerpo(new { estado = "finalizado" }));
        var tras = await CrearViajeAsync(coordinador, "MTK-B", ["VIN-ROTA-01"]);

        Assert.Equal(HttpStatusCode.Created, tras.StatusCode);
        Assert.Equal(6, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas WHERE VehiculoId = (SELECT Id FROM dbo.tblVehiculos WHERE Vin = 'VIN-ROTA-01')"));
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
    }

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD, HttpStatusCode.Created)]
    [InlineData(clsRol.ADMIN, HttpStatusCode.Created)]
    [InlineData(clsRol.SUPER_ADMIN, HttpStatusCode.Created)]
    [InlineData(clsRol.INSPECTOR, HttpStatusCode.Forbidden)]
    [InlineData(clsRol.CONDUCTOR, HttpStatusCode.Forbidden)]
    [InlineData(clsRol.COMERCIAL, HttpStatusCode.Forbidden)]
    public async Task Solo_el_coordinador_y_los_administradores_crean_y_administran_viajes(string rol, HttpStatusCode esperado)
    {
        var quien = await SesionAsync("u@ejemplo.com", rol);
        var id = await Sembrador.ViajeAsync();

        var crear = await CrearViajeAsync(quien, "MTK-ROL");
        var cambiar = await Cliente(quien.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { placa = "NUEVA1" }));
        var estado = await Cliente(quien.Token).PatchAsync($"/api/viajes/{id}/estado", Cuerpo(new { estado = "en_curso" }));
        var agregar = await Cliente(quien.Token).PostAsync($"/api/viajes/{id}/vehiculos", Cuerpo(new { vines = new[] { "X" } }));
        var previsualizar = await Cliente(quien.Token).PostAsync("/api/viajes/cargar/previsualizar", Cuerpo(new { filas = new[] { new { vin = "X" } } }));
        var borrar = await Cliente(quien.Token).DeleteAsync($"/api/viajes/{id}");

        Assert.Equal(esperado, crear.StatusCode);
        if (esperado == HttpStatusCode.Forbidden)
        {
            foreach (var r in new[] { cambiar, estado, agregar, previsualizar, borrar })
            {
                Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            }
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, cambiar.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, borrar.StatusCode);
        }
    }

    [Fact]
    public async Task Los_endpoints_de_viajes_exigen_autenticacion()
    {
        var id = await Sembrador.ViajeAsync();
        foreach (var ruta in new[] { "/api/viajes", "/api/viajes/mios", $"/api/viajes/{id}" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync(ruta)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().PostAsync("/api/viajes", Cuerpo(new { }))).StatusCode);
    }

    // ---------------------------------------------------------------------------------------------- consulta

    [Fact]
    public async Task El_listado_filtra_y_cada_rol_de_campo_ve_solo_sus_viajes()
    {
        var coordinador = await CoordinadorAsync();
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        await ViajeConVinesAsync(coordinador, "MTK-L1", ["VIN-L-01", "VIN-L-02"], conductor.UsuarioId);
        await ViajeConVinesAsync(coordinador, "MTK-L2", ["VIN-L-03"], inspectorId: inspector.UsuarioId);
        var tercero = await ViajeConVinesAsync(coordinador, "MTK-L3", ["VIN-L-04"]);
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{tercero}/estado", Cuerpo(new { estado = "cancelado" }));

        var todos = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes"));
        var porEstado = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes?estado=cancelado"));
        var porCodigo = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes?codigoMtrack=l2"));
        var porVin = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes?vin=vin-l-02"));
        var porConductor = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/viajes?conductorId={conductor.UsuarioId}"));
        var delConductor = await LeerJsonAsync(await Cliente(conductor.Token).GetAsync("/api/viajes"));
        var delInspector = await LeerJsonAsync(await Cliente(inspector.Token).GetAsync("/api/viajes"));
        var invalido = await Cliente(coordinador.Token).GetAsync("/api/viajes?estado=volando&conductorId=no&desde=ayer");

        Assert.Equal(3, todos.GetArrayLength());
        Assert.Equal(["MTK-L3", "MTK-L2", "MTK-L1"], todos.EnumerateArray().Select(v => v.GetProperty("codigo_mtrack").GetString()).ToList());
        Assert.Equal(2, todos[2].GetProperty("total_vehiculos").GetInt32());
        Assert.Equal(0, todos[2].GetProperty("vehiculos_completos").GetInt32());
        Assert.Equal(["MTK-L3"], porEstado.EnumerateArray().Select(v => v.GetProperty("codigo_mtrack").GetString()).ToList());
        Assert.Equal(["MTK-L2"], porCodigo.EnumerateArray().Select(v => v.GetProperty("codigo_mtrack").GetString()).ToList());
        Assert.Equal(["MTK-L1"], porVin.EnumerateArray().Select(v => v.GetProperty("codigo_mtrack").GetString()).ToList());
        Assert.Equal(["MTK-L1"], porConductor.EnumerateArray().Select(v => v.GetProperty("codigo_mtrack").GetString()).ToList());
        Assert.Equal(["MTK-L1"], delConductor.EnumerateArray().Select(v => v.GetProperty("codigo_mtrack").GetString()).ToList());
        Assert.Equal(["MTK-L2"], delInspector.EnumerateArray().Select(v => v.GetProperty("codigo_mtrack").GetString()).ToList());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal(["estado", "conductorId", "desde"], (await LeerJsonAsync(invalido)).GetProperty("details").GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).ToList());
    }

    [Fact]
    public async Task El_detalle_de_un_viaje_ajeno_es_403_el_propio_se_ve_y_uno_inexistente_es_404()
    {
        var coordinador = await CoordinadorAsync();
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var otro = await SesionAsync("otro@ejemplo.com", clsRol.CONDUCTOR);
        var suyo = await ViajeConVinesAsync(coordinador, "MTK-D1", ["VIN-D-01"], conductor.UsuarioId);

        var propio = await Cliente(conductor.Token).GetAsync($"/api/viajes/{suyo}");
        var ajeno = await Cliente(otro.Token).GetAsync($"/api/viajes/{suyo}");
        var delCoordinador = await Cliente(coordinador.Token).GetAsync($"/api/viajes/{suyo}");
        var inexistente = await Cliente(coordinador.Token).GetAsync($"/api/viajes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delCoordinador.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Fact]
    public async Task Un_viaje_cancelado_deja_de_ser_visible_para_inspectores_y_conductores()
    {
        var coordinador = await CoordinadorAsync();
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var id = await ViajeConVinesAsync(coordinador, "MTK-CAN", ["VIN-CAN-01"], conductor.UsuarioId);

        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}/estado", Cuerpo(new { estado = "cancelado" }));

        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(conductor.Token).GetAsync($"/api/viajes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(conductor.Token).GetAsync("/api/vehiculos/VIN-CAN-01/inventario")).StatusCode);
        Assert.Equal(0, (await LeerJsonAsync(await Cliente(conductor.Token).GetAsync("/api/viajes/mios"))).GetArrayLength());
    }

    [Fact]
    public async Task Mios_trae_los_programados_y_en_curso_por_defecto_y_acepta_estado()
    {
        var coordinador = await CoordinadorAsync();
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var enCurso = await ViajeConVinesAsync(coordinador, "MTK-M1", ["VIN-M-01"], conductor.UsuarioId);
        await ViajeConVinesAsync(coordinador, "MTK-M2", ["VIN-M-02"], conductor.UsuarioId);
        var terminado = await ViajeConVinesAsync(coordinador, "MTK-M3", ["VIN-M-03"], conductor.UsuarioId);
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{enCurso}/estado", Cuerpo(new { estado = "en_curso" }));
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{terminado}/estado", Cuerpo(new { estado = "en_curso" }));
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{terminado}/estado", Cuerpo(new { estado = "finalizado" }));

        var activos = await LeerJsonAsync(await Cliente(conductor.Token).GetAsync("/api/viajes/mios"));
        var finalizados = await LeerJsonAsync(await Cliente(conductor.Token).GetAsync("/api/viajes/mios?estado=finalizado"));
        var invalido = await Cliente(conductor.Token).GetAsync("/api/viajes/mios?estado=volando");

        Assert.Equal(["MTK-M1", "MTK-M2"], activos.EnumerateArray().Select(v => v.GetProperty("codigo_mtrack").GetString()).Order().ToList());
        Assert.Equal(["MTK-M3"], finalizados.EnumerateArray().Select(v => v.GetProperty("codigo_mtrack").GetString()).ToList());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------- cambios

    [Fact]
    public async Task Actualizar_cambia_solo_lo_enviado_y_trailer_conductor_y_lugares_admiten_null()
    {
        var coordinador = await CoordinadorAsync();
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var nuevoConductor = await SesionAsync("cond2@ejemplo.com", clsRol.CONDUCTOR);
        var id = await ViajeConVinesAsync(coordinador, "MTK-U1", ["VIN-U-01"], conductor.UsuarioId);

        var cambio = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { placa = "NUEVA11", conductor_id = nuevoConductor.UsuarioId }));
        var quitar = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { trailer = (string?)null, conductor_id = (Guid?)null }));

        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        var tras = await LeerJsonAsync(cambio);
        Assert.Equal("NUEVA11", tras.GetProperty("placa").GetString());
        Assert.Equal("T-9", tras.GetProperty("trailer").GetString());
        Assert.Equal(nuevoConductor.UsuarioId, tras.GetProperty("conductor_id").GetGuid());
        Assert.Equal("MTK-U1", tras.GetProperty("codigo_mtrack").GetString());
        var sin = await LeerJsonAsync(quitar);
        Assert.Equal(JsonValueKind.Null, sin.GetProperty("trailer").ValueKind);
        Assert.Equal(JsonValueKind.Null, sin.GetProperty("conductor_id").ValueKind);
        Assert.Equal("NUEVA11", sin.GetProperty("placa").GetString());
        // Quitarle el conductor lo deja sin acceso a sus VIN.
        Assert.Equal(HttpStatusCode.Forbidden, (await Cliente(nuevoConductor.Token).GetAsync("/api/vehiculos/VIN-U-01/inventario")).StatusCode);
    }

    [Fact]
    public async Task Actualizar_rechaza_codigo_repetido_campos_ajenos_nulos_en_obligatorios_y_viajes_cerrados()
    {
        var coordinador = await CoordinadorAsync();
        var id = await ViajeConVinesAsync(coordinador, "MTK-V1", ["VIN-V-A"]);
        await ViajeConVinesAsync(coordinador, "MTK-V2", ["VIN-V-B"]);

        var repetido = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { codigo_mtrack = "mtk-v2" }));
        var mismoCodigo = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { codigo_mtrack = "MTK-V1", placa = "SIGUE1" }));
        var ajeno = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { estado = "finalizado" }));
        var nuloEnPlaca = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { placa = (string?)null }));
        var nuevoCodigo = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { codigo_mtrack = "MTK-V9" }));
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}/estado", Cuerpo(new { estado = "cancelado" }));
        var cancelado = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { placa = "TARDE11" }));

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mismoCodigo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ajeno.StatusCode);
        Assert.True((await LeerJsonAsync(ajeno)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("estado", out _));
        Assert.Equal(HttpStatusCode.BadRequest, nuloEnPlaca.StatusCode);
        Assert.Equal(HttpStatusCode.OK, nuevoCodigo.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelado.StatusCode);
    }

    [Fact]
    public async Task Cambiar_origen_destino_o_personal_de_apoyo_se_lleva_a_las_etapas_que_no_tienen_informe_cerrado()
    {
        var coordinador = await CoordinadorAsync();
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        var cedi1 = await LugarAsync("cedi_1");
        var cedi2 = await LugarAsync("cedi_2");
        var origenViejo = await LugarAsync("origen");
        var destino = await LugarAsync("destino");
        await Sembrador.VehiculoAsync("VIN-P-01");
        var creado = await LeerJsonAsync(await CrearViajeAsync(coordinador, "MTK-PROP", ["VIN-P-01"], inspectorId: inspector.UsuarioId, origenId: origenViejo, destinoId: destino));
        var id = creado.GetProperty("id").GetGuid();
        var cargue = (Guid)(await Sembrador.EscalarAsync("SELECT e.Id FROM dbo.tblEtapas e JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId WHERE c.Codigo = 'cargue'"))!;
        await Sembrador.InformeAsync(cargue, inspector.UsuarioId, cerrada: true);

        var r = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { origen_id = cedi1, destino_id = cedi2, con_personal_apoyo = true }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(cedi1, (await LeerJsonAsync(r)).GetProperty("origen_id").GetGuid());
        // El cargue ya tiene informe cerrado: conserva su lugar historico. El descargue si cambia.
        Assert.Equal(origenViejo, await Sembrador.EscalarAsync("SELECT PuntoCustodiaId FROM dbo.tblEtapas WHERE Id = @e", ("@e", cargue)));
        Assert.False(Convert.ToBoolean(await Sembrador.EscalarAsync("SELECT ConPersonalApoyo FROM dbo.tblEtapas WHERE Id = @e", ("@e", cargue))));
        Assert.Equal(cedi2, await Sembrador.EscalarAsync("SELECT e.PuntoCustodiaId FROM dbo.tblEtapas e JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId WHERE c.Codigo = 'descargue'"));
    }

    [Fact]
    public async Task El_estado_sigue_el_ciclo_del_viaje_y_rechaza_los_saltos()
    {
        var coordinador = await CoordinadorAsync();
        var id = await ViajeConVinesAsync(coordinador, "MTK-E1", ["VIN-E-01"]);

        var salto = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}/estado", Cuerpo(new { estado = "finalizado" }));
        var invalido = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}/estado", Cuerpo(new { estado = "volando" }));
        var sinEstado = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}/estado", Cuerpo(new { }));
        var inicia = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}/estado", Cuerpo(new { estado = "en_curso" }));
        var termina = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}/estado", Cuerpo(new { estado = "finalizado" }));
        var reabre = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}/estado", Cuerpo(new { estado = "en_curso" }));
        var inexistente = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{Guid.NewGuid()}/estado", Cuerpo(new { estado = "en_curso" }));

        Assert.Equal(HttpStatusCode.Conflict, salto.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinEstado.StatusCode);
        Assert.Equal("en_curso", (await LeerJsonAsync(inicia)).GetProperty("estado").GetString());
        Assert.Equal("finalizado", (await LeerJsonAsync(termina)).GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Conflict, reabre.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion LIKE 'viaje.estado.%'"));
    }

    [Fact]
    public async Task Asignar_vin_crea_sus_etapas_todo_o_nada_y_quitarlo_las_borra()
    {
        var coordinador = await CoordinadorAsync();
        var id = await ViajeConVinesAsync(coordinador, "MTK-AS", ["VIN-AS-01"]);
        await Sembrador.VehiculoAsync("VIN-AS-02");
        await Sembrador.VehiculoAsync("VIN-AS-03");
        await ViajeConVinesAsync(coordinador, "MTK-OTRO", ["VIN-AS-04"]);

        var repetido = await Cliente(coordinador.Token).PostAsync($"/api/viajes/{id}/vehiculos", Cuerpo(new { vines = new[] { "VIN-AS-02", "vin-as-01" } }));
        var enOtro = await Cliente(coordinador.Token).PostAsync($"/api/viajes/{id}/vehiculos", Cuerpo(new { vines = new[] { "VIN-AS-02", "VIN-AS-04" } }));
        var inexistente = await Cliente(coordinador.Token).PostAsync($"/api/viajes/{id}/vehiculos", Cuerpo(new { vines = new[] { "VIN-AS-02", "VIN-NO-EXISTE" } }));
        var vacio = await Cliente(coordinador.Token).PostAsync($"/api/viajes/{id}/vehiculos", Cuerpo(new { vines = Array.Empty<string>() }));
        Assert.Equal(3, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas WHERE ViajeId = @v", ("@v", id)));

        var agregado = await Cliente(coordinador.Token).PostAsync($"/api/viajes/{id}/vehiculos", Cuerpo(new { vines = new[] { "VIN-AS-02", "VIN-AS-03" } }));

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, enOtro.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
        Assert.Equal(HttpStatusCode.OK, agregado.StatusCode);
        Assert.Equal(["VIN-AS-01", "VIN-AS-02", "VIN-AS-03"], Vines(await LeerJsonAsync(agregado)));
        Assert.Equal(9, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas WHERE ViajeId = @v", ("@v", id)));

        var quitado = await Cliente(coordinador.Token).DeleteAsync($"/api/viajes/{id}/vehiculos/vin-as-02");
        var otraVez = await Cliente(coordinador.Token).DeleteAsync($"/api/viajes/{id}/vehiculos/VIN-AS-02");
        var ajeno = await Cliente(coordinador.Token).DeleteAsync($"/api/viajes/{id}/vehiculos/VIN-AS-04");

        Assert.Equal(HttpStatusCode.OK, quitado.StatusCode);
        Assert.Equal(["VIN-AS-01", "VIN-AS-03"], Vines(await LeerJsonAsync(quitado)));
        Assert.Equal(HttpStatusCode.NotFound, otraVez.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Equal(6, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas WHERE ViajeId = @v", ("@v", id)));
    }

    [Fact]
    public async Task Un_vin_con_inspecciones_en_el_viaje_no_se_quita_y_un_viaje_con_inspecciones_no_se_borra()
    {
        var coordinador = await CoordinadorAsync();
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        var conInforme = await ViajeConVinesAsync(coordinador, "MTK-INF", ["VIN-INF-01", "VIN-INF-02"], inspectorId: inspector.UsuarioId);
        var etapaDe01 = (Guid)(await Sembrador.EscalarAsync("SELECT TOP 1 e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = 'VIN-INF-01'"))!;
        await Sembrador.InformeAsync(etapaDe01, inspector.UsuarioId, cerrada: false);
        var sinInforme = await ViajeConVinesAsync(coordinador, "MTK-LIBRE", ["VIN-LIBRE-01"]);

        var quitarConInforme = await Cliente(coordinador.Token).DeleteAsync($"/api/viajes/{conInforme}/vehiculos/VIN-INF-01");
        var quitarSinInforme = await Cliente(coordinador.Token).DeleteAsync($"/api/viajes/{conInforme}/vehiculos/VIN-INF-02");
        var borrarConInforme = await Cliente(coordinador.Token).DeleteAsync($"/api/viajes/{conInforme}");
        var borrarLibre = await Cliente(coordinador.Token).DeleteAsync($"/api/viajes/{sinInforme}");
        var borrarInexistente = await Cliente(coordinador.Token).DeleteAsync($"/api/viajes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Conflict, quitarConInforme.StatusCode);
        Assert.Equal(HttpStatusCode.OK, quitarSinInforme.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, borrarConInforme.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, borrarLibre.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, borrarInexistente.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas WHERE ViajeId = @v", ("@v", sinInforme)));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Vin = 'VIN-LIBRE-01'"));
    }

    // ------------------------------------------------------------------------- la inspeccion cuelga del viaje

    [Fact]
    public async Task Un_inspector_solo_inspecciona_los_viajes_en_los_que_participa()
    {
        var coordinador = await CoordinadorAsync();
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        await ViajeConVinesAsync(coordinador, "MTK-IN1", ["VIN-IN-01"], inspectorId: inspector.UsuarioId);
        await ViajeConVinesAsync(coordinador, "MTK-IN2", ["VIN-IN-02"]);
        Guid EtapaDe(string vin) => (Guid)Sembrador.EscalarAsync("SELECT TOP 1 e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = @v ORDER BY e.Orden", ("@v", vin)).Result!;

        var propia = await Cliente(inspector.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = EtapaDe("VIN-IN-01"), momento = "salida", fecha = FECHA });
        var ajena = await Cliente(inspector.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = EtapaDe("VIN-IN-02"), momento = "salida", fecha = FECHA });
        var delAdmin = await Cliente(admin.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = EtapaDe("VIN-IN-02"), momento = "salida", fecha = FECHA });

        Assert.Equal(HttpStatusCode.Created, propia.StatusCode);
        Assert.Equal(await Sembrador.ViajeDeEtapaAsync(EtapaDe("VIN-IN-01")), (await LeerJsonAsync(propia)).GetProperty("viaje_id").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.Created, delAdmin.StatusCode);
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones"));
    }

    [Fact]
    public async Task La_primera_inspeccion_pone_en_curso_el_viaje_y_uno_finalizado_o_cancelado_no_admite_inspecciones()
    {
        var coordinador = await CoordinadorAsync();
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var viaje = await ViajeConVinesAsync(coordinador, "MTK-ST", ["VIN-ST-01", "VIN-ST-02"], conductor.UsuarioId);
        Guid EtapaDe(string vin, int orden) => (Guid)Sembrador.EscalarAsync("SELECT e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = @v AND e.Orden = @o", ("@v", vin), ("@o", orden)).Result!;

        var primera = await Cliente(conductor.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = EtapaDe("VIN-ST-01", 1), momento = "salida", fecha = FECHA });
        var estadoTras = (string)(await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", viaje)))!;
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viaje}/estado", Cuerpo(new { estado = "finalizado" }));
        var enFinalizado = await Cliente(conductor.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = EtapaDe("VIN-ST-01", 2), momento = "salida", fecha = FECHA });

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal("en_curso", estadoTras);
        Assert.Equal(HttpStatusCode.Conflict, enFinalizado.StatusCode);
        Assert.Contains("finalizado", (await LeerJsonAsync(enFinalizado)).GetProperty("message").GetString());

        var cancelado = await ViajeConVinesAsync(coordinador, "MTK-ST2", ["VIN-ST-03"], conductor.UsuarioId);
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{cancelado}/estado", Cuerpo(new { estado = "cancelado" }));
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var enCancelado = await Cliente(admin.Token).PostAsJsonAsync("/api/inspecciones", new { etapa_id = EtapaDe("VIN-ST-03", 1), momento = "salida", fecha = FECHA });
        Assert.Equal(HttpStatusCode.Conflict, enCancelado.StatusCode);
    }

    [Fact]
    public async Task El_inventario_de_un_vin_en_dos_viajes_muestra_a_cada_usuario_solo_las_etapas_de_sus_viajes()
    {
        var coordinador = await CoordinadorAsync();
        var conductorViejo = await SesionAsync("viejo@ejemplo.com", clsRol.CONDUCTOR);
        var conductorNuevo = await SesionAsync("nuevo@ejemplo.com", clsRol.CONDUCTOR);
        var primero = await ViajeConVinesAsync(coordinador, "MTK-H1", ["VIN-HIST-01"], conductorViejo.UsuarioId);
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{primero}/estado", Cuerpo(new { estado = "en_curso" }));
        await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{primero}/estado", Cuerpo(new { estado = "finalizado" }));
        var segundo = await CrearViajeAsync(coordinador, "MTK-H2", ["VIN-HIST-01"], conductorNuevo.UsuarioId);
        Assert.Equal(HttpStatusCode.Created, segundo.StatusCode);

        var delCoordinador = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/vehiculos/VIN-HIST-01/inventario"));
        var delViejo = await LeerJsonAsync(await Cliente(conductorViejo.Token).GetAsync("/api/vehiculos/VIN-HIST-01/inventario"));
        var delNuevo = await LeerJsonAsync(await Cliente(conductorNuevo.Token).GetAsync("/api/vehiculos/VIN-HIST-01/inventario"));

        Assert.Equal(6, delCoordinador.GetProperty("etapas").GetArrayLength());
        Assert.Equal(["MTK-H1", "MTK-H1", "MTK-H1", "MTK-H2", "MTK-H2", "MTK-H2"], delCoordinador.GetProperty("etapas").EnumerateArray().Select(e => e.GetProperty("codigo_mtrack").GetString()).ToList());
        Assert.Equal(3, delViejo.GetProperty("etapas").GetArrayLength());
        Assert.All(delViejo.GetProperty("etapas").EnumerateArray(), e => Assert.Equal("MTK-H1", e.GetProperty("codigo_mtrack").GetString()));
        Assert.Equal(3, delNuevo.GetProperty("etapas").GetArrayLength());
        Assert.All(delNuevo.GetProperty("etapas").EnumerateArray(), e => Assert.Equal("MTK-H2", e.GetProperty("codigo_mtrack").GetString()));
    }

    [Fact]
    public async Task La_busqueda_y_los_listados_de_vehiculos_se_limitan_a_los_viajes_del_usuario()
    {
        var coordinador = await CoordinadorAsync();
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        await ViajeConVinesAsync(coordinador, "MTK-B1", ["VIN-LIS-MIO"], inspectorId: inspector.UsuarioId);
        await ViajeConVinesAsync(coordinador, "MTK-B2", ["VIN-LIS-OTRO"]);
        await Sembrador.VehiculoAsync("VIN-LIS-SUELTO");

        var busquedaInspector = await LeerJsonAsync(await Cliente(inspector.Token).GetAsync("/api/vehiculos?vin=VIN-LIS"));
        var busquedaCoordinador = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/vehiculos?vin=VIN-LIS"));
        var listadoInspector = await LeerJsonAsync(await Cliente(inspector.Token).GetAsync("/api/vehiculos/listado?tipo=pendientes"));
        var listadoCoordinador = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/vehiculos/listado?tipo=pendientes"));

        Assert.Equal(["VIN-LIS-MIO"], busquedaInspector.EnumerateArray().Select(v => v.GetProperty("vin").GetString()).ToList());
        Assert.Equal(3, busquedaCoordinador.GetArrayLength());
        Assert.Equal(["VIN-LIS-MIO"], listadoInspector.GetProperty("rows").EnumerateArray().Select(v => v.GetProperty("vin").GetString()).ToList());
        Assert.Equal(1, listadoInspector.GetProperty("total").GetInt32());
        Assert.Equal(3, listadoCoordinador.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Los_informes_se_listan_por_viaje_y_un_informe_de_otro_viaje_es_403()
    {
        var coordinador = await CoordinadorAsync();
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        var otro = await SesionAsync("otro@ejemplo.com", clsRol.INSPECTOR);
        var mio = await ViajeConVinesAsync(coordinador, "MTK-R1", ["VIN-REP-01"], inspectorId: inspector.UsuarioId);
        var ajeno = await ViajeConVinesAsync(coordinador, "MTK-R2", ["VIN-REP-02"], inspectorId: otro.UsuarioId);
        Guid EtapaDe(Guid viaje) => (Guid)Sembrador.EscalarAsync("SELECT TOP 1 Id FROM dbo.tblEtapas WHERE ViajeId = @v ORDER BY Orden", ("@v", viaje)).Result!;
        var informeMio = await Sembrador.InformeAsync(EtapaDe(mio), inspector.UsuarioId, cerrada: false);
        var informeAjeno = await Sembrador.InformeAsync(EtapaDe(ajeno), otro.UsuarioId, cerrada: false);

        var delInspector = await LeerJsonAsync(await Cliente(inspector.Token).GetAsync("/api/inspecciones"));
        var delCoordinador = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/inspecciones"));
        var porViaje = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/inspecciones?viajeId={ajeno}"));
        var abrirMio = await Cliente(inspector.Token).GetAsync($"/api/inspecciones/{informeMio}");
        var abrirAjeno = await Cliente(inspector.Token).GetAsync($"/api/inspecciones/{informeAjeno}");

        Assert.Equal([informeMio], delInspector.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList());
        Assert.Equal(2, delCoordinador.GetArrayLength());
        Assert.Equal([informeAjeno], porViaje.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList());
        Assert.Equal(ajeno, porViaje[0].GetProperty("viaje_id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, abrirMio.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, abrirAjeno.StatusCode);
    }

    [Fact]
    public async Task Un_viaje_completo_es_aquel_cuyos_vin_tienen_todas_sus_etapas_cerradas_y_los_listados_lo_usan()
    {
        var coordinador = await CoordinadorAsync();
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        var viaje = await ViajeConVinesAsync(coordinador, "MTK-VC", ["VIN-VC-01", "VIN-VC-02"], inspectorId: inspector.UsuarioId);
        var etapas = new List<Guid>();
        foreach (var vin in new[] { "VIN-VC-01", "VIN-VC-02" })
        {
            await using var conexion = new Microsoft.Data.SqlClient.SqlConnection(BaseDatos.CadenaConexion);
            await conexion.OpenAsync();
            await using var comando = new Microsoft.Data.SqlClient.SqlCommand("SELECT e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId WHERE v.Vin = @v ORDER BY e.Orden", conexion);
            comando.Parameters.AddWithValue("@v", vin);
            await using var lector = await comando.ExecuteReaderAsync();
            var propias = new List<Guid>();
            while (await lector.ReadAsync())
            {
                propias.Add(lector.GetGuid(0));
            }

            etapas.AddRange(propias);
        }

        foreach (var etapa in etapas.Take(3))
        {
            await Sembrador.InformeAsync(etapa, inspector.UsuarioId, cerrada: true);
        }

        await Sembrador.InformeAsync(etapas[3], inspector.UsuarioId, cerrada: true);

        var parcial = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes"));
        var movilizados = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/vehiculos/listado?tipo=movilizados"));

        Assert.Equal(1, parcial[0].GetProperty("vehiculos_completos").GetInt32());
        Assert.Equal(2, parcial[0].GetProperty("total_vehiculos").GetInt32());
        Assert.Equal(["VIN-VC-01"], movilizados.GetProperty("rows").EnumerateArray().Select(v => v.GetProperty("vin").GetString()).ToList());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.vwViajesCompletos WHERE ViajeId = @v", ("@v", viaje)));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM sys.views WHERE name = 'vwVehiculosMovilizados'"));
    }

    // ------------------------------------------------------------------------------------- sofasa y cedulas

    [Fact]
    public async Task Una_orden_sofasa_ya_ingerida_se_vincula_al_viaje_al_asignar_su_vin()
    {
        var coordinador = await CoordinadorAsync();
        await Sembrador.VehiculoAsync("VIN-ORD-01");
        await Sembrador.VehiculoAsync("VIN-ORD-02");
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblOrdenesSofasa (Id, SourceId, Vin) VALUES (NEWID(), 1, 'VIN-ORD-01'), (NEWID(), 2, 'VIN-ORD-02')");
        await Sembrador.EjecutarAsync("INSERT dbo.tblOrdenesSofasa (Id, SourceId, Vin, CodigoMtrack) VALUES (NEWID(), 3, 'VIN-ORD-02', 'MTK-OTRO-CODIGO')");

        var r = await CrearViajeAsync(coordinador, "MTK-ORD", ["VIN-ORD-01", "VIN-ORD-02"]);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var viajeId = (await LeerJsonAsync(r)).GetProperty("id").GetGuid();
        Assert.Equal(viajeId, await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblOrdenesSofasa WHERE SourceId = 1"));
        Assert.Equal(viajeId, await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblOrdenesSofasa WHERE SourceId = 2"));
        // La orden con un codigo MTrack distinto pertenece a otro viaje: no se vincula a este.
        Assert.Null(await Sembrador.EscalarAsync("SELECT ViajeId FROM dbo.tblOrdenesSofasa WHERE SourceId = 3"));
    }

    [Fact]
    public async Task La_cedula_del_usuario_se_normaliza_es_unica_y_se_puede_borrar()
    {
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var creado = await Cliente(superAdmin.Token).PostAsync(
            "/api/usuarios",
            Cuerpo(new { nombre = "Carlos", email = "carlos@ejemplo.com", rol = "inspector", password = "Clave-Segura-1", cedula = "1.234.567" }));
        var repetida = await Cliente(superAdmin.Token).PostAsync(
            "/api/usuarios",
            Cuerpo(new { nombre = "Otro", email = "otro@ejemplo.com", rol = "inspector", password = "Clave-Segura-1", cedula = "1234567" }));
        var invalida = await Cliente(superAdmin.Token).PostAsync(
            "/api/usuarios",
            Cuerpo(new { nombre = "Raro", email = "raro@ejemplo.com", rol = "inspector", password = "Clave-Segura-1", cedula = "12$" }));

        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var usuario = await LeerJsonAsync(creado);
        Assert.Equal("1234567", usuario.GetProperty("cedula").GetString());
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal("Ya existe un usuario con esa cédula", (await LeerJsonAsync(repetida)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.True((await LeerJsonAsync(invalida)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("cedula", out _));

        var borrada = await Cliente(superAdmin.Token).PatchAsync($"/api/usuarios/{usuario.GetProperty("id").GetGuid()}", Cuerpo(new { cedula = "" }));

        Assert.Equal(HttpStatusCode.OK, borrada.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await LeerJsonAsync(borrada)).GetProperty("cedula").ValueKind);
    }

    [Fact]
    public async Task Un_usuario_que_es_conductor_o_inspector_de_un_viaje_se_retira_en_lugar_de_borrarse()
    {
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        await Sembrador.ViajeAsync(conductorId: conductor.UsuarioId);

        var r = await Cliente(superAdmin.Token).DeleteAsync($"/api/usuarios/{conductor.UsuarioId}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("retirado", (await LeerJsonAsync(r)).GetProperty("modo").GetString());
    }

    // ------------------------------------------------------------------------------------- clientes del viaje

    private async Task<Guid> ClienteAsync(string codigo, string nombre, int orden)
    {
        var id = Guid.NewGuid();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblClientes (Id, Codigo, Nombre, Orden) VALUES (@id, @c, @n, @o)",
            ("@id", id), ("@c", codigo), ("@n", nombre), ("@o", orden));
        return id;
    }

    private Task<Guid> SofasaAsync() => IdAsync("tblClientes", "sofasa");

    /// <summary>Un viaje con dos VIN de Sofasa y uno de otro cliente, con su conductor y su inspector.</summary>
    private sealed record ViajeDosMarcas(Guid ViajeId, Guid Sofasa, Guid Otro, Sesion Coordinador, Sesion Conductor, Sesion Inspector);

    private async Task<ViajeDosMarcas> ViajeDosMarcasAsync()
    {
        var sofasa = await SofasaAsync();
        var otro = await ClienteAsync("otro", "Otro cliente", 2);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, clienteId: sofasa);
        var conductor = await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR);
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);
        var viajeId = await ViajeConVinesAsync(coordinador, "MTK-DOS", ["VIN-S-1", "VIN-S-2", "VIN-O-1"], conductor.UsuarioId, inspector.UsuarioId);
        await Sembrador.EjecutarAsync("UPDATE dbo.tblVehiculos SET ClienteId = @c WHERE Vin = 'VIN-O-1'", ("@c", otro));
        return new ViajeDosMarcas(viajeId, sofasa, otro, coordinador, conductor, inspector);
    }

    private static List<string?> Codigos(JsonElement viaje) =>
        viaje.GetProperty("clientes").EnumerateArray().Select(c => c.GetProperty("codigo").GetString()).Order().ToList();

    private static List<Guid> Ids(JsonElement lista) => lista.EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task Un_viaje_con_vin_de_dos_clientes_sale_al_filtrar_por_cualquiera_de_los_dos_y_trae_ambos_clientes()
    {
        var d = await ViajeDosMarcasAsync();
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var porSofasa = await LeerJsonAsync(await Cliente(superAdmin.Token).GetAsync($"/api/viajes?clienteId={d.Sofasa}"));
        var porOtro = await LeerJsonAsync(await Cliente(superAdmin.Token).GetAsync($"/api/viajes?clienteId={d.Otro}"));
        var sinFiltro = await LeerJsonAsync(await Cliente(superAdmin.Token).GetAsync("/api/viajes"));

        Assert.Equal([d.ViajeId], Ids(porSofasa));
        Assert.Equal([d.ViajeId], Ids(porOtro));
        Assert.Equal([d.ViajeId], Ids(sinFiltro));
        Assert.Equal(["otro", "sofasa"], Codigos(porSofasa[0]));
        Assert.Equal(["otro", "sofasa"], Codigos(porOtro[0]));
        Assert.Equal(3, porOtro[0].GetProperty("total_vehiculos").GetInt32());
        var sofasa = porSofasa[0].GetProperty("clientes").EnumerateArray().Single(c => c.GetProperty("codigo").GetString() == "sofasa");
        Assert.Equal(["id", "codigo", "nombre"], sofasa.EnumerateObject().Select(x => x.Name).ToList());
        Assert.Equal(d.Sofasa, sofasa.GetProperty("id").GetGuid());
        Assert.Equal("Otro cliente", porOtro[0].GetProperty("clientes").EnumerateArray().Single(c => c.GetProperty("codigo").GetString() == "otro").GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task El_filtro_por_cliente_sin_viajes_o_inexistente_da_lista_vacia_y_uno_mal_formado_es_400()
    {
        var d = await ViajeDosMarcasAsync();
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var sinViajes = await ClienteAsync("tercero", "Tercero", 3);

        var vacioReal = await Cliente(superAdmin.Token).GetAsync($"/api/viajes?clienteId={sinViajes}");
        var vacioInexistente = await Cliente(superAdmin.Token).GetAsync($"/api/viajes?clienteId={Guid.NewGuid()}");
        var invalido = await Cliente(superAdmin.Token).GetAsync("/api/viajes?clienteId=no-es-uuid");
        var invalidoDelCoordinador = await Cliente(d.Coordinador.Token).GetAsync("/api/viajes?clienteId=no-es-uuid");

        Assert.Equal(0, (await LeerJsonAsync(vacioReal)).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, vacioInexistente.StatusCode);
        Assert.Equal(0, (await LeerJsonAsync(vacioInexistente)).GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.True((await LeerJsonAsync(invalido)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("clienteId", out _));
        Assert.Equal(HttpStatusCode.BadRequest, invalidoDelCoordinador.StatusCode);
    }

    [Fact]
    public async Task Quien_no_es_super_administrador_solo_obtiene_los_viajes_de_su_cliente_con_o_sin_filtro_y_solo_cuenta_sus_vin()
    {
        var d = await ViajeDosMarcasAsync();
        var delOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, clienteId: d.Otro);
        var tercero = await ClienteAsync("tercero", "Tercero", 3);
        var delTercero = await SesionAsync("coord-tercero@ejemplo.com", clsRol.COORDINADOR_CALIDAD, clienteId: tercero);

        var sofasa = await LeerJsonAsync(await Cliente(d.Coordinador.Token).GetAsync("/api/viajes"));
        var sofasaPidiendoOtro = await LeerJsonAsync(await Cliente(d.Coordinador.Token).GetAsync($"/api/viajes?clienteId={d.Otro}"));
        var otro = await LeerJsonAsync(await Cliente(delOtro.Token).GetAsync("/api/viajes"));
        var otroPidiendoSofasa = await LeerJsonAsync(await Cliente(delOtro.Token).GetAsync($"/api/viajes?clienteId={d.Sofasa}"));
        var ninguno = await LeerJsonAsync(await Cliente(delTercero.Token).GetAsync("/api/viajes"));

        Assert.Equal([d.ViajeId], Ids(sofasa));
        Assert.Equal(["sofasa"], Codigos(sofasa[0]));
        Assert.Equal(2, sofasa[0].GetProperty("total_vehiculos").GetInt32());
        Assert.Equal([d.ViajeId], Ids(sofasaPidiendoOtro));
        Assert.Equal(["sofasa"], Codigos(sofasaPidiendoOtro[0]));
        Assert.Equal([d.ViajeId], Ids(otro));
        Assert.Equal(["otro"], Codigos(otro[0]));
        Assert.Equal(1, otro[0].GetProperty("total_vehiculos").GetInt32());
        Assert.Equal(["otro"], Codigos(otroPidiendoSofasa[0]));
        Assert.Equal(0, ninguno.GetArrayLength());
    }

    [Fact]
    public async Task Al_abrir_el_detalle_cada_usuario_de_un_cliente_ve_solo_los_vin_de_su_cliente()
    {
        var d = await ViajeDosMarcasAsync();
        var delOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, clienteId: d.Otro);
        var tercero = await ClienteAsync("tercero", "Tercero", 3);
        var delTercero = await SesionAsync("coord-tercero@ejemplo.com", clsRol.COORDINADOR_CALIDAD, clienteId: tercero);

        var sofasa = await LeerJsonAsync(await Cliente(d.Coordinador.Token).GetAsync($"/api/viajes/{d.ViajeId}"));
        var otro = await LeerJsonAsync(await Cliente(delOtro.Token).GetAsync($"/api/viajes/{d.ViajeId}"));
        var ajeno = await Cliente(delTercero.Token).GetAsync($"/api/viajes/{d.ViajeId}");

        Assert.Equal(["VIN-S-1", "VIN-S-2"], Vines(sofasa));
        Assert.Equal(["sofasa"], Codigos(sofasa));
        Assert.Equal(["VIN-O-1"], Vines(otro));
        Assert.Equal(["otro"], Codigos(otro));
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal("FORBIDDEN", (await LeerJsonAsync(ajeno)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task El_conductor_el_inspector_y_el_super_administrador_ven_todos_los_vin_del_viaje()
    {
        var d = await ViajeDosMarcasAsync();
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);

        var delConductor = await LeerJsonAsync(await Cliente(d.Conductor.Token).GetAsync($"/api/viajes/{d.ViajeId}"));
        var delInspector = await LeerJsonAsync(await Cliente(d.Inspector.Token).GetAsync($"/api/viajes/{d.ViajeId}"));
        var delSuper = await LeerJsonAsync(await Cliente(superAdmin.Token).GetAsync($"/api/viajes/{d.ViajeId}"));
        var mios = await LeerJsonAsync(await Cliente(d.Conductor.Token).GetAsync("/api/viajes/mios"));
        var listadoDelConductor = await LeerJsonAsync(await Cliente(d.Conductor.Token).GetAsync("/api/viajes"));

        foreach (var viaje in new[] { delConductor, delInspector, delSuper, mios[0] })
        {
            Assert.Equal(["VIN-O-1", "VIN-S-1", "VIN-S-2"], Vines(viaje));
            Assert.Equal(["otro", "sofasa"], Codigos(viaje));
        }

        Assert.Equal(3, listadoDelConductor[0].GetProperty("total_vehiculos").GetInt32());
        Assert.Equal(["otro", "sofasa"], Codigos(listadoDelConductor[0]));
    }

    [Fact]
    public async Task Un_viaje_sin_vin_solo_lo_ve_su_cliente_hasta_que_tenga_unidades()
    {
        var sofasa = await SofasaAsync();
        var otro = await ClienteAsync("otro", "Otro cliente", 2);
        var coordinador = await SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD, clienteId: sofasa);
        var delOtro = await SesionAsync("coord-otro@ejemplo.com", clsRol.COORDINADOR_CALIDAD, clienteId: otro);
        var superAdmin = await SesionAsync("super@ejemplo.com", clsRol.SUPER_ADMIN);
        var creado = await CrearViajeAsync(coordinador, "MTK-VACIO");
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var viajeId = (await LeerJsonAsync(creado)).GetProperty("id").GetGuid();

        var propio = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes"));
        var ajeno = await LeerJsonAsync(await Cliente(delOtro.Token).GetAsync("/api/viajes"));
        var detalleAjeno = await Cliente(delOtro.Token).GetAsync($"/api/viajes/{viajeId}");
        var detallePropio = await Cliente(coordinador.Token).GetAsync($"/api/viajes/{viajeId}");
        var delSuper = await LeerJsonAsync(await Cliente(superAdmin.Token).GetAsync($"/api/viajes?clienteId={sofasa}"));

        Assert.Equal([viajeId], Ids(propio));
        Assert.Empty(Codigos(propio[0]));
        Assert.Equal(0, ajeno.GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, detalleAjeno.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detallePropio.StatusCode);
        // Sin VIN no hay a que cliente atribuirlo: el filtro por cliente del super administrador solo trae viajes con unidades de ese cliente.
        Assert.Equal(0, delSuper.GetArrayLength());
    }
}
