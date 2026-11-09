namespace IAAutos.Aplicacion.Viajes.DTOViajes;

/// <summary>
/// Una unidad en la entrega de un punto. <c>Estado</c>: <c>sin_inspeccion</c> (aun no tiene la inspeccion de llegada del Descargue, donde se firma), <c>pendiente</c> (se puede firmar o esta abierta) o
/// <c>cerrada</c> (su transferencia ya tiene cierre: <c>firmada</c>, <c>con_reservas</c>, <c>no_firma</c> o <c>recibo_sin_entrega</c>). Cada unidad conserva su propia transferencia.
/// <c>MotivoCierre</c> es el motivo escrito del cierre con una sola firma (<c>recibo_sin_entrega</c>) y es nulo en cualquier otro caso; <c>NoFirmaParte</c> dice cual parte no firmo (la otra es la que firmo).
/// <c>RecibeExternoNombre</c> y <c>RecibeExternoCedula</c> son los de la persona que recibio cuando no es un usuario (el punto no tiene inspector) y son nulos si recibio un usuario o si nadie recibio.
/// </summary>
public sealed record EntregaUnidadDto(
    Guid VehiculoId,
    string Vin,
    string Estado,
    Guid? InspeccionId,
    string? TipoCierre,
    DateTimeOffset? EntregaFirmadaAt,
    DateTimeOffset? RecibeFirmadaAt,
    string? NoFirmaParte,
    string? MotivoCierre = null,
    string? RecibeExternoNombre = null,
    string? RecibeExternoCedula = null);

/// <summary>Las unidades del viaje que se entregan en un lugar de destino, con lo que falta de cada una. <c>Entregada</c> es verdadero cuando ninguna esta pendiente.</summary>
public sealed record EntregaPuntoDto(
    Guid ViajeId,
    string ViajeEstado,
    Guid LugarId,
    string Lugar,
    string? Ciudad,
    int UnidadesTotal,
    int UnidadesPendientes,
    bool Entregada,
    IReadOnlyList<EntregaUnidadDto> Unidades);

/// <summary>Todas las entregas del viaje, una por lugar de destino. <c>UnidadesSinDestino</c> cuenta las unidades activas de viajes anteriores al destino por unidad (sin lugar).</summary>
public sealed record EntregasDelViajeDto(Guid ViajeId, string ViajeEstado, int UnidadesSinDestino, IReadOnlyList<EntregaPuntoDto> Puntos);

/// <summary>
/// La firma de una parte para todas las unidades pendientes de un punto. Quien entrega es por defecto el conductor del viaje y quien recibe, su inspector; en el cuerpo se pueden indicar otros usuarios del
/// viaje (como al firmar una transferencia). Cada unidad guarda esa firma, con su fecha e IP, en su propia transferencia.
/// En un punto SIN inspector asignado recibe una persona externa: <c>RecibeNombre</c>, <c>RecibeCedula</c> y <c>FirmaRecibe</c> (su firma) son obligatorios, <c>Firma</c> es la de quien entrega y la entrega queda cerrada
/// como «firmada». En un punto CON inspector asignado esos tres campos se rechazan: la firma el inspector.
/// </summary>
public sealed record FirmarEntregaRequest(
    string? Firma,
    Guid? EntregaUsuarioId,
    Guid? RecibeUsuarioId,
    string? RecibeNombre = null,
    string? RecibeCedula = null,
    string? FirmaRecibe = null);

/// <summary>
/// Cierra la entrega de un punto con una sola firma: el <c>motivo</c> es obligatorio (hasta 300 caracteres) y la <c>firma</c> solo hace falta si quien cierra aun no habia firmado. Quien cierra es quien
/// entrega o quien recibe; la otra parte no puede haber firmado.
/// </summary>
public sealed record CerrarEntregaUnaFirmaRequest(string? Firma, string? Motivo);
