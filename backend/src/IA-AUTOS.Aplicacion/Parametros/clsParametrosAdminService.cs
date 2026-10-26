using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Parametros;

namespace IAAutos.Aplicacion.Parametros;

/// <summary>Un parametro tal como lo ve el portal: su valor (nulo si no tiene), su tipo, para que sirve y quien lo cambio por ultima vez (nulo en el valor inicial) y cuando.</summary>
public sealed record ParametroDto(string Clave, string? Valor, string Tipo, string Descripcion, Guid? ActualizadoPor, DateTimeOffset UpdatedAt);

/// <summary>Cuerpo de cambiar un parametro: el valor nuevo (texto o numero); nulo lo deja sin valor, solo en los parametros que lo admiten.</summary>
public sealed record ActualizarParametroRequest(string? Valor);

/// <summary>
/// Consulta y cambio de los parametros que el negocio ajusta sin desplegar codigo. Solo quien administra los catalogos (admin, super_admin y coordinador_calidad). Las claves las crea el script
/// de base de datos: la API solo cambia su valor. Cada cambio queda en la auditoria con el valor antes y despues y se aplica en un minuto o menos (la cache de los procesos que los leen).
/// </summary>
public sealed class clsParametrosAdminService(
    IParametroRepository parametros,
    ICatalogosLectura catalogos,
    IParametros lector,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj)
{
    /// <summary>El catalogo cuyos codigos acepta cada parametro de tipo lista.</summary>
    private static readonly IReadOnlyDictionary<string, string> CATALOGO_DE_LISTA = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [clsClavesParametros.NIVELES_CRITICOS] = clsNombresCatalogo.NIVELES_SEVERIDAD,
    };

    public async Task<IReadOnlyList<ParametroDto>> ListarAsync(CancellationToken ct = default) =>
        (await parametros.ListarAsync(ct)).OrderBy(p => p.Clave, StringComparer.Ordinal).Select(ADto).ToList();

    /// <summary>
    /// Cambia el valor. 400 en <c>valor</c> si no cumple su tipo (numero, porcentaje de 0 a 100, lista de codigos existentes, hora HH:mm o correos validos); 404 si la clave no existe. Dar el mismo
    /// valor que ya tiene responde 200 y no cambia ni audita nada.
    /// </summary>
    public async Task<ParametroDto> ActualizarAsync(string clave, ActualizarParametroRequest solicitud, CancellationToken ct = default)
    {
        var parametro = await parametros.ObtenerAsync(clave, ct) ?? throw new clsEntidadNoEncontradaException($"El parámetro {clave} no existe");
        var codigos = parametro.Tipo == clsTiposParametro.LISTA && CATALOGO_DE_LISTA.TryGetValue(parametro.Clave, out var catalogo)
            ? await catalogos.CodigosAsync(catalogo, soloActivos: false, ct)
            : null;
        var antes = parametro.Valor;

        if (parametro.CambiarValor(solicitud.Valor, codigos, actual.IdObligatorio(), reloj.Ahora))
        {
            auditor.Registrar(
                "parametro.actualizar",
                "parametros",
                detalle: new DetalleAuditoria(new { parametro = parametro.Clave, valor = antes }, new { parametro = parametro.Clave, valor = parametro.Valor }));
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
            lector.Invalidar();
        }

        return ADto(parametro);
    }

    private static ParametroDto ADto(clsParametro p) => new(p.Clave, p.Valor, p.Tipo, p.Descripcion, p.ActualizadoPor, p.UpdatedAt);
}
