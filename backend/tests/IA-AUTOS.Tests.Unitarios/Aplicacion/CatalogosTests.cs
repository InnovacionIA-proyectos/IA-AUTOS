using System.Text.Json;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Tests.Unitarios.Falsos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IAAutos.Tests.Unitarios.Aplicacion;

public sealed class CatalogosDominioTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static string CampoDelError(clsValidacionException ex) =>
        ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys.Single();

    [Theory]
    [InlineData("cargue")]
    [InlineData("raya_pintar")]
    [InlineData("1_5cm")]
    [InlineData("diagonal_delantera_derecha")]
    public void Acepta_codigos_en_minusculas_con_numeros_y_guion_bajo(string codigo)
    {
        Assert.Equal(codigo, clsClaseDano.Crear(codigo, "Valor", null, null, AHORA).Codigo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("Mayuscula")]
    [InlineData("con espacio")]
    [InlineData("con-guion")]
    [InlineData("acentó")]
    [InlineData("a_codigo_demasiado_largo_para_el_catalogo")]
    public void Rechaza_codigos_invalidos_indicando_el_campo(string? codigo)
    {
        var ex = Assert.Throws<clsValidacionException>(() => clsClaseDano.Crear(codigo, "Valor", null, null, AHORA));

        Assert.Equal("codigo", CampoDelError(ex));
    }

    [Fact]
    public void Un_valor_nuevo_queda_activo_con_orden_cero_y_el_nombre_recortado()
    {
        var valor = clsEtapaCatalogo.Crear("cargue", "  Cargue  ", null, null, AHORA);

        Assert.True(valor.Activo);
        Assert.Equal(0, valor.Orden);
        Assert.Equal("Cargue", valor.Nombre);
        Assert.NotEqual(Guid.Empty, valor.Id);
    }

    [Theory]
    [InlineData("", "nombre")]
    [InlineData("   ", "nombre")]
    public void Exige_nombre(string nombre, string campo)
    {
        var ex = Assert.Throws<clsValidacionException>(() => clsEtapaCatalogo.Crear("cargue", nombre, null, null, AHORA));

        Assert.Equal(campo, CampoDelError(ex));
    }

    [Fact]
    public void Limita_el_nombre_a_100_caracteres_y_el_orden_a_no_negativo()
    {
        Assert.Equal("nombre", CampoDelError(Assert.Throws<clsValidacionException>(() => clsEtapaCatalogo.Crear("a", new string('n', 101), null, null, AHORA))));
        Assert.Equal("orden", CampoDelError(Assert.Throws<clsValidacionException>(() => clsEtapaCatalogo.Crear("a", "A", -1, null, AHORA))));
    }

    [Fact]
    public void Actualizar_cambia_solo_lo_recibido_y_toca_la_fecha()
    {
        var valor = clsClaseDano.Crear("golpe", "Golpe", 5, true, AHORA);
        var despues = AHORA.AddHours(1);

        valor.Actualizar(null, null, false, despues);

        Assert.Equal("Golpe", valor.Nombre);
        Assert.Equal(5, valor.Orden);
        Assert.False(valor.Activo);
        Assert.Equal(despues, valor.UpdatedAt);
        Assert.Equal(AHORA, valor.CreatedAt);
    }

    [Fact]
    public void La_accion_del_nivel_se_recorta_se_limita_y_un_texto_vacio_la_borra()
    {
        var nivel = clsNivelSeveridad.Crear("v1", "V1", "  Reparar antes de entregar  ", null, null, AHORA);
        Assert.Equal("Reparar antes de entregar", nivel.Accion);

        nivel.CambiarAccion("   ", AHORA);
        Assert.Null(nivel.Accion);

        Assert.Equal("accion", CampoDelError(Assert.Throws<clsValidacionException>(() => nivel.CambiarAccion(new string('a', 501), AHORA))));
    }

    [Fact]
    public void Una_vista_es_obligatoria_por_defecto_y_el_tipo_de_vehiculo_es_opcional()
    {
        var vista = clsVistaFoto.Crear("frontal", "Frontal", null, null, null, null, AHORA);
        var adicional = clsVistaFoto.Crear("cojineria", "Cojinería", false, "  sedan ", null, null, AHORA);

        Assert.True(vista.Obligatoria);
        Assert.Null(vista.TipoVehiculo);
        Assert.False(adicional.Obligatoria);
        Assert.Equal("sedan", adicional.TipoVehiculo);

        adicional.CambiarVista(null, null, cambiaTipoVehiculo: true, AHORA);
        Assert.Null(adicional.TipoVehiculo);
        adicional.CambiarVista(true, "x", cambiaTipoVehiculo: false, AHORA);
        Assert.True(adicional.Obligatoria);
        Assert.Null(adicional.TipoVehiculo);
    }

    [Fact]
    public void Un_cuadrante_exige_numero_positivo_y_guarda_su_poligono_compacto()
    {
        var cuadrante = clsCuadrante.Crear(Guid.NewGuid(), "sedan", 3, "Cuadrante 3", """ { "puntos": [ [0, 0], [0.5, 0], [0.5, 0.5] ] } """, 3, null, AHORA);

        Assert.Equal(3, cuadrante.Numero);
        Assert.Equal("""{"puntos":[[0,0],[0.5,0],[0.5,0.5]]}""", cuadrante.Geometria);
        Assert.Equal("numero", CampoDelError(Assert.Throws<clsValidacionException>(() => clsCuadrante.Crear(Guid.NewGuid(), null, 0, "X", null, null, null, AHORA))));
        Assert.Equal("numero", CampoDelError(Assert.Throws<clsValidacionException>(() => clsCuadrante.Crear(Guid.NewGuid(), null, null, "X", null, null, null, AHORA))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_geometria_es_valido(string? geometria)
    {
        Assert.Null(clsCuadrante.GeometriaValida(geometria));
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("[]")]
    [InlineData("""{"puntos":[[0,0],[1,0]]}""")]
    [InlineData("""{"puntos":[[0,0],[1,0],[2,1]]}""")]
    [InlineData("""{"puntos":[[0,0],[1,0],[-1,1]]}""")]
    [InlineData("""{"puntos":[[0,0],[1,0],["a",1]]}""")]
    [InlineData("""{"puntos":[[0,0],[1,0],[1]]}""")]
    [InlineData("""{"puntos":"nada"}""")]
    [InlineData("""{"otro":[[0,0],[1,0],[1,1]]}""")]
    public void Rechaza_geometrias_que_no_son_un_poligono_normalizado(string geometria)
    {
        Assert.Equal("geometria", CampoDelError(Assert.Throws<clsValidacionException>(() => clsCuadrante.GeometriaValida(geometria))));
    }
}

public sealed class CamposCatalogoTests
{
    private static clsCamposCatalogo Campos(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!);

    [Fact]
    public void Distingue_un_campo_ausente_de_uno_enviado_en_nulo()
    {
        var campos = Campos("""{"tipo_vehiculo":null}""");

        Assert.True(campos.Tiene("tipo_vehiculo"));
        Assert.Null(campos.Texto("tipo_vehiculo"));
        Assert.False(campos.Tiene("nombre"));
        Assert.Null(campos.Texto("nombre"));
    }

    [Fact]
    public void Lee_cada_tipo_y_rechaza_un_tipo_equivocado()
    {
        var campos = Campos("""{"nombre":"Cargue","orden":4,"activo":true,"vista":"3fa85f64-5717-4562-b3fc-2c963f66afa6","geometria":{"puntos":[]}}""");

        Assert.Equal("Cargue", campos.Texto("nombre"));
        Assert.Equal(4, campos.Entero("orden"));
        Assert.True(campos.Booleano("activo"));
        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), campos.Identificador("vista"));
        Assert.Equal("""{"puntos":[]}""", campos.Json("geometria"));

        Assert.Throws<clsValidacionException>(() => Campos("""{"nombre":5}""").Texto("nombre"));
        Assert.Throws<clsValidacionException>(() => Campos("""{"orden":"4"}""").Entero("orden"));
        Assert.Throws<clsValidacionException>(() => Campos("""{"orden":4.5}""").Entero("orden"));
        Assert.Throws<clsValidacionException>(() => Campos("""{"activo":"si"}""").Booleano("activo"));
        Assert.Throws<clsValidacionException>(() => Campos("""{"vista":"no-es-uuid"}""").Identificador("vista"));
        Assert.Throws<clsValidacionException>(() => Campos("""{"geometria":"texto"}""").Json("geometria"));
    }

    [Fact]
    public void Rechaza_los_campos_que_el_catalogo_no_admite()
    {
        var campos = Campos("""{"nombre":"X","sobrante":1}""");

        var ex = Assert.Throws<clsValidacionException>(() => campos.SoloPermitidos(clsCamposCatalogo.COMUNES));

        Assert.Contains("sobrante", ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys);
    }
}

public sealed class CatalogosServiceTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed class CatalogoFalso(string nombre) : ICatalogo
    {
        public int Listados { get; private set; }

        public List<ValorCatalogo> Valores { get; } = [];

        public string Nombre { get; } = nombre;

        public Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
        {
            Listados++;
            return Task.FromResult<IReadOnlyList<ValorCatalogo>>(Valores.ToList());
        }

        public Task<ValorCatalogo> CrearAsync(IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
        {
            var valor = Valor(campos["codigo"].GetString()!, activo: true);
            Valores.Add(valor);
            return Task.FromResult(valor);
        }

        public Task<ValorCatalogo> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
        {
            var valor = Valores.Single(v => v.Id == id);
            valor["activo"] = campos["activo"].GetBoolean();
            return Task.FromResult(valor);
        }

        public Task EliminarAsync(Guid id, CancellationToken ct = default)
        {
            Valores.RemoveAll(v => v.Id == id);
            return Task.CompletedTask;
        }

        public ValorCatalogo Agregar(string codigo, bool activo = true)
        {
            var valor = Valor(codigo, activo);
            Valores.Add(valor);
            return valor;
        }

        private static ValorCatalogo Valor(string codigo, bool activo) => new()
        {
            ["id"] = Guid.NewGuid(),
            ["codigo"] = codigo,
            ["nombre"] = codigo,
            ["orden"] = 0,
            ["activo"] = activo,
        };
    }

    private readonly clsUnidadDeTrabajoFalsa _uow = new();
    private readonly CatalogoFalso _etapas = new(clsNombresCatalogo.ETAPAS);
    private readonly CatalogoFalso _clases = new(clsNombresCatalogo.CLASES_DANO);
    private readonly clsAuditoriaRepositoryFalso _auditoria;

    public CatalogosServiceTests()
    {
        _auditoria = new clsAuditoriaRepositoryFalso(_uow);
        _etapas.Agregar("cargue");
        _etapas.Agregar("transporte");
        _etapas.Agregar("descargue", activo: false);
        _clases.Agregar("golpe");
    }

    private clsCatalogosService Servicio(int cacheSegundos = 60) => new(
        [_etapas, _clases],
        new MemoryCache(new MemoryCacheOptions()),
        _uow,
        new clsAuditor(_auditoria, new clsUsuarioActualFalso(), new clsRelojFijo(AHORA)),
        Options.Create(new clsOpcionesCatalogos { CacheSegundos = cacheSegundos }));

    private static Dictionary<string, JsonElement> Cuerpo(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public async Task Las_lecturas_se_sirven_del_cache_y_la_version_es_estable()
    {
        var servicio = Servicio();

        var primera = await servicio.ObtenerTodosAsync();
        var segunda = await servicio.ObtenerTodosAsync();

        Assert.Equal(primera.Version, segunda.Version);
        Assert.Equal(1, _etapas.Listados);
        Assert.Equal(16, primera.Version.Length);
        Assert.Equal(["clases-dano", "etapas"], primera.Catalogos.Keys.ToList());
    }

    [Fact]
    public async Task Crear_renueva_el_cache_cambia_la_version_y_deja_auditoria()
    {
        var servicio = Servicio();
        var antes = await servicio.ObtenerTodosAsync();
        var versionEtapas = antes.Catalogos[clsNombresCatalogo.ETAPAS].Version;
        var versionClases = antes.Catalogos[clsNombresCatalogo.CLASES_DANO].Version;

        var creado = await servicio.CrearAsync(clsNombresCatalogo.ETAPAS, Cuerpo("""{"codigo":"entrega_final"}"""));

        var despues = await servicio.ObtenerTodosAsync();
        Assert.NotEqual(antes.Version, despues.Version);
        Assert.NotEqual(versionEtapas, despues.Catalogos[clsNombresCatalogo.ETAPAS].Version);
        Assert.Equal(versionClases, despues.Catalogos[clsNombresCatalogo.CLASES_DANO].Version);
        Assert.Contains(despues.Catalogos[clsNombresCatalogo.ETAPAS].Items, i => i.Id == creado.Id);
        Assert.Equal(1, _uow.Confirmaciones);
        Assert.Contains(_auditoria.Registros, r => r.Accion == "catalogo.etapas.crear");
    }

    [Fact]
    public async Task Actualizar_y_eliminar_tambien_renuevan_el_cache()
    {
        var servicio = Servicio();
        var golpe = _clases.Valores.Single();
        await servicio.ObtenerTodosAsync();

        await servicio.ActualizarAsync(clsNombresCatalogo.CLASES_DANO, golpe.Id, Cuerpo("""{"activo":false}"""));
        Assert.False((await servicio.ObtenerAsync(clsNombresCatalogo.CLASES_DANO)).Items.Single().Activo);

        await servicio.EliminarAsync(clsNombresCatalogo.CLASES_DANO, golpe.Id);
        Assert.Empty((await servicio.ObtenerAsync(clsNombresCatalogo.CLASES_DANO)).Items);
        Assert.Contains(_auditoria.Registros, r => r.Accion == "catalogo.clases-dano.actualizar");
        Assert.Contains(_auditoria.Registros, r => r.Accion == "catalogo.clases-dano.eliminar");
    }

    [Fact]
    public async Task Un_catalogo_desconocido_es_404_y_no_confirma_nada()
    {
        var servicio = Servicio();

        await Assert.ThrowsAsync<clsEntidadNoEncontradaException>(() => servicio.ObtenerAsync("inventado"));
        await Assert.ThrowsAsync<clsEntidadNoEncontradaException>(() => servicio.CrearAsync("inventado", Cuerpo("""{"codigo":"x"}""")));
        Assert.Equal(0, _uow.Confirmaciones);
    }

    [Fact]
    public async Task La_lectura_para_validar_distingue_activos_de_todos()
    {
        var servicio = Servicio();

        Assert.Equal(["cargue", "transporte"], (await servicio.CodigosAsync(clsNombresCatalogo.ETAPAS, soloActivos: true)).Order());
        Assert.Equal(["cargue", "descargue", "transporte"], (await servicio.CodigosAsync(clsNombresCatalogo.ETAPAS, soloActivos: false)).Order());
    }

    [Fact]
    public async Task La_ruta_estandar_son_las_etapas_activas_en_su_orden()
    {
        var servicio = Servicio();

        var ruta = await servicio.EtapasDeLaRutaAsync();

        Assert.Equal(_etapas.Valores.Where(v => v.Activo).Select(v => v.Id), ruta);
    }

    [Fact]
    public async Task Solo_existe_activo_un_valor_presente_y_activo()
    {
        var servicio = Servicio();
        var cargue = _etapas.Valores.Single(v => v.Codigo == "cargue");
        var descargue = _etapas.Valores.Single(v => v.Codigo == "descargue");

        Assert.True(await servicio.ExisteActivoAsync(clsNombresCatalogo.ETAPAS, cargue.Id));
        Assert.False(await servicio.ExisteActivoAsync(clsNombresCatalogo.ETAPAS, descargue.Id));
        Assert.False(await servicio.ExisteActivoAsync(clsNombresCatalogo.ETAPAS, Guid.NewGuid()));
    }
}
