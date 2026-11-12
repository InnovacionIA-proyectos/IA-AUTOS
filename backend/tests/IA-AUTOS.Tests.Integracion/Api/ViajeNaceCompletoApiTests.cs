using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El viaje nace completo: la fecha programada y el lugar de cargue son obligatorios al crear, el tipo de origen se deduce del tipo del lugar de cargue (por la API y por la carga por archivo, y se recalcula al corregir el
/// lugar), crear rechaza los campos retirados como corregir, y la lista trae los destinos de las unidades y se filtra por ellos.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class ViajeNaceCompletoApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private const string FECHA = "2026-11-18T06:00:00-05:00";

    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> CoordinadorAsync()
    {
        var usuario = await Fabrica.SembrarUsuarioAsync("coord@ejemplo.com", "Clave-Segura-1", clsRol.COORDINADOR_CALIDAD, nombre: "Coordinadora");
        return new Sesion(await LoginAsync("coord@ejemplo.com", "Clave-Segura-1"), usuario.Id);
    }

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> LeerJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Guid> UnidadAsync() => (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;

    private Task<Guid> LugarAsync(Sesion quien, string nombre, string tipo) => LugarRealAsync(Cliente(quien.Token), nombre, tipo);

    private async Task<Guid> TipoAsync(string codigo) => (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = @c", ("@c", codigo)))!;

    private async Task<Dictionary<string, object?>> CuerpoViajeAsync(string codigo, Guid? lugarCargue, string? fecha = FECHA, object[]? unidades = null)
    {
        var cuerpo = new Dictionary<string, object?>
        {
            ["codigo_mtrack"] = codigo,
            ["tipo_unidad_id"] = await UnidadAsync(),
            ["placa"] = "ABC123",
        };
        if (fecha is not null)
        {
            cuerpo["fecha_programada"] = fecha;
        }

        if (lugarCargue is not null)
        {
            cuerpo["lugar_cargue_id"] = lugarCargue;
        }

        if (unidades is not null)
        {
            cuerpo["unidades"] = unidades;
        }

        return cuerpo;
    }

    private Task<HttpResponseMessage> CrearAsync(Sesion quien, Dictionary<string, object?> cuerpo) => Cliente(quien.Token).PostAsync("/api/viajes", Cuerpo(cuerpo));

    private static async Task<string?> ErrorDeCampoAsync(HttpResponseMessage r, string campo)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var errores = (await LeerJsonAsync(r)).GetProperty("details").GetProperty("fieldErrors");
        return errores.TryGetProperty(campo, out var mensajes) ? mensajes[0].GetString() : null;
    }

    // ------------------------------------------------------------------------------------------------ ESC-01 a ESC-03

    [Fact]
    public async Task Crear_un_viaje_con_su_fecha_y_su_lugar_lo_deja_programado_con_el_tipo_de_origen_del_lugar()
    {
        var coordinador = await CoordinadorAsync();
        var cedi = await LugarAsync(coordinador, "CEDI Envigado", "cedi_1");

        var r = await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0417", cedi));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var viaje = await LeerJsonAsync(r);
        Assert.Equal(DateTimeOffset.Parse(FECHA), viaje.GetProperty("fecha_programada").GetDateTimeOffset());
        Assert.Equal(cedi, viaje.GetProperty("lugar_cargue_id").GetGuid());
        Assert.Equal("CEDI Envigado", viaje.GetProperty("lugar_cargue").GetString());
        Assert.Equal(await TipoAsync("cedi_1"), viaje.GetProperty("origen_id").GetGuid());
        Assert.Equal("CEDI 1", viaje.GetProperty("origen").GetString());
    }

    [Fact]
    public async Task Sin_fecha_o_sin_lugar_el_viaje_no_se_crea_y_el_mensaje_nombra_el_campo()
    {
        var coordinador = await CoordinadorAsync();
        var cedi = await LugarAsync(coordinador, "CEDI Envigado", "cedi_1");

        var sinFecha = await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0418", cedi, fecha: null));
        var sinLugar = await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0419", null));
        var fechaSinZona = await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0420", cedi, fecha: "2026-11-18 06:00"));
        var lugarInexistente = await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0421", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, (await Cliente(coordinador.Token).PatchAsync($"/api/catalogos/lugares/{cedi}", Cuerpo(new { activo = false }))).StatusCode);
        var lugarInactivo = await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0422", cedi));

        Assert.Equal("La fecha programada es obligatoria", await ErrorDeCampoAsync(sinFecha, "fecha_programada"));
        Assert.Equal("El lugar de cargue es obligatorio", await ErrorDeCampoAsync(sinLugar, "lugar_cargue_id"));
        Assert.Contains("ISO 8601", await ErrorDeCampoAsync(fechaSinZona, "fecha_programada"));
        Assert.Contains("no existe", await ErrorDeCampoAsync(lugarInexistente, "lugar_cargue_id"));
        Assert.Contains("inactivo", await ErrorDeCampoAsync(lugarInactivo, "lugar_cargue_id"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
    }

    [Theory]
    [InlineData("origen_id")]
    [InlineData("destino_id")]
    [InlineData("vines")]
    [InlineData("con_personal_apoyo")]
    [InlineData("campo_inventado")]
    public async Task Crear_con_un_campo_retirado_responde_Campo_no_admitido_y_no_crea_nada(string campo)
    {
        var coordinador = await CoordinadorAsync();
        var cedi = await LugarAsync(coordinador, "CEDI Envigado", "cedi_1");
        var cuerpo = await CuerpoViajeAsync("MT-2026-0430", cedi);
        cuerpo[campo] = campo == "con_personal_apoyo" ? true : Guid.NewGuid();

        var r = await CrearAsync(coordinador, cuerpo);

        Assert.StartsWith("Campo no admitido", await ErrorDeCampoAsync(r, campo));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
    }

    // ------------------------------------------------------------------------------------------------ el tipo de origen sigue al lugar

    [Fact]
    public async Task Corregir_el_lugar_de_cargue_recalcula_el_tipo_de_origen_y_lo_lleva_al_cargue_de_sus_unidades()
    {
        var coordinador = await CoordinadorAsync();
        var cedi = await LugarAsync(coordinador, "CEDI Envigado", "cedi_1");
        var patio = await LugarAsync(coordinador, "Patio de Bello", "origen");
        var destino = await LugarAsync(coordinador, "Concesionario Norte", "concesionario");
        await Sembrador.VehiculoAsync("VIN-N-01");
        var creado = await LeerJsonAsync(await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0440", cedi, unidades: [new { vin = "VIN-N-01", lugar_destino_id = destino }])));
        var id = creado.GetProperty("id").GetGuid();
        Assert.Equal(await TipoAsync("cedi_1"), creado.GetProperty("origen_id").GetGuid());

        var r = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{id}", Cuerpo(new { lugar_cargue_id = patio }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var viaje = await LeerJsonAsync(r);
        Assert.Equal(patio, viaje.GetProperty("lugar_cargue_id").GetGuid());
        Assert.Equal(await TipoAsync("origen"), viaje.GetProperty("origen_id").GetGuid());
        Assert.Equal("Origen", viaje.GetProperty("origen").GetString());
        Assert.Equal(
            await TipoAsync("origen"),
            await Sembrador.EscalarAsync("SELECT e.PuntoCustodiaId FROM dbo.tblEtapas e JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId WHERE e.ViajeId = @v AND c.Codigo = 'cargue'", ("@v", id)));
    }

    [Fact]
    public async Task Un_viaje_anterior_sin_fecha_ni_lugar_se_sigue_leyendo_y_corrigiendo()
    {
        var coordinador = await CoordinadorAsync();
        var cedi = await LugarAsync(coordinador, "CEDI Envigado", "cedi_1");
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-OLD-01");
        var viejo = await Sembrador.ViajeDeEtapaAsync(etapas[0]);

        var leido = await Cliente(coordinador.Token).GetAsync($"/api/viajes/{viejo}");
        var corregido = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viejo}", Cuerpo(new { placa = "XYZ789" }));
        var conLugar = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viejo}", Cuerpo(new { lugar_cargue_id = cedi, fecha_programada = FECHA }));

        Assert.Equal(HttpStatusCode.OK, leido.StatusCode);
        var detalle = await LeerJsonAsync(leido);
        Assert.Equal(JsonValueKind.Null, detalle.GetProperty("fecha_programada").ValueKind);
        Assert.Equal(JsonValueKind.Null, detalle.GetProperty("lugar_cargue_id").ValueKind);
        Assert.Equal(HttpStatusCode.OK, corregido.StatusCode);
        Assert.Equal(HttpStatusCode.OK, conLugar.StatusCode);
        Assert.Equal(await TipoAsync("cedi_1"), (await LeerJsonAsync(conLugar)).GetProperty("origen_id").GetGuid());
    }

    [Fact]
    public async Task El_viaje_que_entra_por_archivo_queda_con_el_tipo_de_origen_de_su_lugar_de_cargue()
    {
        var coordinador = await CoordinadorAsync();
        await Fabrica.SembrarUsuarioAsync("cond@ejemplo.com", "Clave-Segura-1", clsRol.CONDUCTOR, nombre: "Conductor", cedula: "1234567");
        await Sembrador.VehiculoAsync("VIN-A-01");
        await LugarAsync(coordinador, "CEDI Envigado", "cedi_1");
        await LugarAsync(coordinador, "Concesionario Norte", "concesionario");
        var fila = new { fila = 1, vin = "VIN-A-01", placa = "ABC123", trailer = "T-1", cedula_conductor = "1234567", unidad = "Niñera", codigo_mtrack = "MT-2026-0450", fecha_programada = "2026-11-18 06:00", origen = "CEDI Envigado", destino = "Concesionario Norte" };

        var r = await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar", Cuerpo(new { filas = new[] { fila } }));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes WHERE CodigoMtrack = 'MT-2026-0450' AND OrigenId IS NOT NULL"));
        Assert.Equal(await TipoAsync("cedi_1"), await Sembrador.EscalarAsync("SELECT OrigenId FROM dbo.tblViajes WHERE CodigoMtrack = 'MT-2026-0450'"));
        Assert.Equal(
            await TipoAsync("cedi_1"),
            await Sembrador.EscalarAsync("SELECT e.PuntoCustodiaId FROM dbo.tblEtapas e JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId JOIN dbo.tblViajes v ON v.Id = e.ViajeId WHERE v.CodigoMtrack = 'MT-2026-0450' AND c.Codigo = 'cargue'"));
    }

    // ------------------------------------------------------------------------------------------------ la lista trae los destinos (ESC-04 y ESC-05)

    [Fact]
    public async Task La_fila_del_listado_trae_los_destinos_distintos_de_sus_unidades_con_cuantas_van_a_cada_uno_y_el_filtro_los_busca_una_sola_vez()
    {
        var coordinador = await CoordinadorAsync();
        var cedi = await LugarAsync(coordinador, "CEDI Envigado", "cedi_1");
        var norte = await LugarAsync(coordinador, "Concesionario Norte", "concesionario");
        var sur = await LugarAsync(coordinador, "Concesionario Sur", "concesionario");
        var unidades = new List<object>();
        for (var i = 1; i <= 8; i++)
        {
            var vin = $"VIN-L-{i:00}";
            await Sembrador.VehiculoAsync(vin);
            unidades.Add(new { vin, lugar_destino_id = i <= 5 ? norte : sur });
        }

        var creado = await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0417", cedi, unidades: [.. unidades]));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var otro = await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0418", cedi));
        Assert.Equal(HttpStatusCode.Created, otro.StatusCode);

        var lista = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes"));
        var fila = lista.GetProperty("items").EnumerateArray().Single(v => v.GetProperty("codigo_mtrack").GetString() == "MT-2026-0417");
        var porSur = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/viajes?destino_unidad_id={sur}"));
        var porCedi = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/viajes?destino_unidad_id={cedi}"));
        var invalido = await Cliente(coordinador.Token).GetAsync("/api/viajes?destino_unidad_id=no-es-uuid");

        Assert.Equal(
            [("Concesionario Norte", 5), ("Concesionario Sur", 3)],
            fila.GetProperty("destinos").EnumerateArray().Select(d => (d.GetProperty("lugar").GetString()!, d.GetProperty("unidades").GetInt32())).ToList());
        Assert.Equal(norte, fila.GetProperty("destinos")[0].GetProperty("lugar_id").GetGuid());
        Assert.Equal("CEDI Envigado", fila.GetProperty("lugar_cargue").GetString());
        Assert.Equal(8, fila.GetProperty("total_vehiculos").GetInt32());
        Assert.Equal(0, lista.GetProperty("items").EnumerateArray().Single(v => v.GetProperty("codigo_mtrack").GetString() == "MT-2026-0418").GetProperty("destinos").GetArrayLength());
        // El viaje con 3 unidades a «Concesionario Sur» sale una sola vez, y el lugar de cargue no es un destino de unidad.
        Assert.Equal(1, porSur.GetProperty("total").GetInt32());
        Assert.Equal("MT-2026-0417", porSur.GetProperty("items")[0].GetProperty("codigo_mtrack").GetString());
        Assert.Equal(0, porCedi.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }

    [Fact]
    public async Task El_filtro_destino_id_sigue_siendo_el_tipo_de_destino_del_viaje_y_no_el_de_sus_unidades()
    {
        var coordinador = await CoordinadorAsync();
        var cedi = await LugarAsync(coordinador, "CEDI Envigado", "cedi_1");
        var norte = await LugarAsync(coordinador, "Concesionario Norte", "concesionario");
        await Sembrador.VehiculoAsync("VIN-D-01");
        await CrearAsync(coordinador, await CuerpoViajeAsync("MT-2026-0460", cedi, unidades: [new { vin = "VIN-D-01", lugar_destino_id = norte }]));

        var porTipo = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/viajes?destino_id={await TipoAsync("concesionario")}"));

        // Los viajes nuevos no tienen destino de viaje: el destino es de cada unidad.
        Assert.Equal(0, porTipo.GetProperty("total").GetInt32());
    }
}
