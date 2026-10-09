using System.Text.Json;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Catalogos;

/// <summary>
/// Base de los catalogos sobre EF: lista, crea, edita y borra. Cada catalogo declara los campos que admite y como los muestra;
/// la validacion de cada campo vive en su entidad de dominio.
/// </summary>
internal abstract class clsCatalogoEf<T>(clsAppDbContext contexto, IReloj reloj, string nombre) : ICatalogo
    where T : clsCatalogoBase
{
    public string Nombre { get; } = nombre;

    protected clsAppDbContext Contexto { get; } = contexto;

    protected IReloj Reloj { get; } = reloj;

    protected DbSet<T> Conjunto => Contexto.Set<T>();

    /// <summary>Campos que acepta el cuerpo de crear o editar este catalogo.</summary>
    protected abstract IReadOnlyList<string> CamposPermitidos { get; }

    protected abstract ValorCatalogo Mapear(T entidad);

    /// <summary>Muestra un valor ya guardado o recien creado. Se sobrescribe cuando el valor necesita datos de otra tabla.</summary>
    protected virtual Task<ValorCatalogo> MapearAsync(T entidad, CancellationToken ct) => Task.FromResult(Mapear(entidad));

    protected abstract Task<T> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct);

    /// <summary>Aplica los campos propios de este catalogo al editar (los comunes ya se aplicaron).</summary>
    protected virtual Task AplicarAsync(T entidad, clsCamposCatalogo campos, CancellationToken ct) => Task.CompletedTask;

    public virtual async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await Conjunto.AsNoTracking().OrderBy(e => e.Orden).ThenBy(e => e.Nombre).ToListAsync(ct);
        return filas.Select(Mapear).ToList();
    }

    public async Task<ValorCatalogo> CrearAsync(IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var lector = new clsCamposCatalogo(campos);
        lector.SoloPermitidos(CamposPermitidos);
        var entidad = await CrearEntidadAsync(lector, ct);
        Conjunto.Add(entidad);
        return await MapearAsync(entidad, ct);
    }

    public async Task<ValorCatalogo> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var lector = new clsCamposCatalogo(campos);
        lector.SoloPermitidos(CamposPermitidos);
        var entidad = await BuscarAsync(id, ct);

        if (entidad is clsCatalogoCodificado codificado && lector.Tiene("codigo") && lector.Texto("codigo") != codificado.Codigo)
        {
            throw clsErroresCampos.De("codigo", "El código no se puede cambiar: cree un valor nuevo e inactive este");
        }

        foreach (var campo in new[] { "nombre", "orden", "activo" }.Where(lector.Tiene))
        {
            if (campos[campo].ValueKind == JsonValueKind.Null)
            {
                throw clsErroresCampos.De(campo, "No admite valor nulo");
            }
        }

        entidad.Actualizar(lector.Texto("nombre"), lector.Entero("orden"), lector.Booleano("activo"), Reloj.Ahora);
        await AplicarAsync(entidad, lector, ct);
        return await MapearAsync(entidad, ct);
    }

    public async Task EliminarAsync(Guid id, CancellationToken ct = default) => Conjunto.Remove(await BuscarAsync(id, ct));

    private async Task<T> BuscarAsync(Guid id, CancellationToken ct) =>
        await Conjunto.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new clsEntidadNoEncontradaException($"No existe el valor {id} en el catálogo {Nombre}");

    protected static ValorCatalogo Comunes(clsCatalogoCodificado e) => new()
    {
        ["id"] = e.Id,
        ["codigo"] = e.Codigo,
        ["nombre"] = e.Nombre,
        ["orden"] = e.Orden,
        ["activo"] = e.Activo,
    };
}

/// <summary>Catalogo cuyos valores solo tienen codigo, nombre, orden y estado (etapas, lugares, tipos de unidad, clases y tamanos de dano).</summary>
internal sealed class clsCatalogoSimple<T>(
    clsAppDbContext contexto,
    IReloj reloj,
    string nombre,
    Func<string?, string?, int?, bool?, DateTimeOffset, T> fabrica) : clsCatalogoEf<T>(contexto, reloj, nombre)
    where T : clsCatalogoCodificado
{
    protected override IReadOnlyList<string> CamposPermitidos => clsCamposCatalogo.COMUNES;

    protected override ValorCatalogo Mapear(T entidad) => Comunes(entidad);

    protected override Task<T> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct) =>
        Task.FromResult(fabrica(campos.Texto("codigo"), campos.Texto("nombre"), campos.Entero("orden"), campos.Booleano("activo"), Reloj.Ahora));
}

internal sealed class clsCatalogoNiveles(clsAppDbContext contexto, IReloj reloj)
    : clsCatalogoEf<clsNivelSeveridad>(contexto, reloj, clsNombresCatalogo.NIVELES_SEVERIDAD)
{
    protected override IReadOnlyList<string> CamposPermitidos { get; } = [.. clsCamposCatalogo.COMUNES, "accion"];

    protected override ValorCatalogo Mapear(clsNivelSeveridad n)
    {
        var valor = Comunes(n);
        valor["accion"] = n.Accion;
        return valor;
    }

    protected override Task<clsNivelSeveridad> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct) =>
        Task.FromResult(clsNivelSeveridad.Crear(
            campos.Texto("codigo"), campos.Texto("nombre"), campos.Texto("accion"), campos.Entero("orden"), campos.Booleano("activo"), Reloj.Ahora));

    protected override Task AplicarAsync(clsNivelSeveridad entidad, clsCamposCatalogo campos, CancellationToken ct)
    {
        if (campos.Tiene("accion"))
        {
            entidad.CambiarAccion(campos.Texto("accion"), Reloj.Ahora);
        }

        return Task.CompletedTask;
    }
}

