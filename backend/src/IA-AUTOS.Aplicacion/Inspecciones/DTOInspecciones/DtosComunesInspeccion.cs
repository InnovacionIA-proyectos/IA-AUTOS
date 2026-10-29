namespace IAAutos.Aplicacion.Inspecciones.DTOInspecciones;

/// <summary>Posicion marcada sobre el diagrama del vehiculo, en coordenadas normalizadas [0, 1].</summary>
public sealed record CoordsDiagramaDto(double X, double Y);

/// <summary>
/// Una inspeccion de salida o de llegada de una etapa. <c>Version</c> es 1 en la original y 2, 3... en las correcciones, que referencian
/// en <c>CorrigeInspeccionId</c> a la version anterior. <c>MomentoNombre</c> es el texto para pantalla («Inspección de salida»). <c>MotivoSinInspector</c> solo viene si el conductor hizo la salida
/// del cargue porque el inspector previsto no estaba, y <c>NovedadesProceso</c> trae lo que paso en el proceso con esa inspeccion (vacio en la gran mayoria).
/// </summary>
public sealed record InspeccionDto(
    Guid Id,
    Guid EtapaId,
    Guid ViajeId,
    Guid UsuarioId,
    string Momento,
    DateTimeOffset Fecha,
    double? Lat,
    double? Lng,
    string Estado,
    string? ConductorNombre,
    string? Placa,
    string? Trailer,
    string? Notas,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset IniciadaAt,
    DateTimeOffset? CerradaAt,
    int Version,
    Guid? CorrigeInspeccionId,
    string MomentoNombre,
    MotivoSinInspectorDto? MotivoSinInspector,
    IReadOnlyList<NovedadProcesoDto> NovedadesProceso);

