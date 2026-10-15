using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class VehiculosApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private async Task<string> TokenAsync(string rol, string email)
    {
        await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol);
        return await LoginAsync(email, "Clave-Segura-1");
    }

    private static object Item(string vin, string marca = "Renault", string operacion = "nacional", string? modelo = null, string? color = null) =>
        new { vin, marca, modelo, color, operacion };

    private static async Task<JsonElement> ClonarAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    [Fact]
    public async Task Carga_masiva_crea_los_vehiculos_y_sus_etapas_nacen_despues_con_el_viaje()
    {
        var token = await TokenAsync(clsRol.ADMIN, "admin@ejemplo.com");

        var r = await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = new[] { Item("  9fbhsr4e8pm000001 ", modelo: "Sandero"), Item("9FBHSR4E8PM000002", "Toyota", "importacion") } });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await ClonarAsync(r);
        Assert.Equal(2, cuerpo.GetProperty("recibidos").GetInt32());
        Assert.Equal(2, cuerpo.GetProperty("creados").GetInt32());
        Assert.Equal(0, cuerpo.GetProperty("actualizados").GetInt32());
        Assert.Equal(0, cuerpo.GetProperty("fallidos").GetInt32());
        Assert.Equal(0, cuerpo.GetProperty("errores").GetArrayLength());
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Vin = '9FBHSR4E8PM000001'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'vehiculos.cargar'"));
    }

    [Fact]
    public async Task Recargar_el_mismo_archivo_es_idempotente_y_conserva_modelo_y_color()
    {
        var token = await TokenAsync(clsRol.ADMIN, "admin@ejemplo.com");
        await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = new[] { Item("VIN-UNO", modelo: "Sandero", color: "Rojo"), Item("VIN-DOS") } });

        var r = await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = new[] { Item("vin-uno", "Toyota", "importacion"), Item("VIN-DOS") } });

        var cuerpo = await ClonarAsync(r);
        Assert.Equal(0, cuerpo.GetProperty("creados").GetInt32());
        Assert.Equal(2, cuerpo.GetProperty("actualizados").GetInt32());
        Assert.Equal(2, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
        Assert.Equal("Toyota", await Sembrador.EscalarAsync("SELECT Marca FROM dbo.tblVehiculos WHERE Vin = 'VIN-UNO'"));
        Assert.Equal("importacion", await Sembrador.EscalarAsync("SELECT Operacion FROM dbo.tblVehiculos WHERE Vin = 'VIN-UNO'"));
        Assert.Equal("Sandero", await Sembrador.EscalarAsync("SELECT Modelo FROM dbo.tblVehiculos WHERE Vin = 'VIN-UNO'"));
        Assert.Equal("Rojo", await Sembrador.EscalarAsync("SELECT Color FROM dbo.tblVehiculos WHERE Vin = 'VIN-UNO'"));
    }

    [Fact]
    public async Task Un_vin_repetido_dentro_del_archivo_se_crea_una_vez_y_la_segunda_es_actualizacion()
    {
        var token = await TokenAsync(clsRol.ADMIN, "admin@ejemplo.com");

        var r = await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = new[] { Item("VIN-REP", "Renault"), Item("vin-rep", "Nissan") } });

        var cuerpo = await ClonarAsync(r);
        Assert.Equal(1, cuerpo.GetProperty("creados").GetInt32());
        Assert.Equal(1, cuerpo.GetProperty("actualizados").GetInt32());
        Assert.Equal("Nissan", await Sembrador.EscalarAsync("SELECT Marca FROM dbo.tblVehiculos WHERE Vin = 'VIN-REP'"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
    }

    [Fact]
    public async Task Una_fila_que_falla_se_reporta_y_el_resto_del_lote_se_procesa_incluso_las_actualizaciones()
    {
        var token = await TokenAsync(clsRol.ADMIN, "admin@ejemplo.com");
        await Sembrador.VehiculoConRutaAsync("VIN-EXISTENTE", "Renault");
        var vinDemasiadoLargo = new string('X', 80);

        var r = await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new
        {
            items = new[] { Item("VIN-A"), Item(vinDemasiadoLargo), Item("VIN-EXISTENTE", "Mazda"), Item("VIN-B") },
        });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var cuerpo = await ClonarAsync(r);
        Assert.Equal(4, cuerpo.GetProperty("recibidos").GetInt32());
        Assert.Equal(2, cuerpo.GetProperty("creados").GetInt32());
        Assert.Equal(1, cuerpo.GetProperty("actualizados").GetInt32());
        Assert.Equal(1, cuerpo.GetProperty("fallidos").GetInt32());
        var error = cuerpo.GetProperty("errores")[0];
        Assert.Equal(vinDemasiadoLargo, error.GetProperty("vin").GetString());
        Assert.Equal("No se pudo procesar el VIN", error.GetProperty("error").GetString());
        // La actualizacion posterior al fallo se guardo (el contexto se recupero correctamente).
        Assert.Equal("Mazda", await Sembrador.EscalarAsync("SELECT Marca FROM dbo.tblVehiculos WHERE Vin = 'VIN-EXISTENTE'"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE Vin = 'VIN-B'"));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos WHERE LEN(Vin) > 64"));
    }

    [Fact]
    public async Task Recargar_un_vin_que_ya_tiene_viaje_no_toca_sus_etapas()
    {
        var token = await TokenAsync(clsRol.ADMIN, "admin@ejemplo.com");
        await Sembrador.VehiculoConRutaAsync("VIN-MEDIO", cantidadEtapas: 1);

        await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = new[] { Item("VIN-MEDIO") } });

        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblEtapas"));
        Assert.Equal(1, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblViajeVehiculos"));
    }

    [Fact]
    public async Task Carga_con_datos_invalidos_responde_400_con_errores_bajo_items()
    {
        var token = await TokenAsync(clsRol.ADMIN, "admin@ejemplo.com");

        var vacio = await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = Array.Empty<object>() });
        var sinMarca = await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = new object[] { new { vin = "A", operacion = "nacional" } } });
        var demasiadas = await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = Enumerable.Range(0, 5001).Select(i => Item("V" + i)).ToArray() });

        var respuestas = new[] { vacio, sinMarca, demasiadas };
        var cuerpos = new List<JsonElement>();
        foreach (var respuesta in respuestas)
        {
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            var c = await ClonarAsync(respuesta);
            cuerpos.Add(c);
            Assert.Equal("VALIDATION_ERROR", c.GetProperty("code").GetString());
            Assert.Equal("Datos inválidos", c.GetProperty("message").GetString());
            Assert.True(c.GetProperty("details").GetProperty("fieldErrors").TryGetProperty("items", out _));
        }

        var mensajes = cuerpos[1].GetProperty("details").GetProperty("fieldErrors").GetProperty("items").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Marca requerida", mensajes);
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblVehiculos"));
    }

    [Theory]
    [InlineData(clsRol.INSPECTOR, HttpStatusCode.Forbidden)]
    [InlineData(clsRol.CONDUCTOR, HttpStatusCode.Forbidden)]
    [InlineData(clsRol.ADMIN, HttpStatusCode.Created)]
    [InlineData(clsRol.SUPER_ADMIN, HttpStatusCode.Created)]
    public async Task La_carga_masiva_es_solo_de_administracion_y_el_super_admin_puede(string rol, HttpStatusCode esperado)
    {
        var token = await TokenAsync(rol, "u@ejemplo.com");

        var r = await Cliente(token).PostAsJsonAsync("/api/vehiculos/cargar", new { items = new[] { Item("VIN-ROL") } });

        Assert.Equal(esperado, r.StatusCode);
    }

    [Fact]
    public async Task Buscar_coincide_por_fragmento_de_vin_o_marca_sin_distinguir_mayusculas_y_limita_a_ocho()
    {
        var token = await TokenAsync(clsRol.COORDINADOR_CALIDAD, "i@ejemplo.com");
        for (var i = 0; i < 10; i++)
        {
            await Sembrador.VehiculoConRutaAsync($"ABC{i:00}XYZ", "Renault");
        }

        await Sembrador.VehiculoConRutaAsync("OTRO-VIN", "Zeppelin");

        var porVin = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos?vin=abc"));
        var porMarca = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos?vin=zepp"));
        var vacio = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos?vin="));
        var comodin = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos?vin=%25"));

        Assert.Equal(8, porVin.GetArrayLength());
        Assert.Equal("ABC00XYZ", porVin[0].GetProperty("vin").GetString());
        Assert.Equal(["vin", "marca", "modelo", "color", "operacion"], porVin[0].EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("OTRO-VIN", porMarca[0].GetProperty("vin").GetString());
        Assert.Equal(0, vacio.GetArrayLength());
        Assert.Equal(0, comodin.GetArrayLength()); // el comodin % se trata como texto literal
    }

    [Fact]
    public async Task Listados_distinguen_movilizados_pendientes_y_con_novedad()
    {
        var token = await TokenAsync(clsRol.COORDINADOR_CALIDAD, "i@ejemplo.com");
        var usuarioId = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblUsuarios WHERE Email = 'i@ejemplo.com'"))!;
        var (_, etapasA) = await Sembrador.VehiculoConRutaAsync("VIN-A-MOVILIZADO");
        foreach (var e in etapasA)
        {
            await Sembrador.InformeAsync(e, usuarioId, cerrada: true);
        }

        await Sembrador.VehiculoConRutaAsync("VIN-B-PENDIENTE");
        var (_, etapasC) = await Sembrador.VehiculoConRutaAsync("VIN-C-NOVEDAD");
        await Sembrador.InformeAsync(etapasC[0], usuarioId, cerrada: true, hallazgos: 2, evidencias: 1);
        await Sembrador.VehiculoConRutaAsync("VIN-D-SIN-RUTA", cantidadEtapas: 0);

        var movilizados = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos/listado"));
        var pendientes = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos/listado?tipo=pendientes"));
        var novedad = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos/listado?tipo=novedad"));

        Assert.Equal(["VIN-A-MOVILIZADO"], movilizados.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("vin").GetString()).ToList());
        Assert.Equal(1, movilizados.GetProperty("total").GetInt32());
        Assert.Equal(1, movilizados.GetProperty("page").GetInt32());
        Assert.Equal(500, movilizados.GetProperty("pageSize").GetInt32());
        var a = movilizados.GetProperty("rows")[0];
        Assert.Equal(3, a.GetProperty("etapas").GetInt32());
        Assert.Equal(3, a.GetProperty("completas").GetInt32());
        Assert.Equal(0, a.GetProperty("novedades").GetInt32());

        Assert.Equal(["VIN-B-PENDIENTE", "VIN-C-NOVEDAD", "VIN-D-SIN-RUTA"], pendientes.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("vin").GetString()).ToList());
        var c = novedad.GetProperty("rows")[0];
        Assert.Equal("VIN-C-NOVEDAD", c.GetProperty("vin").GetString());
        Assert.Equal(1, c.GetProperty("completas").GetInt32());
        Assert.Equal(2, c.GetProperty("novedades").GetInt32());
    }

    [Fact]
    public async Task Listado_filtra_por_vin_pagina_y_valida_el_tipo()
    {
        var token = await TokenAsync(clsRol.COORDINADOR_CALIDAD, "i@ejemplo.com");
        await Sembrador.VehiculoConRutaAsync("FILTRO-1");
        await Sembrador.VehiculoConRutaAsync("OTRO-2");

        var filtrado = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos/listado?tipo=pendientes&vin=filtro"));
        var paginaDos = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos/listado?tipo=pendientes&page=2"));
        var paginaRara = await ClonarAsync(await Cliente(token).GetAsync("/api/vehiculos/listado?tipo=pendientes&page=abc"));
        var invalido = await Cliente(token).GetAsync("/api/vehiculos/listado?tipo=otro");

        Assert.Equal(1, filtrado.GetProperty("total").GetInt32());
        Assert.Equal(0, paginaDos.GetProperty("rows").GetArrayLength());
        Assert.Equal(2, paginaDos.GetProperty("total").GetInt32());
        Assert.Equal(2, paginaDos.GetProperty("page").GetInt32());
        Assert.Equal(1, paginaRara.GetProperty("page").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        var detalle = (await ClonarAsync(invalido)).GetProperty("details").GetProperty("fieldErrors");
        Assert.True(detalle.TryGetProperty("tipo", out _));
    }

    [Fact]
    public async Task Inventario_consolida_etapas_hallazgos_evidencias_y_transferencia_sin_exponer_rutas_internas()
    {
        var token = await TokenAsync(clsRol.COORDINADOR_CALIDAD, "i@ejemplo.com");
        var usuarioId = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblUsuarios WHERE Email = 'i@ejemplo.com'"))!;
        var (vehiculoId, etapas) = await Sembrador.VehiculoConRutaAsync("INV-VIN-0001");
        var informeId = await Sembrador.InformeAsync(etapas[0], usuarioId, cerrada: true, hallazgos: 1, evidencias: 2);
        await Fabrica.SembrarUsuarioAsync("receptor@ejemplo.com", "x", clsRol.CONDUCTOR);
        var receptorId = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblUsuarios WHERE Email = 'receptor@ejemplo.com'"))!;
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmadaAt, TipoCierre) VALUES (NEWID(), @i, @e, @r, N'f1.png', N'f2.png', SYSDATETIMEOFFSET(), 'firmada')",
            ("@i", informeId), ("@e", usuarioId), ("@r", receptorId));

        var r = await Cliente(token).GetAsync("/api/vehiculos/inv-vin-0001/inventario");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var inv = await ClonarAsync(r);
        Assert.Equal(vehiculoId, inv.GetProperty("id").GetGuid());
        Assert.Equal("INV-VIN-0001", inv.GetProperty("vin").GetString());
        Assert.Equal(
            ["id", "vin", "marca", "tipo_vehiculo", "modelo", "chasis", "color", "operacion", "created_at", "updated_at", "etapas"],
            inv.EnumerateObject().Select(p => p.Name).ToList());
        var listaEtapas = inv.GetProperty("etapas");
        Assert.Equal(["cargue", "transporte", "descargue"], listaEtapas.EnumerateArray().Select(e => e.GetProperty("proceso").GetString()).ToList());
        Assert.Equal([1, 2, 3], listaEtapas.EnumerateArray().Select(e => e.GetProperty("orden").GetInt32()).ToList());
        var viajeId = await Sembrador.ViajeDeEtapaAsync(etapas[0]);
        Assert.All(listaEtapas.EnumerateArray(), e => Assert.Equal(viajeId, e.GetProperty("viaje_id").GetGuid()));
        Assert.False(listaEtapas[0].GetProperty("con_personal_apoyo").GetBoolean());
        Assert.StartsWith("MTK-S", listaEtapas[0].GetProperty("codigo_mtrack").GetString());
        Assert.True(listaEtapas[0].GetProperty("requiere_inspeccion").GetBoolean());
        Assert.Equal(0, listaEtapas[1].GetProperty("inspecciones").GetArrayLength());

        var insp = listaEtapas[0].GetProperty("inspecciones")[0];
        Assert.Equal("cerrada", insp.GetProperty("estado").GetString());
        var hallazgo = insp.GetProperty("hallazgos")[0];
        Assert.Equal(0.5, hallazgo.GetProperty("coords_diagrama").GetProperty("x").GetDouble());
        var evidencias = insp.GetProperty("evidencias");
        Assert.Equal(2, evidencias.GetArrayLength());
        Assert.Equal("/api/uploads/foto-0.jpg", evidencias[0].GetProperty("url").GetString());
        Assert.False(evidencias[0].TryGetProperty("storage_path", out _));
        var transferencia = insp.GetProperty("transferencia");
        Assert.True(transferencia.GetProperty("firmada").GetBoolean());
        Assert.Equal("firmada", transferencia.GetProperty("tipo_cierre").GetString());
        Assert.False(transferencia.TryGetProperty("firma_entrega", out _));
    }

    [Fact]
    public async Task Inventario_de_un_vin_inexistente_responde_404()
    {
        var token = await TokenAsync(clsRol.COORDINADOR_CALIDAD, "i@ejemplo.com");

        var r = await Cliente(token).GetAsync("/api/vehiculos/NO-EXISTE/inventario");

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("Vehículo con VIN NO-EXISTE no encontrado", (await ClonarAsync(r)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Los_endpoints_de_vehiculos_exigen_autenticacion()
    {
        foreach (var ruta in new[] { "/api/vehiculos?vin=a", "/api/vehiculos/listado", "/api/vehiculos/X/inventario" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Cliente().GetAsync(ruta)).StatusCode);
        }
    }
}
