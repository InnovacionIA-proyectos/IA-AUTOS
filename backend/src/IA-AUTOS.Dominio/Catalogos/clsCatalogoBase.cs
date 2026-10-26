using System.Text.RegularExpressions;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Catalogos;

/// <summary>Un valor de catalogo con un codigo estable que se define al crearlo y no cambia despues.</summary>
public interface IConCodigo
{
    string Codigo { get; }
}

/// <summary>
/// Base de los catalogos maestros: nombre visible, orden de presentacion y estado. Un valor nunca se borra si esta en uso: se
/// inactiva (Activo = false) y deja de ofrecerse, pero lo ya guardado que lo referencia sigue siendo valido.
/// </summary>
public abstract class clsCatalogoBase : clsEntidadBase
{
    public const int NOMBRE_MAX = 100;

    public string Nombre { get; protected set; } = string.Empty;

    /// <summary>Posicion al mostrar el catalogo (menor primero).</summary>
    public int Orden { get; protected set; }

    public bool Activo { get; protected set; } = true;

    /// <summary>Actualiza solo lo recibido (los nulos se conservan).</summary>
    public void Actualizar(string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        if (nombre is not null)
        {
            Nombre = NombreValido(nombre);
        }

        if (orden is not null)
        {
            Orden = OrdenValido(orden.Value);
        }

        if (activo is not null)
        {
            Activo = activo.Value;
        }

        Tocar(ahora);
    }

    protected void Iniciar(string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        Nombre = NombreValido(nombre);
        Orden = OrdenValido(orden ?? 0);
        Activo = activo ?? true;
        Inicializar(ahora);
    }

    protected static string NombreValido(string? nombre)
    {
        var limpio = nombre?.Trim();
        if (string.IsNullOrEmpty(limpio))
        {
            throw clsErroresCampos.De("nombre", "El nombre es requerido");
        }

        if (limpio.Length > NOMBRE_MAX)
        {
            throw clsErroresCampos.De("nombre", $"El nombre admite máximo {NOMBRE_MAX} caracteres");
        }

        return limpio;
    }

    protected static int OrdenValido(int orden) =>
        orden >= 0 ? orden : throw clsErroresCampos.De("orden", "El orden debe ser un entero >= 0");
}

/// <summary>
/// Catalogo con codigo: la clave estable en minusculas que usan la API, las apps y las tablas que lo referencian (por ejemplo
/// tblHallazgos guarda el codigo de la clase de dano). Se define al crear el valor y no cambia despues.
/// </summary>
public abstract partial class clsCatalogoCodificado : clsCatalogoBase, IConCodigo
{
    public const int CODIGO_MAX = 30;

    public string Codigo { get; protected set; } = string.Empty;

    protected void Iniciar(string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        Codigo = CodigoValido(codigo);
        Iniciar(nombre, orden, activo, ahora);
    }

    public static string CodigoValido(string? codigo)
    {
        var limpio = codigo?.Trim();
        if (string.IsNullOrEmpty(limpio))
        {
            throw clsErroresCampos.De("codigo", "El código es requerido");
        }

        if (limpio.Length > CODIGO_MAX || !FormatoCodigo().IsMatch(limpio))
        {
            throw clsErroresCampos.De("codigo", $"El código admite solo minúsculas, números y guion bajo (máximo {CODIGO_MAX} caracteres)");
        }

        return limpio;
    }

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex FormatoCodigo();
}
