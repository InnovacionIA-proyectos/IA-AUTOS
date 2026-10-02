using FluentValidation;
using IAAutos.Aplicacion.Asignaciones.DTOAsignaciones;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Asignaciones;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;

namespace IAAutos.Aplicacion.Asignaciones;

public sealed class CrearAsignacionRequestValidator : AbstractValidator<CrearAsignacionRequest>
{
    public CrearAsignacionRequestValidator()
    {
        RuleFor(x => x.VehiculoId).Must(v => v is not null && v != Guid.Empty).WithMessage("Debe ser un UUID válido");
        RuleFor(x => x.Ninera).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("La niñera es requerida");
    }
}

/// <summary>El administrador asigna un VIN a una ninera (placa) y, opcionalmente, a un conductor.</summary>
public sealed class clsAsignacionesService(
    IAsignacionRepository asignaciones,
    IVehiculoRepository vehiculos,
    IUsuarioRepository usuarios,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<CrearAsignacionRequest> validador)
{
    public async Task<AsignacionDto> CrearAsync(CrearAsignacionRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var vehiculoId = solicitud.VehiculoId!.Value;

        if (!await vehiculos.ExisteAsync(vehiculoId, ct))
        {
            throw new clsEntidadNoEncontradaException("El vehículo indicado no existe");
        }

        if (solicitud.ConductorId is { } conductorId)
        {
            var conductor = await usuarios.ObtenerPorIdAsync(conductorId, ct)
                ?? throw new clsEntidadNoEncontradaException("El conductor indicado no existe");
            if (conductor.Rol != clsRol.CONDUCTOR)
            {
                throw new clsValidacionException("El usuario indicado no es un conductor");
            }
        }

        var asignacion = clsAsignacion.Crear(vehiculoId, solicitud.Ninera!, solicitud.ConductorId, actual.IdObligatorio(), reloj.Ahora);
        asignaciones.Agregar(asignacion);
        auditor.Registrar("asignacion.crear", "asignaciones", asignacion.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new AsignacionDto(
            asignacion.Id,
            asignacion.VehiculoId,
            asignacion.Ninera,
            asignacion.ConductorId,
            asignacion.AsignadoPor,
            asignacion.CreatedAt,
            asignacion.UpdatedAt);
    }
}