internal sealed class clsCatalogoVistas(clsAppDbContext contexto, IReloj reloj)
    : clsCatalogoEf<clsVistaFoto>(contexto, reloj, clsNombresCatalogo.VISTAS_FOTO)
{
    protected override IReadOnlyList<string> CamposPermitidos { get; } = [.. clsCamposCatalogo.COMUNES, "obligatoria", "tipo_vehiculo"];

    protected override ValorCatalogo Mapear(clsVistaFoto v)
    {
        var valor = Comunes(v);
        valor["obligatoria"] = v.Obligatoria;
        valor["tipo_vehiculo"] = v.TipoVehiculo;
        return valor;
    }

    protected override Task<clsVistaFoto> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct) =>
        Task.FromResult(clsVistaFoto.Crear(
            campos.Texto("codigo"),
            campos.Texto("nombre"),
            campos.Booleano("obligatoria"),
            campos.Texto("tipo_vehiculo"),
            campos.Entero("orden"),
            campos.Booleano("activo"),
            Reloj.Ahora));

    protected override async Task AplicarAsync(clsVistaFoto entidad, clsCamposCatalogo campos, CancellationToken ct)
    {
        // Las vistas adicionales no tienen cuadrantes: una vista que ya los tiene no puede pasar a ser adicional.
        if (campos.Booleano("obligatoria") == false && await Contexto.Cuadrantes.AnyAsync(c => c.VistaFotoId == entidad.Id, ct))
        {
            throw new clsConflictoException("La vista tiene cuadrantes, así que no puede ser adicional. Inactive los cuadrantes o la vista");
        }

        entidad.CambiarVista(campos.Booleano("obligatoria"), campos.Texto("tipo_vehiculo"), campos.Tiene("tipo_vehiculo"), Reloj.Ahora);
    }
}

internal sealed class clsCatalogoCuadrantes(clsAppDbContext contexto, IReloj reloj)
    : clsCatalogoEf<clsCuadrante>(contexto, reloj, clsNombresCatalogo.CUADRANTES)
{
    protected override IReadOnlyList<string> CamposPermitidos { get; } =
        ["vista_foto_id", "tipo_vehiculo", "numero", "nombre", "geometria", "orden", "activo"];

    public override async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
    {
        var filas = await (from c in Contexto.Cuadrantes.AsNoTracking()
                           join v in Contexto.VistasFoto.AsNoTracking() on c.VistaFotoId equals v.Id
                           orderby c.Orden, c.Numero
                           select new { Cuadrante = c, Vista = v.Codigo }).ToListAsync(ct);
        return filas.Select(f => Mapear(f.Cuadrante, f.Vista)).ToList();
    }

    protected override ValorCatalogo Mapear(clsCuadrante c) => Mapear(c, vista: null);

    protected override async Task<ValorCatalogo> MapearAsync(clsCuadrante c, CancellationToken ct) =>
        Mapear(c, await Contexto.VistasFoto.Where(v => v.Id == c.VistaFotoId).Select(v => v.Codigo).FirstOrDefaultAsync(ct));

    private static ValorCatalogo Mapear(clsCuadrante c, string? vista) => new()
    {
        ["id"] = c.Id,
        ["vista_foto_id"] = c.VistaFotoId,
        ["vista"] = vista,
        ["tipo_vehiculo"] = c.TipoVehiculo,
        ["numero"] = c.Numero,
        ["nombre"] = c.Nombre,
        // La geometria sale como objeto JSON (no como texto) para que la app y el portal la lean directamente.
        ["geometria"] = c.Geometria is null ? null : JsonSerializer.Deserialize<JsonElement>(c.Geometria),
        ["orden"] = c.Orden,
        ["activo"] = c.Activo,
    };

    protected override async Task<clsCuadrante> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct)
    {
        var vistaId = campos.Identificador("vista_foto_id") ?? throw clsErroresCampos.De("vista_foto_id", "La vista es requerida");
        await AsegurarVistaConCuadrantesAsync(vistaId, ct);
        return clsCuadrante.Crear(
            vistaId,
            campos.Texto("tipo_vehiculo"),
            campos.Entero("numero"),
            campos.Texto("nombre"),
            campos.Json("geometria"),
            campos.Entero("orden"),
            campos.Booleano("activo"),
            Reloj.Ahora);
    }

    protected override async Task AplicarAsync(clsCuadrante entidad, clsCamposCatalogo campos, CancellationToken ct)
    {
        var vistaId = campos.Identificador("vista_foto_id");
        if (vistaId is not null)
        {
            await AsegurarVistaConCuadrantesAsync(vistaId.Value, ct);
        }

        entidad.Cambiar(
            vistaId,
            campos.Texto("tipo_vehiculo"),
            campos.Tiene("tipo_vehiculo"),
            campos.Entero("numero"),
            campos.Json("geometria"),
            campos.Tiene("geometria"),
            Reloj.Ahora);
    }

    /// <summary>La vista debe existir, estar activa y ser obligatoria: las vistas adicionales (baul, cojineria) no tienen cuadrantes.</summary>
    private async Task AsegurarVistaConCuadrantesAsync(Guid vistaId, CancellationToken ct)
    {
        var vista = await Contexto.VistasFoto.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vistaId, ct)
            ?? throw clsErroresCampos.De("vista_foto_id", "La vista no existe");
        if (!vista.Obligatoria)
        {
            throw clsErroresCampos.De("vista_foto_id", "Las vistas adicionales no tienen cuadrantes");
        }

        if (!vista.Activo)
        {
            throw clsErroresCampos.De("vista_foto_id", "La vista está inactiva");
        }
    }
}
