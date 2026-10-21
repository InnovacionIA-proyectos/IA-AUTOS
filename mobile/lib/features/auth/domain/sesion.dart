class Usuario {
  const Usuario({
    required this.id,
    required this.nombre,
    required this.email,
    required this.rol,
    this.cedula,
    this.debeCambiarPin = false,
  });

  factory Usuario.fromJson(Map<String, dynamic> json) => Usuario(
        id: json['id'] as String,
        nombre: json['nombre'] as String,
        email: (json['email'] as String?) ?? '',
        rol: json['rol'] as String,
        cedula: json['cedula'] as String?,
        debeCambiarPin: json['debe_cambiar_pin'] as bool? ?? false,
      );

  final String id;
  final String nombre;
  final String email;
  final String rol;

  /// Quien entra con cédula y PIN puede no tener correo; quien entra con contraseña no tiene cédula.
  final String? cedula;

  /// El PIN lo asignó o reinició un administrador: hasta cambiarlo, el resto de la API responde 403.
  final bool debeCambiarPin;

  Map<String, dynamic> toJson() =>
      {'id': id, 'nombre': nombre, 'email': email, 'rol': rol, 'cedula': cedula, 'debe_cambiar_pin': debeCambiarPin};
}

class Sesion {
  const Sesion({required this.token, required this.usuario});

  factory Sesion.fromJson(Map<String, dynamic> json) =>
      Sesion(token: json['token'] as String, usuario: Usuario.fromJson(json['usuario'] as Map<String, dynamic>));

  final String token;
  final Usuario usuario;

  Map<String, dynamic> toJson() => {'token': token, 'usuario': usuario.toJson()};
}

/// Acceso a la API de autenticación.
abstract interface class AuthRepositorio {
  Future<Sesion> iniciarSesion(String email, String password);

  /// Conductor e inspector de campo, que pueden no tener correo.
  Future<Sesion> iniciarSesionConPin(String cedula, String pin);

  /// Cambia el PIN propio. Obligatorio cuando lo asignó o reinició un administrador.
  Future<void> cambiarPin(String pinActual, String pinNuevo);
}
