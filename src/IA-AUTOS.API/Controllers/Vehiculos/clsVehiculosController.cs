using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Vehiculos;

[Route("api/vehiculos")]
public sealed class clsVehiculosController(clsVehiculosService servicio) : clsControladorBase
{
    /// <summary>Carga masiva de VINs (maximo 5000 por solicitud) con su ruta Cargue, Transporte, Descargue.</summary>
    [HttpPost("cargar")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<ResultadoCargaDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Cargar(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CargarVinsRequest>(ct);
        return Creado(await servicio.CargarVinsAsync(solicitud, ct));
    }

    /// <summary>Autocompletado del inventario: fragmento de VIN o de marca (maximo 8 resultados).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<VehiculoBusquedaDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Buscar([FromQuery] string? vin, CancellationToken ct) =>
        Ok(await servicio.BuscarAsync(vin, ct));

    /// <summary>Listados del tablero: tipo = movilizados, pendientes o novedad (500 por pagina).</summary>
    [HttpGet("listado")]
    [ProducesResponseType<ListadoPaginadoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] string? tipo, [FromQuery] string? vin, [FromQuery] string? page, CancellationToken ct)
    {
        var pagina = int.TryParse(page, out var p) ? p : 1;
        return Ok(await servicio.ListarAsync(tipo, vin, pagina, ct));
    }

    /// <summary>Inventario del VIN en PDF, como descarga.</summary>
    [HttpGet("{vin}/inventario/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> InventarioPdf(string vin, CancellationToken ct) =>
        File(await servicio.ObtenerInventarioPdfAsync(vin, ct), "application/pdf", $"inventario-{vin}.pdf");

    [HttpGet("{vin}/inventario")]
    [ProducesResponseType<InventarioDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Inventario(string vin, CancellationToken ct) =>
        Ok(await servicio.ObtenerInventarioAsync(vin, ct));
}
