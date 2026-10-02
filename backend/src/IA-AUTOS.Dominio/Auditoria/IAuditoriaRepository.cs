namespace IAAutos.Dominio.Auditoria;

public interface IAuditoriaRepository
{
    void Agregar(clsAuditLog registro);
}
