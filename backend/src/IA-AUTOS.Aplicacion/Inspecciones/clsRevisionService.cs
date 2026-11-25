using System.Text.Json;
using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Inspecciones.Validators;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// La calificacion de un hallazgo sin nivel (el del conductor, el de un extra, cualquier otro): la zona (<c>cuadrante_numero</c> y, si el cuadrante lleva lado, <c>lado</c>), la clase y el nivel V; la zona es
/// obligatoria salvo que el hallazgo ya la tenga (la conserva) o sea el dano de un extra (no lleva zona), el tamano si hay que corregir el que eligio el conductor y una nota. Si el dano ya estaba registrado, <c>EsPreexistente</c> y <c>HallazgoOrigenId</c> (opcionales) lo unen al hallazgo previo del mismo VIN, con la misma
/// validacion de cuando se crea un hallazgo.
/// </summary>
public sealed record RevisarHallazgoRequest(
    int? CuadranteNumero,
    string? Lado,
    string? ClaseDano,
    string? NivelSeveridad,
    string? TamanoDano,
    string? Nota,
    bool? EsPreexistente = null,
    Guid? HallazgoOrigenId = null);

public sealed class RevisarHallazgoRequestValidator : AbstractValidator<RevisarHallazgoRequest>
{
    public RevisarHallazgoRequestValidator()
    {
        // La zona la pide el servicio solo cuando el hallazgo no la tiene (la conserva) ni es el dano de un extra: aqui solo se revisa su forma.
        RuleFor(x => x.CuadranteNumero).GreaterThanOrEqualTo(1).When(x => x.CuadranteNumero is not null)
            .WithMessage("El número del cuadrante debe ser un entero >= 1");
        RuleFor(x => x.Lado).Must(l => l is not null && clsLado.VALORES.Contains(l)).When(x => x.Lado is not null)
            .WithMessage("El lado debe ser izquierdo o derecho");
        RuleFor(x => x.ClaseDano).Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Clase de daño inválida");
        RuleFor(x => x.NivelSeveridad).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nivel de severidad es requerido");
        RuleFor(x => x.Nota).Must(n => n is null || n.Trim().Length <= clsHallazgo.NOTA_MAX)
            .WithMessage($"La nota admite máximo {clsHallazgo.NOTA_MAX} caracteres");
        // Un hallazgo preexistente siempre apunta al hallazgo previo del vehiculo que ya lo tenia (y solo ese lleva origen), como al crearlo.
        RuleFor(x => x.HallazgoOrigenId).NotNull().When(x => x.EsPreexistente == true)
            .WithMessage("Un hallazgo preexistente exige hallazgo_origen_id: el hallazgo previo del vehículo que ya tenía el daño");
        RuleFor(x => x.HallazgoOrigenId).Must(id => id is null).When(x => x.EsPreexistente != true)
            .WithMessage("hallazgo_origen_id solo se envía con es_preexistente verdadero");
        RuleFor(x => x.HallazgoOrigenId).Must(id => id is null || clsReglasComunes.UuidValido(id))
            .WithMessage(clsReglasComunes.MENSAJE_UUID);
    }
}

