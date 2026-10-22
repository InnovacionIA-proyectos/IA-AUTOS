import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../domain/sesion.dart';
import '../providers.dart';

enum FaseAuth { cargando, autenticado, noAutenticado }

class EstadoAuth {
  const EstadoAuth(this.fase, {this.sesion, this.aviso});

  final FaseAuth fase;
  final Sesion? sesion;

  /// Mensaje para mostrar en el login (por ejemplo, "Tu sesión expiró").
  final String? aviso;

  /// Hay sesión **y** ya se puede usar el resto de la API. Con un PIN temporal el servidor responde
  /// 403 PIN_CHANGE_REQUIRED a todo salvo cambiarlo, así que lo que arranca solo —la cola de envíos y los
  /// catálogos— debe esperar: si no, gasta intentos y marca como fallido lo que solo estaba a destiempo.
  bool get puedeOperar => fase == FaseAuth.autenticado && !(sesion?.usuario.debeCambiarPin ?? false);
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

  /// Ingreso del personal de campo con cédula y PIN. Devuelve null si entró bien o el mensaje de error.
  Future<String?> iniciarSesionConPin(String cedula, String pin) async {
    try {
      final sesion = await ref.read(authRepositorioProvider).iniciarSesionConPin(cedula.trim(), pin);
      await ref.read(almacenSesionProvider).guardar(sesion);
      state = EstadoAuth(FaseAuth.autenticado, sesion: sesion);
      return null;
    } on ErrorApi catch (e) {
      return switch (e.status) {
        401 => 'Cédula o PIN incorrectos.',
        // La cuenta se bloquea tras varios PIN errados seguidos; el servidor dice hasta cuándo.
        423 => _mensajeBloqueo(e),
        429 => 'Demasiados intentos. Espera un minuto e inténtalo de nuevo.',
        0 => 'No se pudo conectar con el servidor. Revisa tu conexión.',
        _ => e.message,
      };
    } catch (_) {
      return 'No se pudo iniciar sesión. Inténtalo de nuevo.';
    }
  }

  /// Cambia el PIN propio y deja la sesión lista para usar el resto de la app con el mismo token.
  Future<String?> cambiarPin(String pinActual, String pinNuevo) async {
    final sesion = state.sesion;
    if (sesion == null) return 'Tu sesión no es válida. Inicia sesión de nuevo.';
    try {
      await ref.read(authRepositorioProvider).cambiarPin(pinActual, pinNuevo);
    } on ErrorApi catch (e) {
      return switch (e.status) {
        400 => _mensajeDeCampo(e) ?? 'Revisa el PIN actual y el nuevo.',
        423 => _mensajeBloqueo(e),
        0 => 'No se pudo conectar con el servidor. Revisa tu conexión.',
        _ => e.message,
      };
    } catch (_) {
      return 'No se pudo cambiar el PIN. Inténtalo de nuevo.';
    }

    // El token sigue sirviendo: solo deja de exigirse el cambio.
    final actualizada = Sesion(token: sesion.token, usuario: sesion.usuario.conCambioDePin(false));
    await _guardarTolerante(actualizada);
    state = EstadoAuth(FaseAuth.autenticado, sesion: actualizada);
    return null;
  }

  /// La API respondió 403 PIN_CHANGE_REQUIRED: a esta persona le reiniciaron el PIN con la sesión abierta. Se
  /// marca la sesión y el router la lleva a cambiarlo, igual que en el primer ingreso.
  void requiereCambioDePin() {
    final sesion = state.sesion;
    if (sesion == null || sesion.usuario.debeCambiarPin) return;
    final actualizada = Sesion(token: sesion.token, usuario: sesion.usuario.conCambioDePin(true));
    unawaited(_guardarTolerante(actualizada));
    state = EstadoAuth(FaseAuth.autenticado, sesion: actualizada);
  }

  /// El cambio ya ocurrió en el servidor, así que un fallo del almacén seguro no puede escaparse: dejaría la
  /// pantalla de cambio de PIN girando sin salida. `guardar` es el único de los tres métodos del almacén que
  /// lanza; si falla, se borra lo que hubiera para no restaurar al arrancar una sesión con la marca vieja.
  Future<void> _guardarTolerante(Sesion sesion) async {
    final almacen = ref.read(almacenSesionProvider);
    try {
      await almacen.guardar(sesion);
    } catch (_) {
      await almacen.borrar();
    }
  }

  /// «Cuenta bloqueada hasta las 14:35» a partir de `details.bloqueado_hasta`.
  static String _mensajeBloqueo(ErrorApi e) {
    final hasta = (e.details is Map ? (e.details! as Map)['bloqueado_hasta'] : null) as String?;
    final fecha = hasta == null ? null : DateTime.tryParse(hasta)?.toLocal();
    if (fecha == null) return 'Cuenta bloqueada por varios PIN errados. Espera unos minutos o pide que te desbloqueen.';
    final hora = '${fecha.hour.toString().padLeft(2, '0')}:${fecha.minute.toString().padLeft(2, '0')}';
    return 'Cuenta bloqueada por varios PIN errados. Vuelve a intentar a las $hora o pide que te desbloqueen.';
  }

  /// Primer error por campo que devuelve el servidor (`pin_actual` o `pin_nuevo`).
  static String? _mensajeDeCampo(ErrorApi e) {
    final campos = (e.details is Map ? (e.details! as Map)['fieldErrors'] : null) as Map?;
    for (final valores in campos?.values ?? const []) {
      if (valores is List && valores.isNotEmpty) return valores.first.toString();
    }
    return null;
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
