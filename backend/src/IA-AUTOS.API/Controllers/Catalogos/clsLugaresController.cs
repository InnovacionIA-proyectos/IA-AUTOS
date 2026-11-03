using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Catalogos;

/// <summary>
/// La ubicacion de los lugares fuera del CRUD del catalogo: «fijar ubicacion aqui» y las propuestas de autocalibracion. La carga manual de latitud, longitud y radio se hace por
/// <c>PATCH /api/catalogos/lugares/{id}</c> (admin, super_admin y coordinador_calidad).
/// </summary>
[Route("api/lugares")]
public sealed class clsLugaresController(clsLugaresService servicio) : clsControladorBase
{
    /// <summary>
    /// «Fijar ubicacion aqui»: quien esta en un lugar SIN ubicacion deja su posicion (<c>lat</c>, <c>lng</c> y la <c>precision</c> del GPS en metros) y el lugar queda «pendiente de confirmar»: todavia no genera marcas
    /// de «fuera del rango» para nadie. 400 sin coordenadas validas; 404 si el lugar no existe; 409 si ya tiene ubicacion (pendiente o confirmada).
    /// </summary>
    [HttpPost("{id:guid}/ubicacion")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<LugarUbicacionDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(FijarUbicacionRequest))]
    public async Task<IActionResult> FijarUbicacionAqui(Guid id, CancellationToken ct) =>
        Ok(await servicio.FijarUbicacionAquiAsync(id, await LeerCuerpoAsync<FijarUbicacionRequest>(ct), ct));

    /// <summary>Las propuestas de la autocalibracion (centro en la mediana de las fotos selladas y radio segun su dispersion), las mas recientes primero; <c>estado</c> filtra por pendiente, confirmada o descartada.</summary>
    [HttpGet("propuestas")]
    [RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<IReadOnlyList<PropuestaUbicacionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarPropuestas([FromQuery] string? estado, CancellationToken ct) => Ok(await servicio.ListarPropuestasAsync(estado, ct));

    /// <summary>Confirma la propuesta: el lugar queda con su centro y su radio como ubicacion confirmada. 404 si no existe; 409 si ya estaba decidida.</summary>
    [HttpPost("propuestas/{id:guid}/confirmar")]
    [RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<PropuestaUbicacionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmarPropuesta(Guid id, CancellationToken ct) => Ok(await servicio.ConfirmarPropuestaAsync(id, ct));

    /// <summary>Descarta la propuesta: el lugar no cambia. 404 si no existe; 409 si ya estaba decidida.</summary>
    [HttpPost("propuestas/{id:guid}/descartar")]
    [RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<PropuestaUbicacionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DescartarPropuesta(Guid id, CancellationToken ct) => Ok(await servicio.DescartarPropuestaAsync(id, ct));
}
