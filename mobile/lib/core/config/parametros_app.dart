import 'dart:async';
import 'dart:convert';
import 'dart:developer' as developer;

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../../features/auth/presentation/auth_controller.dart';
import '../../features/sync/providers.dart';
import '../db/cache_lecturas.dart';
import '../http/cliente_api.dart';

/// Los dos valores que TI ajusta en el portal y que la app usa en la captura (`GET /api/parametros/app`, S8-HU-09.1
/// CA-10): cuántas fotos de la acomodación pide el cierre de la salida del cargue y desde cuántos metros de margen
/// del GPS se avisa de precisión baja. Un campo que no llega queda en null y manda el valor de hoy de la regla: ya
/// no hay un valor fijado al compilar (S8-HU-09.2 CA-1).
class ParametrosApp {
  const ParametrosApp({this.acomodacionFotosMinimas, this.gpsPrecisionMinimaM});

  factory ParametrosApp.fromJson(Map<String, dynamic> json) => ParametrosApp(
        // 0 es válido: el servidor lo admite y entonces el cierre no exige fotos de la niñera (la captura no las pide).
        acomodacionFotosMinimas: switch (json['acomodacion_fotos_minimas']) {
          final num n when n >= 0 => n.toInt(),
          _ => null,
        },
        gpsPrecisionMinimaM: switch (json['gps_precision_minima_m']) {
          final num n when n > 0 => n.toDouble(),
          _ => null,
        },
      );

  final int? acomodacionFotosMinimas;
  final double? gpsPrecisionMinimaM;

  /// Estos valores, y los que falten, de [anterior]: un campo que llegó vacío no borra el que ya se sabía.
  ParametrosApp completadoCon(ParametrosApp? anterior) => ParametrosApp(
        acomodacionFotosMinimas: acomodacionFotosMinimas ?? anterior?.acomodacionFotosMinimas,
        gpsPrecisionMinimaM: gpsPrecisionMinimaM ?? anterior?.gpsPrecisionMinimaM,
      );

  Map<String, dynamic> toJson() => {
        'acomodacion_fotos_minimas': acomodacionFotosMinimas,
        'gps_precision_minima_m': gpsPrecisionMinimaM,
      };
}

/// De dónde salen los parámetros de la app. Null si no hay ni respuesta del servidor ni copia guardada.
abstract interface class FuenteParametrosApp {
  /// Pide los del servidor y los guarda; sin respuesta, la copia local.
  Future<ParametrosApp?> leer();

  /// Solo la copia local, sin ir a la red. Null si nunca se guardó ninguna.
  Future<ParametrosApp?> guardados();
}

/// Los pide al servidor y guarda la respuesta; sin señal (o con un error) usa la última que guardó, para que la
/// captura sin señal exija lo mismo que exigió la última vez que hubo.
class ParametrosAppDelServidor implements FuenteParametrosApp {
  ParametrosAppDelServidor(this._api, this._cache);

  static const ruta = '/api/parametros/app';
  static const clave = 'parametros-app';

  /// Lo que se acaba de traer vale este tiempo sin volver a pedirlo. Es la caché del servidor (un cambio en el portal
  /// se ve en un minuto o menos): pedirlo otra vez antes devolvería lo mismo. Así el refresco al volver la señal y la
  /// pantalla que lo estaba mirando hacen una sola petición, no dos.
  static const vigencia = Duration(minutes: 1);

  final ClienteApi _api;
  final CacheLecturasRepo _cache;

  ParametrosApp? _reciente;
  DateTime? _recienteEn;

  @override
  Future<ParametrosApp?> leer() async {
    if ((_reciente, _recienteEn) case (final reciente?, final en?) when DateTime.now().difference(en) < vigencia) {
      return reciente;
    }
    try {
      final respuesta = await _api.get(ruta);
      if (respuesta is Map<String, dynamic>) {
        // Un campo que llega nulo o fuera de rango no pisa el bueno de la copia local.
        final parametros = ParametrosApp.fromJson(respuesta).completadoCon(await guardados());
        await _cache.escribir(clave, jsonEncode(parametros.toJson()));
        _reciente = parametros;
        _recienteEn = DateTime.now();
        return parametros;
      }
    } catch (_) {
      // Sin señal, sin sesión o un servidor sin la ruta: se sigue con lo guardado.
    }
    return guardados();
  }

