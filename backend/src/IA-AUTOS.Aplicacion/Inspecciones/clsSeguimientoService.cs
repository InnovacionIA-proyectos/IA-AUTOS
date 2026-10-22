using FluentValidation;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Cuerpo de cambiar el seguimiento de un hallazgo: el estado nuevo, una nota y, si lo valido Sofasa, la foto del sello de quien valida.</summary>
public sealed record CambiarSeguimientoRequest(string? Estado, string? Nota, FotoEvidenciaRequest? EvidenciaSello);

/// <summary>
/// Una fila del historial de seguimiento de un hallazgo. <c>Detalle</c> es el JSON de lo asignado en una revision; <c>EvidenciaSello</c> la foto del sello de quien valida
/// (nula si no la hay).
/// </summary>
public sealed record SeguimientoDto(
    Guid Id,
    string Accion,
    string? EstadoAnterior,
    string? EstadoNuevo,
    Guid UsuarioId,
    string? UsuarioNombre,
    string? Nota,
    EvidenciaDto? EvidenciaSello,
    object? Detalle,
    DateTimeOffset CreatedAt);

public sealed class CambiarSeguimientoRequestValidator : AbstractValidator<CambiarSeguimientoRequest>
{
    public CambiarSeguimientoRequestValidator()
    {
        RuleFor(x => x.Estado).Must(e => e is not null && clsEstadoSeguimiento.ASIGNABLES.Contains(e))
            .WithMessage("Estado inválido: use validado, rechazado, recuperado o derogado");
        RuleFor(x => x.Nota).Must(n => n is null || n.Trim().Length <= clsHallazgoSeguimiento.NOTA_MAX)
            .WithMessage($"La nota admite máximo {clsHallazgoSeguimiento.NOTA_MAX} caracteres");
        RuleFor(x => x.EvidenciaSello!).SetValidator(new Validators.FotoEvidenciaRequestValidator()).When(x => x.EvidenciaSello is not null);
    }
}

/// <summary>
/// Recalcula si los hallazgos de una inspeccion tienen su evidencia completa (su foto propia y la transferencia cerrada firmada o con reservas) y lo guarda. Se usa al
/// cambiar el seguimiento de un hallazgo y al cerrarse la transferencia de su inspeccion.
/// </summary>
public sealed class clsEvidenciaDeHallazgos(IInspeccionRepository inspecciones, IReloj reloj)
{
    public async Task RecalcularAsync(Guid inspeccionId, string? tipoCierre, CancellationToken ct = default)
    {
        var hallazgos = await inspecciones.ObtenerHallazgosDeInspeccionAsync(inspeccionId, ct);
        if (hallazgos.Count == 0)
        {
            return;
        }

        var conFoto = await inspecciones.HallazgosConFotoAsync(hallazgos.Select(h => h.Id).ToList(), ct);
        foreach (var hallazgo in hallazgos)
        {
            hallazgo.CambiarEvidenciaCompleta(clsEvidenciaHallazgo.EstaCompleta(conFoto.Contains(hallazgo.Id), tipoCierre), reloj.Ahora);
        }
    }
}

