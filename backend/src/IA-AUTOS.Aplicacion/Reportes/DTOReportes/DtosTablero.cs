namespace IAAutos.Aplicacion.Reportes.DTOReportes;

// Los reportes del tablero (GET /api/reportes/*), en snake_case como el resto de la API.

// ----------------------------------------------------------------------------------------------- cobertura

/// <summary>Unidades despachadas (un VIN de un viaje con la salida de su Cargue cerrada, sin las que no salen) y cuantas tienen sus seis registros cerrados.</summary>
public sealed record CoberturaGrupoDto(int Despachadas, int Completas, decimal Cobertura);

public sealed record CoberturaOperacionDto(string Operacion, int Despachadas, int Completas, decimal Cobertura);

public sealed record NovedadProcesoTipoDto(string Tipo, int Total);

public sealed record NovedadProcesoPuntoDto(Guid? PuntoId, string? PuntoNombre, int Total);

public sealed record NovedadProcesoClienteDto(Guid ClienteId, string? ClienteNombre, int Total);

public sealed record NovedadProcesoMesDto(string Mes, int Total);

/// <summary>
/// Las novedades de proceso (cargue sin inspector y recibo sin entrega) del periodo, para el tablero: el total y su conteo por tipo, por punto de custodia (el de la etapa), por cliente (el del
/// vehiculo) y por mes (AAAA-MM, hora de Colombia). No son hallazgos: no entran en los patrones ni en la tasa de rechazo.
/// </summary>
public sealed record NovedadesProcesoDto(
    int Total,
    IReadOnlyList<NovedadProcesoTipoDto> PorTipo,
    IReadOnlyList<NovedadProcesoPuntoDto> PorPunto,
    IReadOnlyList<NovedadProcesoClienteDto> PorCliente,
    IReadOnlyList<NovedadProcesoMesDto> PorMes);

public sealed record CoberturaClienteDto(Guid ClienteId, string? ClienteNombre, int Despachadas, int Completas, decimal Cobertura);

public sealed record CoberturaPuntoDto(Guid PuntoId, string? PuntoNombre, int Despachadas, int Completas, decimal Cobertura);

/// <summary>Cobertura = unidades con sus seis registros cerrados / unidades despachadas. <c>Meta</c> es el objetivo (90 %).</summary>
public sealed record CoberturaDto(
    int Despachadas,
    int Completas,
    decimal Cobertura,
    decimal Meta,
    IReadOnlyList<CoberturaOperacionDto> PorOperacion,
    IReadOnlyList<CoberturaClienteDto> PorCliente,
    IReadOnlyList<CoberturaPuntoDto> PorPunto);

// ------------------------------------------------------------------------------------------------ adopcion

public sealed record AdopcionDiaDto(DateOnly Fecha, int Programados, int ConSalidaDigital, decimal Adopcion);

public sealed record AdopcionOperacionDto(string Operacion, int Programados, int ConSalidaDigital, decimal Adopcion);

public sealed record AdopcionClienteDto(Guid ClienteId, string? ClienteNombre, int Programados, int ConSalidaDigital, decimal Adopcion);

/// <summary>La adopcion de un destino: <c>LugarId</c> y <c>Lugar</c> son nulos en lo que no tiene destino unico (las unidades de viajes anteriores sin destino y, por camion, los viajes que van a varios destinos).</summary>
public sealed record AdopcionDestinoDto(Guid? LugarId, string? Lugar, int Programados, int ConSalidaDigital, decimal Adopcion);

/// <summary>Una medida de adopcion con su numerador (<c>ConSalidaDigital</c>), su denominador (<c>Programados</c>) y su desglose por destino.</summary>
public sealed record AdopcionMedidaDto(int Programados, int ConSalidaDigital, decimal Adopcion, IReadOnlyList<AdopcionDestinoDto> PorDestino);

/// <summary>Adopcion (formula propuesta): de los VIN de los viajes programados, cuantos tienen una inspeccion de salida en el sistema. <c>Meta</c> es el objetivo (95 %).</summary>
public sealed record AdopcionDto(
    int Programados,
    int ConSalidaDigital,
    decimal Adopcion,
    decimal Meta,
    IReadOnlyList<AdopcionDiaDto> PorDia,
    IReadOnlyList<AdopcionOperacionDto> PorOperacion,
    IReadOnlyList<AdopcionClienteDto> PorCliente,
    AdopcionMedidaDto? PorCamion = null,
    AdopcionMedidaDto? PorCarro = null);

// ------------------------------------------------------------------------------------------ tiempo de captura

public sealed record TiempoProcesoDto(string Proceso, decimal MedianaMinutos, int Inspecciones);

public sealed record TiempoInspectorDto(Guid UsuarioId, string? Nombre, decimal MedianaMinutos, int Inspecciones);

/// <summary>Mediana, en minutos, entre el inicio y el cierre de las inspecciones cerradas de Cargue y Descargue. Nula si no hubo inspecciones.</summary>
public sealed record TiempoCapturaDto(decimal? MedianaMinutos, IReadOnlyList<TiempoProcesoDto> PorProceso, IReadOnlyList<TiempoInspectorDto> PorInspector);

