namespace IAAutos.Dominio.Notificaciones;

public interface INotificacionRepository
{
    /// <summary>La notificacion (para marcarla leida), o nulo si no existe.</summary>
    Task<clsNotificacion?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>De las claves recibidas, las que ya tienen una notificacion: una alerta con la misma clave no se repite.</summary>
    Task<IReadOnlySet<string>> ClavesExistentesAsync(IReadOnlyCollection<string> claves, CancellationToken ct = default);

    /// <summary>True si alguna notificacion de la novedad (la de cualquier destinatario o canal) ya tiene respuesta.</summary>
    Task<bool> NovedadRespondidaAsync(Guid hallazgoId, CancellationToken ct = default);

    /// <summary>
    /// Guarda la respuesta del coordinador en TODAS las notificaciones de esa novedad que aun no la tienen (la de cada coordinador y administrador, por cada canal): la
    /// novedad queda atendida para todos. Devuelve cuantas filas cambio; si es 0 otro usuario la respondio antes.
    /// </summary>
    Task<int> ResponderNovedadAsync(Guid hallazgoId, string texto, string medio, Guid usuarioId, DateTimeOffset ahora, CancellationToken ct = default);

    void Agregar(clsNotificacion notificacion);
}

public interface IDispositivoRepository
{
    /// <summary>El dispositivo con ese token (activo o no), o nulo.</summary>
    Task<clsDispositivo?> ObtenerPorTokenAsync(string tokenFcm, CancellationToken ct = default);

    /// <summary>Los dispositivos activos de esos usuarios.</summary>
    Task<IReadOnlyList<clsDispositivo>> ObtenerActivosDeUsuariosAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default);

    void Agregar(clsDispositivo dispositivo);
}
