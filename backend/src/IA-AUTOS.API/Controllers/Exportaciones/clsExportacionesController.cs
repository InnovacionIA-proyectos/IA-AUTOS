using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Exportaciones;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Exportaciones;

/// <summary>
/// Las exportaciones a Excel (capacidad <c>exportar_soportes</c>: coordinador de calidad, comercial, admin y super_admin; 403 los demas). Cada una acepta los mismos filtros del listado
/// correspondiente, trae solo los datos del cliente de quien la pide (el super administrador ve todos y elige un cliente) y se descarga con <c>Content-Disposition</c> con su nombre y la fecha.
/// </summary>
[Route("api/exportaciones")]
[RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.COMERCIAL, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
public sealed class clsExportacionesController(clsExportacionesService servicio) : clsControladorBase
{
    private const string XLSX = clsExportacionesService.TIPO_XLSX;

    private FileContentResult Descargar(ArchivoExportado archivo) => File(archivo.Contenido, archivo.TipoContenido, archivo.Nombre);

    /// <summary>
    /// Viajes en Excel: una fila por unidad (VIN), etapa y momento (salida o llegada) con los datos del viaje, el estado de la unidad y de su registro (<c>sin_inspeccion</c>, <c>borrador</c> o <c>cerrada</c>).
    /// Filtros del listado de viajes: <c>estado</c>, <c>codigo_mtrack</c>, <c>vin</c>, <c>conductor_id</c>, <c>inspector_id</c>, <c>cliente_id</c> (solo el super administrador), <c>origen_id</c>, <c>destino_id</c>,
    /// <c>desde</c> y <c>hasta</c>. Maximo 50000 filas.
    /// </summary>
    [HttpGet("viajes.xlsx")]
    [Produces(XLSX)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, XLSX)]
    public async Task<IActionResult> Viajes(
        [FromQuery] string? estado,
        [FromQuery(Name = "codigo_mtrack")] string? codigoMtrack,
        [FromQuery] string? vin,
        [FromQuery(Name = "conductor_id")] string? conductorId,
        [FromQuery(Name = "inspector_id")] string? inspectorId,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "origen_id")] string? origenId,
        [FromQuery(Name = "destino_id")] string? destinoId,
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        CancellationToken ct) =>
        Descargar(await servicio.ExportarViajesAsync(new FiltrosViajesRequest(estado, codigoMtrack, vin, conductorId, inspectorId, clienteId, origenId, destinoId, desde, hasta), ct));

    /// <summary>
    /// Inspecciones en Excel: una fila por inspeccion (cada version) con los datos del viaje, la etapa, el inspector y las firmas de la transferencia (quien, fecha, IP y tipo de cierre).
    /// Filtros del listado de inspecciones: <c>vin</c>, <c>usuario_id</c>, <c>viaje_id</c>, <c>momento</c>, <c>estado</c>, <c>proceso</c>, <c>operacion</c>, <c>marca</c>, <c>cliente_id</c> (solo el super administrador),
    /// <c>origen_id</c>, <c>destino_id</c>, <c>desde</c> y <c>hasta</c>. Maximo 50000 filas.
    /// </summary>
    [HttpGet("inspecciones.xlsx")]
    [Produces(XLSX)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, XLSX)]
    public async Task<IActionResult> Inspecciones(
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
        CancellationToken ct) =>
        Descargar(await servicio.ExportarInspeccionesAsync(
            new FiltrosInspeccionesRequest(vin, usuarioId, viajeId, momento, estado, proceso, operacion, marca, clienteId, origenId, destinoId, desde, hasta), ct));

    /// <summary>
    /// Hallazgos en Excel: una fila por hallazgo con los datos del viaje y la etapa, la zona (lado + numero + nombre), la clase, el tamano, el nivel, si es preexistente, su origen, su estado de seguimiento y la
    /// fecha, hora y ubicacion de su foto. Filtros del listado de hallazgos: <c>estado_seguimiento</c>, <c>origen</c>, <c>revisado</c>, <c>cliente_id</c> (solo el super administrador), <c>viaje_id</c>, <c>vin</c>,
    /// <c>inspeccion_id</c>, <c>nivel</c>, <c>cuadrante_numero</c>, <c>lado</c>, <c>proceso</c>, <c>desde</c> y <c>hasta</c>. Maximo 50000 filas.
    /// </summary>
    [HttpGet("hallazgos.xlsx")]
    [Produces(XLSX)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, XLSX)]
    public async Task<IActionResult> Hallazgos(
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
        CancellationToken ct) =>
        Descargar(await servicio.ExportarHallazgosAsync(
            new FiltrosHallazgosRequest(estadoSeguimiento, origen, revisado, clienteId, viajeId, vin, inspeccionId, nivel, cuadranteNumero, lado, proceso, desde, hasta, null, null, null), ct));
}
