using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Reportes.DTOReportes;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Reportes;

/// <summary>
/// Los reportes del tablero (capacidad <c>ver_reportes</c>: coordinador de calidad, comercial, admin y super_admin; 403 el inspector y el conductor). Todos aceptan los filtros <c>desde</c> y
/// <c>hasta</c> (AAAA-MM-DD, dias de Colombia; por defecto los ultimos 30 dias), <c>operacion</c>, <c>cliente_id</c> (solo el super administrador; los demas reciben lo de su cliente y 403 si piden
/// otro), <c>punto_id</c> (origen o destino del viaje), <c>marca</c>, <c>trailer</c>, <c>proceso</c> y <c>nivel</c>; un filtro mal formado responde 400. Cuentan los hallazgos de inspecciones cerradas aunque
/// la unidad no haya terminado el viaje y calculan el cliente por el vehiculo.
/// </summary>
[Route("api/reportes")]
[RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.COMERCIAL, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
public sealed class clsReportesTableroController(clsReportesTableroService servicio) : clsControladorBase
{
    private static FiltrosReporteRequest Filtros(
        string? desde, string? hasta, string? operacion, string? clienteId, string? puntoId, string? marca, string? trailer, string? proceso, string? nivel, string? fecha = null) =>
        new(desde, hasta, operacion, clienteId, puntoId, marca, trailer, proceso, nivel, fecha);

    /// <summary>Cobertura: unidades despachadas (VIN de un viaje con la salida del Cargue cerrada, sin las que no salen) y las que tienen sus seis registros cerrados. Meta 90 %. Por operacion, cliente y punto.</summary>
    [HttpGet("cobertura")]
    [ProducesResponseType<CoberturaDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cobertura(
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] string? operacion,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "punto_id")] string? puntoId,
        [FromQuery] string? marca,
        [FromQuery] string? trailer,
        [FromQuery] string? proceso,
        [FromQuery] string? nivel,
        CancellationToken ct) =>
        Ok(await servicio.CoberturaAsync(Filtros(desde, hasta, operacion, clienteId, puntoId, marca, trailer, proceso, nivel), ct));

    /// <summary>Adopcion (formula propuesta, pendiente de confirmar por Calidad): de los VIN de los viajes programados cada dia, cuantos tienen una inspeccion de salida en el sistema. Meta 95 %. Por dia, operacion y cliente.</summary>
    [HttpGet("adopcion")]
    [ProducesResponseType<AdopcionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Adopcion(
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] string? operacion,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "punto_id")] string? puntoId,
        [FromQuery] string? marca,
        [FromQuery] string? trailer,
        [FromQuery] string? proceso,
        [FromQuery] string? nivel,
        CancellationToken ct) =>
        Ok(await servicio.AdopcionAsync(Filtros(desde, hasta, operacion, clienteId, puntoId, marca, trailer, proceso, nivel), ct));

    /// <summary>Tiempo de captura: la mediana, en minutos, entre el inicio y el cierre de las inspecciones cerradas de Cargue y Descargue, por proceso y por inspector.</summary>
    [HttpGet("tiempo-captura")]
    [ProducesResponseType<TiempoCapturaDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TiempoCaptura(
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] string? operacion,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "punto_id")] string? puntoId,
        [FromQuery] string? marca,
        [FromQuery] string? trailer,
        [FromQuery] string? proceso,
        [FromQuery] string? nivel,
        CancellationToken ct) =>
        Ok(await servicio.TiempoDeCapturaAsync(Filtros(desde, hasta, operacion, clienteId, puntoId, marca, trailer, proceso, nivel), ct));

    /// <summary>Averias por configuracion: por trailer, placa, conductor (con su cedula), tipo de unidad y marca, los VIN despachados, los que tienen novedad y los hallazgos; y los hallazgos por clase y por nivel.</summary>
    [HttpGet("averias")]
    [ProducesResponseType<AveriasDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Averias(
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] string? operacion,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "punto_id")] string? puntoId,
        [FromQuery] string? marca,
        [FromQuery] string? trailer,
        [FromQuery] string? proceso,
        [FromQuery] string? nivel,
        CancellationToken ct) =>
        Ok(await servicio.AveriasAsync(Filtros(desde, hasta, operacion, clienteId, puntoId, marca, trailer, proceso, nivel), ct));

    /// <summary>Patrones (mapa de calor): los hallazgos por proceso, por zona (lado + numero) y por zona y proceso, por nivel, clase y tamano, con la marca de umbral superado. Reemplaza a <c>/api/patrones</c> y <c>/api/patrones/analisis</c>.</summary>
    [HttpGet("patrones")]
    [ProducesResponseType<PatronesTableroDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Patrones(
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] string? operacion,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "punto_id")] string? puntoId,
        [FromQuery] string? marca,
        [FromQuery] string? trailer,
        [FromQuery] string? proceso,
        [FromQuery] string? nivel,
        CancellationToken ct) =>
        Ok(await servicio.PatronesAsync(Filtros(desde, hasta, operacion, clienteId, puntoId, marca, trailer, proceso, nivel), ct));

    /// <summary>
    /// Indicador diario: de las unidades despachadas el dia <c>fecha</c> (AAAA-MM-DD; por defecto <c>hasta</c> o hoy), el porcentaje que tuvo al menos un hallazgo V1 o V1+ ese dia, frente al objetivo del
    /// parametro <c>objetivo_diario_novedad</c>. Por operacion y cliente.
    /// </summary>
    [HttpGet("indicador-diario")]
    [ProducesResponseType<IndicadorDiarioDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> IndicadorDiario(
        [FromQuery] string? fecha,
        [FromQuery] string? hasta,
        [FromQuery] string? operacion,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "punto_id")] string? puntoId,
        [FromQuery] string? marca,
        [FromQuery] string? trailer,
        [FromQuery] string? proceso,
        [FromQuery] string? nivel,
        CancellationToken ct) =>
        Ok(await servicio.IndicadorDiarioAsync(Filtros(null, hasta, operacion, clienteId, puntoId, marca, trailer, proceso, nivel, fecha), ct));
}
