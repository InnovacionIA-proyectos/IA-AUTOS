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
    /// <summary>
    /// Crea la inspeccion de salida o de llegada de una etapa. El usuario sale del token, el estado inicial es siempre borrador y la placa, el
    /// trailer y el conductor salen del viaje de la etapa. La salida exige que la etapa anterior del VIN tenga cerrada su llegada (409
    /// ETAPA_PREVIA_SIN_CIERRE).
    /// </summary>
    [HttpPost]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<InspeccionDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Inspecciones.DTOInspecciones.CrearInspeccionRequest))]
    public async Task<IActionResult> Crear(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearInspeccionRequest>(ct);
        return Creado(await servicio.CrearAsync(solicitud, ct));
    }

    /// <summary>
    /// Corrige una inspeccion CERRADA: crea la version siguiente (en borrador, vacia: se vuelven a tomar las fotos) que referencia a la anterior;
    /// la anterior no cambia. 409 si la inspeccion no esta cerrada o ya fue corregida. Cuerpo opcional.
    /// </summary>
    [HttpPost("{id:guid}/corregir")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<InspeccionDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Inspecciones.DTOInspecciones.CorregirInspeccionRequest))]
    public async Task<IActionResult> Corregir(Guid id, CancellationToken ct)
    {
        var solicitud = Request.ContentLength is > 0 ? await LeerCuerpoAsync<CorregirInspeccionRequest>(ct) : new CorregirInspeccionRequest(null, null, null, null);
        return Creado(await servicio.CorregirAsync(id, solicitud, ct));
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

    /// <summary>
    /// Agrega una foto: de una vista (<c>vista_id</c>, una de las 7 obligatorias o de las 2 adicionales con su <c>nota</c>) o de un hallazgo
    /// (<c>hallazgo_id</c>). El archivo debe haberse subido antes con POST /api/uploads. El servidor agrega su sello y el hash SHA-256.
    /// </summary>
    [HttpPost("{id:guid}/evidencias")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<EvidenciaDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Inspecciones.DTOInspecciones.CrearEvidenciaRequest))]
    public async Task<IActionResult> AgregarEvidencia(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearEvidenciaRequest>(ct);
        return Creado(await servicio.AgregarEvidenciaAsync(id, solicitud, ct));
    }

    /// <summary>
    /// Guarda un hallazgo con su zona (<c>cuadrante_numero</c> + <c>lado</c>, o <c>zona_provisional</c>), clase, tamano, nivel de severidad y su foto propia
    /// (<c>evidencia</c>, obligatoria: sin ella 400). <c>parte_vehiculo</c> y <c>coords_diagrama</c> ya no se aceptan.
    /// </summary>
    [HttpPost("{id:guid}/hallazgos")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<HallazgoCreadoDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Inspecciones.DTOInspecciones.CrearHallazgoRequest))]
    public async Task<IActionResult> AgregarHallazgo(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearHallazgoRequest>(ct, "parte_vehiculo", "coords_diagrama");
        return Creado(await servicio.AgregarHallazgoAsync(id, solicitud, ct));
    }

    /// <summary>
    /// Cierra la inspeccion como evidencia inmutable (409 si ya estaba cerrada). En Cargue y Descargue exige la foto de cada vista obligatoria
    /// (400 con el nombre de la que falta), una foto propia por hallazgo y que toda foto tenga ubicacion.
    /// </summary>
    [HttpPost("{id:guid}/cerrar")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<CerrarInspeccionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cerrar(Guid id, CancellationToken ct) => Ok(await servicio.CerrarAsync(id, ct));
}
