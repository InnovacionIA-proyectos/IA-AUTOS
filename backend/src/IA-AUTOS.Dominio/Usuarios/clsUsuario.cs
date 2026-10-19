using System.Text.RegularExpressions;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Usuarios;

/// <summary>Usuario del sistema (inspector, conductor, administrador, etc.). Admite baja logica.</summary>
public sealed partial class clsUsuario : clsEntidadBase
{
    public const int CEDULA_MIN = 4;
    public const int CEDULA_MAX = 20;

    private clsUsuario()
    {
    }

    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Correo en minusculas. Nulo en quien entra con cedula y PIN y no tiene correo. Unico entre quienes lo tienen.</summary>
    public string? Email { get; private set; }

    public string Rol { get; private set; } = string.Empty;

    public string? SubtipoInspector { get; private set; }

    /// <summary>Baja logica: false significa retirado (conserva historial, no puede ingresar).</summary>
    public bool Activo { get; private set; } = true;

    public string? PasswordHash { get; private set; }

    /// <summary>
    /// Id de la persona en el portal de administracion (tblSAPUsuariosApp.ID). Nulo en quien no entra por el portal
    /// (por ejemplo, los conductores). Es unico entre los usuarios que lo tienen.
    /// </summary>
    public long? PortalUsuarioId { get; private set; }

    /// <summary>Documento de la persona, sin puntos ni espacios. El conductor se identifica por ella (carga de viajes y login). Unica entre quienes la tienen.</summary>
    public string? Cedula { get; private set; }

    /// <summary><see cref="clsTipoCredencial"/>: con que entra el usuario. "pin" exige cedula; "password" exige correo.</summary>
    public string TipoCredencial { get; private set; } = clsTipoCredencial.PASSWORD;

    public string? PinHash { get; private set; }

    /// <summary>El PIN fue asignado o reiniciado por un administrador: la persona debe cambiarlo antes de seguir usando la aplicacion.</summary>
    public bool DebeCambiarPin { get; private set; }

    /// <summary>PIN errados seguidos desde el ultimo ingreso o desbloqueo.</summary>
    public int IntentosFallidos { get; private set; }

    /// <summary>Si es futuro, la cuenta esta bloqueada por intentos de PIN fallidos hasta ese momento.</summary>
    public DateTimeOffset? BloqueadoHasta { get; private set; }

    public DateTimeOffset? UltimoAccesoAt { get; private set; }

    /// <summary>Cliente al que pertenece. Nulo en el super administrador, que ve todos los clientes.</summary>
    public Guid? ClienteId { get; private set; }

    public bool EntraConPin => TipoCredencial == clsTipoCredencial.PIN;

    /// <summary>Alta con correo y contrasena.</summary>
    public static clsUsuario Crear(
        string nombre,
        string email,
        string rol,
        string? subtipoInspector,
        string passwordHash,
        DateTimeOffset ahora,
        string? cedula = null,
        Guid? clienteId = null)
    {
        var usuario = new clsUsuario
        {
            Nombre = clsValidador.Requerido(nombre, "El nombre es requerido"),
            Email = NormalizarEmail(email) ?? throw clsErroresCampos.De("email", "El correo es requerido para entrar con contraseña"),
            Rol = ValidarRol(rol),
            PasswordHash = passwordHash,
            TipoCredencial = clsTipoCredencial.PASSWORD,
            Cedula = NormalizarCedula(cedula),
            Activo = true,
        };
        usuario.SubtipoInspector = usuario.SubtipoValido(subtipoInspector);
        usuario.ClienteId = usuario.ClienteValido(clienteId);
        usuario.AsegurarCoherencia();
        usuario.Inicializar(ahora);
        return usuario;
    }

