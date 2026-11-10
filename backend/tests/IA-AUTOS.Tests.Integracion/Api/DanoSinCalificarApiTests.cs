using System.Net;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// El dano sin calificar (sin nivel) se muestra y cuenta (S7-HU-03): el listado de hallazgos lo marca (<c>sin_calificar</c>) y trae el conductor y el motivo de «sin inspector», el mapa de calor, los patrones y las
/// averias lo cuentan por su zona y su clase, el indicador diario lo entrega como una cifra aparte sin tocar «con novedad», la tasa de rechazo no cambia, el listado de informes trae el lugar, el codigo MTrack y la
/// unidad, y finalizar un viaje con danos sin calificar funciona igual.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed class DanoSinCalificarApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    private const string INSPECCION_V1_SALIDA = "11111111-0000-0000-0000-000000000001";
    private const string INSPECCION_V3_LLEGADA = "33333333-0000-0000-0000-000000000002";

    private static string Cuerpo(object valor) => JsonSerializer.Serialize(valor);

    /// <summary>
    /// Agrega al escenario: dos danos con zona (izquierdo 7, golpe) y sin nivel, y una novedad del conductor aun sin revisar (sin zona, clase ni nivel), todos en la llegada del Cargue de V3 (que ya tiene un hallazgo
    /// con nivel); y el motivo «El inspector no llegó» a la salida del Cargue de V1 (viaje con conductor). Devuelve los ids de los tres danos sin calificar.
    /// </summary>
    private async Task<(Guid ConZonaA, Guid ConZonaB, Guid Novedad)> SembrarSinCalificarAsync()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var novedad = Guid.NewGuid();
        await Sembrador.EjecutarAsync(
            $"""
            ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL;
            DECLARE @zona7 uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE Numero = 7 AND Lado = 'izquierdo');
            INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES
                (@a, '{INSPECCION_V3_LLEGADA}', @zona7, 'golpe', '5_10cm', NULL),
                (@b, '{INSPECCION_V3_LLEGADA}', @zona7, 'golpe', '1_5cm', NULL);
            INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad) VALUES (@n, '{INSPECCION_V3_LLEGADA}', '5_10cm', 'conductor', 'golpe');
            UPDATE dbo.tblInspecciones SET MotivoSinInspector = 'inspector_no_llego' WHERE Id = '{INSPECCION_V1_SALIDA}';
            ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
            """,
            ("@a", a), ("@b", b), ("@n", novedad));
        return (a, b, novedad);
    }

    private static int Total(JsonElement filas, string campo, string clave, string? valor) =>
        filas.GetProperty(campo).EnumerateArray().Where(f => f.GetProperty(clave).ValueKind == (valor is null ? JsonValueKind.Null : JsonValueKind.String) && f.GetProperty(clave).GetString() == valor)
            .Sum(f => f.GetProperty("total").GetInt32());

    // ------------------------------------------------------------------------------- CA-2: mapa de calor y patrones

    [Fact]
    public async Task Los_patrones_y_las_averias_cuentan_el_dano_sin_nivel_por_su_zona_y_su_clase_y_lo_separan_en_sin_calificar()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var patronesAntes = await ConsultarAsync(coordinador, "/api/reportes/patrones");
        var averiasAntes = await ConsultarAsync(coordinador, "/api/reportes/averias");
        Assert.Equal(0, patronesAntes.GetProperty("sin_calificar").GetInt32());
        Assert.Equal(0, averiasAntes.GetProperty("sin_calificar").GetInt32());
        var izquierdo7Antes = patronesAntes.GetProperty("por_region").EnumerateArray().Single(r => r.GetProperty("cuadrante_numero").ValueKind == JsonValueKind.Number && r.GetProperty("cuadrante_numero").GetInt32() == 7).GetProperty("total").GetInt32();
        var golpesAntes = Total(patronesAntes, "por_clase", "clase", "golpe");

        await SembrarSinCalificarAsync();
        var patrones = await ConsultarAsync(coordinador, "/api/reportes/patrones");
        var averias = await ConsultarAsync(coordinador, "/api/reportes/averias");

        // Los tres danos suman al total y se cuentan aparte; ninguno se descarta en silencio.
        Assert.Equal(patronesAntes.GetProperty("total").GetInt32() + 3, patrones.GetProperty("total").GetInt32());
        Assert.Equal(3, patrones.GetProperty("sin_calificar").GetInt32());
        Assert.Equal(3, Total(patrones, "por_nivel", "nivel", null));
        Assert.Equal(3, averias.GetProperty("sin_calificar").GetInt32());
        Assert.Equal(3, Total(averias, "por_nivel", "nivel", null));

        // Por su zona y su clase: dos danos mas en izquierdo 7 y dos golpes mas; la novedad sin revisar no tiene zona ni clase.
        var izquierdo7 = patrones.GetProperty("por_region").EnumerateArray().Single(r => r.GetProperty("cuadrante_numero").ValueKind == JsonValueKind.Number && r.GetProperty("cuadrante_numero").GetInt32() == 7);
        Assert.Equal(izquierdo7Antes + 2, izquierdo7.GetProperty("total").GetInt32());
        Assert.Equal(golpesAntes + 2, Total(patrones, "por_clase", "clase", "golpe"));
        Assert.Equal(1, Total(patrones, "por_clase", "clase", null));
        Assert.Equal(golpesAntes + 2, Total(averias, "por_clase", "clase", "golpe"));
        Assert.Equal(1, patrones.GetProperty("por_region").EnumerateArray().Where(r => r.GetProperty("cuadrante_numero").ValueKind == JsonValueKind.Null).Sum(r => r.GetProperty("total").GetInt32()));
    }

    // ------------------------------------------------------------------- CA-3 y CA-3b: indicador diario y tasa de rechazo

    [Fact]
    public async Task El_indicador_diario_entrega_los_danos_sin_calificar_como_una_cifra_aparte_sin_sumarlos_a_con_novedad_y_la_tasa_de_rechazo_no_cambia()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var consulta = $"?fecha={e.Dias.Dia}";
        var indicadorAntes = await ConsultarAsync(coordinador, "/api/reportes/indicador-diario", consulta);
        var rechazoAntes = await (await Cliente(coordinador.Token).GetAsync("/api/metricas/rechazo")).Content.ReadAsStringAsync();
        Assert.Equal(0, indicadorAntes.GetProperty("sin_calificar").GetInt32());

        await SembrarSinCalificarAsync();
        var indicador = await ConsultarAsync(coordinador, "/api/reportes/indicador-diario", consulta);
        var rechazo = await (await Cliente(coordinador.Token).GetAsync("/api/metricas/rechazo")).Content.ReadAsStringAsync();

        // Las unidades con novedad (V1 o V1+) y el porcentaje no cambian: lo sin calificar no se suma a ellas ni se descarta.
        Assert.Equal(3, indicador.GetProperty("sin_calificar").GetInt32());
        Assert.Equal(indicadorAntes.GetProperty("despachados").GetInt32(), indicador.GetProperty("despachados").GetInt32());
        Assert.Equal(indicadorAntes.GetProperty("con_novedad").GetInt32(), indicador.GetProperty("con_novedad").GetInt32());
        Assert.Equal(indicadorAntes.GetProperty("porcentaje").GetDecimal(), indicador.GetProperty("porcentaje").GetDecimal());
        Assert.Equal(3, indicador.GetProperty("por_operacion").EnumerateArray().Sum(o => o.GetProperty("sin_calificar").GetInt32()));
        Assert.Equal(3, indicador.GetProperty("por_cliente").EnumerateArray().Sum(o => o.GetProperty("sin_calificar").GetInt32()));
        // CA-3b: la tasa de rechazo sigue contando las unidades con al menos un hallazgo (V3 ya tenia uno): la misma respuesta.
        Assert.Equal(rechazoAntes, rechazo);
    }

    // ---------------------------------------------------- CA-1, CA-5 y CA-7: listado de hallazgos y finalizar el viaje

    [Fact]
    public async Task El_listado_de_hallazgos_marca_el_dano_sin_calificar_y_trae_el_conductor_y_el_motivo_de_sin_inspector_tambien_con_el_viaje_finalizado()
    {
        var e = await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var (conZona, _, novedad) = await SembrarSinCalificarAsync();
        var viajeB = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = 'MTK-IND-B'"))!;
        var viajeA = (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = 'MTK-IND-A'"))!;

        // CA-7: finalizar el viaje con danos sin calificar funciona igual que antes.
        var finalizar = await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{viajeB}/estado", new StringContent(Cuerpo(new { estado = "finalizado" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, finalizar.StatusCode);
        Assert.Equal("finalizado", await Sembrador.EscalarAsync("SELECT Estado FROM dbo.tblViajes WHERE Id = @v", ("@v", viajeB)));

        // CA-1: con el viaje finalizado los tres siguen en el listado, marcados como sin calificar.
        var lista = await ConsultarAsync(coordinador, "/api/hallazgos", $"?viaje_id={viajeB}");
        var filas = lista.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(4, filas.Count);
        Assert.Equal(3, filas.Count(f => f.GetProperty("sin_calificar").GetBoolean()));
        var conNivel = filas.Single(f => !f.GetProperty("sin_calificar").GetBoolean());
        Assert.Equal("v3", conNivel.GetProperty("nivel_severidad").GetString());
        Assert.All(filas.Where(f => f.GetProperty("sin_calificar").GetBoolean()), f => Assert.Equal(JsonValueKind.Null, f.GetProperty("nivel_severidad").ValueKind));
        var deLaZona = filas.Single(f => f.GetProperty("id").GetGuid() == conZona);
        Assert.Equal(7, deLaZona.GetProperty("cuadrante_numero").GetInt32());
        Assert.Equal("golpe", deLaZona.GetProperty("clase_dano").GetString());

        // CA-5 (viaje B, sin conductor ni motivo): vacios cuando no aplican.
        Assert.All(filas, f =>
        {
            Assert.Equal(JsonValueKind.Null, f.GetProperty("conductor_nombre").ValueKind);
            Assert.Equal(JsonValueKind.Null, f.GetProperty("motivo_sin_inspector").ValueKind);
        });

        // CA-5 (viaje A: conductor y la salida del Cargue de V1 con motivo): el nombre del conductor y el motivo en la fila, sin abrir el informe.
        await Sembrador.EjecutarAsync(
            $"""
            ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
            INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad) VALUES (NEWID(), '{INSPECCION_V1_SALIDA}', '5_10cm', 'conductor', 'golpe');
            ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
            """);
        var deA = (await ConsultarAsync(coordinador, "/api/hallazgos", $"?viaje_id={viajeA}&origen=conductor")).GetProperty("items").EnumerateArray().Single();
        Assert.True(deA.GetProperty("sin_calificar").GetBoolean());
        Assert.Equal("Carlos Conductor", deA.GetProperty("conductor_nombre").GetString());
        var motivo = deA.GetProperty("motivo_sin_inspector");
        Assert.Equal("inspector_no_llego", motivo.GetProperty("codigo").GetString());
        Assert.Equal("El inspector no llegó", motivo.GetProperty("nombre").GetString());
        Assert.Equal(JsonValueKind.Null, motivo.GetProperty("texto").ValueKind);

        // El detalle trae lo mismo, y el hallazgo del inspector del mismo viaje (con nivel) no esta marcado.
        var detalle = await ConsultarAsync(coordinador, $"/api/hallazgos/{deA.GetProperty("id").GetGuid()}");
        Assert.Equal("Carlos Conductor", detalle.GetProperty("conductor_nombre").GetString());
        Assert.Equal("inspector_no_llego", detalle.GetProperty("motivo_sin_inspector").GetProperty("codigo").GetString());
        var delInspector = (await ConsultarAsync(coordinador, "/api/hallazgos", $"?viaje_id={viajeA}&origen=inspector")).GetProperty("items").EnumerateArray().First();
        Assert.False(delInspector.GetProperty("sin_calificar").GetBoolean());
        Assert.Equal("Carlos Conductor", delInspector.GetProperty("conductor_nombre").GetString());
        Assert.NotEqual(Guid.Empty, novedad);
        Assert.NotEqual(Guid.Empty, e.Sofasa);
    }

    // ------------------------------------------------------------------------------------------- CA-4: listado de informes

    [Fact]
    public async Task Las_filas_del_listado_de_informes_traen_el_lugar_el_codigo_mtrack_y_la_unidad()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        await Sembrador.EjecutarAsync(
            $"UPDATE e SET e.PuntoCustodiaId = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1') FROM dbo.tblEtapas e JOIN dbo.tblInspecciones i ON i.EtapaId = e.Id WHERE i.Id = '{INSPECCION_V1_SALIDA}'");
        var lugar = (string)(await Sembrador.EscalarAsync("SELECT Nombre FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1'"))!;
        var unidad = (string)(await Sembrador.EscalarAsync("SELECT Nombre FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;

        var filas = (await ConsultarAsync(coordinador, "/api/inspecciones", "?tamano=200")).GetProperty("items").EnumerateArray().ToList();

        var fila = filas.Single(f => f.GetProperty("id").GetGuid() == Guid.Parse(INSPECCION_V1_SALIDA));
        Assert.Equal(lugar, fila.GetProperty("lugar").GetString());
        Assert.Equal("MTK-IND-A", fila.GetProperty("codigo_mtrack").GetString());
        Assert.Equal(unidad, fila.GetProperty("unidad").GetString());
        // Vacios cuando no hay: una etapa sin punto de custodia no tiene lugar.
        var sinLugar = filas.First(f => f.GetProperty("id").GetGuid() != Guid.Parse(INSPECCION_V1_SALIDA) && f.GetProperty("lugar").ValueKind == JsonValueKind.Null);
        Assert.False(string.IsNullOrEmpty(sinLugar.GetProperty("codigo_mtrack").GetString()));
    }
}
