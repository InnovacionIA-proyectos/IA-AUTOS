using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Usuarios;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Usuarios;

[Route("api/usuarios")]
public sealed class clsUsuariosController(clsUsuariosService servicio, IUsuarioActual actual) : clsControladorBase
{
    /// <summary>
    /// Lista de usuarios. Administradores: datos completos (gestion web). Demas roles: solo id, nombre y rol de los usuarios
    /// activos, que es lo que necesita el selector de entrega y recibo de la app (sin exponer correos).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UsuarioDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        actual.EsAdministrador() ? Ok(await servicio.ListarAsync(ct)) : Ok(await servicio.ListarBasicoAsync(ct));

    [HttpPost]
    [RolesPermitidos(clsRol.SUPER_ADMIN)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Crear(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearUsuarioRequest>(ct);
        return Creado(await servicio.CrearAsync(solicitud, ct));
    }

    [HttpPatch("{id:guid}")]
    [RolesPermitidos(clsRol.SUPER_ADMIN)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Actualizar(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<ActualizarUsuarioRequest>(ct);
        return Ok(await servicio.ActualizarAsync(id, solicitud, ct));
    }

    /// <summary>Elimina el usuario o, si tiene historial, lo marca como retirado (baja logica).</summary>
    [HttpDelete("{id:guid}")]
    [RolesPermitidos(clsRol.SUPER_ADMIN)]
    [ProducesResponseType<EliminarUsuarioResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct) =>
        Ok(await servicio.EliminarAsync(id, ct));
}