/// <summary>
/// Seguimiento de los hallazgos: el coordinador los lleva a validado, rechazado, recuperado o derogado y cada cambio queda en un historial que no se modifica. Cada nivel tiene
/// su estado de llegada (V1+ recuperado, V1 rechazado, V2 validado por Sofasa, V3 derogado) que se expone en <c>nivel_accion</c>; el servidor no impide otro estado, el coordinador
/// decide. Marcar un hallazgo como rechazado no se bloquea por la evidencia: la respuesta dice que le falta.
/// </summary>
public sealed class clsSeguimientoService(
    IInspeccionRepository inspecciones,
    ITransferenciaCustodiaRepository transferencias,
    IConsultasInspecciones consultas,
    clsFabricaEvidencias fabricaEvidencias,
    clsEvidenciaDeHallazgos evidenciaDeHallazgos,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<CambiarSeguimientoRequest> validador)
{
    /// <summary>
    /// Cambia el estado de seguimiento del hallazgo y responde con el hallazgo ya actualizado. 409 si su inspeccion sigue en borrador o es una novedad del conductor
    /// que ningun inspector ha revisado; 400 si un hallazgo V2 se valida sin la foto del sello; 403 si es de un cliente distinto del de quien lo cambia.
    /// </summary>
    public async Task<HallazgoDto> CambiarAsync(Guid hallazgoId, CambiarSeguimientoRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var hallazgo = await ObtenerVisibleAsync(hallazgoId, ct);
        var inspeccion = await inspecciones.ObtenerPorIdAsync(hallazgo.InspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {hallazgo.InspeccionId} no encontrada");
        if (inspeccion.Estado != clsEstadoInspeccion.CERRADA)
        {
            throw new clsConflictoException("La inspección del hallazgo está en borrador: el seguimiento empieza cuando se cierra");
        }

        if (hallazgo.Origen == clsOrigenHallazgo.CONDUCTOR && hallazgo.NivelSeveridad is null)
        {
            throw new clsConflictoException("Es una novedad del conductor sin revisar: un inspector debe revisarla antes de darle seguimiento");
        }

        var estado = solicitud.Estado!;
        if (estado == clsEstadoSeguimiento.VALIDADO && hallazgo.NivelSeveridad == clsCodigosNivel.V2 && solicitud.EvidenciaSello is null)
        {
            throw clsErroresCampos.De("evidencia_sello", "Un hallazgo V2 se valida con la foto del sello de quien lo valida (Sofasa)");
        }

        clsEvidencia? sello = null;
        if (solicitud.EvidenciaSello is { } foto)
        {
            sello = await fabricaEvidencias.CrearSelloAsync(
                inspeccion.Id, foto.StoragePath!, foto.SelloFecha!.Value, foto.SelloLat, foto.SelloLng, foto.Precision, foto.DispositivoId, foto.Nota, foto.HashSha256, ct);
            inspecciones.Agregar(sello);
        }

        inspecciones.Agregar(clsHallazgoSeguimiento.CrearCambioDeEstado(
            hallazgo.Id, hallazgo.EstadoSeguimiento, estado, actual.IdObligatorio(), solicitud.Nota, sello?.Id, reloj.Ahora));
        await evidenciaDeHallazgos.RecalcularAsync(inspeccion.Id, (await transferencias.ObtenerPorInspeccionAsync(inspeccion.Id, ct))?.TipoCierre, ct);
        await auditor.RegistrarDeViajeAsync(
            "hallazgo.seguimiento",
            "hallazgos",
            hallazgo.Id,
            inspeccion.ViajeId,
            new DetalleAuditoria(
                new { estado_seguimiento = hallazgo.EstadoSeguimiento },
                new { estado_seguimiento = estado, solicitud.Nota, evidencia_sello_id = sello?.Id }),
            ct: ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await consultas.ObtenerHallazgoAsync(hallazgo.Id, ct) ?? throw new clsEntidadNoEncontradaException($"Hallazgo {hallazgoId} no encontrado");
    }

    /// <summary>El historial del hallazgo, del mas antiguo al mas reciente.</summary>
    public async Task<IReadOnlyList<SeguimientoDto>> HistorialAsync(Guid hallazgoId, CancellationToken ct = default)
    {
        await ObtenerVisibleAsync(hallazgoId, ct);
        return await consultas.ObtenerHistorialAsync(hallazgoId, ct);
    }

    /// <summary>El hallazgo, si existe y es del cliente de quien consulta (403 si es de otro cliente).</summary>
    private async Task<clsHallazgo> ObtenerVisibleAsync(Guid hallazgoId, CancellationToken ct)
    {
        var hallazgo = await inspecciones.ObtenerHallazgoAsync(hallazgoId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Hallazgo {hallazgoId} no encontrado");
        if (actual.ClienteRestringido() is { } cliente && !await consultas.EsDeClienteAsync(hallazgo.InspeccionId, cliente, ct))
        {
            throw new clsProhibidoException("El hallazgo es de un VIN de otro cliente");
        }

        return hallazgo;
    }
}
