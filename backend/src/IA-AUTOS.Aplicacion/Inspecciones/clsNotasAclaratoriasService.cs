using FluentValidation;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>Una nota aclaratoria con su autor (nombre y rol) y su fecha.</summary>
public sealed record NotaAclaratoriaDto(Guid Id, string Entidad, Guid EntidadId, Guid AutorId, string? AutorNombre, string? AutorRol, string Texto, DateTimeOffset CreatedAt);

/// <summary>El texto de la aclaracion (hasta 1000 caracteres).</summary>
public sealed record AgregarNotaRequest(string? Texto);

public sealed class AgregarNotaRequestValidator : AbstractValidator<AgregarNotaRequest>
{
    public AgregarNotaRequestValidator()
    {
        RuleFor(x => x.Texto).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("El texto de la nota es requerido");
        RuleFor(x => x.Texto).Must(t => t is null || t.Trim().Length <= clsNotaAclaratoria.TEXTO_MAX).WithMessage($"El texto admite máximo {clsNotaAclaratoria.TEXTO_MAX} caracteres");
    }
}

/// <summary>Lectura de las notas aclaratorias con el nombre y el rol de su autor. La implementa la infraestructura.</summary>
public interface IConsultasNotas
{
    /// <summary>Las notas de un registro, de la mas antigua a la mas reciente.</summary>
    Task<IReadOnlyList<NotaAclaratoriaDto>> ListarAsync(string entidad, Guid entidadId, CancellationToken ct = default);

    /// <summary>Las notas de varios registros del mismo tipo, agrupadas por registro (cada uno de la mas antigua a la mas reciente).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<NotaAclaratoriaDto>>> ListarPorEntidadesAsync(string entidad, IReadOnlyCollection<Guid> entidadIds, CancellationToken ct = default);
}

