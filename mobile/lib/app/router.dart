import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/presentation/auth_controller.dart';
import '../features/auth/presentation/cambiar_pin_page.dart';
import '../features/auth/presentation/login_page.dart';
import '../features/custodia/presentation/firma_page.dart';
import '../features/inspecciones/presentation/captura_page.dart';
import '../features/inspecciones/presentation/lista_inspecciones_page.dart';
import '../features/sofasa/presentation/sofasa_page.dart';
import '../features/sync/presentation/cola_page.dart';
import '../features/sync/presentation/sync_controller.dart';
import '../features/vehiculos/presentation/vin_page.dart';
import '../features/viajes/presentation/buscar_vin_page.dart';
import '../features/viajes/presentation/mis_viajes_page.dart';

/// Rutas de la app. El redirect de sesión es la única puerta: sin sesión solo existe /login.
final routerProvider = Provider<GoRouter>((ref) {
  final cambios = ValueNotifier<int>(0);
  ref.listen(authControllerProvider.select((e) => (e.fase, e.sesion?.usuario.debeCambiarPin)), (_, _) => cambios.value++);
  ref.onDispose(cambios.dispose);

  final router = GoRouter(
    initialLocation: '/cargando',
    refreshListenable: cambios,
    redirect: (context, state) {
      final fase = ref.read(authControllerProvider).fase;
      final ruta = state.matchedLocation;
      switch (fase) {
        case FaseAuth.cargando:
          return ruta == '/cargando' ? null : '/cargando';
        case FaseAuth.noAutenticado:
          return ruta == '/login' ? null : '/login';
        case FaseAuth.autenticado:
          // Con un PIN temporal el resto de la API responde 403: no tiene sentido dejar navegar.
          if (ref.read(authControllerProvider).sesion?.usuario.debeCambiarPin ?? false) {
            return ruta == '/cambiar-pin' ? null : '/cambiar-pin';
          }
          if (ruta == '/cambiar-pin') return '/viajes';
          return ruta == '/login' || ruta == '/cargando' ? '/viajes' : null;
      }
    },
    routes: [
      GoRoute(path: '/cargando', builder: (_, _) => const Scaffold(body: Center(child: CircularProgressIndicator()))),
      GoRoute(path: '/login', builder: (_, _) => const LoginPage()),
      GoRoute(path: '/cambiar-pin', builder: (_, _) => const CambiarPinPage()),
      StatefulShellRoute.indexedStack(
        builder: (context, state, shell) => _Estructura(shell: shell),
        branches: [
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/viajes',
              builder: (_, _) => const MisViajesPage(),
              routes: [GoRoute(path: 'buscar', builder: (_, _) => const BuscarVinPage())],
            ),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/inspecciones',
              builder: (_, _) => const ListaInspeccionesPage(),
              routes: [
                GoRoute(path: 'cola', builder: (_, _) => const ColaPage()),
                GoRoute(
                  path: 'vin/:vin',
                  builder: (_, state) => VinPage(vin: state.pathParameters['vin']!),
                  routes: [
                    GoRoute(
                      path: 'captura/:etapaId',
                      builder: (_, state) => CapturaPage(
                        vin: state.pathParameters['vin']!,
                        etapaId: state.pathParameters['etapaId']!,
                        proceso: state.uri.queryParameters['proceso'] ?? '',
                        viajeId: state.uri.queryParameters['viajeId'],
                      ),
                    ),
                    GoRoute(
                      path: 'firma/:inspeccionId',
                      builder: (_, state) => FirmaPage(
                        vin: state.pathParameters['vin']!,
                        inspeccionId: state.pathParameters['inspeccionId']!,
                        proceso: state.uri.queryParameters['proceso'] ?? '',
                        soloFirma: state.uri.queryParameters['soloFirma'] == '1',
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ]),
          StatefulShellBranch(routes: [GoRoute(path: '/sofasa', builder: (_, _) => const SofasaPage())]),
        ],
      ),
    ],
  );
  ref.onDispose(router.dispose);
  return router;
});

/// Barra de navegación inferior: las tres pantallas de campo, con «Mis viajes» primera porque es la entrada del
/// trabajo, y la cantidad de envíos pendientes como insignia en Inspecciones.
class _Estructura extends ConsumerWidget {
  const _Estructura({required this.shell});

  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final pendientes = ref.watch(syncControllerProvider.select((s) => s.pendientes + s.fallidos));
    return Scaffold(
      body: shell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: shell.currentIndex,
        onDestinationSelected: (i) => shell.goBranch(i, initialLocation: i == shell.currentIndex),
        destinations: [
          const NavigationDestination(
            icon: Icon(Icons.local_shipping_outlined),
            selectedIcon: Icon(Icons.local_shipping),
            label: 'Mis viajes',
          ),
          NavigationDestination(
            icon: Badge(isLabelVisible: pendientes > 0, label: Text('$pendientes'), child: const Icon(Icons.assignment_outlined)),
            selectedIcon: Badge(isLabelVisible: pendientes > 0, label: Text('$pendientes'), child: const Icon(Icons.assignment)),
            label: 'Inspecciones',
          ),
          const NavigationDestination(icon: Icon(Icons.inventory_2_outlined), selectedIcon: Icon(Icons.inventory_2), label: 'Sofasa'),
        ],
      ),
    );
  }
}
