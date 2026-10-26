/// Lo que la persona puede hacer, en los nombres que manda el servidor en `GET /api/auth/me`. Salen de las mismas
/// listas de roles que autorizan los endpoints, así que el menú y los permisos no se desalinean.
abstract final class Capacidad {
  static const verInspecciones = 'ver_inspecciones';
  static const registrarInspecciones = 'registrar_inspecciones';
  static const verViajes = 'ver_viajes';
  static const gestionarViajes = 'gestionar_viajes';
  static const gestionarUsuarios = 'gestionar_usuarios';
  static const administrarCatalogos = 'administrar_catalogos';
  static const cargarVehiculos = 'cargar_vehiculos';
  static const verReportes = 'ver_reportes';
}

/// Respaldo cuando no se pudieron traer del servidor: una sesión restaurada de una versión anterior, o un `/me`
/// que no respondió. Reproduce `clsCapacidades.De` del backend y por eso puede quedarse corta si allá cambian los
/// roles; es una red de seguridad para no dejar a nadie sin menú, no la fuente de verdad.
List<String> capacidadesDeRol(String rol) => [
      Capacidad.verInspecciones,
      Capacidad.verViajes,
      Capacidad.verReportes,
      if (rol == 'inspector' || rol == 'conductor' || rol == 'admin') Capacidad.registrarInspecciones,
      if (rol == 'admin' || rol == 'super_admin' || rol == 'coordinador_calidad') ...[
        Capacidad.gestionarViajes,
        Capacidad.gestionarUsuarios,
        Capacidad.administrarCatalogos,
      ],
      if (rol == 'admin' || rol == 'super_admin') Capacidad.cargarVehiculos,
    ];

class Usuario {
  const Usuario({
    required this.id,
    required this.nombre,
    required this.email,
    required this.rol,
    this.cedula,
    this.debeCambiarPin = false,
    this.capacidades = const [],
  });

  factory Usuario.fromJson(Map<String, dynamic> json) => Usuario(
        id: json['id'] as String,
        nombre: json['nombre'] as String,
        email: (json['email'] as String?) ?? '',
        rol: json['rol'] as String,
        cedula: json['cedula'] as String?,
        debeCambiarPin: json['debe_cambiar_pin'] as bool? ?? false,
        // El login no las manda (salen de /me) y las sesiones guardadas por versiones anteriores tampoco.
        capacidades: [...?(json['capacidades'] as List<dynamic>?)?.cast<String>()],
      );

  final String id;
  final String nombre;
  final String email;
  final String rol;

  /// Quien entra con cédula y PIN puede no tener correo; quien entra con contraseña no tiene cédula.
  final String? cedula;

  /// El PIN lo asignó o reinició un administrador: hasta cambiarlo, el resto de la API responde 403.
  final bool debeCambiarPin;

  /// Lo que puede hacer, según el servidor. Si viene vacía se cae al respaldo por rol.
  final List<String> capacidades;

  /// Las capacidades efectivas: nunca vacías, para que nadie se quede sin menú.
  List<String> get capacidadesEfectivas => capacidades.isEmpty ? capacidadesDeRol(rol) : capacidades;

  bool puede(String capacidad) => capacidadesEfectivas.contains(capacidad);

  /// Copia con otra marca de cambio de PIN. Es lo único que la app cambia de un usuario ya autenticado, y
  /// concentrarlo aquí evita que un campo nuevo se pierda en la copia.
  Usuario conCambioDePin(bool valor) => copiar(debeCambiarPin: valor);

  Usuario copiar({bool? debeCambiarPin, List<String>? capacidades}) => Usuario(
        id: id,
        nombre: nombre,
        email: email,
        rol: rol,
        cedula: cedula,
        debeCambiarPin: debeCambiarPin ?? this.debeCambiarPin,
        capacidades: capacidades ?? this.capacidades,
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'nombre': nombre,
        'email': email,
        'rol': rol,
        'cedula': cedula,
        'debe_cambiar_pin': debeCambiarPin,
        'capacidades': capacidades,
      };
}

class Sesion {
  const Sesion({required this.token, required this.usuario});

  factory Sesion.fromJson(Map<String, dynamic> json) =>
      Sesion(token: json['token'] as String, usuario: Usuario.fromJson(json['usuario'] as Map<String, dynamic>));

  final String token;
  final Usuario usuario;

  Sesion conUsuario(Usuario nuevo) => Sesion(token: token, usuario: nuevo);

  Map<String, dynamic> toJson() => {'token': token, 'usuario': usuario.toJson()};
}

/// Acceso a la API de autenticación.
abstract interface class AuthRepositorio {
  Future<Sesion> iniciarSesion(String email, String password);

  /// Conductor e inspector de campo, que pueden no tener correo.
  Future<Sesion> iniciarSesionConPin(String cedula, String pin);

  /// Cambia el PIN propio. Obligatorio cuando lo asignó o reinició un administrador.
  Future<void> cambiarPin(String pinActual, String pinNuevo);

  /// Capacidades del perfil (`GET /api/auth/me`). Se puede pedir con un PIN temporal: el endpoint está exento.
  Future<List<String>> capacidades();
}
