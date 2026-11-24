using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Aplicacion.Catalogos;

/// <summary>
/// Un lugar con su ubicacion: <c>EstadoUbicacion</c> es sin_ubicacion, pendiente_confirmar o confirmada; <c>UbicacionFijadaPorNombre</c> es el nombre de quien la fijo (nulo si no aplica); <c>PrecisionM</c> es la del
/// GPS con que se fijo «aqui» (guardada en el lugar; nula en los lugares fijados antes de guardarla o cargados a mano).
/// </summary>
public sealed record LugarUbicacionDto(
    Guid Id, string Nombre, double? Latitud, double? Longitud, int? RadioMetros, string EstadoUbicacion, Guid? UbicacionFijadaPor, DateTimeOffset? UbicacionFijadaAt, double? PrecisionM = null, string? UbicacionFijadaPorNombre = null);

/// <summary>La posicion de quien esta en el lugar: <c>Lat</c> y <c>Lng</c> son obligatorias; <c>Precision</c> (metros) es la del GPS del dispositivo.</summary>
public sealed record FijarUbicacionRequest(double? Lat, double? Lng, double? Precision);

/// <summary>Una propuesta de la autocalibracion: el centro (mediana de las fotos) y el radio (segun su dispersion) que se proponen para el lugar, las fotos y las visitas que los sustentan y su decision (con el nombre de quien la decidio; nulo si esta pendiente). <c>PrecisionM</c> es la precision del GPS con que se fijo la ubicacion actual del lugar (nula si no se guardo).</summary>
public sealed record PropuestaUbicacionDto(
    Guid Id, Guid LugarId, string Lugar, double CentroLat, double CentroLng, int RadioMetros, int Fotos, int Visitas, string Estado, Guid? DecididaPor, DateTimeOffset? DecididaAt, DateTimeOffset CreatedAt, string? DecididaPorNombre = null, double? PrecisionM = null);

