using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Los danos de extras en su propia fila del mapa de calor y de los patrones (S7-HU-04.5): <c>por_region</c> y <c>por_region_proceso</c> los separan del grupo «sin zona», que lleva un rotulo fijo; los extras siguen
/// contando en el total y las zonas con cuadrante numerado no cambian.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_2)]
public sealed class ExtrasEnLosPatronesApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    private const string INSPECCION_V3_LLEGADA = "33333333-0000-0000-0000-000000000002";

    private static JsonElement Fila(JsonElement patrones, string lista, Func<JsonElement, bool> filtro) => patrones.GetProperty(lista).EnumerateArray().Single(filtro);

    private static bool EsExtra(JsonElement f) => f.GetProperty("extra").GetBoolean();

    private static bool EsSinZona(JsonElement f) => !EsExtra(f) && f.GetProperty("cuadrante_numero").ValueKind == JsonValueKind.Null;

    private static bool EsCuadrante(JsonElement f, int numero) => f.GetProperty("cuadrante_numero").ValueKind == JsonValueKind.Number && f.GetProperty("cuadrante_numero").GetInt32() == numero;

    /// <summary>Dos extras danados de tipos distintos (plataforma y barras del techo) y una novedad del conductor sin zona, en la llegada del Cargue de V3.</summary>
    private async Task SembrarExtrasYSinZonaAsync()
    {
        await Sembrador.EjecutarAsync(
            $"""
            ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspeccionExtras DISABLE TRIGGER ALL;
            INSERT dbo.tblInspeccionExtras (Id, InspeccionId, Tipo) VALUES (@e1, '{INSPECCION_V3_LLEGADA}', 'platon'), (@e2, '{INSPECCION_V3_LLEGADA}', 'barras_techo');
            INSERT dbo.tblHallazgos (Id, InspeccionId, ClaseDano, TamanoDano, NivelSeveridad, ExtraId) VALUES
                (NEWID(), '{INSPECCION_V3_LLEGADA}', 'golpe', '5_10cm', 'v3', @e1),
                (NEWID(), '{INSPECCION_V3_LLEGADA}', 'rotura', '1_5cm', 'v1', @e2);
            INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad) VALUES (NEWID(), '{INSPECCION_V3_LLEGADA}', '5_10cm', 'conductor', 'golpe');
            ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspeccionExtras ENABLE TRIGGER ALL;
            """,
            ("@e1", Guid.NewGuid()), ("@e2", Guid.NewGuid()));
    }

    [Fact]
    public async Task Los_danos_de_extras_salen_en_su_propia_fila_separados_del_grupo_sin_zona_que_lleva_un_rotulo_fijo()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var antes = await ConsultarAsync(coordinador, "/api/reportes/patrones");
        var cuadrantes = antes.GetProperty("por_region").EnumerateArray().Where(f => f.GetProperty("cuadrante_numero").ValueKind == JsonValueKind.Number).ToList();
        Assert.NotEmpty(cuadrantes);
        Assert.DoesNotContain(antes.GetProperty("por_region").EnumerateArray(), EsExtra);
        var sinZonaAntes = antes.GetProperty("por_region").EnumerateArray().Where(EsSinZona).Sum(f => f.GetProperty("total").GetInt32());

        await SembrarExtrasYSinZonaAsync();
        var patrones = await ConsultarAsync(coordinador, "/api/reportes/patrones");

        // CA-1: los dos extras, aunque son de tipos distintos, forman un solo grupo propio, con su indicador y su rotulo, sin cuadrante ni lado.
        var extras = Fila(patrones, "por_region", EsExtra);
        Assert.Equal(2, extras.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, extras.GetProperty("cuadrante_numero").ValueKind);
        Assert.Equal(JsonValueKind.Null, extras.GetProperty("lado").ValueKind);
        Assert.Equal("Extras del vehículo", extras.GetProperty("nombre").GetString());
        Assert.Equal("Extras del vehículo", extras.GetProperty("zona").GetString());
        // CA-3: el grupo sin zona es otro, con una etiqueta fija (y no el nombre de una muestra), y solo suma lo que no es extra.
        var sinZona = Fila(patrones, "por_region", EsSinZona);
        Assert.Equal(sinZonaAntes + 1, sinZona.GetProperty("total").GetInt32());
        Assert.Equal("Sin zona en el dibujo", sinZona.GetProperty("nombre").GetString());
        Assert.Equal("Sin zona en el dibujo", sinZona.GetProperty("zona").GetString());
        Assert.False(sinZona.GetProperty("extra").GetBoolean());
        // CA-5: las zonas con cuadrante numerado no cambian, ni su indicador de extra.
        foreach (var antesCuadrante in cuadrantes)
        {
            var numero = antesCuadrante.GetProperty("cuadrante_numero").GetInt32();
            var ahora = Fila(patrones, "por_region", f => EsCuadrante(f, numero) && f.GetProperty("lado").GetString() == antesCuadrante.GetProperty("lado").GetString());
            Assert.Equal(antesCuadrante.GetProperty("total").GetInt32(), ahora.GetProperty("total").GetInt32());
            Assert.Equal(antesCuadrante.GetProperty("zona").GetString(), ahora.GetProperty("zona").GetString());
            Assert.False(ahora.GetProperty("extra").GetBoolean());
        }

        // CA-4: los extras siguen contando en el total (los dos extras y el hallazgo sin zona) y en los demas conteos.
        Assert.Equal(antes.GetProperty("total").GetInt32() + 3, patrones.GetProperty("total").GetInt32());
        Assert.Equal(patrones.GetProperty("total").GetInt32(), patrones.GetProperty("por_region").EnumerateArray().Sum(f => f.GetProperty("total").GetInt32()));
        Assert.Equal(patrones.GetProperty("total").GetInt32(), patrones.GetProperty("por_region_proceso").EnumerateArray().Sum(f => f.GetProperty("total").GetInt32()));
    }

    [Fact]
    public async Task Los_danos_de_extras_tambien_salen_en_su_propia_fila_por_proceso()
    {
        await EscenarioAsync();
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var antes = await ConsultarAsync(coordinador, "/api/reportes/patrones");
        await SembrarExtrasYSinZonaAsync();

        var patrones = await ConsultarAsync(coordinador, "/api/reportes/patrones");

        // CA-2: por proceso, cada grupo propio con su rotulo; los extras estan en un solo proceso (el de la inspeccion).
        var extras = Fila(patrones, "por_region_proceso", EsExtra);
        Assert.Equal(2, extras.GetProperty("total").GetInt32());
        Assert.Equal("Extras del vehículo", extras.GetProperty("zona").GetString());
        Assert.Equal(JsonValueKind.Null, extras.GetProperty("cuadrante_numero").ValueKind);
        Assert.False(string.IsNullOrEmpty(extras.GetProperty("proceso").GetString()));
        var proceso = extras.GetProperty("proceso").GetString();
        var sinZona = Fila(patrones, "por_region_proceso", f => EsSinZona(f) && f.GetProperty("proceso").GetString() == proceso);
        Assert.Equal("Sin zona en el dibujo", sinZona.GetProperty("zona").GetString());
        Assert.Equal("Sin zona en el dibujo", sinZona.GetProperty("nombre").GetString());
        var sinZonaAntes = antes.GetProperty("por_region_proceso").EnumerateArray().Where(f => EsSinZona(f) && f.GetProperty("proceso").GetString() == proceso).Sum(f => f.GetProperty("total").GetInt32());
        Assert.Equal(sinZonaAntes + 1, sinZona.GetProperty("total").GetInt32());
    }
}
