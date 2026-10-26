using System.Globalization;

namespace IAAutos.Aplicacion.Comun;

/// <summary>Una pagina de un listado: sus filas, el total que cumple los filtros y la pagina y el tamano que se aplicaron.</summary>
public sealed record PaginaDto<T>(IReadOnlyList<T> Items, int Total, int Pagina, int Tamano);

/// <summary>La pagina pedida, ya validada: desde 1 y con el tamano dentro del maximo.</summary>
public sealed record PaginaSolicitada(int Pagina, int Tamano)
{
    public int Desplazamiento => (Pagina - 1) * Tamano;
}

/// <summary>El orden pedido (<c>campo:asc</c> o <c>campo:desc</c>), con el campo ya comprobado contra los que admite el listado.</summary>
public sealed record OrdenSolicitado(string Campo, bool Descendente);

/// <summary>
/// El formato unico de paginacion y orden de los listados: <c>pagina</c> (desde 1), <c>tamano</c> (50 por defecto, maximo 200: uno mayor se reduce a 200) y <c>orden</c> (<c>campo:asc|desc</c>, solo
/// los campos que admite cada listado). Un valor que no es numero o un orden mal formado o de un campo no permitido agrega el error de su campo, para responder 400 con todos los errores juntos.
/// </summary>
public static class clsPaginacion
{
    public const int TAMANO_POR_DEFECTO = 50;
    public const int TAMANO_MAXIMO = 200;

    public static PaginaSolicitada Pagina(string? pagina, string? tamano, Dictionary<string, string[]> errores)
    {
        var numeroPagina = Entero(pagina, "pagina", errores) ?? 1;
        var numeroTamano = Entero(tamano, "tamano", errores) ?? TAMANO_POR_DEFECTO;
        return new PaginaSolicitada(Math.Max(numeroPagina, 1), Math.Clamp(numeroTamano, 1, TAMANO_MAXIMO));
    }

    /// <summary>La pagina de quien ya la trae como numero (por ejemplo, el listado de usuarios): se acota igual.</summary>
    public static PaginaSolicitada Pagina(int? pagina, int? tamano) =>
        new(Math.Max(pagina ?? 1, 1), Math.Clamp(tamano ?? TAMANO_POR_DEFECTO, 1, TAMANO_MAXIMO));

    /// <summary>El orden pedido o nulo si no se pidio (el listado usa el suyo). Los campos se escriben en snake_case y el sentido admite mayusculas.</summary>
    public static OrdenSolicitado? Orden(string? orden, IReadOnlySet<string> permitidos, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(orden))
        {
            return null;
        }

        var partes = orden.Trim().Split(':');
        var campo = partes[0].Trim().ToLowerInvariant();
        var sentido = partes.Length == 2 ? partes[1].Trim().ToLowerInvariant() : "asc";
        if (partes.Length > 2 || sentido is not ("asc" or "desc") || !permitidos.Contains(campo))
        {
            errores["orden"] = [$"Orden inválido: use campo:asc o campo:desc con uno de estos campos: {string.Join(", ", permitidos.Order())}"];
            return null;
        }

        return new OrdenSolicitado(campo, sentido == "desc");
    }

    private static int? Entero(string? texto, string campo, Dictionary<string, string[]> errores)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (int.TryParse(texto.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero))
        {
            return numero;
        }

        errores[campo] = [$"{campo} debe ser un número entero"];
        return null;
    }
}
