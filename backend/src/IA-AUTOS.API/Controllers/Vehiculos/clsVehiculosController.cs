using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Vehiculos;

[Route("api/vehiculos")]
public sealed class clsVehiculosController(clsVehiculosService servicio, clsInventarioPdfService inventarioPdf) : clsControladorBase
{
    /// <summary>Carga masiva de VINs (maximo 5000 por solicitud) con su ruta Cargue, Transporte, Descargue.</summary>
    [HttpPost("cargar")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<ResultadoCargaDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Vehiculos.DTOVehiculos.CargarVinsRequest))]
    public async Task<IActionResult> Cargar(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CargarVinsRequest>(ct);
        return Creado(await servicio.CargarVinsAsync(solicitud, ct));
    }

    /// <summary>
    /// Baja logica del vehiculo: <c>activo = false</c> y responde <c>{ "modo": "inactivado" }</c>. Ya no se busca ni se agrega a un viaje, pero conserva sus etapas, inspecciones y evidencias. Solo el
    /// administrador, el super administrador y el coordinador de calidad (de un VIN de su cliente). 404 si no existe.
    /// </summary>
    [HttpDelete("{vin}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<BajaVehiculoResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DarDeBaja(string vin, CancellationToken ct) => Ok(await servicio.DarDeBajaAsync(vin, ct));

    /// <summary>Autocompletado del inventario: fragmento de VIN o de marca (maximo 8 resultados).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<VehiculoBusquedaDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Buscar([FromQuery] string? vin, CancellationToken ct) =>
        Ok(await servicio.BuscarAsync(vin, ct));

    /// <summary>
    /// Listado del tablero por paginas <c>{items, total, pagina, tamano}</c>. Filtros: <c>tipo</c> (<c>movilizados</c> por defecto, <c>pendientes</c> o <c>novedad</c>), <c>vin</c>, <c>operacion</c>, <c>marca</c>,
    /// <c>cliente_id</c> (solo el super administrador elige otro), <c>estado_unidad</c>, <c>activo</c>, <c>desde</c> y <c>hasta</c> (alta del vehiculo); <c>pagina</c>, <c>tamano</c> (50, maximo 200) y <c>orden</c>
    /// (<c>campo:asc|desc</c>).
    /// </summary>
    [HttpGet("listado")]
    [ProducesResponseType<PaginaDto<ListadoVehiculoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? tipo,
        [FromQuery] string? vin,
        [FromQuery] string? operacion,
        [FromQuery] string? marca,
        [FromQuery(Name = "cliente_id")] string? clienteId,
        [FromQuery(Name = "estado_unidad")] string? estadoUnidad,
        [FromQuery] string? activo,
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] string? pagina,
        [FromQuery] string? tamano,
        [FromQuery] string? orden,
        CancellationToken ct) =>
        Ok(await servicio.ListarAsync(new FiltrosVehiculosRequest(tipo, vin, operacion, marca, clienteId, estadoUnidad, activo, desde, hasta, pagina, tamano, orden), ct));

    /// <summary>Inventario del VIN en PDF, como descarga.</summary>
    [HttpGet("{vin}/inventario/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> InventarioPdf(string vin, CancellationToken ct) =>
        File(await inventarioPdf.GenerarAsync(vin, ct), "application/pdf", $"inventario-{vin}.pdf");

    [HttpGet("{vin}/inventario")]
    [ProducesResponseType<InventarioDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Inventario(string vin, CancellationToken ct) =>
        Ok(await servicio.ObtenerInventarioAsync(vin, ct));
}