/// <summary>
/// La ubicacion de los lugares fuera del CRUD del catalogo: «fijar ubicacion aqui» (quien esta en un lugar sin ubicacion deja la suya, que queda pendiente de confirmar) y las propuestas de autocalibracion que el
/// coordinador de calidad confirma (el lugar queda con ese centro y radio como ubicacion confirmada) o descarta (el lugar no cambia). La carga manual de coordenadas y radio va por el catalogo <c>lugares</c>.
/// </summary>
public sealed class clsLugaresService(
    ILugarRepository lugares,
    IUsuarioRepository usuarios,
    IPropuestaUbicacionRepository propuestas,
    clsCatalogosService catalogos,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    clsEmisorNotificaciones emisor)
{
    /// <summary>
    /// Fija la ubicacion de un lugar SIN ubicacion con la posicion y la precision enviadas; queda «pendiente de confirmar» y todavia no genera marcas de «fuera del rango». 400 sin coordenadas validas o con precision
    /// negativa; 404 si el lugar no existe; 409 si el lugar ya tiene ubicacion (pendiente o confirmada). La precision queda guardada en el lugar y en la auditoria de la operacion, y se avisa (urgencia media, solo
    /// bandeja) a los coordinadores de calidad y administradores del cliente de quien la fijo.
    /// </summary>
    public async Task<LugarUbicacionDto> FijarUbicacionAquiAsync(Guid id, FijarUbicacionRequest solicitud, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        if (solicitud.Lat is null)
        {
            errores["lat"] = ["La latitud es requerida"];
        }

        if (solicitud.Lng is null)
        {
            errores["lng"] = ["La longitud es requerida"];
        }

        if (solicitud.Precision is { } precision && (precision < 0 || !double.IsFinite(precision)))
        {
            errores["precision"] = ["La precisión debe ser un número en metros mayor o igual a 0"];
        }

        if (errores.Count > 0)
        {
            throw new clsValidacionException("Datos inválidos", errores);
        }

        var lugar = await lugares.ObtenerAsync(id, ct) ?? throw new clsEntidadNoEncontradaException($"Lugar {id} no encontrado");
        var antes = Instantanea(lugar);
        lugar.FijarUbicacionAqui(solicitud.Lat!.Value, solicitud.Lng!.Value, actual.IdObligatorio(), reloj.Ahora, solicitud.Precision);
        auditor.Registrar("lugar.fijar_ubicacion", "lugares", lugar.Id, detalle: new DetalleAuditoria(antes, new { Instantanea(lugar).Latitud, Instantanea(lugar).Longitud, estado_ubicacion = lugar.EstadoUbicacion, precision_m = solicitud.Precision }));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        catalogos.InvalidarCache();
        var dto = await ConNombreAsync(Dto(lugar), ct);
        await AvisarUbicacionFijadaAsync(lugar, dto.UbicacionFijadaPorNombre, ct);
        return dto;
    }

    /// <summary>
    /// Avisa que se fijo la ubicacion de un lugar «aqui» (urgencia media, solo bandeja) a los coordinadores de calidad y administradores del cliente de quien la fijo: quien, cuando y con que precision. Un aviso por lugar.
    /// Nunca lanza: la ubicacion ya quedo guardada y no se deshace.
    /// </summary>
    private async Task AvisarUbicacionFijadaAsync(clsLugar lugar, string? quien, CancellationToken ct)
    {
        try
        {
            if (actual.ClienteId is not { } clienteId)
            {
                return;
            }

            var destinatarios = (await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], clienteId, ct)).Select(u => u.Id).ToList();
            var fijadaAt = lugar.UbicacionFijadaAt ?? reloj.Ahora;
            var cuando = fijadaAt.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var precision = lugar.PrecisionM is { } p ? $"precisión {p:0.#} m" : "sin precisión del GPS";
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.UBICACION_FIJADA,
                    clsUrgenciaNotificacion.MEDIA,
                    destinatarios,
                    clienteId,
                    null,
                    null,
                    null,
                    null,
                    $"Ubicación fijada: {lugar.Nombre}",
                    $"{quien ?? "Un usuario"} fijó la ubicación de {lugar.Nombre} el {cuando} ({precision}). Queda pendiente de confirmar.",
                    $"{clsTipoNotificacion.UBICACION_FIJADA}:{lugar.Id}",
                    new Dictionary<string, object?>
                    {
                        ["lugar_id"] = lugar.Id,
                        ["lugar"] = lugar.Nombre,
                        ["usuario_nombre"] = quien,
                        ["precision_m"] = lugar.PrecisionM,
                        ["fijada_at"] = fijadaAt,
                    }),
                ct);
        }
        catch (Exception)
        {
            // El aviso es un efecto aparte: la ubicacion ya quedo guardada y no se deshace.
        }
    }

    /// <summary>Las propuestas de autocalibracion, las mas recientes primero; con <paramref name="estado"/> (pendiente, confirmada o descartada) solo las de ese estado (400 si no es uno de los tres).</summary>
    public async Task<IReadOnlyList<PropuestaUbicacionDto>> ListarPropuestasAsync(string? estado, CancellationToken ct = default)
    {
        if (estado is not null && !clsEstadoPropuesta.VALORES.Contains(estado))
        {
            throw clsErroresCampos.De("estado", "El estado debe ser pendiente, confirmada o descartada");
        }

        var filas = await propuestas.ListarAsync(estado, ct);
        var nombres = await usuarios.NombresAsync(filas.Where(x => x.Propuesta.DecididaPor is not null).Select(x => x.Propuesta.DecididaPor!.Value).ToList(), ct);
        return filas.Select(x => Dto(x.Propuesta, x.LugarNombre, x.Propuesta.DecididaPor is { } id ? nombres.GetValueOrDefault(id) : null, x.PrecisionM)).ToList();
    }

    /// <summary>Confirma la propuesta: el lugar queda con su centro y su radio como ubicacion CONFIRMADA y la propuesta queda decidida con su autor y su fecha. 404 si no existe; 409 si ya estaba decidida.</summary>
    public async Task<PropuestaUbicacionDto> ConfirmarPropuestaAsync(Guid id, CancellationToken ct = default)
    {
        var propuesta = await propuestas.ObtenerAsync(id, ct) ?? throw new clsEntidadNoEncontradaException($"Propuesta {id} no encontrada");
        var lugar = await lugares.ObtenerAsync(propuesta.LugarId, ct) ?? throw new clsEntidadNoEncontradaException($"Lugar {propuesta.LugarId} no encontrado");
        var usuario = actual.IdObligatorio();
        var antes = Instantanea(lugar);
        propuesta.Confirmar(usuario, reloj.Ahora);
        lugar.FijarUbicacionConfirmada(propuesta.CentroLat, propuesta.CentroLng, propuesta.RadioMetros, usuario, reloj.Ahora);
        auditor.Registrar("lugar.confirmar_propuesta", "lugares", lugar.Id, detalle: new DetalleAuditoria(antes, Instantanea(lugar)));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        catalogos.InvalidarCache();
        return Dto(propuesta, lugar.Nombre, (await usuarios.NombresAsync([usuario], ct)).GetValueOrDefault(usuario));
    }

    /// <summary>Descarta la propuesta: el lugar no cambia. 404 si no existe; 409 si ya estaba decidida.</summary>
    public async Task<PropuestaUbicacionDto> DescartarPropuestaAsync(Guid id, CancellationToken ct = default)
    {
        var propuesta = await propuestas.ObtenerAsync(id, ct) ?? throw new clsEntidadNoEncontradaException($"Propuesta {id} no encontrada");
        var lugar = await lugares.ObtenerAsync(propuesta.LugarId, ct);
        var decide = actual.IdObligatorio();
        propuesta.Descartar(decide, reloj.Ahora);
        auditor.Registrar("lugar.descartar_propuesta", "lugares", propuesta.LugarId, detalle: new DetalleAuditoria(null, new { propuesta_id = propuesta.Id, propuesta.CentroLat, propuesta.CentroLng, propuesta.RadioMetros }));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return Dto(propuesta, lugar?.Nombre ?? string.Empty, (await usuarios.NombresAsync([decide], ct)).GetValueOrDefault(decide));
    }

    /// <summary>Completa el nombre de quien fijo la ubicacion (nulo si nadie la fijo).</summary>
    private async Task<LugarUbicacionDto> ConNombreAsync(LugarUbicacionDto dto, CancellationToken ct) =>
        dto.UbicacionFijadaPor is { } id ? dto with { UbicacionFijadaPorNombre = (await usuarios.NombresAsync([id], ct)).GetValueOrDefault(id) } : dto;

    private static LugarUbicacionDto Instantanea(clsLugar l) => Dto(l);

    private static LugarUbicacionDto Dto(clsLugar l) =>
        new(l.Id, l.Nombre, l.Latitud, l.Longitud, l.RadioMetros, l.EstadoUbicacion, l.UbicacionFijadaPor, l.UbicacionFijadaAt, l.PrecisionM);

    private static PropuestaUbicacionDto Dto(clsPropuestaUbicacion p, string lugar, string? decididaPorNombre, double? precisionM = null) =>
        new(p.Id, p.LugarId, lugar, p.CentroLat, p.CentroLng, p.RadioMetros, p.Fotos, p.Visitas, p.Estado, p.DecididaPor, p.DecididaAt, p.CreatedAt, decididaPorNombre, precisionM);
}
