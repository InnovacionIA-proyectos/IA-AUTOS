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

        if (entidad is IConCodigo codificado && lector.Tiene("codigo") && lector.Texto("codigo") != codificado.Codigo)
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

    public virtual async Task EliminarAsync(Guid id, CancellationToken ct = default) => Conjunto.Remove(await BuscarAsync(id, ct));

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

/// <summary>
/// Los estados de las ordenes Sofasa: codigo (el <c>StatusCode</c> real), nombre (la etiqueta), orden y activo. Un estado que alguna orden usa no se borra (409): se inactiva.
/// </summary>
internal sealed class clsCatalogoEstadosSofasa(clsAppDbContext contexto, IReloj reloj) : clsCatalogoEf<clsEstadoSofasa>(contexto, reloj, clsNombresCatalogo.ESTADOS_SOFASA)
{
    protected override IReadOnlyList<string> CamposPermitidos => clsCamposCatalogo.COMUNES;

    protected override ValorCatalogo Mapear(clsEstadoSofasa e) => new()
    {
        ["id"] = e.Id,
        ["codigo"] = e.Codigo,
        ["nombre"] = e.Nombre,
        ["orden"] = e.Orden,
        ["activo"] = e.Activo,
    };

    protected override Task<clsEstadoSofasa> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct) =>
        Task.FromResult(clsEstadoSofasa.Crear(campos.Texto("codigo"), campos.Texto("nombre"), campos.Entero("orden"), campos.Booleano("activo"), Reloj.Ahora));

    public override async Task EliminarAsync(Guid id, CancellationToken ct = default)
    {
        // La orden guarda el StatusCode como texto libre (sin llave foranea, para que la ingesta del ETL no falle con un estado nuevo): la regla de «en uso» se aplica aqui.
        var estado = await Conjunto.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new clsEntidadNoEncontradaException($"No existe el valor {id} en el catálogo {Nombre}");
        if (await Contexto.OrdenesSofasa.AnyAsync(o => o.StatusCode == estado.Codigo, ct))
        {
            throw new clsConflictoException("El valor está en uso y no se puede borrar: solo puede inactivarlo");
        }

        Conjunto.Remove(estado);
    }
}

/// <summary>
/// Los lugares reales (CEDI Envigado, Concesionario Autolarte Medellin...): <c>nombre</c> (unico), <c>tipo</c> (el codigo de un tipo de lugar de <c>puntos-custodia</c>), <c>ciudad</c>, <c>orden</c> y
/// <c>activo</c>. Salen ademas el nombre del tipo, la ubicacion (<c>latitud</c>, <c>longitud</c> y <c>radio_metros</c>, nulos mientras no se fijan) y <c>sin_ubicacion</c>. Un lugar que un viaje o una unidad usa
/// no se borra (409): se inactiva.
/// </summary>
internal sealed class clsCatalogoLugares(clsAppDbContext contexto, IReloj reloj) : clsCatalogoEf<clsLugar>(contexto, reloj, clsNombresCatalogo.LUGARES)
{
    protected override IReadOnlyList<string> CamposPermitidos { get; } = ["nombre", "tipo", "ciudad", "orden", "activo"];

    private async Task<Dictionary<Guid, (string Codigo, string Nombre)>> TiposAsync(CancellationToken ct) =>
        (await Contexto.PuntosCustodia.AsNoTracking().Select(p => new { p.Id, p.Codigo, p.Nombre }).ToListAsync(ct)).ToDictionary(p => p.Id, p => (p.Codigo, p.Nombre));

    private static ValorCatalogo Armar(clsLugar l, IReadOnlyDictionary<Guid, (string Codigo, string Nombre)> tipos) => new()
    {
        ["id"] = l.Id,
        ["nombre"] = l.Nombre,
        ["tipo"] = tipos.TryGetValue(l.TipoId, out var tipo) ? tipo.Codigo : null,
        ["tipo_nombre"] = tipos.TryGetValue(l.TipoId, out var tipoNombre) ? tipoNombre.Nombre : null,
        ["ciudad"] = l.Ciudad,
        ["orden"] = l.Orden,
        ["activo"] = l.Activo,
        ["latitud"] = l.Latitud,
        ["longitud"] = l.Longitud,
        ["radio_metros"] = l.RadioMetros,
        ["sin_ubicacion"] = l.SinUbicacion,
        ["estado_ubicacion"] = l.EstadoUbicacion,
    };

