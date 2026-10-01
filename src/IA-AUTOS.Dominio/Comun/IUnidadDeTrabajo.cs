namespace IAAutos.Dominio.Comun;

/// <summary>Confirma en bloque los cambios de los repositorios (una transaccion por caso de uso).</summary>
public interface IUnidadDeTrabajo
{
    Task<int> GuardarCambiosAsync(CancellationToken ct = default);

    /// <summary>Descarta los cambios pendientes (por ejemplo, tras un fallo en una fila de una carga masiva).</summary>
    void DescartarCambios();
}