    /// <summary>Alta con cedula y PIN (conductor o inspector). El PIN nace temporal: la persona lo cambia en su primer ingreso. El correo es opcional.</summary>
    public static clsUsuario CrearConPin(
        string nombre,
        string? email,
        string rol,
        string? subtipoInspector,
        string? cedula,
        string pinHash,
        DateTimeOffset ahora,
        Guid? clienteId = null)
    {
        var usuario = new clsUsuario
        {
            Nombre = clsValidador.Requerido(nombre, "El nombre es requerido"),
            Email = NormalizarEmail(email),
            Rol = ValidarRol(rol),
            Cedula = NormalizarCedula(cedula),
            TipoCredencial = clsTipoCredencial.PIN,
            PinHash = pinHash,
            DebeCambiarPin = true,
            Activo = true,
        };
        usuario.SubtipoInspector = usuario.SubtipoValido(subtipoInspector);
        usuario.ClienteId = usuario.ClienteValido(clienteId);
        usuario.AsegurarCoherencia();
        usuario.Inicializar(ahora);
        return usuario;
    }

    /// <summary>Actualiza solo los campos recibidos (los nulos se conservan). No puede limpiar el correo, el subtipo ni la cedula: para eso, <see cref="Modificar"/>.</summary>
    public void Actualizar(
        string? nombre,
        string? email,
        string? rol,
        string? subtipoInspector,
        string? nuevoPasswordHash,
        bool? activo,
        DateTimeOffset ahora,
        string? cedula = null) =>
        Modificar(
            nombre,
            cambiaEmail: email is not null, email,
            rol,
            cambiaSubtipo: subtipoInspector is not null, subtipoInspector,
            nuevoPasswordHash,
            activo,
            cambiaCedula: cedula is not null, cedula,
            cambiaCliente: false, clienteId: null,
            ahora);

    /// <summary>
    /// Edita los campos indicados. Los "cambia*" distinguen "no enviado" de "enviado en nulo" (que limpia el valor). Una contrasena nueva
    /// pasa al usuario a entrar con contrasena (exige correo) y descarta su PIN. Al terminar el usuario debe seguir siendo coherente
    /// (correo si entra con contrasena, cedula si entra con PIN o es conductor) o se lanza el error de validacion.
    /// </summary>
    public void Modificar(
        string? nombre,
        bool cambiaEmail,
        string? email,
        string? rol,
        bool cambiaSubtipo,
        string? subtipoInspector,
        string? nuevoPasswordHash,
        bool? activo,
        bool cambiaCedula,
        string? cedula,
        bool cambiaCliente,
        Guid? clienteId,
        DateTimeOffset ahora)
    {
        if (nombre is not null)
        {
            Nombre = clsValidador.Requerido(nombre, "El nombre es requerido");
        }

        if (cambiaEmail)
        {
            Email = NormalizarEmail(email);
        }

        if (rol is not null)
        {
            Rol = ValidarRol(rol);
            if (Rol != clsRol.INSPECTOR)
            {
                // El subtipo solo existe para inspectores.
                SubtipoInspector = null;
            }
        }

        if (cambiaSubtipo)
        {
            SubtipoInspector = SubtipoValido(subtipoInspector);
        }

        if (nuevoPasswordHash is not null)
        {
            PasswordHash = nuevoPasswordHash;
            TipoCredencial = clsTipoCredencial.PASSWORD;
            PinHash = null;
            DebeCambiarPin = false;
        }

        if (activo is not null)
        {
            Activo = activo.Value;
        }

        if (cambiaCedula)
        {
            Cedula = NormalizarCedula(cedula);
        }

        if (cambiaCliente || Rol == clsRol.SUPER_ADMIN)
        {
            ClienteId = ClienteValido(cambiaCliente ? clienteId : ClienteId);
        }

        // Un conductor anterior sin cedula se puede seguir editando (por ejemplo, retirarlo) mientras no se toque su rol ni su cedula.
        AsegurarCoherencia(exigeCedulaDelConductor: cambiaCedula || rol is not null);
        Tocar(ahora);
    }

