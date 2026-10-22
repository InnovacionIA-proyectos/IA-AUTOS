import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/config/entorno.dart';
import '../core/db/app_database.dart';
import '../core/dispositivo/servicios.dart';
import '../core/dispositivo/servicios_reales.dart';
import '../core/http/cliente_api.dart';
import '../features/auth/presentation/auth_controller.dart';

// Providers transversales (entorno, base de datos, cliente HTTP y dispositivo). Los de cada funcionalidad viven en
// features/<feature>/providers.dart.

/// El entorno se inyecta en `main` (desde las variables de compilación) y en las pruebas.
final entornoProvider = Provider<Entorno>((ref) => throw UnimplementedError('entornoProvider debe sobrescribirse'));

final appDatabaseProvider = Provider<AppDatabase>((ref) {
  final db = AppDatabase.abrir();
  ref.onDispose(db.close);
  return db;
});

/// El token y el aviso de 401 se leen de forma perezosa (dentro de los callbacks) para no crear un ciclo con el
/// controlador de sesión.
final clienteApiProvider = Provider<ClienteApi>((ref) {
  final entorno = ref.watch(entornoProvider);
  return ClienteApi(
    baseUrl: entorno.apiUrl,
    token: () async => ref.read(authControllerProvider).sesion?.token,
    alNoAutorizado: () => ref.read(authControllerProvider.notifier).sesionExpirada(),
    alRequerirCambioDePin: () => ref.read(authControllerProvider.notifier).requiereCambioDePin(),
  );
});

final servicioUbicacionProvider = Provider<ServicioUbicacion>((ref) => const ServicioUbicacionGeolocator());
final servicioFotosProvider = Provider<ServicioFotos>((ref) => const ServicioFotosArchivos());
