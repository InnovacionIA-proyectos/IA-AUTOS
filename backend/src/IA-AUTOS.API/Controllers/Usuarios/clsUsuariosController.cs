using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Usuarios;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Usuarios;

/// <summary>
/// Usuarios. Administran admin, coordinador de calidad y super administrador (cada uno solo los de su cliente; el nivel super_admin
/// solo lo asigna un super administrador). Los demas roles solo consultan la lista reducida.
/// </summary>
[Route("api/usuarios")]
public sealed class clsUsuariosController(clsUsuariosService servicio, clsCargaConductoresService carga, IAAutos.Aplicacion.Comun.IUsuarioActual actual) : clsControladorBase
{
    /// <summary>
    /// Quien administra usuarios recibe una pagina de usuarios completos, con filtros <c>q</c> (nombre, correo o cedula), <c>rol</c>,
    /// <c>activo</c> y, solo el super administrador, <c>clienteId</c>. Los demas roles reciben solo id, nombre y rol de los usuarios activos
    /// de su cliente, que es lo que necesita el selector de entrega y recibo de la app (sin exponer correos).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<ListadoUsuariosDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? q,
        [FromQuery] string? rol,
        [FromQuery] string? activo,
        [FromQuery] string? clienteId,
        [FromQuery] int? pagina,
        [FromQuery] int? tamano,
        CancellationToken ct) =>
        actual.Rol is { } r && clsRol.GESTION_USUARIOS.Contains(r)
            ? Ok(await servicio.ListarAsync(new FiltrosUsuariosRequest(q, rol, activo, clienteId, pagina, tamano), ct))
            : Ok(await servicio.ListarBasicoAsync(ct));

    /// <summary>
    /// Crea un usuario. Con <c>password</c> entra con correo y contrasena; sin ella, con cedula y PIN (conductor o inspector): si no se
    /// envia <c>pin</c> se genera uno temporal que viene en <c>pin_temporal</c>, una sola vez.
    /// </summary>
    [HttpPost]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(CrearUsuarioRequest))]
    public async Task<IActionResult> Crear(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CrearUsuarioRequest>(ct);
        return Creado(await servicio.CrearAsync(solicitud, ct));
    }

    /// <summary>
    /// Cambia solo los campos enviados. <c>subtipo_inspector</c>, <c>email</c> y <c>cedula</c> admiten null para limpiarlos; una
    /// <c>password</c> nueva pasa al usuario a entrar con contrasena.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(ActualizarUsuarioRequest))]
    public async Task<IActionResult> Actualizar(Guid id, CancellationToken ct) =>
        Ok(await servicio.ActualizarAsync(id, await LeerCamposAsync(ct), ct));

    /// <summary>Elimina el usuario o, si tiene historial, lo marca como retirado (baja logica).</summary>
    [HttpDelete("{id:guid}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<EliminarUsuarioResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Eliminar(Guid id, CancellationToken ct) =>
        Ok(await servicio.EliminarAsync(id, ct));

    /// <summary>
    /// Asigna o reinicia el PIN (el enviado o uno generado, que viene en <c>pin_temporal</c> una sola vez). El usuario debe cambiarlo
    /// antes de continuar y se levanta su bloqueo. Solo conductor o inspector con cedula; pasa a entrar con cedula y PIN.
    /// </summary>
    [HttpPost("{id:guid}/pin")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(AsignarPinRequest))]
    public async Task<IActionResult> AsignarPin(Guid id, CancellationToken ct)
    {
        var solicitud = Request.ContentLength is > 0 ? await LeerCuerpoAsync<AsignarPinRequest>(ct) : new AsignarPinRequest(null);
        return Ok(await servicio.AsignarPinAsync(id, solicitud, ct));
    }

    /// <summary>Obliga a la persona a cambiar su PIN en su proximo uso, sin cambiarlo.</summary>
    [HttpPost("{id:guid}/pin/forzar-cambio")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForzarCambioDePin(Guid id, CancellationToken ct) =>
        Ok(await servicio.ForzarCambioDePinAsync(id, ct));

    /// <summary>Levanta el bloqueo por PIN errados antes de que venza.</summary>
    [HttpPost("{id:guid}/desbloquear")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Desbloquear(Guid id, CancellationToken ct) =>
        Ok(await servicio.DesbloquearAsync(id, ct));

    /// <summary>Vista previa de la carga masiva de conductores por Excel: valida cada fila y devuelve su error, sin guardar nada.</summary>
    [HttpPost("cargar/previsualizar")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ResultadoCargaConductoresDto>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(CargarConductoresRequest))]
    public async Task<IActionResult> PrevisualizarCarga(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CargarConductoresRequest>(ct);
        return Ok(await carga.PrevisualizarAsync(solicitud, ct));
    }

    /// <summary>
    /// Confirma la carga: crea un conductor por cada fila valida, con un PIN temporal que viene en <c>pin_temporal</c> de cada fila, una
    /// sola vez. Las filas invalidas quedan reportadas con su error.
    /// </summary>
    [HttpPost("cargar")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ResultadoCargaConductoresDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(CargarConductoresRequest))]
    public async Task<IActionResult> Cargar(CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<CargarConductoresRequest>(ct);
        return Creado(await carga.CargarAsync(solicitud, ct));
    }
}
