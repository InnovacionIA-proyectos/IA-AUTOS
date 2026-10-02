using IAAutos.Dominio.Auditoria;

namespace IAAutos.Aplicacion.Comun;

/// <summary>Registra acciones relevantes en tblAuditLog dentro de la misma transaccion del caso de uso.</summary>
public interface IAuditor
{
    void Registrar(string accion, string? entidad = null, Guid? entidadId = null, Guid? usuarioId = null);
}

internal sealed class clsAuditor(IAuditoriaRepository repositorio, IUsuarioActual actual, IReloj reloj) : IAuditor
{
    public void Registrar(string accion, string? entidad = null, Guid? entidadId = null, Guid? usuarioId = null) =>
        repositorio.Agregar(clsAuditLog.Crear(usuarioId ?? actual.Id, accion, entidad, entidadId, reloj.Ahora));
}
