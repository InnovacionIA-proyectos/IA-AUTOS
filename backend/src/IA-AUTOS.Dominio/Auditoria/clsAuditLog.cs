using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Auditoria;

/// <summary>Registro append-only de toda accion relevante. UsuarioId nulo indica una accion del sistema (ETL).</summary>
public sealed class clsAuditLog : clsEntidadBase
{
    private clsAuditLog()
    {
    }

    public Guid? UsuarioId { get; private set; }

    public string Accion { get; private set; } = string.Empty;

    public string? Entidad { get; private set; }

    public Guid? EntidadId { get; private set; }

    public static clsAuditLog Crear(Guid? usuarioId, string accion, string? entidad, Guid? entidadId, DateTimeOffset ahora)
    {
        var registro = new clsAuditLog
        {
            UsuarioId = usuarioId,
            Accion = clsValidador.Requerido(accion, "La acción es requerida"),
            Entidad = clsValidador.Opcional(entidad),
            EntidadId = entidadId,
        };
        registro.Inicializar(ahora);
        return registro;
    }
}
