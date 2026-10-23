using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Inspecciones;

[Route("api/inspecciones")]
public sealed class clsInspeccionesController(clsInspeccionesService servicio, clsNotasAclaratoriasService notas, clsInformePdfService informePdf) : clsControladorBase
{
    /// <summary>
    /// Crea la inspeccion de salida o de llegada de una etapa. El usuario sale del token, el estado inicial es siempre borrador y la placa, el
    /// trailer y el conductor salen del viaje de la etapa. La salida exige que la etapa anterior del VIN tenga cerrada su llegada (409
    /// ETAPA_PREVIA_SIN_CIERRE).
    /// </summary>
    [HttpPost]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [CodigoError("ETAPA_PREVIA_SIN_CIERRE", "La salida exige la llegada cerrada de la etapa anterior del VIN (details.etapa_previa dice cual)")]
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

    /// <summary>
    /// Una pagina de informes <c>{items, total, pagina, tamano}</c> (mas recientes primero por defecto). Filtros: <c>vin</c>, <c>viaje_id</c>, <c>usuario_id</c>, <c>momento</c>, <c>estado</c>, <c>proceso</c>, <c>operacion</c>,
    /// <c>marca</c>, <c>cliente_id</c> (solo el super administrador elige otro), <c>origen_id</c>, <c>destino_id</c>, <c>desde</c> y <c>hasta</c>; <c>pagina</c>, <c>tamano</c> (50, maximo 200) y <c>orden</c> (<c>campo:asc|desc</c>).
    /// Inspectores y conductores ven solo los informes de sus viajes.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PaginaDto<InspeccionListadoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? vin,
        [FromQuery(Name = "usuario_id")] string? usuarioId,
        [FromQuery(Name = "viaje_id")] string? viajeId,
        [FromQuery] string? momento,
        [FromQuery] string? estado,
        [FromQuery] string? proceso,
        [FromQuery] string? operacion,
        [FromQuery] string? marca,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "origen_id")] string? origenId,
        [FromQuery(Name = "destino_id")] string? destinoId,
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] string? pagina,
        [FromQuery] string? tamano,
        [FromQuery] string? orden,
        CancellationToken ct) =>
        Ok(await servicio.ListarAsync(
            new FiltrosInspeccionesRequest(vin, usuarioId, viajeId, momento, estado, proceso, operacion, marca, clienteId, origenId, destinoId, desde, hasta, pagina, tamano, orden), ct));

    /// <summary>Informe completo para revision: contexto, hallazgos, evidencias con URL y transferencia con firmas.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<InformeDetalleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct) => Ok(await servicio.ObtenerInformeAsync(id, ct));

    /// <summary>
    /// El informe de la inspeccion en PDF, como descarga: los datos de la unidad, el viaje y la etapa; las fotos de cada vista con su sello (fecha, hora, ubicacion, sello del servidor y hash); los
    /// hallazgos con su zona, clase, tamano, nivel y foto; el panel de danos con las zonas marcadas; la transferencia con sus firmas, reservas o constancia; las notas aclaratorias y el historial de
    /// seguimiento. Lo ve quien puede ver el informe (403 si es de otro cliente o de un viaje ajeno; el conductor, sin los hallazgos que no registro). Lleva hasta 60 fotos, primero las de los hallazgos.
    /// </summary>
    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var informe = await informePdf.GenerarAsync(id, ct);
        return File(informe.Contenido, "application/pdf", informe.Nombre);
    }

    /// <summary>
    /// Agrega una nota aclaratoria a la inspeccion (<c>texto</c> de hasta 1000 caracteres), tambien si ya esta cerrada: es un registro nuevo y el original no cambia. Solo el autor de la inspeccion,
    /// el administrador y el coordinador de calidad de su cliente (403 los demas). Nadie la edita ni la borra.
    /// </summary>
    [HttpPost("{id:guid}/notas")]
    [ProducesResponseType<NotaAclaratoriaDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(AgregarNotaRequest))]
    public async Task<IActionResult> AgregarNota(Guid id, CancellationToken ct) =>
        Creado(await notas.AgregarAInspeccionAsync(id, await LeerCuerpoAsync<AgregarNotaRequest>(ct), ct));

    /// <summary>Las notas aclaratorias de la inspeccion, de la mas antigua a la mas reciente. Las ve quien puede ver la inspeccion.</summary>
    [HttpGet("{id:guid}/notas")]
    [ProducesResponseType<IReadOnlyList<NotaAclaratoriaDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarNotas(Guid id, CancellationToken ct) => Ok(await notas.ListarDeInspeccionAsync(id, ct));

    /// <summary>
    /// Los hallazgos de las inspecciones cerradas anteriores del mismo VIN, del mas reciente al mas antiguo, con su viaje, proceso, momento, cierre y fotos. El cliente
    /// los descarga para proponer un hallazgo nuevo como preexistente (misma zona y clase) aun sin senal. No es para el conductor (403).
    /// </summary>
    [HttpGet("{id:guid}/hallazgos-previos")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<IReadOnlyList<HallazgoPrevioDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> HallazgosPrevios(Guid id, CancellationToken ct) => Ok(await servicio.ObtenerHallazgosPreviosAsync(id, ct));

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
