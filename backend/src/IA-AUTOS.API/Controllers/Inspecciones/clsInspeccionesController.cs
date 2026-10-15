using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Inspecciones;

[Route("api/inspecciones")]
public sealed class clsInspeccionesController(clsInspeccionesService servicio) : clsControladorBase
{
    /// <summary>Crea la inspeccion de una etapa. El usuario sale del token, el estado inicial es siempre borrador y la placa, el trailer y el conductor salen del viaje de la etapa.</summary>
    [HttpPost]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<InspeccionDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Inspecciones.DTOInspecciones.CrearInspeccionRequest))]
    public async Task<IActionResult> Crear(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearInspeccionRequest>(ct);
        return Creado(await servicio.CrearAsync(solicitud, ct));
    }

    /// <summary>Listado (maximo 200, mas recientes primero) con filtros por VIN, usuario, viaje, momento, estado, proceso y fechas. Inspectores y conductores ven solo los informes de sus viajes.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InspeccionListadoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? vin,
        [FromQuery] string? usuarioId,
        [FromQuery] string? viajeId,
        [FromQuery] string? momento,
        [FromQuery] string? estado,
        [FromQuery] string? proceso,
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        CancellationToken ct) =>
        Ok(await servicio.ListarAsync(new FiltrosInspeccionesRequest(vin, usuarioId, viajeId, momento, estado, proceso, desde, hasta), ct));

    /// <summary>Informe completo para revision: contexto, hallazgos, evidencias con URL y transferencia con firmas.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<InformeDetalleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct) => Ok(await servicio.ObtenerInformeAsync(id, ct));

    [HttpPost("{id:guid}/evidencias")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<EvidenciaDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Inspecciones.DTOInspecciones.CrearEvidenciaRequest))]
    public async Task<IActionResult> AgregarEvidencia(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearEvidenciaRequest>(ct);
        return Creado(await servicio.AgregarEvidenciaAsync(id, solicitud, ct));
    }

    [HttpPost("{id:guid}/hallazgos")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<HallazgoDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Inspecciones.DTOInspecciones.CrearHallazgoRequest))]
    public async Task<IActionResult> AgregarHallazgo(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearHallazgoRequest>(ct);
        return Creado(await servicio.AgregarHallazgoAsync(id, solicitud, ct));
    }

    /// <summary>Firma de entrega y recibo (parcial o completa). Se acumulan las firmas hasta completar ambas.</summary>
    [HttpPost("{id:guid}/transferencia")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<TransferenciaDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Inspecciones.DTOInspecciones.FirmarTransferenciaRequest))]
    public async Task<IActionResult> FirmarTransferencia(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<FirmarTransferenciaRequest>(ct);
        return Creado(await servicio.FirmarTransferenciaAsync(id, solicitud, ct));
    }

    /// <summary>Cierra la inspeccion como evidencia inmutable (409 si ya estaba cerrada).</summary>
    [HttpPost("{id:guid}/cerrar")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<CerrarInspeccionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cerrar(Guid id, CancellationToken ct) => Ok(await servicio.CerrarAsync(id, ct));
}