  @override
  Future<ParametrosApp?> guardados() async {
    final guardado = await _cache.leer(clave);
    if (guardado == null) return null;
    try {
      return ParametrosApp.fromJson(jsonDecode(guardado) as Map<String, dynamic>);
    } on FormatException {
      return null;
    }
  }
}

final fuenteParametrosAppProvider = Provider<FuenteParametrosApp>(
  (ref) => ParametrosAppDelServidor(ref.watch(clienteApiProvider), CacheLecturasRepo(ref.watch(appDatabaseProvider))),
);

/// Se vuelve a pedir cada vez que una pantalla lo empieza a mirar (al abrir la captura) y al cambiar de sesión:
/// así un cambio en el portal se nota en la siguiente captura (S8-HU-09 ESC-06).
///
/// Primero da la copia local, al instante, y después lo que diga el servidor (S8-HU-09.2 CA-1): sin señal la
/// petición tarda lo que tarda en rendirse, y mientras tanto la captura avisaba con el valor inicial de la regla y
/// no con el último que dijo TI.
final parametrosAppProvider = StreamProvider.autoDispose<ParametrosApp?>((ref) async* {
  ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));
  final fuente = ref.watch(fuenteParametrosAppProvider);
  if (await fuente.guardados() case final copia?) yield copia;
  final parametros = await fuente.leer();
  if (parametros?.acomodacionFotosMinimas == null || parametros?.gpsPrecisionMinimaM == null) {
    // S8-HU-01.3 CA-3: si el valor no llega se usa el de hoy, y se deja dicho en el registro de la app.
    developer.log(
      'Sin los parámetros de la app (${ParametrosAppDelServidor.ruta}), del servidor ni guardados: '
      'se usan los valores iniciales de la regla.',
      name: 'parametros',
    );
  }
  yield parametros;
});

/// Mantiene al día la copia local de los parámetros (S8-HU-09.2 CA-1): se piden al iniciar sesión y al volver una
/// señal estable, los mismos momentos en que se descargan los catálogos y se sincroniza la cola.
///
/// Abrir la captura ya los pide, pero en el patio y sin señal eso solo encuentra la copia: si la última vez que
/// hubo señal nadie abrió una captura, la copia sería la de hace días y la app exigiría lo que TI ya cambió. Por eso
/// se refrescan también cuando hay con qué, sin esperar a que alguien capture. La app lo mantiene vivo.
final refrescoParametrosAppProvider = Provider<void>((ref) {
  Future<void> refrescar() async {
    // `leer` guarda la respuesta; si falla, no toca la copia. Quien esté mirando los parámetros los vuelve a leer, sin
    // otra petición: la fuente recuerda lo que acaba de traer (ver [ParametrosAppDelServidor.vigencia]).
    await ref.read(fuenteParametrosAppProvider).leer();
    ref.invalidate(parametrosAppProvider);
  }

  final suscripcion = ref.watch(estadoRedProvider).cambios.listen((estable) {
    if (estable && ref.read(authControllerProvider).puedeOperar) unawaited(refrescar());
  });
  ref.onDispose(suscripcion.cancel);

  // Por transición y por `puedeOperar`, igual que los catálogos: con un PIN temporal la ruta también responde 403.
  ref.listen(authControllerProvider.select((e) => e.puedeOperar), (anterior, actual) {
    if (actual && anterior != true) unawaited(refrescar());
  });
});

/// Cuántas fotos de la acomodación se exigen: las del servidor (o las de la copia local) o, sin ninguna, el valor de hoy.
int fotosAcomodacionMinimasDe(ParametrosApp? parametros, int porDefecto) =>
    parametros?.acomodacionFotosMinimas ?? porDefecto;

/// Desde cuántos metros se avisa de precisión baja: el del servidor o la copia local (null = el valor inicial de la regla).
double? precisionBajaDe(ParametrosApp? parametros) => parametros?.gpsPrecisionMinimaM;
