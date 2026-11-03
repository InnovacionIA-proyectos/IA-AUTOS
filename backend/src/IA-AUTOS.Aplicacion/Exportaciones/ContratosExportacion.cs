namespace IAAutos.Aplicacion.Exportaciones;

/// <summary>Una columna de un archivo de Excel: su titulo y su ancho aproximado en caracteres.</summary>
public sealed record ColumnaExcel(string Titulo, int Ancho);

/// <summary>
/// Genera un archivo .xlsx de una sola hoja. Cada celda puede ser un texto, un numero, una fecha con hora (<see cref="DateTimeOffset"/>, que se escribe en hora de Colombia), un si o no
/// (<see cref="bool"/>) o vacia (<c>null</c>).
/// </summary>
public interface IGeneradorExcel
{
    byte[] Generar(string hoja, IReadOnlyList<ColumnaExcel> columnas, IReadOnlyList<object?[]> filas);
}

/// <summary>Los datos de la unidad y del viaje que comparten los tres archivos: el VIN, la marca, el tipo de vehiculo, la operacion, el cliente, el viaje y la etapa.</summary>
public sealed record ContextoExportacion(
    string Vin,
    string Marca,
    string? TipoVehiculo,
    string Operacion,
    Guid ClienteId,
    string? CodigoMtrack,
    string? Placa,
    string? Trailer,
    string? Unidad,
    string? ConductorNombre,
    string? ConductorCedula,
    string Etapa,
    string? Lugar);

/// <summary>Un registro de la unidad en el viaje (una etapa y un momento): <c>Estado</c> es <c>sin_inspeccion</c>, <c>borrador</c> o <c>cerrada</c> y el resto es de la ultima version.</summary>
public sealed record FilaExportViaje(
    ContextoExportacion Contexto,
    string EstadoViaje,
    string EstadoUnidad,
    string? Origen,
    string? Destino,
    string Momento,
    int? Version,
    string Estado,
    DateTimeOffset? Inicio,
    DateTimeOffset? Cierre);

/// <summary>Una inspeccion (cada version) con quien la hizo, las firmas de su transferencia y cuantos extras del vehiculo reporto.</summary>
public sealed record FilaExportInspeccion(
    ContextoExportacion Contexto,
    string Momento,
    int Version,
    string Estado,
    DateTimeOffset Inicio,
    DateTimeOffset? Cierre,
    string? Inspector,
    string? TipoCierre,
    string? EntregaNombre,
    DateTimeOffset? EntregaFecha,
    string? EntregaIp,
    string? RecibeNombre,
    DateTimeOffset? RecibeFecha,
    string? RecibeIp,
    int Extras = 0);

/// <summary>Un hallazgo con la inspeccion donde se registro, su zona (lado + numero + nombre) y la fecha, hora y ubicacion de su foto.</summary>
public sealed record FilaExportHallazgo(
    ContextoExportacion Contexto,
    string Momento,
    int Version,
    string EstadoInspeccion,
    DateTimeOffset Inicio,
    DateTimeOffset? Cierre,
    string? ZonaLado,
    int? ZonaNumero,
    string? ZonaNombre,
    string? ZonaAiag,
    string? Clase,
    string Tamano,
    string? Nivel,
    bool EsPreexistente,
    string Origen,
    string EstadoSeguimiento,
    DateTimeOffset? FotoFecha,
    double? FotoLat,
    double? FotoLng);

/// <summary>El resultado de una consulta de exportacion: sus filas y si hubo mas que el maximo (en ese caso solo trae el maximo).</summary>
public sealed record ResultadoExportacion<T>(IReadOnlyList<T> Filas, bool Excedido);

/// <summary>Lectura de lo que se exporta, con los mismos filtros de los listados. La implementa la infraestructura.</summary>
public interface IConsultasExportaciones
{
    Task<ResultadoExportacion<FilaExportViaje>> ViajesAsync(Viajes.DTOViajes.FiltrosViajes filtros, Guid? soloDeCliente, int maximo, CancellationToken ct = default);

    Task<ResultadoExportacion<FilaExportInspeccion>> InspeccionesAsync(
        Inspecciones.DTOInspecciones.FiltrosInspecciones filtros, Guid? clienteId, int maximo, CancellationToken ct = default);

    Task<ResultadoExportacion<FilaExportHallazgo>> HallazgosAsync(Inspecciones.FiltrosHallazgos filtros, Guid? clienteId, int maximo, CancellationToken ct = default);
}
