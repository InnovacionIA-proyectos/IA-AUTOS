import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/config/entorno.dart';
import '../core/db/app_database.dart';
import '../core/dispositivo/servicios.dart';
import '../core/dispositivo/servicios_reales.dart';
import '../core/http/cliente_api.dart';
import '../core/seguridad/almacen_sesion.dart';
import '../features/auth/data/auth_repositorio_api.dart';
import '../features/auth/domain/sesion.dart';
import '../features/auth/presentation/auth_controller.dart';
import '../features/custodia/data/usuarios_repositorio.dart';
import '../features/inspecciones/data/cache_lecturas.dart';
import '../features/inspecciones/data/inspecciones_repositorio.dart';
import '../features/sofasa/data/sofasa_repositorio.dart';
import '../features/sync/data/archivos_locales_io.dart';
import '../features/sync/data/cola_drift.dart';
import '../features/sync/data/estado_red_conectividad.dart';
import '../features/sync/data/gateway_informes_api.dart';
import '../features/sync/domain/procesador_cola.dart';
import '../features/sync/domain/puertos.dart';
import '../features/vehiculos/data/vehiculos_repositorio.dart';

/// El entorno se inyecta en `main` (desde las variables de compilación) y en las pruebas.
final entornoProvider = Provider<Entorno>((ref) => throw UnimplementedError('entornoProvider debe sobrescribirse'));

final appDatabaseProvider = Provider<AppDatabase>((ref) {
  final db = AppDatabase.abrir();
  ref.onDispose(db.close);
  return db;
});

final almacenSesionProvider = Provider<AlmacenSesion>((ref) => AlmacenSesionSeguro());

final authRepositorioProvider = Provider<AuthRepositorio>((ref) => AuthRepositorioApi(ref.watch(clienteApiProvider)));

/// El token y el aviso de 401 se leen de forma perezosa (dentro de los callbacks) para no crear un ciclo con el
/// controlador de sesión.
final clienteApiProvider = Provider<ClienteApi>((ref) {
  final entorno = ref.watch(entornoProvider);
  return ClienteApi(
    baseUrl: entorno.apiUrl,
    token: () async => ref.read(authControllerProvider).sesion?.token,
    alNoAutorizado: () => ref.read(authControllerProvider.notifier).sesionExpirada(),
  );
});

final estadoRedProvider = Provider<EstadoRed>((ref) => EstadoRedConectividad(apiUrl: ref.watch(entornoProvider).apiUrl));

final colaRepositorioProvider = Provider<ColaRepositorio>((ref) => ColaDrift(ref.watch(appDatabaseProvider)));

final procesadorColaProvider = Provider<ProcesadorCola>(
  (ref) => ProcesadorCola(
    cola: ref.watch(colaRepositorioProvider),
    gateway: GatewayInformesApi(ref.watch(clienteApiProvider)),
    red: ref.watch(estadoRedProvider),
    archivos: const ArchivosLocalesIo(),
  ),
);

final servicioUbicacionProvider = Provider<ServicioUbicacion>((ref) => const ServicioUbicacionGeolocator());
final servicioFotosProvider = Provider<ServicioFotos>((ref) => const ServicioFotosArchivos());

final cacheLecturasProvider = Provider<CacheLecturasRepo>((ref) => CacheLecturasRepo(ref.watch(appDatabaseProvider)));
final vehiculosRepositorioProvider = Provider<VehiculosRepositorio>((ref) => VehiculosRepositorio(ref.watch(clienteApiProvider)));
final usuariosRepositorioProvider = Provider<UsuariosRepositorio>((ref) => UsuariosRepositorio(ref.watch(clienteApiProvider)));
final sofasaRepositorioProvider = Provider<SofasaRepositorio>((ref) => SofasaRepositorio(ref.watch(clienteApiProvider)));
final inspeccionesRepositorioProvider = Provider<InspeccionesRepositorio>(
  (ref) => InspeccionesRepositorio(ref.watch(clienteApiProvider), ref.watch(cacheLecturasProvider)),
);
