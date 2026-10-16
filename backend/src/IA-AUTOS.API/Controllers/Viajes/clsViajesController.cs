using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Viajes;

/// <summary>
/// El viaje es el eje del modelo. Crearlos y administrarlos es del coordinador (y de los administradores); inspectores y conductores
/// solo ven los viajes en los que participan.
/// </summary>
[Route("api/viajes")]
public sealed class clsViajesController(clsViajesService servicio, clsCargaViajesService carga, clsTransferenciasService transferencias) : clsControladorBase
{
    /// <summary>Crea un viaje con su unidad, placa, trailer, conductor, codigo MTrack y, opcionalmente, sus VIN (se crean sus etapas). 409 si el codigo MTrack ya existe.</summary>
    [HttpPost]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(CrearViajeRequest))]
    public async Task<IActionResult> Crear(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearViajeRequest>(ct);
        return Creado(await servicio.CrearAsync(solicitud, ct));
    }

    /// <summary>Listado (maximo 200, mas recientes primero). Inspectores y conductores ven solo sus viajes.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ViajeResumenDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? estado,
        [FromQuery] string? codigoMtrack,
        [FromQuery] string? vin,
        [FromQuery] string? conductorId,
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        CancellationToken ct) =>
        Ok(await servicio.ListarAsync(new FiltrosViajesRequest(estado, codigoMtrack, vin, conductorId, desde, hasta), ct));

    /// <summary>Los viajes del usuario autenticado (como conductor o inspector) con sus VIN. Sin estado: los programados y en curso.</summary>
    [HttpGet("mios")]
    [ProducesResponseType<IReadOnlyList<ViajeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mios([FromQuery] string? estado, CancellationToken ct) =>
        Ok(await servicio.MiosAsync(estado, ct));

    /// <summary>Los usuarios del viaje que pueden firmar una transferencia (su conductor y su inspector activos): solo ellos aparecen al elegir los firmantes.</summary>
    [HttpGet("{id:guid}/firmantes")]
    [ProducesResponseType<IReadOnlyList<FirmanteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Firmantes(Guid id, CancellationToken ct) => Ok(await transferencias.FirmantesDelViajeAsync(id, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken ct) => Ok(await servicio.ObtenerAsync(id, ct));

    /// <summary>Cambia solo los campos enviados de un viaje programado o en curso; trailer, conductor, inspector, origen y destino admiten null para quitarlos.</summary>
    [HttpPatch("{id:guid}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(ActualizarViajeRequest))]
    public async Task<IActionResult> Actualizar(Guid id, CancellationToken ct) =>
        Ok(await servicio.ActualizarAsync(id, await LeerCamposAsync(ct), ct));

    /// <summary>Cambia el estado: programado a en_curso, en_curso a finalizado, o cancelar un viaje programado o en curso.</summary>
    [HttpPatch("{id:guid}/estado")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(CambiarEstadoViajeRequest))]
    public async Task<IActionResult> CambiarEstado(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CambiarEstadoViajeRequest>(ct);
        return Ok(await servicio.CambiarEstadoAsync(id, solicitud, ct));
    }

    /// <summary>Asigna VIN al viaje (se crean sus etapas). Todo o nada: si alguno no existe o ya esta en otro viaje activo, no se agrega ninguno.</summary>
    [HttpPost("{id:guid}/vehiculos")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(AgregarVinesRequest))]
    public async Task<IActionResult> AgregarVines(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<AgregarVinesRequest>(ct);
        return Ok(await servicio.AgregarVinesAsync(id, solicitud, ct));
    }

    /// <summary>Quita un VIN del viaje (se borran sus etapas). 409 si ya tiene inspecciones en el viaje.</summary>
    [HttpDelete("{id:guid}/vehiculos/{vin}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> QuitarVin(Guid id, string vin, CancellationToken ct) =>
        Ok(await servicio.QuitarVinAsync(id, vin, ct));

    /// <summary>Borra un viaje (con sus VIN y etapas) solo si no tiene inspecciones; si las tiene, 409 y se cancela en su lugar.</summary>
    [HttpDelete("{id:guid}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct)
    {
        await servicio.EliminarAsync(id, ct);
        return NoContent();
    }

    /// <summary>Vista previa de la carga masiva por Excel: valida cada fila y devuelve su error, sin guardar nada.</summary>
    [HttpPost("cargar/previsualizar")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ResultadoCargaViajesDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(CargarViajesRequest))]
    public async Task<IActionResult> PrevisualizarCarga(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CargarViajesRequest>(ct);
        return Ok(await carga.PrevisualizarAsync(solicitud, ct));
    }

    /// <summary>Confirma la carga masiva: solo se guardan los viajes de las filas validas; las invalidas quedan reportadas con su error.</summary>
    [HttpPost("cargar")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ResultadoCargaViajesDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(CargarViajesRequest))]
    public async Task<IActionResult> Cargar(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CargarViajesRequest>(ct);
        return Creado(await carga.CargarAsync(solicitud, ct));
    }
}
