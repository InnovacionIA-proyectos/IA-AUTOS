using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Inspecciones;

/// <summary>
/// Los hallazgos: su listado y detalle (inspector de sus viajes; coordinador de calidad, comercial y administradores de su cliente; el conductor no) y su seguimiento
/// (coordinador de calidad, comercial y administradores, capacidad <c>gestionar_seguimiento</c>).
/// </summary>
[Route("api/hallazgos")]
public sealed class clsHallazgosController(clsSeguimientoService seguimiento, clsHallazgosService hallazgos) : clsControladorBase
{
    /// <summary>
    /// Una pagina de hallazgos (<c>items</c>, <c>total</c>, <c>pagina</c>, <c>tamano</c> hasta 200) con filtros <c>estado_seguimiento</c>, <c>origen</c>, <c>revisado</c> (solo novedades del
    /// conductor), <c>cliente_id</c> (solo el super administrador), <c>viaje_id</c>, <c>vin</c>, <c>inspeccion_id</c>, <c>nivel</c>, <c>cuadrante_numero</c>, <c>lado</c>, <c>proceso</c>, <c>desde</c> y
    /// <c>hasta</c> (cierre de la inspeccion) y <c>orden</c> (recientes, antiguos o severidad). Cada fila es el hallazgo con su VIN, viaje, proceso, momento, inspeccion y foto propia.
    /// </summary>
    [HttpGet]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.COORDINADOR_CALIDAD, clsRol.COMERCIAL, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<ListadoHallazgosDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery(Name = "estado_seguimiento")] string? estadoSeguimiento,
        [FromQuery] string? origen,
        [FromQuery] string? revisado,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "viaje_id")] string? viajeId,
        [FromQuery] string? vin,
        [FromQuery(Name = "inspeccion_id")] string? inspeccionId,
        [FromQuery] string? nivel,
        [FromQuery(Name = "cuadrante_numero")] string? cuadranteNumero,
        [FromQuery] string? lado,
        [FromQuery] string? proceso,
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] string? orden,
        [FromQuery] int? pagina,
        [FromQuery] int? tamano,
        CancellationToken ct) =>
        Ok(await hallazgos.ListarAsync(
            new FiltrosHallazgosRequest(estadoSeguimiento, origen, revisado, clienteId, viajeId, vin, inspeccionId, nivel, cuadranteNumero, lado, proceso, desde, hasta, orden, pagina, tamano), ct));

    /// <summary>El hallazgo como una fila del listado, con su historial de seguimiento (<c>seguimiento</c>) y sus notas aclaratorias (<c>notas_aclaratorias</c>).</summary>
    [HttpGet("{id:guid}")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.COORDINADOR_CALIDAD, clsRol.COMERCIAL, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<HallazgoDetalleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct) => Ok(await hallazgos.ObtenerAsync(id, ct));

    /// <summary>
    /// Cambia el estado del hallazgo. Un hallazgo V2 se valida con <c>evidencia_sello</c> (la foto del sello de quien valida, 400 si falta). 409 si su inspeccion esta en borrador
    /// o es una novedad del conductor sin revisar. Marcar rechazado nunca se bloquea por la evidencia: la respuesta trae <c>evidencia_completa</c> y <c>evidencia_faltante</c>.
    /// </summary>
    [HttpPost("{id:guid}/seguimiento")]
    [RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.COMERCIAL, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<HallazgoDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(CambiarSeguimientoRequest))]
    public async Task<IActionResult> CambiarSeguimiento(Guid id, CancellationToken ct) =>
        Ok(await seguimiento.CambiarAsync(id, await LeerCuerpoAsync<CambiarSeguimientoRequest>(ct), ct));

    /// <summary>El historial del hallazgo (cambios de seguimiento, revisiones y «no sale»), del mas antiguo al mas reciente.</summary>
    [HttpGet("{id:guid}/seguimiento")]
    [RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.COMERCIAL, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<IReadOnlyList<SeguimientoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Historial(Guid id, CancellationToken ct) => Ok(await seguimiento.HistorialAsync(id, ct));
}
