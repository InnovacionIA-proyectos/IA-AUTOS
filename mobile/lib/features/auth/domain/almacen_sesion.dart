import 'sesion.dart';

/// Guarda la sesión (JWT y datos del usuario) de forma segura: Keystore en Android. Nunca en texto plano en
/// SharedPreferences ni en SQLite.
abstract interface class AlmacenSesion {
  Future<Sesion?> leer();
  Future<void> guardar(Sesion sesion);
  Future<void> borrar();
}
