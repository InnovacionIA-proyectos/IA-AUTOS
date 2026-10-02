import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/errores/error_api.dart';
import '../domain/sesion.dart';

enum FaseAuth { cargando, autenticado, noAutenticado }

class EstadoAuth {
  const EstadoAuth(this.fase, {this.sesion, this.aviso});

  final FaseAuth fase;
  final Sesion? sesion;

  /// Mensaje para mostrar en el login (por ejemplo, "Tu sesión expiró").
  final String? aviso;
}

/// Sesión del usuario: se restaura de forma segura al arrancar, se cierra al vencer el token (401) y nunca se
/// guarda fuera del almacén seguro.
class AuthController extends Notifier<EstadoAuth> {
  @override
  EstadoAuth build() {
    Future.microtask(_restaurar);
    return const EstadoAuth(FaseAuth.cargando);
  }

  Future<void> _restaurar() async {
    final sesion = await ref.read(almacenSesionProvider).leer();
    state = sesion == null ? const EstadoAuth(FaseAuth.noAutenticado) : EstadoAuth(FaseAuth.autenticado, sesion: sesion);
  }

  /// Devuelve null si entró bien o el mensaje de error para mostrar.
  Future<String?> iniciarSesion(String email, String password) async {
    try {
      final sesion = await ref.read(authRepositorioProvider).iniciarSesion(email.trim(), password);
      await ref.read(almacenSesionProvider).guardar(sesion);
      state = EstadoAuth(FaseAuth.autenticado, sesion: sesion);
      return null;
    } on ErrorApi catch (e) {
      return switch (e.status) {
        401 => 'Correo o contraseña incorrectos.',
        429 => 'Demasiados intentos. Espera un minuto e inténtalo de nuevo.',
        0 => 'No se pudo conectar con el servidor. Revisa tu conexión.',
        _ => e.message,
      };
    } catch (_) {
      return 'No se pudo iniciar sesión. Inténtalo de nuevo.';
    }
  }

  Future<void> cerrarSesion() async {
    await ref.read(almacenSesionProvider).borrar();
    state = const EstadoAuth(FaseAuth.noAutenticado);
  }

  /// La API rechazó el token (venció o ya no es válido): se cierra la sesión y se avisa. La cola de envío se
  /// conserva y se retomará al volver a entrar.
  void sesionExpirada() {
    if (state.fase != FaseAuth.autenticado) return;
    ref.read(almacenSesionProvider).borrar();
    state = const EstadoAuth(FaseAuth.noAutenticado, aviso: 'Tu sesión expiró. Inicia sesión de nuevo.');
  }
}

final authControllerProvider = NotifierProvider<AuthController, EstadoAuth>(AuthController.new);
