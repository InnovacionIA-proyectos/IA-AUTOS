using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Catalogos;

/// <summary>
/// Estado de una orden Sofasa: el <c>Codigo</c> es el <c>StatusCode</c> real que trae la orden (texto libre del origen, que se conserva tal cual, con sus mayusculas) y el <c>Nombre</c>, la etiqueta que se
/// muestra. A diferencia de los demas catalogos, el codigo no esta limitado a minusculas y guion bajo porque es el del sistema de origen; como en ellos, no cambia despues de creado.
/// </summary>
public sealed class clsEstadoSofasa : clsCatalogoBase, IConCodigo
{
    public const int CODIGO_MAX = 100;

    private clsEstadoSofasa()
    {
    }

    public string Codigo { get; private set; } = string.Empty;

    public static clsEstadoSofasa Crear(string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var estado = new clsEstadoSofasa { Codigo = CodigoValido(codigo) };
        estado.Iniciar(nombre, orden, activo, ahora);
        return estado;
    }

    /// <summary>El <c>StatusCode</c> recortado; no puede estar vacio, pasar de 100 caracteres ni llevar saltos de linea u otros caracteres de control.</summary>
    public static string CodigoValido(string? codigo)
    {
        var limpio = codigo?.Trim();
        if (string.IsNullOrEmpty(limpio))
        {
            throw clsErroresCampos.De("codigo", "El código es requerido: es el status_code de la orden");
        }

        if (limpio.Length > CODIGO_MAX || limpio.Any(char.IsControl))
        {
            throw clsErroresCampos.De("codigo", $"El código es el status_code de la orden: texto de máximo {CODIGO_MAX} caracteres, sin saltos de línea");
        }

        return limpio;
    }
}
