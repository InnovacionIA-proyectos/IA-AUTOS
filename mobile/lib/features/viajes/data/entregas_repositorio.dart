import 'dart:convert';

import '../../../core/db/cache_lecturas.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/http/cliente_api.dart';
import '../../sync/domain/job.dart';
import '../domain/entrega_punto.dart';
import '../domain/espera_del_punto.dart';

/// Lo que hay por entregar en cada parada de un viaje. Solo lectura: la firma va por la cola.
class EntregasRepositorio {
  /// [copias] y [usuarioActual] guardan la copia sin señal de cada parada (S8-HU-05.2 CA-3). Se leen perezosamente:
  /// solo se tocan cuando hay algo que guardar o cuando no hubo señal.
  EntregasRepositorio(this._api, {this._copias, this._usuarioActual});

  final ClienteApi _api;
  final CacheLecturasRepo Function()? _copias;
  final String? Function()? _usuarioActual;

  /// La copia es por usuario, como la de los viajes: en un equipo compartido nadie lee la parada de otro.
  static String clave(String usuarioId, String viajeId, String lugarId) => 'parada.$usuarioId.$viajeId.$lugarId';

  /// Una entrega por parada del viaje, y cuántos carros no tienen destino.
  Future<EntregasDelViaje> delViaje(String viajeId) async {
    final r = await _api.get('/api/viajes/${Uri.encodeComponent(viajeId)}/entregas') as Map<String, dynamic>;
    return EntregasDelViaje.fromJson(r);
  }

  /// Una sola parada: lo que abre «Entregar en esta parada» y lo que se refresca para saber si el conductor ya
  /// marcó «Llegué» (S8-HU-04.2 CA-2).
  ///
  /// Guarda la copia de la parada, con su espera y los X e Y vigentes: es lo que deja contar el reloj de «No hay
  /// inspector» tras reiniciar la app sin señal (S8-HU-05.2 CA-3). Sin señal devuelve esa copia, marcada como tal;
  /// cualquier otro error (un 403, un 404) se propaga, porque ahí la copia diría algo que ya no es cierto.
  Future<EntregaPunto> deParada(String viajeId, String lugarId) async {
    final usuarioId = _usuarioActual?.call();
    final clave = usuarioId == null ? null : EntregasRepositorio.clave(usuarioId, viajeId, lugarId);
    final cache = _copias;
    try {
      final r = await _api.get('/api/viajes/${Uri.encodeComponent(viajeId)}/entregas/${Uri.encodeComponent(lugarId)}');
      final json = r as Map<String, dynamic>;
      if (clave != null && cache != null) {
        try {
          await cache().escribir(clave, jsonEncode(json));
        } catch (_) {
          // Una copia que no se pudo guardar no estropea la parada que sí llegó.
        }
      }
      return EntregaPunto.fromJson(json);
    } on ErrorApi catch (e) {
      if (e.status != 0 || clave == null || cache == null) rethrow;
      String? copia;
      try {
        copia = await cache().leer(clave);
      } catch (_) {
        copia = null;
      }
      if (copia == null) rethrow;
      return EntregaPunto.fromJson(jsonDecode(copia) as Map<String, dynamic>, copiaSinSenal: true);
    }
  }

  /// S8-HU-05.2 CA-3: al activar «No hay inspector», la hora de activación y los X e Y se anotan en la copia sin señal
  /// de la parada. Así el reloj sigue después de que la activación sale de la cola, aunque la app se reinicie sin
  /// señal antes de volver a pedir la parada. La copia del servidor la reemplaza en cuanto llega.
  Future<void> anotarActivacion(SinInspectorParadaJob activacion) async {
    final usuarioId = _usuarioActual?.call();
    final cache = _copias;
    if (usuarioId == null || cache == null) return;
    final clave = EntregasRepositorio.clave(usuarioId, activacion.viajeId, activacion.lugarId);
    try {
      final copia = await cache().leer(clave);
      if (copia == null) return;
      final json = jsonDecode(copia) as Map<String, dynamic>;
      json['espera'] = EsperaDelPunto.anotada(activacion).toJson();
      await cache().escribir(clave, jsonEncode(json));
    } catch (_) {
      // Sin copia anotada el reloj sigue con la activación mientras esté en la cola, y después con el servidor.
    }
  }

  /// La revisión de llegada de un carro, para saber si ya está cerrada y qué daños con foto justifican rechazarlo
  /// (S8-HU-04.2 CA-4).
  Future<RevisionDeLlegada> revisionDeLlegada(String inspeccionId) async {
    final r = await _api.get('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}');
    return RevisionDeLlegada.fromJson(r as Map<String, dynamic>);
  }
}
