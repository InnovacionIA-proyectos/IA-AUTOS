using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>Lo que se aclara con una nota: una inspeccion, un hallazgo o la transferencia de una inspeccion.</summary>
public static class clsEntidadNota
{
    public const string INSPECCION = "inspeccion";
    public const string HALLAZGO = "hallazgo";
    public const string TRANSFERENCIA = "transferencia";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { INSPECCION, HALLAZGO, TRANSFERENCIA };
}

/// <summary>
/// La aclaracion de un registro ya guardado: un registro NUEVO con su autor y su fecha. El original no cambia (aunque su inspeccion este cerrada) y la nota no se edita ni se borra (la base de datos lo
/// impide). <see cref="EntidadId"/> es el id de la inspeccion, del hallazgo o de la transferencia segun <see cref="Entidad"/>.
/// </summary>
public sealed class clsNotaAclaratoria
{
    public const int TEXTO_MAX = 1000;

    private clsNotaAclaratoria()
    {
    }

    public Guid Id { get; private set; }

    public string Entidad { get; private set; } = string.Empty;

    public Guid EntidadId { get; private set; }

    public Guid AutorId { get; private set; }

    public string Texto { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static clsNotaAclaratoria Crear(string entidad, Guid entidadId, Guid autorId, string? texto, DateTimeOffset ahora)
    {
        clsValidador.EnCatalogo(entidad, clsEntidadNota.VALORES, "entidad");
        var limpio = clsValidador.Opcional(texto) ?? throw clsErroresCampos.De("texto", "El texto de la nota es requerido");
        if (limpio.Length > TEXTO_MAX)
        {
            throw clsErroresCampos.De("texto", $"El texto admite máximo {TEXTO_MAX} caracteres");
        }

        return new clsNotaAclaratoria { Id = Guid.NewGuid(), Entidad = entidad, EntidadId = entidadId, AutorId = autorId, Texto = limpio, CreatedAt = ahora };
    }
}

public interface INotaAclaratoriaRepository
{
    void Agregar(clsNotaAclaratoria nota);
}
