namespace IAAutos.Aplicacion.Usuarios.DTOUsuarios;

/// <summary>Asigna uno o varios puntos a un inspector: <c>LugarIds</c> son ids de lugares activos del catalogo <c>lugares</c>.</summary>
public sealed record AsignarPuntosRequest(IReadOnlyList<Guid>? LugarIds);

/// <summary>Un punto del inspector: el lugar real (con su ciudad) y cuando se le asigno.</summary>
public sealed record PuntoDeInspectorDto(Guid LugarId, string? Lugar, string? Ciudad, DateTimeOffset AsignadoAt);

/// <summary>Los puntos de un inspector, por nombre de lugar.</summary>
public sealed record PuntosDeInspectorDto(Guid UsuarioId, IReadOnlyList<PuntoDeInspectorDto> Puntos);
