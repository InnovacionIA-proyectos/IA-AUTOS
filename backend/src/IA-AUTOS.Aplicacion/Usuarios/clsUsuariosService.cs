using FluentValidation;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Aplicacion.Usuarios;

/// <summary>Gestion de usuarios (alta, cambios, baja). La baja conserva el historial cuando existe.</summary>
public sealed class clsUsuariosService(
    IUsuarioRepository usuarios,
    IServicioContrasenas contrasenas,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<CrearUsuarioRequest> validadorCrear,
    IValidator<ActualizarUsuarioRequest> validadorActualizar)
{
    public async Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct = default) =>
        (await usuarios.ListarAsync(ct)).Select(clsMapeadorUsuarios.ADto).ToList();

    /// <summary>Lista reducida (id, nombre y rol) para usuarios que no son administradores.</summary>
    public async Task<IReadOnlyList<UsuarioListaDto>> ListarBasicoAsync(CancellationToken ct = default) =>
        (await usuarios.ListarAsync(ct)).Where(u => u.Activo).Select(u => new UsuarioListaDto(u.Id, u.Nombre, u.Rol)).ToList();

    public async Task<UsuarioDto> CrearAsync(CrearUsuarioRequest solicitud, CancellationToken ct = default)
    {
        await validadorCrear.ValidarOLanzarAsync(solicitud, ct);
        var usuario = clsUsuario.Crear(
            solicitud.Nombre!,
            solicitud.Email!,
            solicitud.Rol!,
            solicitud.SubtipoInspector,
            contrasenas.Hashear(solicitud.Password!),
            reloj.Ahora,
            solicitud.Cedula);
        usuarios.Agregar(usuario);
        auditor.Registrar("usuario.crear", "usuarios", usuario.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorUsuarios.ADto(usuario);
    }

    public async Task<UsuarioDto> ActualizarAsync(Guid id, ActualizarUsuarioRequest solicitud, CancellationToken ct = default)
    {
        await validadorActualizar.ValidarOLanzarAsync(solicitud, ct);
        var usuario = await usuarios.ObtenerPorIdAsync(id, ct)
            ?? throw new clsEntidadNoEncontradaException("Usuario no encontrado");

        usuario.Actualizar(
            solicitud.Nombre,
            solicitud.Email,
            solicitud.Rol,
            solicitud.SubtipoInspector,
            solicitud.Password is null ? null : contrasenas.Hashear(solicitud.Password),
            solicitud.Activo,
            reloj.Ahora,
            solicitud.Cedula);
        auditor.Registrar("usuario.actualizar", "usuarios", usuario.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorUsuarios.ADto(usuario);
    }

    /// <summary>
    /// Sin operaciones asociadas se borra de verdad; si ya realizo operaciones se marca como retirado
    /// (activo = false) para conservar su historial. Bloquea la auto-eliminacion.
    /// </summary>
    public async Task<EliminarUsuarioResponse> EliminarAsync(Guid id, CancellationToken ct = default)
    {
        var actorId = actual.IdObligatorio();
        if (id == actorId)
        {
            throw new clsConflictoException("No puedes eliminar tu propio usuario");
        }

        var usuario = await usuarios.ObtenerPorIdAsync(id, ct)
            ?? throw new clsEntidadNoEncontradaException("Usuario no encontrado");

        if (!await usuarios.TieneHistorialAsync(id, ct))
        {
            usuarios.Eliminar(usuario);
            auditor.Registrar("usuario.eliminar", "usuarios", id);
            try
            {
                await unidadDeTrabajo.GuardarCambiosAsync(ct);
                return new EliminarUsuarioResponse("eliminado");
            }
            catch (clsConflictoException)
            {
                // Aparecio historial entre la consulta y el borrado: se cae a la baja logica.
                unidadDeTrabajo.DescartarCambios();
                usuario = await usuarios.ObtenerPorIdAsync(id, ct)
                    ?? throw new clsEntidadNoEncontradaException("Usuario no encontrado");
            }
        }

        usuario.Retirar(reloj.Ahora);
        auditor.Registrar("usuario.retirar", "usuarios", usuario.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new EliminarUsuarioResponse("retirado");
    }
}