    protected override ValorCatalogo Mapear(clsLugar l) => Armar(l, new Dictionary<Guid, (string, string)>());

    protected override async Task<ValorCatalogo> MapearAsync(clsLugar entidad, CancellationToken ct) => Armar(entidad, await TiposAsync(ct));

    public override async Task<IReadOnlyList<ValorCatalogo>> ListarAsync(CancellationToken ct = default)
    {
        var tipos = await TiposAsync(ct);
        var filas = await Conjunto.AsNoTracking().OrderBy(l => l.Orden).ThenBy(l => l.Nombre).ToListAsync(ct);
        return filas.Select(l => Armar(l, tipos)).ToList();
    }

    protected override async Task<clsLugar> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct) =>
        clsLugar.Crear(campos.Texto("nombre"), await TipoActivoAsync(campos.Texto("tipo"), requerido: true, ct) ?? Guid.Empty, campos.Texto("ciudad"), campos.Entero("orden"), campos.Booleano("activo"), Reloj.Ahora);

    protected override async Task AplicarAsync(clsLugar entidad, clsCamposCatalogo campos, CancellationToken ct)
    {
        if (campos.Tiene("tipo") || campos.Tiene("ciudad"))
        {
            entidad.CambiarLugar(campos.Tiene("tipo") ? await TipoActivoAsync(campos.Texto("tipo"), requerido: true, ct) : null, campos.Texto("ciudad"), Reloj.Ahora);
        }
    }

    /// <summary>El id del tipo de lugar con ese codigo (activo); 400 si falta o no existe.</summary>
    private async Task<Guid?> TipoActivoAsync(string? codigo, bool requerido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            return requerido ? throw clsErroresCampos.De("tipo", "El tipo de lugar es requerido: use el código de uno de puntos-custodia") : null;
        }

        var limpio = codigo.Trim();
        var tipo = await Contexto.PuntosCustodia.AsNoTracking().Where(p => p.Codigo == limpio && p.Activo).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        return tipo ?? throw clsErroresCampos.De("tipo", "El tipo de lugar no existe en puntos-custodia o está inactivo");
    }
}

/// <summary>
/// Los motivos por los que no esta el inspector previsto: codigo, nombre, orden, activo y <c>requiere_texto</c> (el motivo «otro» exige escribir cual es). Un motivo que una inspeccion o una
/// novedad de proceso usa no se borra (409): se inactiva.
/// </summary>
internal sealed class clsCatalogoMotivosSinInspector(clsAppDbContext contexto, IReloj reloj)
    : clsCatalogoEf<clsMotivoSinInspector>(contexto, reloj, clsNombresCatalogo.MOTIVOS_SIN_INSPECTOR)
{
    protected override IReadOnlyList<string> CamposPermitidos { get; } = [.. clsCamposCatalogo.COMUNES, "requiere_texto"];

    protected override ValorCatalogo Mapear(clsMotivoSinInspector m)
    {
        var valor = Comunes(m);
        valor["requiere_texto"] = m.RequiereTexto;
        return valor;
    }

    protected override Task<clsMotivoSinInspector> CrearEntidadAsync(clsCamposCatalogo campos, CancellationToken ct) =>
        Task.FromResult(clsMotivoSinInspector.Crear(
            campos.Texto("codigo"), campos.Texto("nombre"), campos.Booleano("requiere_texto"), campos.Entero("orden"), campos.Booleano("activo"), Reloj.Ahora));

    protected override Task AplicarAsync(clsMotivoSinInspector entidad, clsCamposCatalogo campos, CancellationToken ct)
    {
        if (campos.Tiene("requiere_texto"))
        {
            entidad.CambiarRequiereTexto(campos.Booleano("requiere_texto"), Reloj.Ahora);
        }

        return Task.CompletedTask;
    }
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
        // Las vistas adicionales no tienen cuadrantes: una vista que ya tiene poligonos no puede pasar a ser adicional.
        if (campos.Booleano("obligatoria") == false && await Contexto.CuadranteVistas.AnyAsync(c => c.VistaFotoId == entidad.Id, ct))
        {
            throw new clsConflictoException("La vista tiene cuadrantes dibujados, así que no puede ser adicional. Borre sus polígonos o inactive la vista");
        }

        entidad.CambiarVista(campos.Booleano("obligatoria"), campos.Texto("tipo_vehiculo"), campos.Tiene("tipo_vehiculo"), Reloj.Ahora);
    }
}

