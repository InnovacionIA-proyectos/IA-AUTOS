namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Una foto del PDF con su titulo y las lineas de su pie (sello: fecha, hora, ubicacion, sello del servidor y hash). <c>Contenido</c> es nulo si el archivo ya no esta en el almacen.</summary>
public sealed record FotoInforme(byte[]? Contenido, string Titulo, IReadOnlyList<string> Pie);

/// <summary>Un punto del poligono de una region, en coordenadas normalizadas de 0 a 1 (origen arriba a la izquierda).</summary>
public readonly record struct PuntoPanel(double X, double Y);

/// <summary>Una region resaltada sobre un panel: su poligono y la etiqueta que lleva (el numero del hallazgo o cuantos hay).</summary>
public sealed record RegionPanel(IReadOnlyList<PuntoPanel> Puntos, string Etiqueta);

/// <summary>Un panel del dibujo de danos: su nombre, su imagen y las regiones de las zonas con hallazgos.</summary>
public sealed record PanelInforme(string Nombre, byte[] Imagen, IReadOnlyList<RegionPanel> Regiones);

/// <summary>Un paso del historial de seguimiento de un hallazgo.</summary>
public sealed record SeguimientoInforme(DateTimeOffset Fecha, string Accion, string? EstadoAnterior, string? EstadoNuevo, string? Usuario, string? Nota, FotoInforme? Sello);

/// <summary>Un hallazgo del informe, con su numero (el de su etiqueta en el panel), su zona, su clase, su tamano, su nivel, sus fotos y su historial de seguimiento.</summary>
public sealed record HallazgoInforme(
    int Numero,
    string Zona,
    string? Clase,
    string Tamano,
    string? Nivel,
    string? NivelAccion,
    string Origen,
    bool EsPreexistente,
    string? EstadoSeguimiento,
    string? Nota,
    IReadOnlyList<FotoInforme> Fotos,
    IReadOnlyList<SeguimientoInforme> Seguimiento,
    IReadOnlyList<NotaInforme> NotasAclaratorias);

/// <summary>La transferencia de custodia del informe: como se cerro, las dos firmas, las reservas o la constancia de no firma (con su testigo y su foto).</summary>
public sealed record TransferenciaInforme(
    string? TipoCierre,
    DateTimeOffset? FirmadaAt,
    FirmaConstancia Entrega,
    FirmaConstancia Recibe,
    string? Reservas,
    string? NoFirmaParte,
    string? MotivoNoFirma,
    string? Testigo,
    FotoInforme? Constancia,
    IReadOnlyList<NotaInforme> NotasAclaratorias);

/// <summary>Una nota aclaratoria: quien la escribio, cuando y su texto.</summary>
public sealed record NotaInforme(DateTimeOffset Fecha, string Autor, string Texto);

/// <summary>Una unidad del viaje con su posicion en la ninera (nula si no la tiene), para el informe de la segunda inspeccion del cargue; <c>NoSale</c> marca la que no sale del viaje.</summary>
public sealed record UnidadAcomodacionInforme(string Vin, int? Posicion, bool NoSale);

/// <summary>Un extra del vehiculo del informe: como se nombra («Barras de techo», «Otro: portabicicletas»), su observacion y sus fotos selladas.</summary>
public sealed record ExtraInforme(string Nombre, string? Observacion, IReadOnlyList<FotoInforme> Fotos);

/// <summary>La acomodacion en la ninera del informe de la segunda inspeccion del cargue: cada unidad con su posicion y las fotos de la acomodacion.</summary>
public sealed record AcomodacionInforme(IReadOnlyList<UnidadAcomodacionInforme> Unidades, IReadOnlyList<FotoInforme> Fotos);

/// <summary>Todo lo que lleva el PDF del informe de una inspeccion. <c>Acomodacion</c> solo va en el informe de la segunda inspeccion del cargue.</summary>
public sealed record DatosInforme(
    string Vin,
    string Marca,
    string? Modelo,
    string? TipoVehiculo,
    string? Color,
    string? Operacion,
    string? Cliente,
    string? CodigoMtrack,
    string? Placa,
    string? Trailer,
    string? Unidad,
    string? Conductor,
    string? ConductorCedula,
    string Proceso,
    string? Lugar,
    string MomentoNombre,
    int Version,
    string Estado,
    DateTimeOffset IniciadaAt,
    DateTimeOffset? CerradaAt,
    string? Inspector,
    string? Observaciones,
    IReadOnlyList<FotoInforme> FotosDeVistas,
    IReadOnlyList<HallazgoInforme> Hallazgos,
    IReadOnlyList<PanelInforme> Paneles,
    IReadOnlyList<string> ZonasSinRegion,
    TransferenciaInforme? Transferencia,
    IReadOnlyList<NotaInforme> NotasAclaratorias,
    int FotosOmitidas,
    DateTimeOffset GeneradaAt,
    AcomodacionInforme? Acomodacion = null,
    IReadOnlyList<ExtraInforme>? Extras = null);

/// <summary>Genera el informe de una inspeccion en PDF, con las fotos y los paneles incrustados.</summary>
public interface IGeneradorPdfInforme
{
    byte[] Generar(DatosInforme datos);
}
