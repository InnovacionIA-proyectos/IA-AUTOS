import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../features/auth/domain/sesion.dart';

/// Guarda la sesión (JWT y datos del usuario) de forma segura: Keystore en Android. Nunca en texto plano en
/// SharedPreferences ni en SQLite.
abstract interface class AlmacenSesion {
  Future<Sesion?> leer();
  Future<void> guardar(Sesion sesion);
  Future<void> borrar();
}

class AlmacenSesionSeguro implements AlmacenSesion {
  AlmacenSesionSeguro([FlutterSecureStorage? almacen]) : _almacen = almacen ?? const FlutterSecureStorage();

  static const _clave = 'traza.sesion.v1';
  final FlutterSecureStorage _almacen;

  @override
  Future<Sesion?> leer() async {
    try {
      final crudo = await _almacen.read(key: _clave);
      if (crudo == null) return null;
      final sesion = Sesion.fromJson(jsonDecode(crudo) as Map<String, dynamic>);
      return sesion.token.isEmpty ? null : sesion;
    } catch (_) {
      // Datos corruptos o ilegibles (por ejemplo tras restaurar una copia): se trata como "sin sesión".
      return null;
    }
  }

  @override
  Future<void> guardar(Sesion sesion) => _almacen.write(key: _clave, value: jsonEncode(sesion.toJson()));

  @override
  Future<void> borrar() async {
    try {
      await _almacen.delete(key: _clave);
    } catch (_) {
      // Sin nada que borrar o almacén no disponible: la sesión en memoria ya se descartó.
    }
  }
}