// ----------------------------------------------------------------------------------------------- averias

/// <summary>Por cada configuracion: las unidades despachadas (<c>Vines</c>), las que tienen al menos un hallazgo (<c>ConNovedad</c>) y los hallazgos.</summary>
public sealed record AveriaTrailerDto(string? Trailer, int Vines, int ConNovedad, int Hallazgos);

public sealed record AveriaPlacaDto(string? Placa, int Vines, int ConNovedad, int Hallazgos);

public sealed record AveriaConductorDto(Guid? UsuarioId, string? Nombre, string? Cedula, int Vines, int ConNovedad, int Hallazgos);

public sealed record AveriaUnidadDto(Guid? TipoUnidadId, string? Unidad, int Vines, int ConNovedad, int Hallazgos);

public sealed record AveriaMarcaDto(string Marca, int Vines, int ConNovedad, int Hallazgos);

public sealed record ConteoClaseTableroDto(string? Clase, int Total);

public sealed record ConteoNivelTableroDto(string? Nivel, int Total);

public sealed record AveriasDto(
    IReadOnlyList<AveriaTrailerDto> PorTrailer,
    IReadOnlyList<AveriaPlacaDto> PorPlaca,
    IReadOnlyList<AveriaConductorDto> PorConductor,
    IReadOnlyList<AveriaUnidadDto> PorUnidad,
    IReadOnlyList<AveriaMarcaDto> PorMarca,
    IReadOnlyList<ConteoClaseTableroDto> PorClase,
    IReadOnlyList<ConteoNivelTableroDto> PorNivel,
    int SinCalificar = 0);

// ---------------------------------------------------------------------------------------------- patrones

/// <summary>Umbral de alerta de patrones: <c>Cantidad</c> hallazgos dentro de una ventana de <c>VentanaDias</c> dias.</summary>
public sealed record UmbralPatronDto(int Cantidad, int VentanaDias);

public sealed record PatronProcesoDto(string Proceso, int Total, bool SuperaUmbral);

/// <summary>
/// Una zona del vehiculo (lado + numero de cuadrante): <c>Nombre</c> es el del cuadrante y <c>Zona</c> el texto completo para mostrar. Hay dos grupos fijos sin cuadrante (<c>CuadranteNumero</c> y <c>Lado</c> nulos): los
/// danos de los extras del vehiculo (<c>Extra</c> verdadero, «Extras del vehículo») y los hallazgos sin zona en el dibujo («Sin zona en el dibujo»).
/// </summary>
public sealed record PatronRegionDto(int? CuadranteNumero, string? Lado, string? Nombre, string? Zona, int Total, bool SuperaUmbral, bool Extra = false);

/// <summary>Lo mismo que <see cref="PatronRegionDto"/> por proceso.</summary>
public sealed record PatronRegionProcesoDto(int? CuadranteNumero, string? Lado, string? Nombre, string? Zona, string Proceso, int Total, bool SuperaUmbral, bool Extra = false);

public sealed record PatronNivelDto(string? Nivel, int Total);

public sealed record PatronClaseDto(string? Clase, int Total);

public sealed record PatronTamanoDto(string Tamano, int Total);

/// <summary>Mapa de calor de los hallazgos de inspecciones cerradas por proceso y por zona. <c>SuperaUmbral</c> compara los hallazgos de la ventana del umbral (que termina en <c>hasta</c>) con la cantidad.</summary>
public sealed record PatronesTableroDto(
    int Total,
    IReadOnlyList<PatronProcesoDto> PorProceso,
    IReadOnlyList<PatronRegionDto> PorRegion,
    IReadOnlyList<PatronRegionProcesoDto> PorRegionProceso,
    IReadOnlyList<PatronNivelDto> PorNivel,
    IReadOnlyList<PatronClaseDto> PorClase,
    IReadOnlyList<PatronTamanoDto> PorTamano,
    UmbralPatronDto Umbral,
    int SinCalificar = 0);

// ------------------------------------------------------------------------------------------ indicador diario

public sealed record IndicadorOperacionDto(string Operacion, int Despachados, int ConNovedad, decimal Porcentaje, int SinCalificar = 0);

public sealed record IndicadorClienteDto(Guid ClienteId, string? ClienteNombre, int Despachados, int ConNovedad, decimal Porcentaje, int SinCalificar = 0);

/// <summary>
/// Vehiculos con novedad (al menos un hallazgo V1 o V1+ ese dia) sobre las unidades despachadas ese dia, frente al objetivo del parametro. <c>SinCalificar</c> son los danos sin nivel de esas unidades ese dia:
/// una cifra aparte que no se suma a <c>ConNovedad</c> ni al porcentaje, y tampoco se descarta.
/// </summary>
public sealed record IndicadorDiarioDto(
    DateOnly Fecha,
    int Despachados,
    int ConNovedad,
    decimal Porcentaje,
    decimal Objetivo,
    IReadOnlyList<IndicadorOperacionDto> PorOperacion,
    IReadOnlyList<IndicadorClienteDto> PorCliente,
    int SinCalificar = 0);
