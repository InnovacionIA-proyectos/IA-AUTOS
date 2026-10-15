using System.Security.Cryptography;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAAutos.Aplicacion.Auth;

/// <summary>Datos de la conexion que se guardan junto al refresh token (auditoria de la sesion).</summary>
public sealed record ContextoCliente(string? Ip, string? Dispositivo);

/// <summary>Sesion emitida: el access y el refresh token (en claro) van en el cuerpo de la respuesta; en la base solo queda el hash.</summary>
public sealed record SesionEmitida(string Access, UsuarioAutenticadoDto User, string RefreshPlano, DateTimeOffset RefreshExpiraAt);

/// <summary>
/// Sesion del portal (inicio de sesion unico): quien ya inicio sesion en el portal de administracion entra a IA Autos sin
/// una segunda pantalla de login. El cliente solo aporta el id del portal; el servidor lo revalida contra la base de
/// usuarios de TI (ignora cualquier permiso que envie el navegador), resuelve el rol de IA Autos, crea la cuenta si es la
/// primera vez y emite el access token y un refresh token rotativo.
/// </summary>
public sealed class clsSesionPortalService(
    IUsuarioRepository usuarios,
    IRefreshTokenRepository refrescos,
    IPortalUsuarios portal,
    ICatalogosLectura catalogos,
    IServicioContrasenas contrasenas,
    IEmisorToken emisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IReloj reloj,
    IOptions<clsOpcionesSesion> opcionesSesion,
    IOptions<clsOpcionesPortalAplicacion> opcionesPortal,
    ILogger<clsSesionPortalService> registro)
{
    public const string MENSAJE_SIN_ACCESO =
        "Tu usuario no tiene acceso habilitado a IA Autos. Solicita el permiso al administrador del portal.";

    private const string MENSAJE_SESION_INVALIDA = "Sesión no válida";

    // ------------------------------------------------------------------------------------------------ login-portal

    public async Task<SesionEmitida> LoginPortalAsync(
        IReadOnlyList<LoginPortalUsuarioDto>? solicitud,
        ContextoCliente cliente,
        CancellationToken ct = default)
    {
        if (solicitud is not { Count: > 0 } || solicitud[0].Id is not > 0)
        {
            throw new clsValidacionException("El cuerpo debe ser un arreglo con al menos un usuario del portal (con su id)");
        }

        // Solo se usa el id. El nombre y los permisos que manda el navegador no deciden nada: el rol sale de TI.
        var idPortal = solicitud[0].Id!.Value;

        var usuario = portal.Disponible
            ? await ResolverContraPortalAsync(idPortal, ct)
            : await ResolverSoloVinculadoAsync(idPortal, ct);

        usuario.RegistrarAcceso(reloj.Ahora);
        return await EmitirAsync(usuario, Guid.NewGuid(), cliente, "auth.login_portal", ct);
    }

    /// <summary>Camino normal: la base de TI confirma la identidad y los roles; si es la primera vez se crea la cuenta.</summary>
    private async Task<clsUsuario> ResolverContraPortalAsync(long idPortal, CancellationToken ct)
    {
        var infoPortal = await portal.ObtenerAsync(idPortal, ct);
        var coincidencias = infoPortal is null ? [] : clsRolesPortal.Coincidencias(infoPortal.Roles);
        if (infoPortal is null || coincidencias.Count == 0)
        {
            throw await RechazarAsync(idPortal, infoPortal is null ? "no existe en el portal" : "sin rol de IA Autos en el portal", ct);
        }

        // Varios roles de IA Autos: gana el de mayor privilegio (orden fijo en clsRolesPortal).
        var rol = coincidencias[0];
        if (coincidencias.Count > 1)
        {
            registro.LogInformation(
                "Usuario del portal {IdPortal} con varios roles de IA Autos ({Roles}): se aplica {Rol}",
                idPortal,
                string.Join(", ", coincidencias),
                rol);
        }

        var vinculado = await usuarios.ObtenerPorPortalIdAsync(idPortal, ct);
        var email = NormalizarEmail(infoPortal.Email);
        var usuario = vinculado ?? (email is null ? null : await usuarios.ObtenerPorEmailAsync(email, ct));

        if (usuario is null)
        {
            return await CrearDesdePortalAsync(infoPortal, email, rol, ct);
        }

        if (!usuario.Activo)
        {
            throw await RechazarAsync(idPortal, "usuario retirado en IA Autos", ct, usuario.Id);
        }

        if (usuario.PortalUsuarioId is not null && usuario.PortalUsuarioId != idPortal)
        {
            // El correo coincide pero la cuenta ya pertenece a otra identidad del portal: no se le entrega.
            throw await RechazarAsync(idPortal, "la cuenta ya esta vinculada a otro usuario del portal", ct, usuario.Id);
        }

        var cambios = false;
        if (usuario.PortalUsuarioId is null)
        {
            usuario.VincularPortal(idPortal, reloj.Ahora);
            auditor.Registrar("usuario.vincular_portal", "usuarios", usuario.Id, usuario.Id);
            cambios = true;
        }

        if (usuario.Rol != rol)
        {
            // El portal manda sobre el rol: si cambio desde la ultima vez, se refleja de inmediato.
            usuario.Actualizar(nombre: null, email: null, rol, subtipoInspector: null, nuevoPasswordHash: null, activo: null, reloj.Ahora);
            auditor.Registrar("usuario.rol_desde_portal", "usuarios", usuario.Id, usuario.Id);
            cambios = true;
        }

        if (usuario.ClienteId is null && usuario.Rol != clsRol.SUPER_ADMIN)
        {
            // Quien deja de ser super administrador pasa a pertenecer a un cliente.
            usuario.AsignarCliente(await catalogos.ClienteAsync(null, ct), reloj.Ahora);
            cambios = true;
        }

        if (cambios)
        {
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }

        return usuario;
    }

    private async Task<clsUsuario> CrearDesdePortalAsync(clsUsuarioPortal info, string? email, string rol, CancellationToken ct)
    {
        var correo = email;
        if (correo is null)
        {
            var dominio = opcionesPortal.Value.DominioCorreo?.Trim().TrimStart('@');
            if (string.IsNullOrWhiteSpace(dominio))
            {
                throw await RechazarAsync(info.Id, "el portal no tiene correo para crear la cuenta", ct);
            }

            correo = NormalizarEmail($"{info.NombreUsuario.Trim()}@{dominio}");
        }

        if (correo is null)
        {
            throw await RechazarAsync(info.Id, "correo invalido para crear la cuenta", ct);
        }

        var nombre = string.IsNullOrWhiteSpace(info.DescripcionUsuario) ? info.NombreUsuario : info.DescripcionUsuario;

        // Cuenta de inicio de sesion unico: la contrasena es un valor aleatorio que nadie conoce, asi que no se puede usar.
        var passwordInerte = contrasenas.Hashear(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var clienteId = rol == clsRol.SUPER_ADMIN ? (Guid?)null : await catalogos.ClienteAsync(null, ct);
        var usuario = clsUsuario.Crear(nombre.Trim(), correo, rol, subtipoInspector: null, passwordInerte, reloj.Ahora, clienteId: clienteId);
        usuario.VincularPortal(info.Id, reloj.Ahora);
        usuarios.Agregar(usuario);
        auditor.Registrar("usuario.crear_desde_portal", "usuarios", usuario.Id, usuarioId: null);

        // Se guarda aqui para que la cuenta exista antes del refresh token y la auditoria que la referencian.
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return usuario;
    }

    /// <summary>
    /// Sin base de TI configurada no se puede verificar la identidad contra el portal: solo entra quien ya esta vinculado
    /// (por la carga inicial o por el administrador) y activo. El rol es el que tiene en IA Autos.
    /// </summary>
    private async Task<clsUsuario> ResolverSoloVinculadoAsync(long idPortal, CancellationToken ct)
    {
        var usuario = await usuarios.ObtenerPorPortalIdAsync(idPortal, ct);
        if (usuario is null || !usuario.Activo)
        {
            throw await RechazarAsync(idPortal, "sin vinculo previo (la base de TI no esta configurada)", ct, usuario?.Id);
        }

        return usuario;
    }

    // ---------------------------------------------------------------------------------------------------- refresh

    public async Task<SesionEmitida> RefrescarAsync(string? refreshPlano, ContextoCliente cliente, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshPlano))
        {
            throw new clsValidacionException("El refresh token es requerido");
        }

        var ahora = reloj.Ahora;
        var token = await refrescos.ObtenerPorHashAsync(clsTokensRefresh.Hash(refreshPlano), ct)
            ?? throw new clsNoAutenticadoException(MENSAJE_SESION_INVALIDA);

        if (token.RevocadoAt is not null)
        {
            await ManejarReusoAsync(token, ahora, ct);
            throw new clsNoAutenticadoException(MENSAJE_SESION_INVALIDA);
        }

        if (token.ExpiraAt <= ahora)
        {
            throw new clsNoAutenticadoException("La sesión expiró");
        }

        var usuario = await usuarios.ObtenerPorIdAsync(token.UsuarioId, ct);
        if (usuario is null || !usuario.Activo)
        {
            await refrescos.RevocarFamiliaAsync(token.FamiliaId, ahora, ct);
            throw new clsNoAutenticadoException("Usuario retirado: no tiene acceso. Contacta al administrador.");
        }

        return await EmitirAsync(usuario, token.FamiliaId, cliente, "auth.refresh", ct, anterior: token);
    }

    /// <summary>
    /// Un refresh ya usado se presento otra vez. Si acaba de rotar, es casi seguro una carrera entre dos pestañas y no se
    /// castiga; si paso mas tiempo, se asume robo y se revoca toda la sesion (tambien el access token vigente).
    /// </summary>
    private async Task ManejarReusoAsync(clsRefreshToken token, DateTimeOffset ahora, CancellationToken ct)
    {
        var gracia = TimeSpan.FromSeconds(Math.Max(opcionesSesion.Value.RefreshGraciaSegundos, 0));
        if (token.ReemplazadoPorId is not null && token.RevocadoAt is { } revocado && ahora - revocado <= gracia)
        {
            return;
        }

        await refrescos.RevocarFamiliaAsync(token.FamiliaId, ahora, ct);
        registro.LogWarning("Reuso de un refresh token ya rotado (usuario {UsuarioId}): se revoco la sesion completa", token.UsuarioId);
        auditor.Registrar("auth.refresh_reuso", "usuarios", token.UsuarioId, token.UsuarioId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
    }

    // ---------------------------------------------------------------------------------------------------- logout

    /// <summary>
    /// Cierra la sesion de verdad: revoca los refresh tokens de la familia (la del refresh del cuerpo y la del access que
    /// se presente), con lo que el access token tambien deja de servir. Siempre termina bien: no revela si habia sesion.
    /// </summary>
    public async Task CerrarSesionAsync(string? refreshPlano, Guid? familiaDelAccess, CancellationToken ct = default)
    {
        var ahora = reloj.Ahora;
        var familias = new HashSet<Guid>();
        Guid? usuarioId = null;

        if (!string.IsNullOrWhiteSpace(refreshPlano))
        {
            var token = await refrescos.ObtenerPorHashAsync(clsTokensRefresh.Hash(refreshPlano), ct);
            if (token is not null)
            {
                familias.Add(token.FamiliaId);
                usuarioId = token.UsuarioId;
            }
        }

        if (familiaDelAccess is { } sid)
        {
            familias.Add(sid);
        }

        foreach (var familia in familias)
        {
            await refrescos.RevocarFamiliaAsync(familia, ahora, ct);
        }

        if (usuarioId is not null)
        {
            auditor.Registrar("auth.logout", "usuarios", usuarioId, usuarioId);
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }
    }

    // --------------------------------------------------------------------------------------------------- comunes

    private async Task<SesionEmitida> EmitirAsync(
        clsUsuario usuario,
        Guid familiaId,
        ContextoCliente cliente,
        string accionAuditoria,
        CancellationToken ct,
        clsRefreshToken? anterior = null)
    {
        var opciones = opcionesSesion.Value;
        var ahora = reloj.Ahora;
        var (plano, hash) = clsTokensRefresh.Generar();

        var nuevo = clsRefreshToken.Emitir(
            usuario.Id, hash, familiaId, TimeSpan.FromDays(Math.Max(opciones.RefreshDias, 1)), cliente.Ip, cliente.Dispositivo, ahora);
        anterior?.RevocarPorRotacion(nuevo.Id, ahora);
        refrescos.Agregar(nuevo);

        var dto = clsAuthService.ADto(usuario);
        var access = emisor.EmitirSesion(dto, familiaId, TimeSpan.FromMinutes(Math.Max(opciones.AccessMinutos, 1)));

        auditor.Registrar(accionAuditoria, "usuarios", usuario.Id, usuario.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new SesionEmitida(access, dto, plano, nuevo.ExpiraAt);
    }

    /// <summary>
    /// Registra el rechazo (solo en el log del servidor, con el id del portal) y devuelve el 403 con codigo propio. El mensaje
    /// al cliente es siempre el mismo: no revela por que no hay acceso.
    /// </summary>
    private async Task<clsPortalUsuarioNoVinculadoException> RechazarAsync(long idPortal, string motivo, CancellationToken ct, Guid? usuarioId = null)
    {
        registro.LogWarning("login-portal rechazado para el usuario del portal {IdPortal}: {Motivo}", idPortal, motivo);
        auditor.Registrar("auth.login_portal_rechazado", "usuarios", usuarioId, usuarioId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new clsPortalUsuarioNoVinculadoException(MENSAJE_SIN_ACCESO);
    }

    private static string? NormalizarEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var limpio = email.Trim().ToLowerInvariant();
        return limpio.Contains('@') && !limpio.Contains(' ') ? limpio : null;
    }
}