/// <summary>
/// Un hallazgo con su zona: <c>CuadranteNumero</c> y <c>Lado</c> (o <c>ZonaProvisional</c>), <c>CuadranteNombre</c> y <c>Zona</c> ya armada para mostrar
/// («Izquierdo 7 · Puerta delantera», «Frente»). <c>NivelNombre</c> y <c>NivelAccion</c> salen del catalogo de niveles. <c>EvidenciaFaltante</c> dice que
/// le falta para tener la evidencia completa (<c>foto_hallazgo</c>, <c>transferencia_sin_cierre</c> o <c>transferencia_no_firma</c>) y
/// <c>EvidenciaCompleta</c> es verdadera cuando no falta nada. <c>ParteVehiculo</c> y <c>CoordsDiagrama</c> solo vienen en los hallazgos historicos. Al conductor solo se le muestra lo que ve en lectura (zona, clase, tamano, tipo de
/// novedad, origen, si es preexistente, fecha y foto): <c>EstadoSeguimiento</c>, <c>EvidenciaCompleta</c> y <c>EvidenciaFaltante</c> llegan nulos, y tambien el nivel, la nota, la revision y el origen.
/// </summary>
public record HallazgoDto(
    Guid Id,
    Guid InspeccionId,
    string? ParteVehiculo,
    string? ClaseDano,
    string TamanoDano,
    CoordsDiagramaDto? CoordsDiagrama,
    int? CuadranteNumero,
    string? Lado,
    string? ZonaProvisional,
    string? CuadranteNombre,
    string? Zona,
    string? TipoNovedad,
    string? NivelSeveridad,
    string? NivelNombre,
    string? NivelAccion,
    string Origen,
    string? Nota,
    bool EsPreexistente,
    Guid? HallazgoOrigenId,
    Guid? ConfirmadoPor,
    string? EstadoSeguimiento,
    bool? EvidenciaCompleta,
    IReadOnlyList<string>? EvidenciaFaltante,
    Guid? RevisadoPor,
    DateTimeOffset? RevisadoAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>El hallazgo recien guardado junto con la foto propia que se guardo con el.</summary>
public sealed record HallazgoCreadoDto : HallazgoDto
{
    public HallazgoCreadoDto(HallazgoDto hallazgo, EvidenciaDto evidencia)
        : base(hallazgo)
    {
        Evidencia = evidencia;
    }

    public EvidenciaDto Evidencia { get; init; }
}

/// <summary>
/// Un hallazgo de una inspeccion cerrada anterior del mismo VIN, con el contexto de donde salio: la inspeccion (<c>InspeccionId</c> ya viene en el
/// hallazgo), el viaje y su codigo, el proceso, el momento, cuando se cerro y sus fotos. Con ellos el cliente propone un hallazgo nuevo como preexistente
/// comparando zona (lado + numero) y clase.
/// </summary>
public sealed record HallazgoPrevioDto : HallazgoDto
{
    public HallazgoPrevioDto(
        HallazgoDto hallazgo,
        Guid viajeId,
        string? codigoMtrack,
        string proceso,
        string momento,
        DateTimeOffset cerradaAt,
        IReadOnlyList<EvidenciaDto> evidencias)
        : base(hallazgo)
    {
        ViajeId = viajeId;
        CodigoMtrack = codigoMtrack;
        Proceso = proceso;
        Momento = momento;
        CerradaAt = cerradaAt;
        Evidencias = evidencias;
    }

    public Guid ViajeId { get; init; }

    public string? CodigoMtrack { get; init; }

    public string Proceso { get; init; }

    public string Momento { get; init; }

    public DateTimeOffset CerradaAt { get; init; }

    public IReadOnlyList<EvidenciaDto> Evidencias { get; init; }
}

/// <summary>
/// La ruta interna de almacenamiento no se expone: el cliente recibe solo la URL servible. Cada foto trae su propio sello (fecha, ubicacion y
/// precision del dispositivo), el del servidor (<c>SelloServidorAt</c>) y el hash SHA-256 del archivo. <c>Url</c> es una URL absoluta firmada que vence en <c>UrlExpiraAt</c> (la firma la agrega la API al
/// responder, a partir de <c>RutaArchivo</c>, que no se expone); nula si el archivo no es de los que sirve esta API. <c>SinUbicacion</c> marca la foto
/// tomada sin senal de GPS (hay que repetirla antes de cerrar). <c>EsSello</c> marca la foto del sello de quien valida un hallazgo.
/// </summary>
public sealed record EvidenciaDto(
    Guid Id,
    Guid InspeccionId,
    Guid? HallazgoId,
    DateTimeOffset SelloFecha,
    double SelloLat,
    double SelloLng,
    string? Url,
    DateTimeOffset? UrlExpiraAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? VistaId,
    string? HashSha256,
    DateTimeOffset SelloServidorAt,
    double? Precision,
    string? DispositivoId,
    string? Nota,
    bool SinUbicacion,
    bool EsConstancia,
    bool EsSello,
    [property: System.Text.Json.Serialization.JsonIgnore] string? RutaArchivo = null,
    Guid? ExtraId = null);

/// <summary>
/// Un extra del vehiculo fuera del dibujo estandar que reporta una inspeccion: <c>Tipo</c> (codigo del catalogo <c>tipos-extra</c>) y su nombre, <c>Texto</c> (solo en «otro»), <c>Nombre</c> (como se muestra:
/// el del tipo, o «Otro: texto»), la observacion y sus fotos selladas (las evidencias con este <c>ExtraId</c>).
/// </summary>
public sealed record ExtraDto(
    Guid Id,
    Guid InspeccionId,
    string Tipo,
    string TipoNombre,
    string? Texto,
    string Nombre,
    string? Observacion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<EvidenciaDto> Fotos);

/// <summary>
/// Las firmas dibujadas no se exponen en los listados. <c>Firmada</c> es true cuando estan las dos firmas (tambien si fue con reservas);
/// <c>TipoCierre</c> dice como se cerro (<c>firmada</c>, <c>con_reservas</c>, <c>no_firma</c> o <c>recibo_sin_entrega</c>) y es nulo mientras la transferencia esta abierta;
/// en <c>recibo_sin_entrega</c> no hay <c>EntregaUsuarioId</c>.
/// </summary>
public sealed record TransferenciaDto(
    Guid Id,
    Guid InspeccionId,
    Guid? EntregaUsuarioId,
    Guid RecibeUsuarioId,
    bool Firmada,
    DateTimeOffset? FirmadaAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? TipoCierre,
    DateTimeOffset? EntregaFirmadaAt,
    DateTimeOffset? RecibeFirmadaAt,
    string? ReservasTexto,
    string? NoFirmaParte,
    string? MotivoNoFirma,
    Guid? TestigoUsuarioId,
    Guid? EvidenciaConstanciaId);
