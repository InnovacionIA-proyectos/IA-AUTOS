using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Asignaciones;
using IAAutos.Aplicacion.Asignaciones.DTOAsignaciones;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Asignaciones;

[Route("api")]
public sealed class clsAsignacionesController(clsAsignacionesService asignaciones, clsEtapasService etapas) : clsControladorBase
{
    /// <summary>El administrador asigna un VIN a una ninera (placa) y, opcionalmente, a un conductor.</summary>
    [HttpPost("asignaciones")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<AsignacionDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Asignaciones.DTOAsignaciones.CrearAsignacionRequest))]
    public async Task<IActionResult> CrearAsignacion(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearAsignacionRequest>(ct);
        return Creado(await asignaciones.CrearAsync(solicitud, ct));
    }

    /// <summary>El administrador define una etapa de la ruta de custodia de un VIN.</summary>
    [HttpPost("etapas")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<EtapaDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Vehiculos.CrearEtapaRequest))]
    public async Task<IActionResult> CrearEtapa(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearEtapaRequest>(ct);
        return Creado(await etapas.CrearAsync(solicitud, ct));
    }
}