    /// <summary>
    /// Asigna o reinicia el PIN. El usuario pasa a entrar con cedula y PIN (queda sin contrasena), se levanta el bloqueo y, si
    /// <paramref name="debeCambiar"/>, debera cambiarlo antes de continuar.
    /// </summary>
    public void AsignarPin(string pinHash, bool debeCambiar, DateTimeOffset ahora)
    {
        TipoCredencial = clsTipoCredencial.PIN;
        PinHash = pinHash;
        PasswordHash = null;
        DebeCambiarPin = debeCambiar;
        IntentosFallidos = 0;
        BloqueadoHasta = null;
        AsegurarCoherencia();
        Tocar(ahora);
    }

    /// <summary>Cambia el cliente del usuario (el super administrador siempre queda sin cliente).</summary>
    public void AsignarCliente(Guid? clienteId, DateTimeOffset ahora)
    {
        ClienteId = ClienteValido(clienteId);
        Tocar(ahora);
    }

    /// <summary>La propia persona cambio su PIN: ya no debe cambiarlo.</summary>
    public void CambiarPin(string pinHash, DateTimeOffset ahora)
    {
        AsegurarEntraConPin();
        PinHash = pinHash;
        DebeCambiarPin = false;
        Tocar(ahora);
    }

    /// <summary>Obliga a cambiar el PIN en el proximo uso, sin cambiarlo.</summary>
    public void ForzarCambioDePin(DateTimeOffset ahora)
    {
        AsegurarEntraConPin();
        DebeCambiarPin = true;
        Tocar(ahora);
    }

    public bool EstaBloqueado(DateTimeOffset ahora) => BloqueadoHasta is { } hasta && hasta > ahora;

    /// <summary>
    /// Suma un PIN errado. Al llegar a <paramref name="maxIntentos"/> bloquea la cuenta <paramref name="minutosBloqueo"/> minutos y
    /// reinicia el contador. Devuelve true si la cuenta quedo bloqueada.
    /// </summary>
    public bool RegistrarFalloDePin(DateTimeOffset ahora, int maxIntentos, int minutosBloqueo)
    {
        if (BloqueadoHasta is not null && !EstaBloqueado(ahora))
        {
            BloqueadoHasta = null;
            IntentosFallidos = 0;
        }

        IntentosFallidos++;
        var bloquea = IntentosFallidos >= Math.Max(maxIntentos, 1);
        if (bloquea)
        {
            BloqueadoHasta = ahora.AddMinutes(Math.Max(minutosBloqueo, 1));
            IntentosFallidos = 0;
        }

        Tocar(ahora);
        return bloquea;
    }

    /// <summary>Ingreso correcto: limpia los intentos y el bloqueo y marca el ultimo acceso.</summary>
    public void RegistrarAcceso(DateTimeOffset ahora)
    {
        IntentosFallidos = 0;
        BloqueadoHasta = null;
        UltimoAccesoAt = ahora;
        Tocar(ahora);
    }

    /// <summary>Un administrador levanta el bloqueo antes de que venza.</summary>
    public void Desbloquear(DateTimeOffset ahora)
    {
        IntentosFallidos = 0;
        BloqueadoHasta = null;
        Tocar(ahora);
    }

    /// <summary>Reemplaza el hash (por ejemplo, al actualizar un hash legado en el siguiente login).</summary>
    public void ReemplazarPasswordHash(string passwordHash, DateTimeOffset ahora)
    {
        PasswordHash = passwordHash;
        Tocar(ahora);
    }

    /// <summary>Reemplaza el hash del PIN (por ejemplo, al actualizar un hash en formato anterior en el siguiente ingreso).</summary>
    public void ReemplazarPinHash(string pinHash, DateTimeOffset ahora)
    {
        PinHash = pinHash;
        Tocar(ahora);
    }

    /// <summary>Vincula al usuario con su identidad en el portal (inicio de sesion unico).</summary>
    public void VincularPortal(long portalUsuarioId, DateTimeOffset ahora)
    {
        if (portalUsuarioId <= 0)
        {
            throw new clsValidacionException("El id del portal debe ser positivo");
        }

        PortalUsuarioId = portalUsuarioId;
        Tocar(ahora);
    }

