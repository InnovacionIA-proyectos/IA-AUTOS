using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Conductor;
using IAAutos.Aplicacion.Conductor.DTOConductor;
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
public sealed class clsViajesController(
    clsViajesService servicio,
    clsCargaViajesService carga,
    clsTransferenciasService transferencias,
    clsNoSaleService noSale,
    clsConductorService conductor) : clsControladorBase
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

    /// <summary>
    /// Una pagina de viajes <c>{items, total, pagina, tamano}</c> (mas recientes primero por defecto). Filtros: <c>estado</c>, <c>codigo_mtrack</c>, <c>vin</c>, <c>conductor_id</c>, <c>inspector_id</c>, <c>cliente_id</c>,
    /// <c>origen_id</c>, <c>destino_id</c>, <c>desde</c> y <c>hasta</c>; <c>pagina</c>, <c>tamano</c> (50, maximo 200) y <c>orden</c> (<c>campo:asc|desc</c>). Inspectores y conductores ven solo sus viajes.
    /// <c>cliente_id</c> trae los viajes con algun VIN de ese cliente (solo el super administrador elige otro; el resto recibe siempre los de su cliente). Cada fila trae los clientes de sus VIN.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PaginaDto<ViajeResumenDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
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
        [FromQuery] string? pagina,
        [FromQuery] string? tamano,
        [FromQuery] string? orden,
        CancellationToken ct) =>
        Ok(await servicio.ListarAsync(
            new FiltrosViajesRequest(estado, codigoMtrack, vin, conductorId, inspectorId, clienteId, origenId, destinoId, desde, hasta, pagina, tamano, orden), ct));

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

    /// <summary>
    /// Marca que la unidad no sale del viaje por un hallazgo V1+ de una inspeccion cerrada (<c>motivo</c>, <c>hallazgo_id</c> y, si ya lo hay, <c>vin_reemplazo</c>). Solo el inspector del viaje
    /// y el coordinador de calidad (403 a los demas, incluidos admin y comercial). La unidad conserva sus etapas e inspecciones y el viaje sigue; responde con el viaje.
    /// </summary>
    [HttpPost("{id:guid}/vehiculos/{vin}/no-sale")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(MarcarNoSaleRequest))]
    public async Task<IActionResult> MarcarNoSale(Guid id, string vin, CancellationToken ct) =>
        Ok(await noSale.MarcarAsync(id, vin, await LeerCuerpoAsync<MarcarNoSaleRequest>(ct), ct));

    /// <summary>
    /// El conductor del viaje registra una novedad del recorrido de la unidad (<c>fecha</c>, <c>lat</c>, <c>lng</c>, <c>tipo_novedad</c>, <c>tamano_dano</c> y la <c>evidencia</c> con su foto sellada), sin texto libre
    /// (<c>nota</c> responde 400 «Campo no admitido»). Crea la salida de Transporte en borrador si aun no existe (409 ETAPA_PREVIA_SIN_CIERRE si el Cargue no esta cerrado), responde 201 con el hallazgo
    /// y avisa a los coordinadores de calidad y administradores del cliente del vehiculo. 403 si el viaje no es suyo; 409 UNIDAD_NO_SALE si la unidad no sale del viaje.
    /// </summary>
    [HttpPost("{id:guid}/vehiculos/{vin}/novedades")]
    [RolesPermitidos(clsRol.CONDUCTOR)]
    [CodigoError("ETAPA_PREVIA_SIN_CIERRE", "El Cargue de la unidad aun no esta cerrado")]
    [CodigoError("UNIDAD_NO_SALE", "La unidad se marco «no sale» del viaje: no admite novedades")]
    [ProducesResponseType<HallazgoCreadoDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(RegistrarNovedadRequest))]
    public async Task<IActionResult> RegistrarNovedad(Guid id, string vin, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<RegistrarNovedadRequest>(
            ct, "nota", "clase_dano", "nivel_severidad", "cuadrante_numero", "lado", "zona_provisional", "es_preexistente", "hallazgo_origen_id");
        return Creado(await conductor.RegistrarNovedadAsync(id, vin, solicitud, ct));
    }

    /// <summary>
    /// El conductor del viaje confirma la llegada de la unidad (<c>fecha</c>, <c>lat</c>, <c>lng</c>): cierra la salida de Transporte si esta abierta y crea y cierra la llegada. Responde 200 con
    /// <c>inspeccion</c>, <c>novedades</c> y <c>sin_novedad</c>; 400 sin GPS; 409 LLEGADA_YA_CERRADA si ya estaba confirmada (un reintento es un exito) y 409 UNIDAD_NO_SALE si la unidad no sale.
    /// </summary>
    [HttpPost("{id:guid}/vehiculos/{vin}/llegada")]
    [RolesPermitidos(clsRol.CONDUCTOR)]
    [CodigoError("LLEGADA_YA_CERRADA", "La llegada ya estaba confirmada (un reintento del conductor es un exito)")]
    [CodigoError("UNIDAD_NO_SALE", "La unidad se marco «no sale» del viaje: no admite llegada")]
    [ProducesResponseType<LlegadaDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(RegistrarLlegadaRequest))]
    public async Task<IActionResult> RegistrarLlegada(Guid id, string vin, CancellationToken ct) =>
        Ok(await conductor.RegistrarLlegadaAsync(id, vin, await LeerCuerpoAsync<RegistrarLlegadaRequest>(ct), ct));

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
