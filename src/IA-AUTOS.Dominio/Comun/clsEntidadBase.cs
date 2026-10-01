namespace IAAutos.Dominio.Comun;

/// <summary>Entidad con identidad y marcas de auditoria de fila (creacion y ultima modificacion).</summary>
public abstract class clsEntidadBase
{
    public Guid Id { get; protected set; }

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    protected void Inicializar(DateTimeOffset ahora)
    {
        Id = Guid.NewGuid();
        CreatedAt = ahora;
        UpdatedAt = ahora;
    }

    protected void Tocar(DateTimeOffset ahora) => UpdatedAt = ahora;
}
