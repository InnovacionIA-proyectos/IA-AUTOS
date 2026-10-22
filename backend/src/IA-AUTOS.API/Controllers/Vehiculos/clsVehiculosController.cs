using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
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
        File(await inventarioPdf.GenerarAsync(vin, ct), "application/pdf", $"inventario-{vin}.pdf");

    [HttpGet("{vin}/inventario")]
    [ProducesResponseType<InventarioDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Inventario(string vin, CancellationToken ct) =>
        Ok(await servicio.ObtenerInventarioAsync(vin, ct));
}