    /// <summary>Baja logica: conserva el historial de operaciones del usuario.</summary>
    public void Retirar(DateTimeOffset ahora)
    {
        Activo = false;
        Tocar(ahora);
    }

    /// <summary>Un usuario no puede eliminarse a si mismo.</summary>
    public void AsegurarPuedeSerEliminadoPor(Guid actorId)
    {
        if (Id == actorId)
        {
            throw new clsConflictoException("No puedes eliminar tu propio usuario");
        }
    }

    /// <summary>Quita puntos, comas y espacios y pasa a mayuscula, para que "1.234.567" y "1234567" sean la misma cedula. Vacio = sin cedula.</summary>
    public static string? NormalizarCedula(string? cedula)
    {
        var limpia = cedula is null ? string.Empty : new string(cedula.Where(c => c is not ('.' or ',') && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (limpia.Length == 0)
        {
            return null;
        }

        return CEDULA_MIN <= limpia.Length && limpia.Length <= CEDULA_MAX && limpia.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? limpia
            : throw clsErroresCampos.De("cedula", $"La cédula admite letras, números y guion (de {CEDULA_MIN} a {CEDULA_MAX} caracteres)");
    }

    /// <summary>Correo en minusculas, o nulo si viene vacio. Un texto que no es un correo es un error del campo.</summary>
    public static string? NormalizarEmail(string? email)
    {
        var limpio = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(limpio))
        {
            return null;
        }

        return FormatoEmail().IsMatch(limpio) ? limpio : throw clsErroresCampos.De("email", "Email inválido");
    }

    /// <summary>
    /// Reglas entre campos: con contrasena hay correo; con PIN hay cedula y el rol es de campo; el conductor siempre tiene cedula
    /// (con ella se le asignan viajes).
    /// </summary>
    private void AsegurarCoherencia(bool exigeCedulaDelConductor = true)
    {
        if (TipoCredencial == clsTipoCredencial.PASSWORD && Email is null)
        {
            throw clsErroresCampos.De("email", "El correo es requerido para entrar con contraseña");
        }

        if (TipoCredencial == clsTipoCredencial.PIN)
        {
            AsegurarEntraConPin();
            if (!clsTipoCredencial.ROLES_CON_PIN.Contains(Rol))
            {
                throw clsErroresCampos.De("rol", "Solo el conductor y el inspector pueden entrar con cédula y PIN");
            }
        }

        if ((TipoCredencial == clsTipoCredencial.PIN || (exigeCedulaDelConductor && Rol == clsRol.CONDUCTOR)) && Cedula is null)
        {
            throw clsErroresCampos.De("cedula", "La cédula es requerida para el conductor y para quien entra con PIN");
        }
    }

    private void AsegurarEntraConPin()
    {
        if (Cedula is null)
        {
            throw clsErroresCampos.De("cedula", "El usuario no tiene cédula: es necesaria para entrar con PIN");
        }

        if (!clsTipoCredencial.ROLES_CON_PIN.Contains(Rol))
        {
            throw clsErroresCampos.De("rol", "Solo el conductor y el inspector pueden entrar con cédula y PIN");
        }
    }

    private static string ValidarRol(string rol)
    {
        clsValidador.EnCatalogo(rol, clsRol.VALORES, "rol");
        return rol;
    }

    private string? SubtipoValido(string? subtipo)
    {
        if (subtipo is null)
        {
            return null;
        }

        clsValidador.EnCatalogo(subtipo, clsSubtipoInspector.VALORES, "subtipo_inspector");
        return Rol == clsRol.INSPECTOR ? subtipo : null;
    }

    /// <summary>El super administrador ve todos los clientes, asi que no pertenece a ninguno.</summary>
    private Guid? ClienteValido(Guid? clienteId) => Rol == clsRol.SUPER_ADMIN ? null : clienteId;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex FormatoEmail();
}
