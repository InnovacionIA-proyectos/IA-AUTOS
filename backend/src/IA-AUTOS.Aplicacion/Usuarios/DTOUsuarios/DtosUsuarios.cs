namespace IAAutos.Aplicacion.Usuarios.DTOUsuarios;

/// <summary>
/// Alta de un usuario. Con <c>Password</c> (y correo) entra con correo y contrasena. Sin contrasena entra con cedula y PIN (solo
/// conductor e inspector): <c>Pin</c> es opcional y, si no se envia, se genera uno temporal que la respuesta devuelve una sola vez.
/// <c>ClienteId</c> solo lo elige el super administrador; los demas crean usuarios de su propio cliente.
/// </summary>
public sealed record CrearUsuarioRequest(
    string? Nombre,
    string? Email,
    string? Rol,
    string? SubtipoInspector,
    string? Password,
    string? Cedula = null,
    string? Pin = null,
    Guid? ClienteId = null);

/// <summary>
/// Cuerpo de PATCH /api/usuarios/{id} (todos los campos son opcionales). El endpoint lee el cuerpo campo a campo: <c>null</c> limpia
/// <c>subtipo_inspector</c>, <c>email</c> y <c>cedula</c>. Por codigo (el comando de operacion) lo nulo se conserva.
/// </summary>
public sealed record ActualizarUsuarioRequest(
    string? Nombre,
    string? Email,
    string? Rol,
    string? SubtipoInspector,
    string? Password,
    bool? Activo,
    string? Cedula = null,
    Guid? ClienteId = null);

/// <summary>
/// Un usuario tal como lo ve quien administra. <c>PinTemporal</c> solo viene en la respuesta que genera un PIN (alta sin PIN, asignar o
/// reiniciar) y se muestra una unica vez: no se guarda en claro.
/// </summary>
public sealed record UsuarioDto(
    Guid Id,
    string Nombre,
    string? Email,
    string Rol,
    string? SubtipoInspector,
    string? Cedula,
    bool Activo,
    string TipoCredencial,
    bool DebeCambiarPin,
    DateTimeOffset? BloqueadoHasta,
    DateTimeOffset? UltimoAccesoAt,
    Guid? ClienteId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? PinTemporal = null);

/// <summary>Filtros de la lista de usuarios (todos opcionales) y su pagina y orden. <c>Q</c> busca en nombre, correo y cedula.</summary>
public sealed record FiltrosUsuariosRequest(string? Q, string? Rol, string? Activo, string? ClienteId, string? Pagina = null, string? Tamano = null, string? Orden = null);

/// <summary>Una pagina de usuarios.</summary>
public sealed record ListadoUsuariosDto(IReadOnlyList<UsuarioDto> Items, int Total, int Pagina, int Tamano);

/// <summary>Resultado de eliminar: "eliminado" (borrado real) o "retirado" (baja logica por tener historial).</summary>
public sealed record EliminarUsuarioResponse(string Modo);

/// <summary>Vista reducida para quien no administra usuarios (selector de entrega y recibo en la app): sin correos ni estado.</summary>
public sealed record UsuarioListaDto(Guid Id, string Nombre, string Rol);

/// <summary>Asignar o reiniciar el PIN. Sin <c>Pin</c> se genera uno temporal. El usuario debe cambiarlo antes de continuar.</summary>
public sealed record AsignarPinRequest(string? Pin);

/// <summary>Una fila del Excel de conductores (nombre, cedula y correo opcional). <c>Fila</c> es el numero de fila del archivo.</summary>
public sealed record FilaCargaConductorRequest(int? Fila, string? Nombre, string? Cedula, string? Email);

public sealed record CargarConductoresRequest(IReadOnlyList<FilaCargaConductorRequest>? Filas);

/// <summary>Resultado de una fila: valida o con sus errores. Al confirmar, <c>UsuarioId</c> y el <c>PinTemporal</c> del conductor creado (se muestra una sola vez).</summary>
public sealed record FilaCargaConductorDto(
    int Fila,
    string? Nombre,
    string? Cedula,
    bool Valida,
    IReadOnlyList<string> Errores,
    Guid? UsuarioId,
    string? PinTemporal);

/// <summary>Vista previa o resultado de la carga de conductores. <c>Confirmado</c> es falso en la vista previa (no se guarda nada).</summary>
public sealed record ResultadoCargaConductoresDto(
    bool Confirmado,
    int Total,
    int Validas,
    int Invalidas,
    int Creados,
    IReadOnlyList<FilaCargaConductorDto> Filas);