/// <summary>
/// Un inspector (de un viaje de la unidad) o un administrador califica el hallazgo sin nivel (el que registro el conductor en una zona o en un extra, o cualquier otro): lo ubica en su zona si la tiene, le da su
/// clase y le asigna el nivel V, que el conductor nunca asigna, y desde ahi el hallazgo entra al seguimiento y a los patrones. El coordinador de calidad tambien revisa, a distancia, los que el conductor registra en la salida de un Cargue sin personal
/// de apoyo o con la excepcion «No hay inspector» y en la llegada del Descargue que el conductor hace sin inspector en destino. Al revisar se puede confirmar que el dano ya estaba registrado
/// (preexistente, unido al hallazgo previo del mismo VIN). La revision queda en el historial del hallazgo y, si el nivel es critico, alerta al coordinador y al administrador del cliente.
/// </summary>
public sealed class clsRevisionService(
    IInspeccionRepository inspecciones,
    IEtapaRepository etapas,
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IConsultasViajes consultasViajes,
    IReclamoRepository reclamos,
    IConsultasInspecciones consultas,
    ICatalogosLectura catalogos,
    clsResolutorZona resolutorZona,
    clsAlertaHallazgoCritico alertaCritica,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<RevisarHallazgoRequest> validador)
{
    private static readonly JsonSerializerOptions JSON = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Revisa la novedad y responde con el hallazgo ya completo. 400 sin zona, clase o nivel validos; 403 si no es de su viaje o de su cliente (o, para el coordinador, si no es de un Cargue sin
    /// personal de apoyo o con la excepcion «No hay inspector»); 409 si el hallazgo no es del conductor y 409 HALLAZGO_YA_REVISADO si ya se reviso.
    /// </summary>
    public async Task<HallazgoDto> RevisarAsync(Guid hallazgoId, RevisarHallazgoRequest solicitud, CancellationToken ct = default)
    {
        await ValidarAsync(solicitud, ct);
        var hallazgo = await inspecciones.ObtenerHallazgoAsync(hallazgoId, ct) ?? throw new clsEntidadNoEncontradaException($"Hallazgo {hallazgoId} no encontrado");
        var inspeccion = await inspecciones.ObtenerPorIdAsync(hallazgo.InspeccionId, ct) ?? throw new clsEntidadNoEncontradaException($"Inspección {hallazgo.InspeccionId} no encontrada");
        var etapa = await etapas.ObtenerPorIdAsync(inspeccion.EtapaId, ct) ?? throw new clsEntidadNoEncontradaException("La etapa de la inspección no existe");
        var viaje = await viajes.ObtenerPorIdAsync(inspeccion.ViajeId, ct) ?? throw new clsEntidadNoEncontradaException("El viaje de la inspección no existe");
        var vehiculo = await vehiculos.ObtenerPorIdAsync(etapa.VehiculoId, ct) ?? throw new clsEntidadNoEncontradaException("El vehículo de la etapa no existe");
        await AsegurarPermisoAsync(vehiculo, hallazgo, inspeccion, etapa, viaje, ct);
        hallazgo.AsegurarRevisable();

        // Un dano con zona conserva la suya si la peticion no manda otra; el de un extra no lleva zona; los demas la piden.
        clsZonaDano? zona = null;
        if (solicitud.CuadranteNumero is { } cuadrante)
        {
            if (hallazgo.ExtraId is not null)
            {
                throw clsErroresCampos.De("cuadrante_numero", "El daño de un extra no lleva zona del dibujo");
            }

            zona = await resolutorZona.ResolverAsync(inspeccion.Id, cuadrante, solicitud.Lado, ct);
        }
        else if (hallazgo.ZonaId is null && hallazgo.ExtraId is null)
        {
            throw clsErroresCampos.De("cuadrante_numero", "La zona indicada no existe");
        }
        else if (solicitud.Lado is not null)
        {
            throw clsErroresCampos.De("lado", "El lado se envía junto con cuadrante_numero");
        }

        var zonaId = zona?.Id ?? hallazgo.ZonaId;
        var clase = solicitud.ClaseDano!.Trim();
        // El origen debe ser un hallazgo de una inspeccion cerrada anterior del mismo VIN con la misma zona y la misma clase que se asignan aqui.
        if (solicitud.HallazgoOrigenId is { } origenId)
        {
            if (zonaId is null)
            {
                throw clsErroresCampos.De("hallazgo_origen_id", "El daño de un extra no se une a un hallazgo previo: no tiene zona");
            }

            if (!await inspecciones.EsHallazgoPrevioDelVehiculoAsync(origenId, inspeccion.Id, zonaId.Value, clase, ct))
            {
                throw clsErroresCampos.De(
                    "hallazgo_origen_id", "El hallazgo de origen debe ser de una inspección cerrada anterior del mismo vehículo, con la misma zona y la misma clase de daño");
            }
        }

        var tamanoAnterior = hallazgo.TamanoDano;
        var antes = clsInstantaneasAuditoria.De(hallazgo);
        var tamano = clsValidador.Opcional(solicitud.TamanoDano);
        hallazgo.Revisar(zona?.Id, clase, solicitud.NivelSeveridad!.Trim(), tamano, solicitud.Nota, actual.IdObligatorio(), reloj.Ahora, solicitud.HallazgoOrigenId);

        inspecciones.Agregar(clsHallazgoSeguimiento.CrearRevision(hallazgo.Id, actual.IdObligatorio(), solicitud.Nota, Detalle(solicitud, zona, hallazgo, tamanoAnterior), reloj.Ahora));
        await auditor.RegistrarDeViajeAsync(
            "hallazgo.revision", "hallazgos", hallazgo.Id, viaje.Id, new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(hallazgo)), vehiculo.ClienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);

        var proceso = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == etapa.EtapaCatalogoId)?.Codigo ?? "la etapa";
        await alertaCritica.EmitirSiHayCriticosAsync([hallazgo], vehiculo, viaje.Id, proceso, ct);
        return await consultas.ObtenerHallazgoAsync(hallazgo.Id, ct) ?? throw new clsEntidadNoEncontradaException($"Hallazgo {hallazgoId} no encontrado");
    }

    /// <summary>
    /// El inspector califica los de un VIN de sus viajes; el administrador, los de su cliente; el coordinador de calidad, solo los daños del conductor de la salida de un Cargue sin personal de apoyo y de la llegada del Descargue. Quien se limita a su
    /// cliente no revisa los de otro.
    /// </summary>
    private async Task AsegurarPermisoAsync(clsVehiculo vehiculo, clsHallazgo hallazgo, clsInspeccion inspeccion, clsEtapa etapa, clsViaje viaje, CancellationToken ct)
    {
        if (actual.ClienteRestringido() is { } cliente && vehiculo.ClienteId != cliente)
        {
            throw new clsProhibidoException("El hallazgo es de un VIN de otro cliente");
        }

        switch (actual.Rol)
        {
            case clsRol.INSPECTOR when !await consultasViajes.UsuarioVeVinAsync(vehiculo.Vin, actual.IdObligatorio(), ct) && !await TieneLaParadaDelDanoAsync(vehiculo, hallazgo, inspeccion, etapa, viaje, ct):
                throw new clsProhibidoException("El VIN del hallazgo no pertenece a ninguno de sus viajes");
            case clsRol.COORDINADOR_CALIDAD when hallazgo.Origen != clsOrigenHallazgo.CONDUCTOR || !await EsInspeccionSinInspectorAsync(inspeccion, etapa, viaje, ct):
                throw new clsProhibidoException(
                    "El coordinador de calidad solo revisa los hallazgos del conductor en la salida de un Cargue sin personal de apoyo o con la excepción «No hay inspector», y en la llegada del Descargue que el conductor hace");
            case clsRol.INSPECTOR or clsRol.COORDINADOR_CALIDAD or clsRol.ADMIN or clsRol.SUPER_ADMIN:
                return;
            default:
                throw new clsProhibidoException("No tiene permisos para revisar hallazgos");
        }
    }

    /// <summary>
    /// El inspector que tiene el reclamo de la entrega de la parada donde esta el carro califica los danos que el conductor marco en su revision de llegada del Descargue (la que hizo con el plan B antes de que el inspector
    /// llegara), aunque no sea el inspector del viaje. Los demas danos del viaje siguen la regla de siempre.
    /// </summary>
    private async Task<bool> TieneLaParadaDelDanoAsync(clsVehiculo vehiculo, clsHallazgo hallazgo, clsInspeccion inspeccion, clsEtapa etapa, clsViaje viaje, CancellationToken ct)
    {
        if (hallazgo.Origen != clsOrigenHallazgo.CONDUCTOR || inspeccion.Momento != clsMomento.LLEGADA || inspeccion.UsuarioId != viaje.ConductorId)
        {
            return false;
        }

        var proceso = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == etapa.EtapaCatalogoId)?.Codigo;
        if (proceso != clsCodigosEtapa.DESCARGUE || (await viajes.ObtenerVehiculoAsync(viaje.Id, vehiculo.Id, ct))?.LugarDestinoId is not { } parada)
        {
            return false;
        }

        return await reclamos.ObtenerActivoAsync(viaje.Id, clsProcesoReclamo.ENTREGA, parada, ct) is { } reclamo && reclamo.InspectorId == actual.IdObligatorio();
    }

    /// <summary>
    /// La salida de un Cargue que hizo el conductor (con la excepcion «No hay inspector», que guarda su motivo; o una anterior a la excepcion) o la llegada del Descargue que hizo el conductor: en las dos
    /// no hay inspector que revise lo que el conductor registro.
    /// </summary>
    private async Task<bool> EsInspeccionSinInspectorAsync(clsInspeccion inspeccion, clsEtapa etapa, clsViaje viaje, CancellationToken ct)
    {
        var proceso = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == etapa.EtapaCatalogoId)?.Codigo;
        return (proceso == clsCodigosEtapa.CARGUE && inspeccion.Momento == clsMomento.SALIDA && (inspeccion.MotivoSinInspector is not null || viaje.ConductorId == inspeccion.UsuarioId))
            || (proceso == clsCodigosEtapa.DESCARGUE && inspeccion.Momento == clsMomento.LLEGADA && viaje.ConductorId == inspeccion.UsuarioId);
    }

    /// <summary>Valida el cuerpo y que la clase, el nivel y el tamano (si llega) existan en el catalogo y esten activos; los errores de ambas validaciones salen juntos.</summary>
    private async Task ValidarAsync(RevisarHallazgoRequest solicitud, CancellationToken ct)
    {
        var deCatalogo = new Dictionary<string, string[]>();
        if (!string.IsNullOrWhiteSpace(solicitud.ClaseDano)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.CLASES_DANO, soloActivos: true, ct)).Contains(solicitud.ClaseDano.Trim()))
        {
            deCatalogo["clase_dano"] = ["Clase de daño inválida"];
        }

        if (!string.IsNullOrWhiteSpace(solicitud.NivelSeveridad)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.NIVELES_SEVERIDAD, soloActivos: true, ct)).Contains(solicitud.NivelSeveridad.Trim()))
        {
            deCatalogo["nivel_severidad"] = ["Nivel de severidad inválido"];
        }

        if (!string.IsNullOrWhiteSpace(solicitud.TamanoDano)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.TAMANOS_DANO, soloActivos: true, ct)).Contains(solicitud.TamanoDano.Trim()))
        {
            deCatalogo["tamano_dano"] = ["Tamaño de daño inválido"];
        }

        try
        {
            await validador.ValidarOLanzarAsync(solicitud, ct);
        }
        catch (clsValidacionException ex) when (deCatalogo.Count > 0 && ex.Detalles is Dictionary<string, object> detalles
            && detalles.TryGetValue("fieldErrors", out var campos) && campos is Dictionary<string, string[]> porCampo)
        {
            foreach (var (campo, mensajes) in deCatalogo)
            {
                porCampo.TryAdd(campo, mensajes);
            }

            throw;
        }

        if (deCatalogo.Count > 0)
        {
            throw clsErroresCampos.Varios(deCatalogo);
        }
    }

    /// <summary>Lo asignado en la revision, como JSON para el historial: la zona, la clase, el nivel y, si se corrigio, el tamano que habia y el nuevo.</summary>
    private static string Detalle(RevisarHallazgoRequest solicitud, clsZonaDano? zona, clsHallazgo hallazgo, string tamanoAnterior) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["zona_id"] = zona?.Id ?? hallazgo.ZonaId,
                ["cuadrante_numero"] = zona?.Numero,
                ["lado"] = zona?.Lado,
                ["clase_dano"] = hallazgo.ClaseDano,
                ["nivel_severidad"] = hallazgo.NivelSeveridad,
                ["tamano_dano"] = hallazgo.TamanoDano,
                ["tamano_dano_anterior"] = hallazgo.TamanoDano == tamanoAnterior ? null : tamanoAnterior,
            }.Concat(hallazgo.EsPreexistente
                ? new Dictionary<string, object?> { ["es_preexistente"] = true, ["hallazgo_origen_id"] = hallazgo.HallazgoOrigenId, ["confirmado_por"] = hallazgo.ConfirmadoPor }
                : []).ToDictionary(x => x.Key, x => x.Value),
            JSON);
}
