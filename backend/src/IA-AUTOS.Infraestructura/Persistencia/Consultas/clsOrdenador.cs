using System.Linq.Expressions;
using IAAutos.Aplicacion.Comun;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>
/// Aplica el orden pedido (<c>campo:asc|desc</c>) a la consulta de un listado: cada campo permitido se registra con la expresion por la que ordena y, si no se pidio orden, se usa el del listado. Siempre
/// agrega un desempate estable para que dos paginas seguidas no repitan ni salten filas.
/// </summary>
internal sealed class clsOrdenador<T>(IQueryable<T> consulta)
{
    private readonly Dictionary<string, Func<bool, IOrderedQueryable<T>>> _campos = new(StringComparer.Ordinal);

    /// <summary>Un campo por el que se puede ordenar (en snake_case, como lo pide la API).</summary>
    public clsOrdenador<T> Campo<TClave>(string nombre, Expression<Func<T, TClave>> clave)
    {
        _campos[nombre] = descendente => descendente ? consulta.OrderByDescending(clave) : consulta.OrderBy(clave);
        return this;
    }

    /// <summary>Los campos registrados: los que el listado declara como permitidos deben coincidir con estos.</summary>
    public IReadOnlyCollection<string> Campos => _campos.Keys;

    /// <summary>La consulta ordenada por lo pedido (o por <paramref name="porDefecto"/>) y con el <paramref name="desempate"/> al final.</summary>
    public IQueryable<T> Aplicar(
        OrdenSolicitado? orden,
        Func<IQueryable<T>, IOrderedQueryable<T>> porDefecto,
        Func<IOrderedQueryable<T>, IOrderedQueryable<T>> desempate)
    {
        var ordenada = orden is not null && _campos.TryGetValue(orden.Campo, out var aplicar) ? aplicar(orden.Descendente) : porDefecto(consulta);
        return desempate(ordenada);
    }
}

internal static class clsOrdenamiento
{
    /// <summary>Empieza el orden de una consulta (el tipo se deduce de ella, tambien para proyecciones anonimas).</summary>
    public static clsOrdenador<T> De<T>(IQueryable<T> consulta) => new(consulta);
}
