import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/db/cache_lecturas.dart';
import '../../auth/presentation/auth_controller.dart';
import '../domain/viaje.dart';
import '../providers.dart';

/// Dónde se guarda lo visto: la caché de lecturas del teléfono. Las pruebas la reemplazan por una en memoria.
abstract interface class AlmacenDestinosVistos {
  Future<String?> leer(String clave);
  Future<void> escribir(String clave, String valor);
}

class _DestinosVistosEnCache implements AlmacenDestinosVistos {
  const _DestinosVistosEnCache(this._cache);

  final CacheLecturasRepo _cache;

  @override
  Future<String?> leer(String clave) => _cache.leer(clave);

  @override
  Future<void> escribir(String clave, String valor) => _cache.escribir(clave, valor);
}

final almacenDestinosVistosProvider =
    Provider<AlmacenDestinosVistos>((ref) => _DestinosVistosEnCache(ref.watch(cacheViajesProvider)));

/// Qué cambio de destino ya vio el conductor en cada carro (S8-HU-03.2 CA-5).
///
/// Se guarda, por carro de cada viaje, la fecha del cambio que había cuando abrió ese carro. Si el servidor trae
/// otra, el destino cambió después: el carro sale con «Destino nuevo» hasta que lo vuelva a abrir. Se compara la
/// fecha del servidor con la del servidor, no con el reloj del celular, que puede andar corrido.
class DestinosVistos extends AsyncNotifier<Map<String, String>> {
  static String clave(String usuarioId) => 'destinos-vistos.$usuarioId';

  @override
  Future<Map<String, String>> build() async {
    final usuarioId = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));
    if (usuarioId == null) return const {};
    final crudo = await ref.watch(almacenDestinosVistosProvider).leer(clave(usuarioId));
    if (crudo == null) return const {};
    try {
      return {for (final e in (jsonDecode(crudo) as Map<String, dynamic>).entries) e.key: e.value as String};
    } catch (_) {
      return const {}; // copia dañada: se ignora, y a lo sumo un destino vuelve a salir como nuevo
    }
  }

  /// El conductor abrió este carro: lo que cambió hasta ahora ya lo vio.
  Future<void> marcarVisto(String viajeId, ViajeVehiculo unidad) async {
    final cambio = unidad.destinoCambiadoAt;
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    if (cambio == null || usuarioId == null) return;
    // Sobre lo ya leído: si se toca un carro antes de que cargue, partir de un estado vacío borraría lo visto.
    final anteriores = await future;
    final vistos = {...anteriores, _llave(viajeId, unidad.vin): _sello(cambio)};
    state = AsyncData(vistos);
    await ref.read(almacenDestinosVistosProvider).escribir(clave(usuarioId), jsonEncode(vistos));
  }
}

final destinosVistosProvider = AsyncNotifierProvider<DestinosVistos, Map<String, String>>(DestinosVistos.new);

/// Si el destino de este carro cambió después de la última vez que quien mira lo abrió. Mientras no se sepa qué
/// vio ([vistos] null), no: un resaltado que aparece y se va distrae más de lo que ayuda.
bool destinoNuevo(Map<String, String>? vistos, String viajeId, ViajeVehiculo unidad) {
  final cambio = unidad.destinoCambiadoAt;
  if (cambio == null || vistos == null) return false;
  return vistos[_llave(viajeId, unidad.vin)] != _sello(cambio);
}

String _llave(String viajeId, String vin) => '$viajeId|$vin';
String _sello(DateTime fecha) => fecha.toUtc().toIso8601String();
