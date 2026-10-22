using System.Text.Json;
using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Parametros;

/// <summary>
/// Los parametros que el negocio ajusta sin desplegar codigo (capacidad <c>administrar_catalogos</c>: admin, super_admin y coordinador_calidad; 403 los demas). La API solo cambia valores: no crea ni
/// borra claves.
/// </summary>
[Route("api/parametros")]
[RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
public sealed class clsParametrosController(clsParametrosAdminService servicio) : clsControladorBase
{
    /// <summary>Todos los parametros, ordenados por clave: <c>clave</c>, <c>valor</c> (nulo si no tiene), <c>tipo</c>, <c>descripcion</c>, <c>actualizado_por</c> y <c>updated_at</c>.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ParametroDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct) => Ok(await servicio.ListarAsync(ct));

    /// <summary>
    /// Cambia el <c>valor</c> del parametro (texto o numero; <c>null</c> lo deja sin valor, solo en los que lo admiten) y responde el parametro actualizado. 400 en <c>valor</c> si no cumple su tipo;
    /// 404 si la clave no existe. Se aplica en un minuto o menos y queda en la auditoria con el valor antes y despues.
    /// </summary>
    [HttpPatch("{clave}")]
    [ProducesResponseType<ParametroDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(ActualizarParametroRequest))]
    public async Task<IActionResult> Actualizar(string clave, CancellationToken ct)
    {
        var campos = await LeerCamposAsync(ct);
        var extra = campos.Keys.Where(c => c != "valor").ToList();
        if (extra.Count > 0)
        {
            throw clsErroresCampos.De(extra[0], "Campo no admitido");
        }

        if (!campos.TryGetValue("valor", out var valor))
        {
            throw clsErroresCampos.De("valor", "El valor es requerido (null lo deja sin valor)");
        }

        var texto = valor.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => valor.GetString(),
            JsonValueKind.Number => valor.GetRawText(),
            _ => throw clsErroresCampos.De("valor", "El valor debe ser un texto o un número"),
        };
        return Ok(await servicio.ActualizarAsync(clave, new ActualizarParametroRequest(texto), ct));
    }
}
