using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Aplicacion.Usuarios;

internal static class clsMapeadorUsuarios
{
    public static UsuarioDto ADto(clsUsuario u) =>
        new(u.Id, u.Nombre, u.Email, u.Rol, u.SubtipoInspector, u.Activo, u.CreatedAt, u.UpdatedAt);
}
