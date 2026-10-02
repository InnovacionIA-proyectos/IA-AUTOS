import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/presentation/auth_controller.dart';
import '../features/auth/presentation/login_page.dart';
import '../features/custodia/presentation/firma_page.dart';
import '../features/inspecciones/presentation/captura_page.dart';
import '../features/inspecciones/presentation/lista_inspecciones_page.dart';
import '../features/sofasa/presentation/sofasa_page.dart';
import '../features/sync/presentation/cola_page.dart';
import '../features/sync/presentation/sync_controller.dart';
import '../features/vehiculos/presentation/abrir_vin_page.dart';
import '../features/vehiculos/presentation/vin_page.dart';

/// Rutas de la app. El redirect de sesión es la única puerta: sin sesión solo existe /login.
final routerProvider = Provider<GoRouter>((ref) {
  final cambios = ValueNotifier<int>(0);
  ref.listen(authControllerProvider.select((e) => e.fase), (_, _) => cambios.value++);
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
          return ruta == '/login' || ruta == '/cargando' ? '/inspecciones' : null;
      }
    },
    routes: [
      GoRoute(path: '/cargando', builder: (_, _) => const Scaffold(body: Center(child: CircularProgressIndicator()))),
      GoRoute(path: '/login', builder: (_, _) => const LoginPage()),
      StatefulShellRoute.indexedStack(
        builder: (context, state, shell) => _Estructura(shell: shell),
        branches: [
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/inspecciones',
              builder: (_, _) => const ListaInspeccionesPage(),
              routes: [
                GoRoute(path: 'abrir', builder: (_, _) => const AbrirVinPage()),
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

/// Barra de navegación inferior: dos pestañas mínimas (herramienta de campo enfocada), con la cantidad de envíos
/// pendientes como insignia en Inspecciones.
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
          NavigationDestination(
            icon: Badge(isLabelVisible: pendientes > 0, label: Text('$pendientes'), child: const Icon(Icons.assignment_outlined)),
            selectedIcon: Badge(isLabelVisible: pendientes > 0, label: Text('$pendientes'), child: const Icon(Icons.assignment)),
            label: 'Inspecciones',
          ),
          const NavigationDestination(icon: Icon(Icons.local_shipping_outlined), selectedIcon: Icon(Icons.local_shipping), label: 'Sofasa'),
        ],
      ),
    );
  }
}
