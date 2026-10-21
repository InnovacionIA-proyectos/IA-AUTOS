using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Parametros;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>La lectura de parametros: valores tipados, valores por defecto y la cache de un minuto.</summary>
public class ParametrosTests
{
    private sealed class RepositorioFalso(Dictionary<string, string?> valores) : IParametroRepository
    {
        public int Lecturas { get; private set; }

        public bool Falla { get; set; }

        public Task<IReadOnlyList<clsParametro>> ListarAsync(CancellationToken ct = default)
        {
            Lecturas++;
            if (Falla)
            {
                throw new InvalidOperationException("Invalid object name 'tblParametros'");
            }

            // La entidad solo se crea desde la base: se arma por reflexion para la prueba.
            var filas = valores.Select(v =>
            {
                var p = (clsParametro)Activator.CreateInstance(typeof(clsParametro), nonPublic: true)!;
                typeof(clsParametro).GetProperty(nameof(clsParametro.Clave))!.SetValue(p, v.Key);
                typeof(clsParametro).GetProperty(nameof(clsParametro.Valor))!.SetValue(p, v.Value);
                return p;
            }).ToList();
            return Task.FromResult<IReadOnlyList<clsParametro>>(filas);
        }
    }

    private static (clsParametrosService Servicio, RepositorioFalso Repositorio) Crear(Dictionary<string, string?> valores)
    {
        var repositorio = new RepositorioFalso(valores);
        return (new clsParametrosService(repositorio, new MemoryCache(new MemoryCacheOptions()), NullLogger<clsParametrosService>.Instance), repositorio);
    }

    [Fact]
    public async Task Lee_el_valor_el_entero_y_la_lista_de_un_parametro()
    {
        var (servicio, _) = Crear(new() { ["a"] = "30", ["niveles"] = " v1_plus , v1,v1_plus,, v2 ", ["vacio"] = null });

        Assert.Equal("30", await servicio.ValorAsync("a"));
        Assert.Equal(30, await servicio.EnteroAsync("a", 5));
        Assert.Equal(["v1_plus", "v1", "v2"], await servicio.ListaAsync("niveles", ["x"]));
        Assert.Null(await servicio.ValorAsync("vacio"));
        Assert.Null(await servicio.ValorAsync("no-existe"));
    }

    [Fact]
    public async Task Un_parametro_ausente_sin_valor_o_mal_escrito_devuelve_el_valor_por_defecto()
    {
        var (servicio, _) = Crear(new() { ["texto"] = "abc", ["vacio"] = null, ["lista-vacia"] = " , ," });

        Assert.Equal(7, await servicio.EnteroAsync("texto", 7));
        Assert.Equal(7, await servicio.EnteroAsync("vacio", 7));
        Assert.Equal(7, await servicio.EnteroAsync("no-existe", 7));
        Assert.Equal(["x", "y"], await servicio.ListaAsync("vacio", ["x", "y"]));
        Assert.Equal(["x", "y"], await servicio.ListaAsync("lista-vacia", ["x", "y"]));
        Assert.Equal(["x", "y"], await servicio.ListaAsync("no-existe", ["x", "y"]));
    }

    [Fact]
    public async Task Los_valores_se_guardan_en_cache_y_la_tabla_se_lee_una_sola_vez()
    {
        var (servicio, repositorio) = Crear(new() { ["a"] = "1", ["b"] = "2" });

        await servicio.ValorAsync("a");
        await servicio.EnteroAsync("b", 0);
        await servicio.ListaAsync("a", []);

        Assert.Equal(1, repositorio.Lecturas);
        Assert.Equal(TimeSpan.FromMinutes(1), clsParametrosService.VIGENCIA);
    }

    [Fact]
    public async Task Si_la_tabla_no_se_puede_leer_usa_los_valores_por_defecto_sin_lanzar_y_no_vuelve_a_leerla_enseguida()
    {
        var (servicio, repositorio) = Crear(new() { ["a"] = "1" });
        repositorio.Falla = true;

        Assert.Equal(10, await servicio.EnteroAsync("a", 10));
        Assert.Equal(["v1_plus"], await servicio.ListaAsync("niveles", ["v1_plus"]));
        Assert.Equal(1, repositorio.Lecturas);
    }
}