/// <summary>
/// Notas aclaratorias: una aclaracion es un registro nuevo que no cambia el original y que nadie edita ni borra. La agrega el autor del registro (el usuario de la inspeccion; para un hallazgo, el de su
/// inspeccion; para la transferencia, quien la firmo), un administrador o el coordinador de calidad de su cliente; cualquier otro recibe 403. Se admite con la inspeccion cerrada. Cada nota queda en la auditoria.
/// </summary>
public sealed class clsNotasAclaratoriasService(
    IInspeccionRepository inspecciones,
    IEtapaRepository etapas,
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    ITransferenciaCustodiaRepository transferencias,
    INotaAclaratoriaRepository notas,
    IConsultasNotas consultas,
    IUsuarioRepository usuarios,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<AgregarNotaRequest> validador)
{
    /// <summary>El registro que se aclara: su id (el de la inspeccion, el hallazgo o la transferencia), quienes son sus autores y de que viaje y cliente es.</summary>
    private sealed record Registro(string Entidad, Guid EntidadId, IReadOnlyList<Guid> Autores, clsViaje Viaje, Guid ClienteId);

    public Task<NotaAclaratoriaDto> AgregarAInspeccionAsync(Guid inspeccionId, AgregarNotaRequest solicitud, CancellationToken ct = default) =>
        AgregarAsync(clsEntidadNota.INSPECCION, inspeccionId, solicitud, ct);

    public Task<NotaAclaratoriaDto> AgregarAHallazgoAsync(Guid hallazgoId, AgregarNotaRequest solicitud, CancellationToken ct = default) =>
        AgregarAsync(clsEntidadNota.HALLAZGO, hallazgoId, solicitud, ct);

    /// <summary>La nota de la transferencia de la inspeccion indicada (404 si la inspeccion aun no tiene transferencia).</summary>
    public Task<NotaAclaratoriaDto> AgregarATransferenciaAsync(Guid inspeccionId, AgregarNotaRequest solicitud, CancellationToken ct = default) =>
        AgregarAsync(clsEntidadNota.TRANSFERENCIA, inspeccionId, solicitud, ct);

    public Task<IReadOnlyList<NotaAclaratoriaDto>> ListarDeInspeccionAsync(Guid inspeccionId, CancellationToken ct = default) =>
        ListarAsync(clsEntidadNota.INSPECCION, inspeccionId, ct);

    public Task<IReadOnlyList<NotaAclaratoriaDto>> ListarDeHallazgoAsync(Guid hallazgoId, CancellationToken ct = default) =>
        ListarAsync(clsEntidadNota.HALLAZGO, hallazgoId, ct);

    public Task<IReadOnlyList<NotaAclaratoriaDto>> ListarDeTransferenciaAsync(Guid inspeccionId, CancellationToken ct = default) =>
        ListarAsync(clsEntidadNota.TRANSFERENCIA, inspeccionId, ct);

    private async Task<NotaAclaratoriaDto> AgregarAsync(string entidad, Guid id, AgregarNotaRequest solicitud, CancellationToken ct)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var registro = await ResolverAsync(entidad, id, ct);
        var autor = actual.IdObligatorio();
        var esAutor = registro.Autores.Contains(autor);
        var esDeGestion = actual.Rol is clsRol.ADMIN or clsRol.SUPER_ADMIN or clsRol.COORDINADOR_CALIDAD;
        if (!esAutor && !esDeGestion)
        {
            throw new clsProhibidoException("Solo el autor del registro, el administrador y el coordinador de calidad agregan notas aclaratorias");
        }

        AsegurarCliente(registro);
        var nota = clsNotaAclaratoria.Crear(entidad, registro.EntidadId, autor, solicitud.Texto, reloj.Ahora);
        notas.Agregar(nota);
        await auditor.RegistrarDeViajeAsync(
            "nota.crear",
            "notas_aclaratorias",
            nota.Id,
            registro.Viaje.Id,
            new DetalleAuditoria(null, new { nota.Entidad, nota.EntidadId, nota.Texto }),
            registro.ClienteId,
            ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        var usuario = await usuarios.ObtenerPorIdAsync(autor, ct);
        return new NotaAclaratoriaDto(nota.Id, nota.Entidad, nota.EntidadId, nota.AutorId, usuario?.Nombre, usuario?.Rol, nota.Texto, nota.CreatedAt);
    }

    private async Task<IReadOnlyList<NotaAclaratoriaDto>> ListarAsync(string entidad, Guid id, CancellationToken ct)
    {
        var registro = await ResolverAsync(entidad, id, ct);
        AsegurarCliente(registro);
        // Inspector y conductor solo ven lo de sus viajes; el conductor tampoco ve los hallazgos que registraron otros.
        if (actual.SoloSusViajes() is { } usuarioViaje && !registro.Viaje.Participa(usuarioViaje))
        {
            throw new clsProhibidoException("El registro es de un viaje que no está asignado a su usuario");
        }

        if (entidad == clsEntidadNota.HALLAZGO && actual.Rol == clsRol.CONDUCTOR && !registro.Autores.Contains(actual.IdObligatorio()))
        {
            throw new clsProhibidoException("El conductor no ve los hallazgos que registraron otros");
        }

        return await consultas.ListarAsync(entidad, registro.EntidadId, ct);
    }

    private void AsegurarCliente(Registro registro)
    {
        if (actual.ClienteRestringido() is { } cliente && registro.ClienteId != cliente)
        {
            throw new clsProhibidoException("El registro es de un VIN de otro cliente");
        }
    }

    /// <summary>El registro aclarado, su viaje y su cliente. 404 si el hallazgo, la inspeccion o la transferencia no existen.</summary>
    private async Task<Registro> ResolverAsync(string entidad, Guid id, CancellationToken ct)
    {
        Guid inspeccionId;
        if (entidad == clsEntidadNota.HALLAZGO)
        {
            inspeccionId = (await inspecciones.ObtenerHallazgoAsync(id, ct) ?? throw new clsEntidadNoEncontradaException($"Hallazgo {id} no encontrado")).InspeccionId;
        }
        else
        {
            inspeccionId = id;
        }

        var inspeccion = await inspecciones.ObtenerPorIdAsync(inspeccionId, ct) ?? throw new clsEntidadNoEncontradaException($"Inspección {inspeccionId} no encontrada");
        var etapa = await etapas.ObtenerPorIdAsync(inspeccion.EtapaId, ct) ?? throw new clsEntidadNoEncontradaException("La etapa de la inspección no existe");
        var viaje = await viajes.ObtenerPorIdAsync(inspeccion.ViajeId, ct) ?? throw new clsEntidadNoEncontradaException("El viaje de la inspección no existe");
        var vehiculo = await vehiculos.ObtenerPorIdAsync(etapa.VehiculoId, ct) ?? throw new clsEntidadNoEncontradaException("El vehículo de la etapa no existe");

        if (entidad != clsEntidadNota.TRANSFERENCIA)
        {
            return new Registro(entidad, id, [inspeccion.UsuarioId], viaje, vehiculo.ClienteId);
        }

        var transferencia = await transferencias.ObtenerPorInspeccionAsync(inspeccionId, ct) ?? throw new clsEntidadNoEncontradaException("La inspección no tiene transferencia de custodia");
        return new Registro(entidad, transferencia.Id, Firmantes(transferencia), viaje, vehiculo.ClienteId);
    }

    /// <summary>Quienes firmaron la transferencia (quien entrega y quien recibe, cada uno solo si ya firmo su parte).</summary>
    private static List<Guid> Firmantes(clsTransferenciaCustodia transferencia)
    {
        var firmantes = new List<Guid>();
        if (transferencia.FirmaEntrega is not null && transferencia.EntregaUsuarioId is { } entrega)
        {
            firmantes.Add(entrega);
        }

        if (transferencia.FirmaRecibe is not null)
        {
            firmantes.Add(transferencia.RecibeUsuarioId);
        }

        return firmantes;
    }
}
