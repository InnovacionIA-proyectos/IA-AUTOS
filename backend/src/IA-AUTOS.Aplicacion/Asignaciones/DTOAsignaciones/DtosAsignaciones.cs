namespace IAAutos.Aplicacion.Asignaciones.DTOAsignaciones;

public sealed record CrearAsignacionRequest(Guid? VehiculoId, string? Ninera, Guid? ConductorId);

public sealed record AsignacionDto(
    Guid Id,
    Guid VehiculoId,
    string Ninera,
    Guid? ConductorId,
    Guid AsignadoPor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
