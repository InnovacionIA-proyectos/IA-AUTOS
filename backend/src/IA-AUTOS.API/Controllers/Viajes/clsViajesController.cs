using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Auth;
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
    clsEntregasService entregas,
    clsCargueNineraService cargueNinera,
    clsInformeNineraPdfService informeNinera,
    clsCambioCargueService cambioCargue,
    clsReclamosService reclamos,
    clsConductorService conductor,
    clsPermisosSinInspectorService permisosSinInspector) : clsControladorBase
{
    /// <summary>
    /// Crea un viaje con su unidad, placa, trailer, conductor, codigo MTrack, <c>fecha_programada</c> y <c>lugar_cargue_id</c> (obligatorios) y, opcionalmente, sus unidades (cada una con su <c>vin</c> y su
    /// <c>lugar_destino_id</c>; se crean sus etapas con el Descargue en el destino de la unidad). El tipo de origen se deduce del tipo del lugar de cargue. 400 si una unidad no trae destino y «Campo no admitido» con
    /// todo campo que no se declara (<c>origen_id</c>, <c>destino_id</c>, <c>vines</c>, <c>con_personal_apoyo</c>...). 409 si el codigo MTrack ya existe.
    /// </summary>
    [HttpPost]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(CrearViajeRequest))]
    public async Task<IActionResult> Crear(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoSoloAsync<CrearViajeRequest>(
            ct, "codigo_mtrack", "tipo_unidad_id", "placa", "trailer", "conductor_id", "inspector_id", "unidades", "fecha_programada", "lugar_cargue_id", "capacidad");
        return Creado(await servicio.CrearAsync(solicitud, ct));
    }

    /// <summary>
    /// Una pagina de viajes <c>{items, total, pagina, tamano}</c> (mas recientes primero por defecto). Filtros: <c>estado</c>, <c>codigo_mtrack</c>, <c>vin</c>, <c>conductor_id</c>, <c>inspector_id</c>, <c>cliente_id</c>,
    /// <c>origen_id</c>, <c>destino_id</c> (el tipo de destino del viaje, de los viajes anteriores), <c>destino_unidad_id</c> (un lugar de destino de alguna de sus unidades), <c>desde</c> y <c>hasta</c>; <c>pagina</c>,
    /// <c>tamano</c> (50, maximo 200) y <c>orden</c> (<c>campo:asc|desc</c>). Cada fila trae <c>destinos</c>: los destinos distintos de sus unidades con cuantas van a cada uno. Inspectores y conductores ven solo sus viajes.
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
        [FromQuery(Name = "destino_unidad_id")] string? destinoUnidadId,
        CancellationToken ct) =>
        Ok(await servicio.ListarAsync(
            new FiltrosViajesRequest(estado, codigoMtrack, vin, conductorId, inspectorId, clienteId, origenId, destinoId, desde, hasta, pagina, tamano, orden, destinoUnidadId), ct));

    /// <summary>
    /// La lista de pendientes del inspector: los cargues programados para HOY en sus puntos y las entregas de viajes en curso que llegan a sus puntos, cada uno con su estado (<c>disponible</c>, <c>en_revision</c> con el nombre del
    /// otro inspector, o <c>mio</c>). Los de otros dias y otros puntos no salen. De ahi se reclama con <c>POST /api/viajes/{id}/reclamos</c>.
    /// </summary>
    [HttpGet("pendientes")]
    [RolesPermitidos(clsRol.INSPECTOR)]
    [ProducesResponseType<IReadOnlyList<PendienteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pendientes(CancellationToken ct) => Ok(await reclamos.ListarPendientesAsync(ct));

    /// <summary>Los viajes del usuario autenticado (como conductor o inspector) con sus VIN. Sin estado: los programados y en curso.</summary>
    [HttpGet("mios")]
    [ProducesResponseType<IReadOnlyList<ViajeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mios([FromQuery] string? estado, CancellationToken ct) =>
        Ok(await servicio.MiosAsync(estado, ct));

    /// <summary>
    /// Las entregas del viaje, una por lugar de destino: las unidades que se entregan en cada punto y lo que falta de cada una (<c>sin_inspeccion</c>, <c>pendiente</c> o <c>cerrada</c>). Conductor e inspector
    /// solo ven las de sus viajes (403).
    /// </summary>
    [HttpGet("{id:guid}/entregas")]
    [ProducesResponseType<EntregasDelViajeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarEntregas(Guid id, CancellationToken ct) => Ok(await entregas.ListarAsync(id, ct));

    /// <summary>Las unidades que se entregan en un lugar del viaje y lo que falta de cada una (<c>pendientes</c> es «Aquí entregas estas N unidades»). 404 si ninguna unidad activa del viaje va a ese lugar.</summary>
    [HttpGet("{id:guid}/entregas/{lugar:guid}")]
    [ProducesResponseType<EntregaPuntoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ObtenerEntrega(Guid id, Guid lugar, CancellationToken ct) => Ok(await entregas.ObtenerPuntoAsync(id, lugar, ct));

    /// <summary>
    /// Firma la entrega del punto: la firma del usuario autenticado (quien entrega o quien recibe) se guarda en la transferencia de cada unidad pendiente del punto, con la misma firma, fecha e IP. Quien entrega es por
    /// defecto el conductor del viaje y quien recibe su inspector (se pueden indicar otros usuarios del viaje). 403 si no es el conductor ni el inspector del viaje; 409 si no hay unidades pendientes o alguna aun no tiene
    /// la inspeccion de llegada del Descargue.
    /// </summary>
    [HttpPost("{id:guid}/entregas/{lugar:guid}/firma")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR)]
    [ProducesResponseType<EntregaPuntoDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(FirmarEntregaRequest))]
    public async Task<IActionResult> FirmarEntrega(Guid id, Guid lugar, CancellationToken ct) =>
        Ok(await entregas.FirmarAsync(id, lugar, await LeerCuerpoAsync<FirmarEntregaRequest>(ct), Cliente(), ct));

    /// <summary>
    /// Firma el cargue de toda la niñera: una sola firma de cada parte (el inspector del viaje entrega, el conductor del viaje recibe) para todos los carros activos, con el trazo, la hora y el GPS del momento y, solo quien recibe,
    /// las reservas por carro. 400 con lo que falta; 403 si no es la parte del usuario; 409 <c>cargue_incompleto</c> si algun carro activo tiene la llegada o la salida del cargue sin cerrar (el mensaje nombra cual) o si el viaje no esta activo.
    /// Reenviar la misma firma no duplica nada.
    /// </summary>
    [HttpPost("{id:guid}/cargue/firma")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR)]
    [ProducesResponseType<CargueNineraDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(FirmarCargueRequest))]
    public async Task<IActionResult> FirmarCargue(Guid id, CancellationToken ct) =>
        Ok(await cargueNinera.FirmarAsync(id, await LeerCuerpoAsync<FirmarCargueRequest>(ct), Cliente(), ct));

    /// <summary>
    /// El informe de la niñera del viaje en PDF, como descarga, armado al pedirlo con los informes del cargue de sus carros (no se guarda aparte): los puestos con el VIN de cada uno, los daños de cada carro, las fotos de la
    /// niñera, la firma unica del cargue de cada parte con la hora y el GPS del momento en que firmo (o la constancia de no firma) y los carros que no salen marcados con su motivo. Lo ve quien puede ver el viaje (403 si es de otro
    /// cliente o de un viaje ajeno) y al conductor se le quitan el nivel y la nota de cada daño. Con la acomodacion sin cerrar o el cargue sin firmar sale igual, con lo que hay y avisandolo. Un carro que se baja del viaje
    /// deja de salir en el siguiente informe.
    /// </summary>
    [HttpGet("{id:guid}/ninera/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> InformeNinera(Guid id, CancellationToken ct)
    {
        var informe = await informeNinera.GenerarAsync(id, ct);
        return File(informe.Contenido, "application/pdf", informe.Nombre);
    }

    /// <summary>
    /// La constancia de no firma de toda la niñera: la deja la parte que si esta (nunca la que se nego, 403) con el motivo, un testigo con usuario, la foto y la hora y el GPS del momento; cierra como «no firma» el cargue de cada
    /// carro activo y avisa al coordinador de calidad una vez por viaje. 409 si la parte que se nego ya firmo o si el cargue esta incompleto.
    /// </summary>
    [HttpPost("{id:guid}/cargue/no-firma")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR)]
    [ProducesResponseType<CargueNineraDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(NoFirmaDelCargueRequest))]
    public async Task<IActionResult> RegistrarNoFirmaDelCargue(Guid id, CancellationToken ct) =>
        Ok(await cargueNinera.RegistrarNoFirmaAsync(id, await LeerCuerpoAsync<NoFirmaDelCargueRequest>(ct), Cliente(), ct));

    /// <summary>
    /// Cierra la entrega del punto con una sola firma y su <c>motivo</c> (obligatorio, hasta 300 caracteres): cada unidad pendiente queda como «recibo_sin_entrega» con la parte que no firmo, deja su novedad de proceso y
    /// los coordinadores de calidad reciben un aviso por entrega y punto. La <c>firma</c> solo hace falta si quien cierra aun no habia firmado. 403 si no es el conductor ni el inspector del viaje; 409 si la otra parte ya firmo.
    /// </summary>
    [HttpPost("{id:guid}/entregas/{lugar:guid}/una-firma")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR)]
    [ProducesResponseType<EntregaPuntoDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(CerrarEntregaUnaFirmaRequest))]
    public async Task<IActionResult> CerrarEntregaConUnaFirma(Guid id, Guid lugar, CancellationToken ct) =>
        Ok(await entregas.CerrarConUnaFirmaAsync(id, lugar, await LeerCuerpoAsync<CerrarEntregaUnaFirmaRequest>(ct), Cliente(), ct));

    /// <summary>
    /// El inspector reclama la revision del viaje («revisar este»): <c>proceso</c> <c>cargue</c> (un cargue programado para hoy en uno de sus puntos; queda como inspector del viaje) o <c>entrega</c> (un viaje que llega
    /// a uno de sus puntos). <c>201</c> con el reclamo; <c>200</c> si ya era suyo. 403 si el viaje no es de ninguno de sus puntos; 409 «Ya fue tomado por otro inspector» si otro lo reclamo antes (si dos lo hacen a la vez, gana uno).
    /// </summary>
    [HttpPost("{id:guid}/reclamos")]
    [RolesPermitidos(clsRol.INSPECTOR)]
    [ProducesResponseType<ReclamoDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(ReclamarViajeRequest))]
    public async Task<IActionResult> Reclamar(Guid id, CancellationToken ct)
    {
        var (reclamo, creado) = await reclamos.ReclamarAsync(id, await LeerCuerpoAsync<ReclamarViajeRequest>(ct), ct);
        return creado ? Creado(reclamo) : Ok(reclamo);
    }

    /// <summary>
    /// Libera el reclamo activo del proceso (<c>cargue</c> o <c>entrega</c>): lo libera el inspector que lo reclamo, el coordinador de calidad o un administrador (403 cualquier otro). El viaje queda sin inspector y lo
    /// capturado conserva su autor; queda registrado quien libero. 404 si no hay un reclamo activo. Ningun proceso libera reclamos por tiempo.
    /// </summary>
    [HttpDelete("{id:guid}/reclamos/{proceso}")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<ReclamoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LiberarReclamo(Guid id, string proceso, CancellationToken ct) => Ok(await reclamos.LiberarAsync(id, proceso, ct));

    /// <summary>Los reclamos del viaje (activos y liberados), del mas reciente al mas antiguo, con quien reclamo y quien libero.</summary>
    [HttpGet("{id:guid}/reclamos")]
    [ProducesResponseType<IReadOnlyList<ReclamoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarReclamos(Guid id, CancellationToken ct) => Ok(await reclamos.ListarAsync(id, ct));

    /// <summary>Los usuarios del viaje que pueden firmar una transferencia (su conductor y su inspector activos): solo ellos aparecen al elegir los firmantes.</summary>
    [HttpGet("{id:guid}/firmantes")]
    [ProducesResponseType<IReadOnlyList<FirmanteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Firmantes(Guid id, CancellationToken ct) => Ok(await transferencias.FirmantesDelViajeAsync(id, ct));

    /// <summary>
    /// Los trailers distintos de los viajes del cliente de quien pregunta, sin nulos ni vacios y por orden alfabetico (una lista de textos): el portal los ofrece en el filtro de trailer. <c>cliente_id</c> solo lo elige el
    /// super administrador (los demas reciben el suyo y 403 si piden otro).
    /// </summary>
    [HttpGet("trailers")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Trailers([FromQuery(Name = "cliente_id")] string? clienteId, CancellationToken ct) => Ok(await servicio.TrailersAsync(clienteId, ct));

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

    /// <summary>
    /// Cambia el <c>muelle_id</c> (uno activo del lugar de cargue del viaje; <c>null</c> lo quita) y el <c>trailer</c> durante el cargue. Lo hacen el inspector del viaje (o quien reclamo el cargue) y el coordinador de calidad (403 a conductor,
    /// admin y super admin; TI conserva <c>PATCH /api/viajes/{id}</c>). Al inspector se le acepta solo mientras la entrega del cargue no se haya cerrado (409 <c>cargue_firmado</c>); un muelle de otro lugar responde 400. Cuando lo cambia el
    /// inspector, el coordinador del cliente recibe un aviso <c>cambio_cargue_inspector</c> (urgencia media, solo bandeja).
    /// </summary>
    [HttpPatch("{id:guid}/cargue")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(CambiarCargueDelViajeRequest))]
    public async Task<IActionResult> CambiarCargue(Guid id, CancellationToken ct) =>
        Ok(await cambioCargue.CambiarAsync(id, await LeerCamposAsync(ct), ct));

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

    /// <summary>
    /// Asigna unidades al viaje, cada una con su <c>vin</c> y su <c>lugar_destino_id</c> (400 «La unidad requiere un destino» si falta): se crean sus etapas, con el Descargue en el destino de la unidad. Todo o
    /// nada: si alguna no existe o ya esta en otro viaje activo, no se agrega ninguna. <c>vines</c> ya no se acepta.
    /// </summary>
    [HttpPost("{id:guid}/vehiculos")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(AgregarUnidadesRequest))]
    public async Task<IActionResult> AgregarUnidades(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<AgregarUnidadesRequest>(ct, "vines");
        return Ok(await servicio.AgregarUnidadesAsync(id, solicitud, ct));
    }

    /// <summary>
    /// Cambia el lugar de destino de una unidad del viaje (<c>lugar_destino_id</c>, un lugar activo del catalogo <c>lugares</c>): solo se mueve el Descargue de esa unidad. 409 si la unidad no sale del viaje o si su
    /// Descargue ya tiene informe cerrado.
    /// </summary>
    [HttpPatch("{id:guid}/vehiculos/{vin}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(CambiarDestinoUnidadRequest))]
    public async Task<IActionResult> CambiarDestinoDeUnidad(Guid id, string vin, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CambiarDestinoUnidadRequest>(ct);
        return Ok(await servicio.CambiarDestinoDeUnidadAsync(id, vin, solicitud, ct));
    }

    /// <summary>
    /// El conductor del viaje pide el permiso de la excepcion «No hay inspector» cuando el punto de cargue tiene inspectores: <c>motivo_sin_inspector</c>, su texto si el motivo lo pide, la <c>fecha</c> y el GPS (<c>lat</c>,
    /// <c>lng</c>) del momento en que lo pide. Avisa al inspector del punto y al coordinador del cliente. 409 si el punto no tiene inspectores (ahi la excepcion se activa directamente); 403 si no es el conductor del viaje.
    /// </summary>
    [HttpPost("{id:guid}/permiso-sin-inspector")]
    [RolesPermitidos(clsRol.CONDUCTOR)]
    [ProducesResponseType<PermisoSinInspectorDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(SolicitarPermisoSinInspectorRequest))]
    public async Task<IActionResult> SolicitarPermisoSinInspector(Guid id, CancellationToken ct) =>
        Creado(await permisosSinInspector.SolicitarAsync(id, await LeerCuerpoAsync<SolicitarPermisoSinInspectorRequest>(ct), ct));

    /// <summary>La solicitud del permiso del viaje y su estado (pendiente o concedido, y quien lo concedio o si fue el reloj). 404 si no hay.</summary>
    [HttpGet("{id:guid}/permiso-sin-inspector")]
    [RolesPermitidos(clsRol.CONDUCTOR, clsRol.INSPECTOR, clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<PermisoSinInspectorDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ObtenerPermisoSinInspector(Guid id, CancellationToken ct) => Ok(await permisosSinInspector.ObtenerAsync(id, ct));

    /// <summary>El coordinador de calidad o un administrador concede el permiso: el conductor queda habilitado de inmediato, sin esperar al inspector. 404 si no hay solicitud; 409 si ya esta concedido.</summary>
    [HttpPost("{id:guid}/permiso-sin-inspector/conceder")]
    [RolesPermitidos(clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<PermisoSinInspectorDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConcederPermisoSinInspector(Guid id, CancellationToken ct) => Ok(await permisosSinInspector.ConcederAsync(id, ct));

    /// <summary>
    /// Registra la posicion de la unidad en la ninera (<c>posicion</c> de 1 a 7; <c>null</c> la libera) durante la segunda inspeccion del cargue. 400 «La posicion debe estar entre 1 y 7»; 409 «La posicion N ya esta
    /// ocupada por otra unidad de este viaje» (con dos solicitudes simultaneas por la misma, gana una), si la unidad no sale o si el viaje no tiene su segunda inspeccion del cargue en curso. Responde con el viaje.
    /// </summary>
    [HttpPut("{id:guid}/vehiculos/{vin}/posicion")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN, clsRol.SUPER_ADMIN)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(AsignarPosicionRequest))]
    public async Task<IActionResult> AsignarPosicion(Guid id, string vin, CancellationToken ct) =>
        Ok(await servicio.AsignarPosicionAsync(id, vin, await LeerCuerpoAsync<AsignarPosicionRequest>(ct), ct));

    /// <summary>
    /// Marca que la unidad no sale del viaje con un motivo del catalogo <c>motivos-no-sale</c> (<c>motivo_codigo</c>, <c>motivo_texto</c> obligatorio solo con <c>otro</c> y de hasta 300 caracteres, <c>hallazgo_id</c> obligatorio solo
    /// con los motivos de daño y, si ya lo hay, <c>vin_reemplazo</c>). Solo el inspector del viaje y el coordinador de calidad (403 a los demas, incluidos admin, super admin y comercial). La unidad conserva sus etapas e inspecciones
    /// y el viaje sigue; responde con el viaje. 400 «Motivo desconocido» o «Escribe el motivo»; 409 si el hallazgo no corresponde al motivo o si el motivo no aplica en el lugar de cargue (<c>motivo_no_aplica</c>).
    /// </summary>
    [HttpPost("{id:guid}/vehiculos/{vin}/no-sale")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(MarcarNoSaleRequest))]
    public async Task<IActionResult> MarcarNoSale(Guid id, string vin, CancellationToken ct) =>
        Ok(await noSale.MarcarAsync(id, vin, await LeerCuerpoAsync<MarcarNoSaleRequest>(ct), ct));

    /// <summary>
    /// «Vuelve a salir»: devuelve a la unidad que no sale a <c>activa</c>, sin posicion, y borra su marca (el historial queda en la auditoria). Solo el inspector del viaje y el coordinador de calidad (403 a los demas, incluidos
    /// admin y super admin). 409 <c>cargue_firmado</c> si la entrega del cargue del viaje ya se cerro, <c>tiene_reemplazo</c> si el carro ya tiene su reemplazo y <c>sin_cupo</c> si la niñera no tiene puesto libre.
    /// </summary>
    [HttpPost("{id:guid}/vehiculos/{vin}/vuelve-a-salir")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> VolverASalir(Guid id, string vin, CancellationToken ct) =>
        Ok(await noSale.VolverASalirAsync(id, vin, ct));

    /// <summary>
    /// «Bajar del viaje»: saca al carro del viaje (estado <c>bajado</c>) conservando su historia, con su <c>motivo</c> (obligatorio, hasta 300 caracteres). Solo el coordinador de calidad (403 a inspector, conductor, admin y super
    /// admin). Libera su puesto y deja de contar en la cobertura, las entregas y los pendientes; el mismo VIN se puede agregar a otro viaje. 409 <c>cargue_firmado</c> si la entrega del cargue del viaje o la de ese carro ya se cerro.
    /// </summary>
    [HttpPost("{id:guid}/vehiculos/{vin}/bajar")]
    [RolesPermitidos(clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ViajeDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(BajarDelViajeRequest))]
    public async Task<IActionResult> BajarDelViaje(Guid id, string vin, CancellationToken ct) =>
        Ok(await noSale.BajarAsync(id, vin, await LeerCuerpoAsync<BajarDelViajeRequest>(ct), ct));

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

    private ContextoCliente Cliente() =>
        new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
}
