using FluentValidation;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Auth;

/// <summary>
/// Inicio de sesion por correo y contrasena o por cedula y PIN, cambio de PIN y consulta del usuario actual. El PIN se guarda con el
/// mismo servicio de hash que las contrasenas; tras varios PIN errados seguidos la cuenta se bloquea un tiempo.
/// </summary>
public sealed class clsAuthService(
    IUsuarioRepository usuarios,
    IServicioContrasenas contrasenas,
    IEmisorToken emisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IReloj reloj,
    ICatalogosLectura catalogos,
    IOptions<clsOpcionesSeguridad> opciones,
    IValidator<LoginRequest> validador,
    IValidator<LoginConductorRequest> validadorConductor,
    IValidator<CambiarPinRequest> validadorCambioPin)
{
    public async Task<LoginResponse> LoginAsync(LoginRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var email = solicitud.Email!.Trim();
        var password = solicitud.Password!;

        var usuario = await usuarios.ObtenerPorEmailAsync(email, ct);
        // Quien entra con PIN no tiene contrasena guardada, asi que nunca verifica; igual se hace el trabajo para no revelar nada por el tiempo.
        var correcta = contrasenas.Verificar(password, usuario?.PasswordHash);

        // Mensaje generico ante cualquier fallo: no revela si el correo existe.
        if (usuario is null || usuario.EntraConPin || !correcta)
        {
            throw new clsNoAutenticadoException("Correo o contraseña incorrectos");
        }

        if (!usuario.Activo)
        {
            throw new clsNoAutenticadoException("Usuario retirado: no tiene acceso. Contacta al administrador.");
        }

        // Los hashes en formato anterior (scrypt) se regeneran en el formato vigente al primer ingreso exitoso.
        if (contrasenas.RequiereActualizacion(usuario.PasswordHash))
        {
            usuario.ReemplazarPasswordHash(contrasenas.Hashear(password), reloj.Ahora);
        }

        usuario.RegistrarAcceso(reloj.Ahora);
        auditor.Registrar("auth.login", "usuarios", usuario.Id, usuario.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);

        var dto = ADto(usuario);
        return new LoginResponse(emisor.Emitir(dto), dto);
    }

    /// <summary>
    /// Inicio de sesion con cedula y PIN (conductor o inspector sin correo). Cada PIN errado de una cuenta existente se cuenta y se
    /// audita; al llegar al maximo la cuenta se bloquea (423) hasta que pase el tiempo o un administrador la desbloquee. Un PIN
    /// temporal entra normalmente, pero el resto de la API exige cambiarlo primero.
    /// </summary>
    public async Task<LoginResponse> LoginConductorAsync(LoginConductorRequest solicitud, CancellationToken ct = default)
    {
        await validadorConductor.ValidarOLanzarAsync(solicitud, ct);
        var ahora = reloj.Ahora;
        var pin = solicitud.Pin!;

        var usuario = await usuarios.ObtenerPorCedulaAsync(CedulaParaBuscar(solicitud.Cedula), ct);
        var entraConPin = usuario is { EntraConPin: true };

        if (entraConPin && usuario!.EstaBloqueado(ahora))
        {
            throw new clsCuentaBloqueadaException(usuario.BloqueadoHasta!.Value);
        }

        var correcto = contrasenas.Verificar(pin, entraConPin ? usuario!.PinHash : null);
        if (!entraConPin || !correcto)
        {
            // Solo se cuentan los intentos de cuentas reales y activas; el mensaje es el mismo exista o no la cedula.
            if (entraConPin && usuario!.Activo)
            {
                await RegistrarFalloAsync(usuario, "auth.login_conductor_fallido", ct);
            }

            throw new clsNoAutenticadoException("Cédula o PIN incorrectos");
        }

        if (!usuario!.Activo)
        {
            throw new clsNoAutenticadoException("Usuario retirado: no tiene acceso. Contacta al administrador.");
        }

        if (contrasenas.RequiereActualizacion(usuario.PinHash))
        {
            usuario.ReemplazarPinHash(contrasenas.Hashear(pin), ahora);
        }

        usuario.RegistrarAcceso(ahora);
        auditor.Registrar("auth.login_conductor", "usuarios", usuario.Id, usuario.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);

        var dto = ADto(usuario);
        return new LoginResponse(emisor.Emitir(dto), dto);
    }

    /// <summary>La propia persona cambia su PIN (obligatorio cuando es temporal). Pide el actual; un actual errado cuenta como intento fallido.</summary>
    public async Task CambiarPinAsync(Guid usuarioId, CambiarPinRequest solicitud, CancellationToken ct = default)
    {
        await validadorCambioPin.ValidarOLanzarAsync(solicitud, ct);
        var usuario = await usuarios.ObtenerPorIdAsync(usuarioId, ct)
            ?? throw new clsNoAutenticadoException("Sesión no válida");
        if (!usuario.EntraConPin)
        {
            throw new clsConflictoException("El usuario no entra con PIN");
        }

        var ahora = reloj.Ahora;
        if (usuario.EstaBloqueado(ahora))
        {
            throw new clsCuentaBloqueadaException(usuario.BloqueadoHasta!.Value);
        }

        if (!contrasenas.Verificar(solicitud.PinActual!, usuario.PinHash))
        {
            await RegistrarFalloAsync(usuario, "auth.cambiar_pin_fallido", ct);
            throw clsErroresCampos.De("pin_actual", "El PIN actual es incorrecto");
        }

        var nuevo = clsPin.Validar(solicitud.PinNuevo, "pin_nuevo");
        if (contrasenas.Verificar(nuevo, usuario.PinHash))
        {
            throw clsErroresCampos.De("pin_nuevo", "El PIN nuevo debe ser distinto al actual");
        }

        usuario.CambiarPin(contrasenas.Hashear(nuevo), ahora);
        auditor.Registrar("auth.cambiar_pin", "usuarios", usuario.Id, usuario.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
    }

    public async Task<PerfilDto> ObtenerPerfilAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await usuarios.ObtenerPorIdAsync(usuarioId, ct)
            ?? throw new clsNoAutenticadoException("Sesión no válida");
        return new PerfilDto(
            usuario.Id,
            usuario.Nombre,
            usuario.Email,
            usuario.Rol,
            usuario.SubtipoInspector,
            usuario.Cedula,
            usuario.TipoCredencial,
            usuario.DebeCambiarPin,
            await ObtenerClienteAsync(usuario.ClienteId, ct),
            clsCapacidades.De(usuario.Rol));
    }

    internal static UsuarioAutenticadoDto ADto(clsUsuario usuario) =>
        new(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol, usuario.Cedula, usuario.DebeCambiarPin);

    private async Task<ClienteDto?> ObtenerClienteAsync(Guid? clienteId, CancellationToken ct)
    {
        if (clienteId is not { } id)
        {
            return null;
        }

        var valor = (await catalogos.ValoresAsync(clsNombresCatalogo.CLIENTES, ct)).FirstOrDefault(c => c.Id == id);
        return valor is null
            ? null
            : new ClienteDto(valor.Id, valor.Codigo ?? string.Empty, valor.TryGetValue("nombre", out var nombre) ? nombre as string ?? string.Empty : string.Empty);
    }

    /// <summary>Suma el PIN errado, lo audita y guarda (aunque despues se responda un error). Al bloquearse responde 423.</summary>
    private async Task RegistrarFalloAsync(clsUsuario usuario, string accion, CancellationToken ct)
    {
        var seguridad = opciones.Value;
        var ahora = reloj.Ahora;
        var bloqueada = usuario.RegistrarFalloDePin(ahora, seguridad.PinMaxIntentos, seguridad.PinBloqueoMinutos);
        auditor.Registrar(accion, "usuarios", usuario.Id, usuario.Id);
        if (bloqueada)
        {
            auditor.Registrar("auth.cuenta_bloqueada", "usuarios", usuario.Id, usuario.Id);
        }

        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (bloqueada)
        {
            throw new clsCuentaBloqueadaException(usuario.BloqueadoHasta!.Value);
        }
    }

    /// <summary>Una cedula con formato invalido se busca como esta (no existe): responde lo mismo que una cedula desconocida.</summary>
    private static string CedulaParaBuscar(string? cedula)
    {
        try
        {
            return clsUsuario.NormalizarCedula(cedula) ?? string.Empty;
        }
        catch (clsValidacionException)
        {
            return string.Empty;
        }
    }
}
