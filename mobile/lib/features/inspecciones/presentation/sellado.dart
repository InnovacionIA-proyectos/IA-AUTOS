import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../domain/foto_local.dart';

/// Guarda la foto en el dispositivo y le pone su sello: fecha, ubicación, precisión y equipo.
///
/// El sello es de cuando se tomó y no se hereda de nadie: sin GPS la foto queda marcada «sin ubicación», que
/// es un dato, no un hueco. Devuelve null si no se pudo guardar.
Future<FotoLocal?> sellarFoto(WidgetRef ref, String temporal, {String? vistaId, String? nota}) async {
  try {
    final ruta = await ref.read(servicioFotosProvider).guardar(temporal);
    final pos = await ref.read(servicioUbicacionProvider).obtenerRapida();
    final dispositivo = await ref.read(servicioDispositivoProvider).id();
    return FotoLocal(
      ruta: ruta,
      selloFecha: DateTime.now().toUtc().toIso8601String(),
      selloLat: pos?.lat,
      selloLng: pos?.lng,
      vistaId: vistaId,
      nota: nota,
      precision: pos?.precision,
      dispositivoId: dispositivo,
    );
  } catch (_) {
    return null;
  }
}
