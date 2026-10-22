using IAAutos.Dominio.Auditoria;

namespace IAAutos.Aplicacion.Comun;

/// <summary>Registra acciones relevantes en tblAuditLog dentro de la misma transaccion del caso de uso.</summary>
public interface IAuditor
{
    /// <summary>
    /// Registra la accion con el usuario, la IP y el dispositivo de la solicitud en curso. <paramref name="detalle"/> es lo que cambio (nulo en las lecturas);
    /// <paramref name="viajeId"/> el viaje de lo auditado y <paramref name="clienteId"/> su cliente (si no se indica, el del usuario que actua).
    /// </summary>
    void Registrar(
        string accion,
        string? entidad = null,
        Guid? entidadId = null,
        Guid? usuarioId = null,
        DetalleAuditoria? detalle = null,
        Guid? viajeId = null,
        Guid? clienteId = null);

    /// <summary>
    /// Registra la accion sobre algo de un viaje: el cliente se toma de los vehiculos del viaje (no del usuario que actua), para que el administrador de ese cliente la vea
    /// aunque la haya hecho el super administrador.
    /// </summary>
    Task RegistrarDeViajeAsync(
        string accion,
        string entidad,
        Guid entidadId,
        Guid viajeId,
        DetalleAuditoria? detalle = null,
        Guid? clienteId = null,
        CancellationToken ct = default);
}

internal sealed class clsAuditor(
    IAuditoriaRepository repositorio,
    IUsuarioActual actual,
    IContextoSolicitud contexto,
    IOrigenAuditoria origen,
    IReloj reloj) : IAuditor
{
    private readonly Dictionary<Guid, Guid?> _clientesPorViaje = [];

    public void Registrar(
        string accion,
        string? entidad = null,
        Guid? entidadId = null,
        Guid? usuarioId = null,
        DetalleAuditoria? detalle = null,
        Guid? viajeId = null,
        Guid? clienteId = null) =>
        repositorio.Agregar(clsAuditLog.Crear(
            usuarioId ?? actual.Id,
            accion,
            entidad,
            entidadId,
            reloj.Ahora,
            contexto.Ip,
            contexto.Dispositivo,
            detalle?.ATexto(),
            viajeId,
            clienteId ?? actual.ClienteId));

    public async Task RegistrarDeViajeAsync(
        string accion,
        string entidad,
        Guid entidadId,
        Guid viajeId,
        DetalleAuditoria? detalle = null,
        Guid? clienteId = null,
        CancellationToken ct = default)
    {
        if (clienteId is null && !_clientesPorViaje.TryGetValue(viajeId, out clienteId))
        {
            clienteId = await origen.ClienteDelViajeAsync(viajeId, ct);
            _clientesPorViaje[viajeId] = clienteId;
        }

        Registrar(accion, entidad, entidadId, detalle: detalle, viajeId: viajeId, clienteId: clienteId);
    }
}
