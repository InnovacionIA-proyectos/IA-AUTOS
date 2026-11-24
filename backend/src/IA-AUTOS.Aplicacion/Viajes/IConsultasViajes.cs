using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Viajes.DTOViajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>Consultas de solo lectura de viajes y de su visibilidad por usuario.</summary>
public interface IConsultasViajes
{
    /// <summary>Los trailers distintos de los viajes del cliente (de todos si <paramref name="clienteId"/> es nulo), sin nulos ni vacios y por orden alfabetico.</summary>
    Task<IReadOnlyList<string>> TrailersAsync(Guid? clienteId, CancellationToken ct = default);

    /// <summary>
    /// Una pagina del listado (sin <paramref name="orden"/>, los mas recientes primero) y el total que cumple los filtros. Con <paramref name="soloDeUsuario"/> solo los viajes en los que participa ese
    /// usuario. Con <paramref name="soloDeCliente"/> solo los viajes de ese cliente (con algun VIN suyo, o sin VIN y creados por alguien de su cliente), con los conteos y los clientes calculados solo sobre
    /// sus VIN. <c>filtros.ClienteId</c> pide los viajes con algun VIN de ese cliente.
    /// </summary>
    Task<(IReadOnlyList<ViajeResumenDto> Filas, int Total)> ListarAsync(
        FiltrosViajes filtros, Guid? soloDeUsuario, Guid? soloDeCliente, PaginaSolicitada pagina, OrdenSolicitado? orden, CancellationToken ct = default);

    /// <summary>
    /// Viajes (con sus VIN) en los que el usuario es conductor o inspector del viaje, o inspector con un reclamo ACTIVO de entrega (de cualquier parada), en los estados indicados y sin repetir viajes.
    /// </summary>
    Task<IReadOnlyList<ViajeDto>> MiosAsync(Guid usuarioId, IReadOnlyCollection<string> estados, CancellationToken ct = default);

    /// <summary>
    /// El papel del inspector en cada uno de los viajes (<see cref="IAAutos.Dominio.Viajes.clsPapelEnElViaje"/>): carga si es el inspector del viaje o tiene el reclamo activo del cargue, recibe si tiene un reclamo
    /// activo de entrega. Los viajes en los que no hace ninguna de las dos cosas no salen en el resultado.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> PapelesDelInspectorAsync(Guid inspectorId, IReadOnlyCollection<Guid> viajeIds, CancellationToken ct = default);

    /// <summary>El viaje con sus VIN. Con <paramref name="soloDeCliente"/> solo los VIN de ese cliente (y solo esos clientes).</summary>
    Task<ViajeDto?> ObtenerAsync(Guid id, Guid? soloDeCliente = null, CancellationToken ct = default);

    /// <summary>True si el viaje es de ese cliente: tiene algun VIN suyo o, sin VIN, lo creo alguien de su cliente.</summary>
    Task<bool> ViajeEsDeClienteAsync(Guid viajeId, Guid clienteId, CancellationToken ct = default);

    /// <summary>True si el VIN esta en algun viaje no cancelado en el que el usuario es conductor o inspector.</summary>
    Task<bool> UsuarioVeVinAsync(string vin, Guid usuarioId, CancellationToken ct = default);
}
