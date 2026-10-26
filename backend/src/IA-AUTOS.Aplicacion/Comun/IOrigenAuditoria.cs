namespace IAAutos.Aplicacion.Comun;

/// <summary>Resuelve a que cliente pertenece lo que se audita, para que el administrador de un cliente vea solo la auditoria de lo suyo.</summary>
public interface IOrigenAuditoria
{
    /// <summary>El cliente de los vehiculos del viaje (nulo si el viaje aun no tiene vehiculos o no existe).</summary>
    Task<Guid?> ClienteDelViajeAsync(Guid viajeId, CancellationToken ct = default);
}
