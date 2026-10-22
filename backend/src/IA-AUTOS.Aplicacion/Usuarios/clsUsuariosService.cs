using System.Text.Json;
using FluentValidation;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Usuarios;

/// <summary>
/// Gestion de usuarios (alta, cambios, baja, PIN y desbloqueo). Quien administra es admin, coordinador de calidad o super administrador;
/// solo el super administrador asigna o modifica el nivel super_admin y cambia el cliente de alguien. Los demas solo ven y administran
/// a los usuarios de su propio cliente. La baja conserva el historial cuando existe. Sin usuario autenticado (el comando de
/// operacion) actua como el sistema.
/// </summary>
public sealed class clsUsuariosService(
    IUsuarioRepository usuarios,
    IServicioContrasenas contrasenas,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    ICatalogosLectura catalogos,
    IOptions<clsOpcionesSeguridad> opciones,
    IValidator<CrearUsuarioRequest> validadorCrear)
{
    public const int TAMANO_PAGINA_DEFECTO = 50;
    public const int TAMANO_PAGINA_MAXIMO = 200;

    private static readonly string[] CAMPOS_EDITABLES = ["nombre", "email", "rol", "subtipo_inspector", "password", "activo", "cedula", "cliente_id"];
    private static readonly string[] CAMPOS_SIN_NULO = ["nombre", "rol", "password", "activo"];

    /// <summary>Cambios ya leidos: las marcas "Cambia*" distinguen "no enviado" de "enviado en nulo" (que limpia el valor).</summary>
    private sealed record CambiosUsuario(
        string? Nombre,
        bool CambiaEmail,
        string? Email,
        string? Rol,
        bool CambiaSubtipo,
        string? Subtipo,
        string? Password,
        bool? Activo,
        bool CambiaCedula,
        string? Cedula,
        bool CambiaCliente,
        Guid? ClienteId);

    /// <summary>El super administrador (o el sistema) administra todos los clientes; los demas, solo el suyo.</summary>
    private bool VeTodosLosClientes => actual.Rol is null or clsRol.SUPER_ADMIN;

    // ------------------------------------------------------------------------------------------------ lectura

    /// <summary>Busqueda paginada (nombre, correo o cedula; rol; estado; cliente). Quien no es super administrador solo ve su cliente: pedir otro responde 403.</summary>
    public async Task<ListadoUsuariosDto> ListarAsync(FiltrosUsuariosRequest filtros, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        var rol = clsParseoFiltros.Catalogo(filtros.Rol, clsRol.VALORES, "rol", errores);
        var clienteSolicitado = clsParseoFiltros.Uuid(filtros.ClienteId, "cliente_id", errores);
        bool? activo = null;
        if (!string.IsNullOrWhiteSpace(filtros.Activo))
        {
            if (bool.TryParse(filtros.Activo, out var valor))
            {
                activo = valor;
            }
            else
            {
                errores["activo"] = ["activo debe ser true o false"];
            }
        }

        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var pagina = Math.Max(filtros.Pagina ?? 1, 1);
        var tamano = Math.Clamp(filtros.Tamano ?? TAMANO_PAGINA_DEFECTO, 1, TAMANO_PAGINA_MAXIMO);
        var filtro = new clsFiltroUsuarios(filtros.Q, rol, activo, actual.ClienteDelFiltro(clienteSolicitado));
        var (filas, total) = await usuarios.BuscarAsync(filtro, (pagina - 1) * tamano, tamano, ct);
        return new ListadoUsuariosDto(filas.Select(u => clsMapeadorUsuarios.ADto(u)).ToList(), total, pagina, tamano);
    }

    /// <summary>Lista reducida (id, nombre y rol) de los usuarios activos del cliente, para quien no administra usuarios.</summary>
    public async Task<IReadOnlyList<UsuarioListaDto>> ListarBasicoAsync(CancellationToken ct = default) =>
        (await usuarios.ListarAsync(VeTodosLosClientes ? null : ClienteDelActor(), ct))
            .Where(u => u.Activo)
            .Select(u => new UsuarioListaDto(u.Id, u.Nombre, u.Rol))
            .ToList();

    // ------------------------------------------------------------------------------------------------ escritura

    public async Task<UsuarioDto> CrearAsync(CrearUsuarioRequest solicitud, CancellationToken ct = default)
    {
        await validadorCrear.ValidarOLanzarAsync(solicitud, ct);
        var rol = solicitud.Rol!;
        AsegurarPuedeAsignarRol(rol);
        var clienteId = await ClienteDeAltaAsync(rol, solicitud.ClienteId, ct);
        var ahora = reloj.Ahora;

        string? pinTemporal = null;
        clsUsuario usuario;
        if (solicitud.Password is not null)
        {
            usuario = clsUsuario.Crear(
                solicitud.Nombre!, solicitud.Email!, rol, solicitud.SubtipoInspector, contrasenas.Hashear(solicitud.Password), ahora, solicitud.Cedula, clienteId);
        }
        else
        {
            var pin = solicitud.Pin is null ? clsPin.Generar() : clsPin.Validar(solicitud.Pin);
            pinTemporal = solicitud.Pin is null ? pin : null;
            usuario = clsUsuario.CrearConPin(
                solicitud.Nombre!, solicitud.Email, rol, solicitud.SubtipoInspector, solicitud.Cedula, contrasenas.Hashear(pin), ahora, clienteId);
        }

        usuarios.Agregar(usuario);
        auditor.Registrar("usuario.crear", "usuarios", usuario.Id, detalle: new DetalleAuditoria(null, clsInstantaneasAuditoria.De(usuario)), clienteId: usuario.ClienteId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorUsuarios.ADto(usuario, pinTemporal);
    }

    /// <summary>
    /// Cambia solo los campos enviados. <c>subtipo_inspector</c>, <c>email</c> y <c>cedula</c> admiten null para limpiarlos (el correo solo
    /// si el usuario entra con PIN, la cedula solo si entra con contrasena y no es conductor). Una contrasena nueva pasa al usuario a
    /// entrar con contrasena. Al cambiar de rol a uno que no es inspector el subtipo se limpia solo.
    /// </summary>
    public async Task<UsuarioDto> ActualizarAsync(Guid id, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var lector = new clsCamposCatalogo(campos);
        lector.SoloPermitidos(CAMPOS_EDITABLES, "este usuario");
        foreach (var campo in CAMPOS_SIN_NULO.Where(c => lector.Tiene(c) && campos[c].ValueKind == JsonValueKind.Null))
        {
            throw clsErroresCampos.De(campo, "No admite valor nulo");
        }

        return await AplicarAsync(
            id,
            new CambiosUsuario(
                lector.Texto("nombre"),
                lector.Tiene("email"), lector.Texto("email"),
                lector.Texto("rol"),
                lector.Tiene("subtipo_inspector"), clsValidador.Opcional(lector.Texto("subtipo_inspector")),
                lector.Texto("password"),
                lector.Booleano("activo"),
                lector.Tiene("cedula"), lector.Texto("cedula"),
                lector.Tiene("cliente_id"), lector.Identificador("cliente_id")),
            ct);
    }

    /// <summary>Cambios por codigo (el comando de operacion): lo nulo se conserva.</summary>
    public Task<UsuarioDto> ActualizarAsync(Guid id, ActualizarUsuarioRequest solicitud, CancellationToken ct = default) =>
        AplicarAsync(
            id,
            new CambiosUsuario(
                solicitud.Nombre,
                solicitud.Email is not null, solicitud.Email,
                solicitud.Rol,
                solicitud.SubtipoInspector is not null, solicitud.SubtipoInspector,
                solicitud.Password,
                solicitud.Activo,
                solicitud.Cedula is not null, solicitud.Cedula,
                solicitud.ClienteId is not null, solicitud.ClienteId),
            ct);

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

        var usuario = await ObtenerGestionableAsync(id, ct);

        if (!await usuarios.TieneHistorialAsync(id, ct))
        {
            usuarios.Eliminar(usuario);
            auditor.Registrar("usuario.eliminar", "usuarios", id, detalle: new DetalleAuditoria(clsInstantaneasAuditoria.De(usuario), null), clienteId: usuario.ClienteId);
            try
            {
                await unidadDeTrabajo.GuardarCambiosAsync(ct);
                return new EliminarUsuarioResponse("eliminado");
            }
            catch (clsConflictoException)
            {
                // Aparecio historial entre la consulta y el borrado: se cae a la baja logica.
                unidadDeTrabajo.DescartarCambios();
                usuario = await ObtenerGestionableAsync(id, ct);
            }
        }

        var antesDeRetirar = clsInstantaneasAuditoria.De(usuario);
        usuario.Retirar(reloj.Ahora);
        auditor.Registrar(
            "usuario.retirar", "usuarios", usuario.Id, detalle: new DetalleAuditoria(antesDeRetirar, clsInstantaneasAuditoria.De(usuario)), clienteId: usuario.ClienteId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new EliminarUsuarioResponse("retirado");
    }

    // ------------------------------------------------------------------------------------------------ PIN y bloqueo

    /// <summary>
    /// Asigna o reinicia el PIN (el de la solicitud o uno generado, que se devuelve una sola vez). El usuario pasa a entrar con cedula y
    /// PIN, debe cambiarlo antes de continuar y se levanta su bloqueo. Exige que sea conductor o inspector con cedula.
    /// </summary>
    public async Task<UsuarioDto> AsignarPinAsync(Guid id, AsignarPinRequest solicitud, CancellationToken ct = default)
    {
        var usuario = await ObtenerGestionableAsync(id, ct);
        var pin = solicitud.Pin is null ? clsPin.Generar() : clsPin.Validar(solicitud.Pin);
        var antesDelPin = clsInstantaneasAuditoria.De(usuario);
        usuario.AsignarPin(contrasenas.Hashear(pin), debeCambiar: true, reloj.Ahora);
        auditor.Registrar(
            "usuario.pin_asignar", "usuarios", usuario.Id, detalle: new DetalleAuditoria(antesDelPin, clsInstantaneasAuditoria.De(usuario)), clienteId: usuario.ClienteId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorUsuarios.ADto(usuario, solicitud.Pin is null ? pin : null);
    }

    /// <summary>Obliga a la persona a cambiar su PIN en su proximo uso, sin cambiarlo.</summary>
    public async Task<UsuarioDto> ForzarCambioDePinAsync(Guid id, CancellationToken ct = default)
    {
        var usuario = await ObtenerGestionableAsync(id, ct);
        var antesDeForzar = clsInstantaneasAuditoria.De(usuario);
        usuario.ForzarCambioDePin(reloj.Ahora);
        auditor.Registrar(
            "usuario.pin_forzar_cambio", "usuarios", usuario.Id, detalle: new DetalleAuditoria(antesDeForzar, clsInstantaneasAuditoria.De(usuario)), clienteId: usuario.ClienteId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorUsuarios.ADto(usuario);
    }

    /// <summary>Levanta el bloqueo por PIN errados antes de que venza.</summary>
    public async Task<UsuarioDto> DesbloquearAsync(Guid id, CancellationToken ct = default)
    {
        var usuario = await ObtenerGestionableAsync(id, ct);
        usuario.Desbloquear(reloj.Ahora);
        auditor.Registrar("usuario.desbloquear", "usuarios", usuario.Id, clienteId: usuario.ClienteId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorUsuarios.ADto(usuario);
    }

    // ------------------------------------------------------------------------------------------------ internos

    private async Task<UsuarioDto> AplicarAsync(Guid id, CambiosUsuario cambios, CancellationToken ct)
    {
        var usuario = await ObtenerGestionableAsync(id, ct);

        if (cambios.Rol is not null)
        {
            AsegurarPuedeAsignarRol(cambios.Rol);
        }

        var minimo = opciones.Value.PasswordMinimo;
        if (cambios.Password is { } password && password.Length < minimo)
        {
            throw clsErroresCampos.De("password", $"La contraseña debe tener al menos {minimo} caracteres");
        }

        if (cambios.Activo == false && usuario.Id == actual.Id)
        {
            throw new clsConflictoException("No puedes desactivar tu propio usuario");
        }

        var rolFinal = cambios.Rol ?? usuario.Rol;
        if (cambios.CambiaCliente)
        {
            if (!VeTodosLosClientes)
            {
                throw new clsProhibidoException("Solo el super administrador cambia el cliente de un usuario");
            }

            if (cambios.ClienteId is null && rolFinal != clsRol.SUPER_ADMIN)
            {
                throw clsErroresCampos.De("cliente_id", "El cliente es requerido salvo para el super administrador");
            }

            if (cambios.ClienteId is { } cliente && !await catalogos.ExisteActivoAsync(clsNombresCatalogo.CLIENTES, cliente, ct))
            {
                throw clsErroresCampos.De("cliente_id", "El cliente no existe o está inactivo");
            }
        }

        var antes = clsInstantaneasAuditoria.De(usuario);
        usuario.Modificar(
            cambios.Nombre,
            cambios.CambiaEmail, cambios.Email,
            cambios.Rol,
            cambios.CambiaSubtipo, cambios.Subtipo,
            cambios.Password is null ? null : contrasenas.Hashear(cambios.Password),
            cambios.Activo,
            cambios.CambiaCedula, cambios.Cedula,
            cambios.CambiaCliente, cambios.ClienteId,
            reloj.Ahora);

        if (usuario.ClienteId is null && usuario.Rol != clsRol.SUPER_ADMIN)
        {
            // Quien deja de ser super administrador pasa a pertenecer a un cliente.
            usuario.AsignarCliente(await ClienteDeAltaAsync(usuario.Rol, null, ct), reloj.Ahora);
        }

        auditor.Registrar(
            "usuario.actualizar", "usuarios", usuario.Id, detalle: new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(usuario)), clienteId: usuario.ClienteId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorUsuarios.ADto(usuario);
    }

    /// <summary>
    /// El usuario a administrar. Quien no es super administrador no ve (404) a los de otro cliente ni puede tocar a un super administrador (403).
    /// </summary>
    private async Task<clsUsuario> ObtenerGestionableAsync(Guid id, CancellationToken ct)
    {
        var usuario = await usuarios.ObtenerPorIdAsync(id, ct)
            ?? throw new clsEntidadNoEncontradaException("Usuario no encontrado");
        if (VeTodosLosClientes)
        {
            return usuario;
        }

        if (usuario.ClienteId != actual.ClienteId)
        {
            throw new clsEntidadNoEncontradaException("Usuario no encontrado");
        }

        if (usuario.Rol == clsRol.SUPER_ADMIN)
        {
            throw new clsProhibidoException("Solo un super administrador puede modificar a un super administrador");
        }

        return usuario;
    }

    /// <summary>Solo el super administrador (o el sistema) asigna el nivel super_admin.</summary>
    private void AsegurarPuedeAsignarRol(string rol)
    {
        if (rol == clsRol.SUPER_ADMIN && !VeTodosLosClientes)
        {
            throw new clsProhibidoException("Solo un super administrador puede asignar el nivel super administrador");
        }
    }

    /// <summary>
    /// Cliente de un usuario nuevo. El super administrador elige uno (por defecto el primero activo); los demas crean siempre dentro de su
    /// cliente. El super administrador no pertenece a ninguno.
    /// </summary>
    private async Task<Guid?> ClienteDeAltaAsync(string rol, Guid? solicitado, CancellationToken ct)
    {
        if (rol == clsRol.SUPER_ADMIN)
        {
            return null;
        }

        if (VeTodosLosClientes)
        {
            if (solicitado is not { } elegido)
            {
                return await catalogos.ClienteAsync(null, ct);
            }

            return await catalogos.ExisteActivoAsync(clsNombresCatalogo.CLIENTES, elegido, ct)
                ? elegido
                : throw clsErroresCampos.De("cliente_id", "El cliente no existe o está inactivo");
        }

        if (solicitado is { } otro && otro != actual.ClienteId)
        {
            throw new clsProhibidoException("Solo puedes administrar usuarios de tu propio cliente");
        }

        return actual.ClienteId ?? await catalogos.ClienteAsync(null, ct);
    }

    /// <summary>Cliente que filtra lo que ve quien no es super administrador; sin cliente no ve nada.</summary>
    private Guid ClienteDelActor() => actual.ClienteId ?? Guid.Empty;
}
