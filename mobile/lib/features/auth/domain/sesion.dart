class Usuario {
  const Usuario({required this.id, required this.nombre, required this.email, required this.rol});

  factory Usuario.fromJson(Map<String, dynamic> json) => Usuario(
        id: json['id'] as String,
        nombre: json['nombre'] as String,
        email: (json['email'] as String?) ?? '',
        rol: json['rol'] as String,
      );

  final String id;
  final String nombre;
  final String email;
  final String rol;

  Map<String, dynamic> toJson() => {'id': id, 'nombre': nombre, 'email': email, 'rol': rol};
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
}
