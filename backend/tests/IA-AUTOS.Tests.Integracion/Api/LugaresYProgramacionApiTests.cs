using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El catalogo de lugares reales y la carga de cargues con programacion: el coordinador crea lugares (ESC-01), carga por Excel con fecha y hora programada, lugar de cargue y destino por unidad (ESC-02 a ESC-05),
/// crea un lugar desde la previsualizacion (ESC-04), solo quien administra cambia el catalogo (ESC-07) y el viaje programado admite corregir su fecha y su lugar de cargue.
/// </summary>
[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class LugaresYProgramacionApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    private async Task<Sesion> SesionAsync(string email, string rol, string? cedula = null)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email, cedula: cedula);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private Task<Sesion> CoordinadorAsync() => SesionAsync("coord@ejemplo.com", clsRol.COORDINADOR_CALIDAD);

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> LeerJsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<HttpResponseMessage> CrearLugarAsync(Sesion quien, string nombre, string tipo = "cedi_1", string ciudad = "Envigado", object? extra = null) =>
        await Cliente(quien.Token).PostAsync("/api/catalogos/lugares", Cuerpo(extra ?? new { nombre, tipo, ciudad }));

    private static object Fila(int n, string vin, string codigo, string? fecha = "2026-11-03 06:00", string? origen = "CEDI Envigado", string? destino = "Concesionario Autolarte Medellín", string placa = "ABC123") =>
        new { fila = n, vin, placa, trailer = "T-1", cedula_conductor = "1234567", unidad = "Niñera", codigo_mtrack = codigo, fecha_programada = fecha, origen, destino };

    private async Task PrepararAsync(Sesion coordinador, params string[] vines)
    {
        await SesionAsync("cond@ejemplo.com", clsRol.CONDUCTOR, cedula: "1234567");
        foreach (var vin in vines)
        {
            await Sembrador.VehiculoAsync(vin);
        }

        Assert.Equal(HttpStatusCode.Created, (await CrearLugarAsync(coordinador, "CEDI Envigado")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CrearLugarAsync(coordinador, "Concesionario Autolarte Medellín", "concesionario", "Medellín")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CrearLugarAsync(coordinador, "Concesionario Caminos Cali", "concesionario", "Cali")).StatusCode);
    }

    private async Task<JsonElement> PrevisualizarAsync(Sesion quien, params object[] filas) =>
        await LeerJsonAsync(await Cliente(quien.Token).PostAsync("/api/viajes/cargar/previsualizar", Cuerpo(new { filas })));

    private static Dictionary<int, string> Errores(JsonElement resultado) =>
        resultado.GetProperty("filas").EnumerateArray()
            .ToDictionary(f => f.GetProperty("fila").GetInt32(), f => string.Join(" | ", f.GetProperty("errores").EnumerateArray().Select(e => e.GetString())));

    // ----------------------------------------------------------------------------------------------- el catalogo (ESC-01 y ESC-07)

    [Fact]
    public async Task El_coordinador_crea_un_lugar_real_que_sale_activo_y_sin_ubicacion_y_cualquier_autenticado_lo_lista()
    {
        var coordinador = await CoordinadorAsync();
        var inspector = await SesionAsync("insp@ejemplo.com", clsRol.INSPECTOR);

        var creado = await CrearLugarAsync(coordinador, "CEDI Envigado");

        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var lugar = await LeerJsonAsync(creado);
        Assert.Equal(
            ["id", "nombre", "tipo", "tipo_nombre", "ciudad", "orden", "activo", "latitud", "longitud", "radio_metros", "sin_ubicacion"], lugar.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("CEDI Envigado", lugar.GetProperty("nombre").GetString());
        Assert.Equal("cedi_1", lugar.GetProperty("tipo").GetString());
        Assert.Equal("CEDI 1", lugar.GetProperty("tipo_nombre").GetString());
        Assert.Equal("Envigado", lugar.GetProperty("ciudad").GetString());
        Assert.True(lugar.GetProperty("activo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, lugar.GetProperty("latitud").ValueKind);
        Assert.Equal(JsonValueKind.Null, lugar.GetProperty("longitud").ValueKind);
        Assert.Equal(JsonValueKind.Null, lugar.GetProperty("radio_metros").ValueKind);
        Assert.True(lugar.GetProperty("sin_ubicacion").GetBoolean());

        var lista = await Cliente(inspector.Token).GetAsync("/api/catalogos/lugares");
        var todos = await LeerJsonAsync(await Cliente(inspector.Token).GetAsync("/api/catalogos"));
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        Assert.NotNull(lista.Headers.ETag);
        Assert.Equal(["CEDI Envigado"], (await LeerJsonAsync(lista)).GetProperty("items").EnumerateArray().Select(l => l.GetProperty("nombre").GetString()).ToList());
        Assert.Equal(1, todos.GetProperty("catalogos").GetProperty("lugares").GetArrayLength());
        // El catalogo arranca vacio: ningun lugar se siembra.
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares WHERE Nombre <> N'CEDI Envigado'"));
    }

    [Theory]
    [InlineData(clsRol.ADMIN, HttpStatusCode.Created)]
    [InlineData(clsRol.SUPER_ADMIN, HttpStatusCode.Created)]
    [InlineData(clsRol.COORDINADOR_CALIDAD, HttpStatusCode.Created)]
    [InlineData(clsRol.INSPECTOR, HttpStatusCode.Forbidden)]
    [InlineData(clsRol.CONDUCTOR, HttpStatusCode.Forbidden)]
    [InlineData(clsRol.COMERCIAL, HttpStatusCode.Forbidden)]
    public async Task Solo_admin_super_admin_y_coordinador_de_calidad_crean_editan_e_inactivan_lugares(string rol, HttpStatusCode esperado)
    {
        var quien = await SesionAsync("u@ejemplo.com", rol);

        var crear = await CrearLugarAsync(quien, "Patio Norte", "origen", "Bello");

        Assert.Equal(esperado, crear.StatusCode);
        if (esperado == HttpStatusCode.Forbidden)
        {
            Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares"));
            return;
        }

        var id = (await LeerJsonAsync(crear)).GetProperty("id").GetGuid();
        var editado = await Cliente(quien.Token).PatchAsync($"/api/catalogos/lugares/{id}", Cuerpo(new { ciudad = "Medellín", activo = false }));
        Assert.Equal(HttpStatusCode.OK, editado.StatusCode);
        var valor = await LeerJsonAsync(editado);
        Assert.Equal("Medellín", valor.GetProperty("ciudad").GetString());
        Assert.False(valor.GetProperty("activo").GetBoolean());
    }

    [Fact]
    public async Task Crear_un_lugar_valida_nombre_tipo_y_ciudad_y_un_nombre_repetido_es_conflicto()
    {
        var coordinador = await CoordinadorAsync();
        Assert.Equal(HttpStatusCode.Created, (await CrearLugarAsync(coordinador, "CEDI Envigado")).StatusCode);

        var sinNombre = await CrearLugarAsync(coordinador, "", extra: new { tipo = "cedi_1", ciudad = "Envigado" });
        var sinTipo = await CrearLugarAsync(coordinador, "", extra: new { nombre = "Sin tipo", ciudad = "Envigado" });
        var tipoInexistente = await CrearLugarAsync(coordinador, "", extra: new { nombre = "Tipo raro", tipo = "no_existe", ciudad = "Envigado" });
        var sinCiudad = await CrearLugarAsync(coordinador, "", extra: new { nombre = "Sin ciudad", tipo = "cedi_1" });
        var repetido = await CrearLugarAsync(coordinador, "cedi envigado");
        var camposDeMas = await CrearLugarAsync(coordinador, "", extra: new { nombre = "Con ubicacion", tipo = "cedi_1", ciudad = "Envigado", latitud = 6.2 });

        foreach (var r in new[] { sinNombre, sinTipo, tipoInexistente, sinCiudad, camposDeMas })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        var campos = (await LeerJsonAsync(sinTipo)).GetProperty("details").GetProperty("fieldErrors");
        Assert.True(campos.TryGetProperty("tipo", out _));
        Assert.True((await LeerJsonAsync(sinCiudad)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("ciudad", out _));
        Assert.True((await LeerJsonAsync(tipoInexistente)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("tipo", out _));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblLugares"));
    }

    // ----------------------------------------------------------------------------------------------- la carga (ESC-02 a ESC-05)

    [Fact]
    public async Task La_carga_completa_crea_el_viaje_con_su_fecha_su_lugar_de_cargue_y_el_destino_de_cada_unidad()
    {
        var coordinador = await CoordinadorAsync();
        await PrepararAsync(coordinador, "VIN-L-01", "VIN-L-02");
        object[] filas =
        [
            Fila(1, "VIN-L-01", "M-4501", destino: "Concesionario Autolarte Medellín"),
            Fila(2, "VIN-L-02", "M-4501", destino: "Concesionario Caminos Cali"),
        ];

        var vista = await PrevisualizarAsync(coordinador, filas);
        var confirmada = await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar", Cuerpo(new { filas }));

        Assert.Equal(2, vista.GetProperty("validas").GetInt32());
        Assert.Equal(0, vista.GetProperty("invalidas").GetInt32());
        Assert.Equal(1, vista.GetProperty("viajes").GetInt32());
        Assert.Equal(HttpStatusCode.Created, confirmada.StatusCode);
        var viajeId = (await LeerJsonAsync(confirmada)).GetProperty("filas")[0].GetProperty("viaje_id").GetGuid();

        // El viaje queda programado el 2026-11-03 a las 06:00 (hora de Colombia) en CEDI Envigado y cada unidad conserva su destino.
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblViajes v JOIN dbo.tblLugares l ON l.Id = v.LugarCargueId WHERE v.Id = @v AND l.Nombre = N'CEDI Envigado' AND v.FechaProgramada = '2026-11-03T06:00:00-05:00' AND v.Estado = 'programado'", ("@v", viajeId)));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos h ON h.Id = vv.VehiculoId JOIN dbo.tblLugares l ON l.Id = vv.LugarDestinoId WHERE vv.ViajeId = @v AND h.Vin = 'VIN-L-01' AND l.Nombre = N'Concesionario Autolarte Medellín'", ("@v", viajeId)));
        Assert.Equal(1, await Sembrador.ContarAsync(
            "SELECT COUNT(*) FROM dbo.tblViajeVehiculos vv JOIN dbo.tblVehiculos h ON h.Id = vv.VehiculoId JOIN dbo.tblLugares l ON l.Id = vv.LugarDestinoId WHERE vv.ViajeId = @v AND h.Vin = 'VIN-L-02' AND l.Nombre = N'Concesionario Caminos Cali'", ("@v", viajeId)));

        // El viaje lo muestra: fecha programada, lugar de cargue y el destino de cada unidad.
        var viaje = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync($"/api/viajes/{viajeId}"));
        Assert.Equal(DateTimeOffset.Parse("2026-11-03T06:00:00-05:00"), viaje.GetProperty("fecha_programada").GetDateTimeOffset());
        Assert.Equal("CEDI Envigado", viaje.GetProperty("lugar_cargue").GetString());
        Assert.NotEqual(JsonValueKind.Null, viaje.GetProperty("lugar_cargue_id").ValueKind);
        var destinos = viaje.GetProperty("vehiculos").EnumerateArray().ToDictionary(v => v.GetProperty("vin").GetString()!, v => v.GetProperty("lugar_destino").GetString());
        Assert.Equal("Concesionario Autolarte Medellín", destinos["VIN-L-01"]);
        Assert.Equal("Concesionario Caminos Cali", destinos["VIN-L-02"]);
        var listado = await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/viajes"));
        Assert.Equal("CEDI Envigado", listado.GetProperty("items")[0].GetProperty("lugar_cargue").GetString());
    }

    [Fact]
    public async Task Un_lugar_inexistente_o_inactivo_y_una_fecha_faltante_o_mal_escrita_invalidan_solo_su_fila()
    {
        var coordinador = await CoordinadorAsync();
        await PrepararAsync(coordinador, "VIN-L-01", "VIN-L-02", "VIN-L-03", "VIN-L-04", "VIN-L-05", "VIN-L-06");
        var lugares = (await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/catalogos/lugares"))).GetProperty("items");
        var caminos = lugares.EnumerateArray().Single(l => l.GetProperty("nombre").GetString() == "Concesionario Caminos Cali").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await Cliente(coordinador.Token).PatchAsync($"/api/catalogos/lugares/{caminos}", Cuerpo(new { activo = false }))).StatusCode);

        var vista = await PrevisualizarAsync(
            coordinador,
            Fila(1, "VIN-L-01", "M-1"),
            Fila(2, "VIN-L-02", "M-2"),
            Fila(3, "VIN-L-03", "M-3", origen: "CEDI Rionegro"),
            Fila(4, "VIN-L-04", "M-4", fecha: null),
            Fila(5, "VIN-L-05", "M-5", destino: "Concesionario Caminos Cali"),
            Fila(6, "VIN-L-06", "M-6", fecha: "03/11/2026 6am", origen: null, destino: ""));

        var errores = Errores(vista);
        Assert.Equal(string.Empty, errores[1]);
        Assert.Equal(string.Empty, errores[2]);
        Assert.Equal("El lugar CEDI Rionegro no existe en el catálogo", errores[3]);
        Assert.Equal("La fecha y hora programada del cargue es requerida", errores[4]);
        Assert.Equal("El lugar Concesionario Caminos Cali no existe en el catálogo", errores[5]);
        Assert.Contains("no es válida", errores[6]);
        Assert.Contains("El lugar de cargue (origen) es requerido", errores[6]);
        Assert.Contains("El lugar de destino de la unidad es requerido", errores[6]);
        Assert.Equal(2, vista.GetProperty("validas").GetInt32());
        Assert.Equal(4, vista.GetProperty("invalidas").GetInt32());
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes"));
    }

    [Fact]
    public async Task Las_filas_de_un_mismo_codigo_mtrack_deben_coincidir_en_fecha_y_origen_pero_cada_unidad_tiene_su_destino()
    {
        var coordinador = await CoordinadorAsync();
        await PrepararAsync(coordinador, "VIN-L-01", "VIN-L-02", "VIN-L-03", "VIN-L-04");
        Assert.Equal(HttpStatusCode.Created, (await CrearLugarAsync(coordinador, "CEDI Bello", "cedi_2", "Bello")).StatusCode);

        var vista = await PrevisualizarAsync(
            coordinador,
            Fila(1, "VIN-L-01", "M-9"),
            Fila(2, "VIN-L-02", "M-9", fecha: "2026-11-04 06:00"),
            Fila(3, "VIN-L-03", "M-9", origen: "CEDI Bello"),
            Fila(4, "VIN-L-04", "M-9", fecha: "2026-11-04 06:00", origen: "CEDI Bello", destino: "Concesionario Caminos Cali"));

        var errores = Errores(vista);
        Assert.Equal(string.Empty, errores[1]);
        Assert.Contains("No coincide con el viaje de la fila 1 (la fecha programada)", errores[2]);
        Assert.Contains("No coincide con el viaje de la fila 1 (el lugar de cargue)", errores[3]);
        Assert.Contains("la fecha programada, el lugar de cargue", errores[4]);
        Assert.Equal(1, vista.GetProperty("validas").GetInt32());
        // La misma hora escrita con zona horaria es la misma fecha: «06:00» de Colombia y «11:00Z» coinciden.
        var igual = await PrevisualizarAsync(coordinador, Fila(1, "VIN-L-01", "M-8"), Fila(2, "VIN-L-02", "M-8", fecha: "2026-11-03T11:00:00Z", destino: "Concesionario Caminos Cali"));
        Assert.Equal(2, igual.GetProperty("validas").GetInt32());
    }

    [Fact]
    public async Task Crear_el_lugar_desde_la_previsualizacion_deja_la_fila_valida_al_volver_a_previsualizar()
    {
        var coordinador = await CoordinadorAsync();
        await PrepararAsync(coordinador, "VIN-L-01", "VIN-L-02", "VIN-L-03");
        object[] filas = [Fila(1, "VIN-L-01", "M-1"), Fila(2, "VIN-L-02", "M-1"), Fila(3, "VIN-L-03", "M-2", origen: "CEDI Rionegro")];

        var antes = await PrevisualizarAsync(coordinador, filas);
        var creado = await CrearLugarAsync(coordinador, "CEDI Rionegro", "cedi_1", "Rionegro");
        var despues = await PrevisualizarAsync(coordinador, filas);

        Assert.Equal("El lugar CEDI Rionegro no existe en el catálogo", Errores(antes)[3]);
        Assert.Equal(2, antes.GetProperty("validas").GetInt32());
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        Assert.Equal(string.Empty, Errores(despues)[3]);
        Assert.Equal(3, despues.GetProperty("validas").GetInt32());
        Assert.Equal(2, despues.GetProperty("viajes").GetInt32());
    }

    [Fact]
    public async Task Un_lugar_que_un_viaje_usa_no_se_borra_pero_si_se_inactiva()
    {
        var coordinador = await CoordinadorAsync();
        await PrepararAsync(coordinador, "VIN-L-01");
        Assert.Equal(HttpStatusCode.Created, (await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar", Cuerpo(new { filas = new[] { Fila(1, "VIN-L-01", "M-1") } }))).StatusCode);
        var id = (await LeerJsonAsync(await Cliente(coordinador.Token).GetAsync("/api/catalogos/lugares"))).GetProperty("items").EnumerateArray()
            .Single(l => l.GetProperty("nombre").GetString() == "CEDI Envigado").GetProperty("id").GetGuid();

        var borrar = await Cliente(coordinador.Token).DeleteAsync($"/api/catalogos/lugares/{id}");
        var inactivar = await Cliente(coordinador.Token).PatchAsync($"/api/catalogos/lugares/{id}", Cuerpo(new { activo = false }));

        Assert.Equal(HttpStatusCode.Conflict, borrar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inactivar.StatusCode);
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajes WHERE LugarCargueId IS NOT NULL"));
    }

    // ----------------------------------------------------------------------------------------------- corregir la programacion del viaje (CA-6)

    [Fact]
    public async Task El_viaje_programado_admite_corregir_su_fecha_y_su_lugar_de_cargue_y_uno_en_curso_no()
    {
        var coordinador = await CoordinadorAsync();
        await PrepararAsync(coordinador, "VIN-L-01");
        var filas = new[] { Fila(1, "VIN-L-01", "M-1") };
        var viajeId = (await LeerJsonAsync(await Cliente(coordinador.Token).PostAsync("/api/viajes/cargar", Cuerpo(new { filas })))).GetProperty("filas")[0].GetProperty("viaje_id").GetGuid();
        var cali = (await LeerJsonAsync(await CrearLugarAsync(coordinador, "CEDI Cali", "cedi_1", "Cali"))).GetProperty("id").GetGuid();

        var r = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viajeId}", Cuerpo(new { fecha_programada = "2026-11-05T07:30:00-05:00", lugar_cargue_id = cali }));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var viaje = await LeerJsonAsync(r);
        Assert.Equal(DateTimeOffset.Parse("2026-11-05T07:30:00-05:00"), viaje.GetProperty("fecha_programada").GetDateTimeOffset());
        Assert.Equal("CEDI Cali", viaje.GetProperty("lugar_cargue").GetString());
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'viaje.actualizar' AND EntidadId = @v", ("@v", viajeId)));

        // Errores: un lugar inexistente, una fecha sin zona horaria o mal escrita, y quitar la fecha.
        var inexistente = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viajeId}", Cuerpo(new { lugar_cargue_id = Guid.NewGuid() }));
        var sinZona = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viajeId}", Cuerpo(new { fecha_programada = "2026-11-05 07:30" }));
        var quitar = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viajeId}", Cuerpo(new { fecha_programada = (string?)null }));
        Assert.Equal(HttpStatusCode.BadRequest, inexistente.StatusCode);
        Assert.True((await LeerJsonAsync(inexistente)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("lugar_cargue_id", out _));
        Assert.Equal(HttpStatusCode.BadRequest, sinZona.StatusCode);
        Assert.True((await LeerJsonAsync(sinZona)).GetProperty("details").GetProperty("fieldErrors").TryGetProperty("fecha_programada", out _));
        Assert.Equal(HttpStatusCode.OK, quitar.StatusCode);

        // Un viaje en curso ya no se reprograma (409), aunque lo demas del viaje si se siga editando.
        Assert.Equal(HttpStatusCode.OK, (await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viajeId}/estado", Cuerpo(new { estado = "en_curso" }))).StatusCode);
        var enCurso = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viajeId}", Cuerpo(new { fecha_programada = "2026-11-06T07:30:00-05:00" }));
        var otroCampo = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viajeId}", Cuerpo(new { trailer = "T-77" }));
        Assert.Equal(HttpStatusCode.Conflict, enCurso.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otroCampo.StatusCode);
    }
}
