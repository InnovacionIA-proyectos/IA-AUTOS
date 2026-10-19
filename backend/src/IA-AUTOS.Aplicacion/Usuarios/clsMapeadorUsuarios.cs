using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Aplicacion.Usuarios;

internal static class clsMapeadorUsuarios
{
    public static UsuarioDto ADto(clsUsuario u, string? pinTemporal = null) =>
        new(
            u.Id,
            u.Nombre,
            u.Email,
            u.Rol,
            u.SubtipoInspector,
            u.Cedula,
            u.Activo,
            u.TipoCredencial,
            u.DebeCambiarPin,
            u.BloqueadoHasta,
            u.UltimoAccesoAt,
            u.ClienteId,
            u.CreatedAt,
            u.UpdatedAt,
            pinTemporal);
}
