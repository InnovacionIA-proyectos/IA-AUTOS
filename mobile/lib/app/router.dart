import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/domain/sesion.dart';
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
import 'pestanas.dart';
import 'sin_permisos_page.dart';

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
          // A su primera pestaña, no siempre a /viajes: quien no pueda verlas no tiene por qué aterrizar ahí.
          final inicio = destinoInicial(ref.read(authControllerProvider).sesion?.usuario);
          if (ruta == '/cambiar-pin') return inicio;
          return ruta == '/login' || ruta == '/cargando' ? inicio : null;
      }
    },
    routes: [
      GoRoute(path: '/cargando', builder: (_, _) => const Scaffold(body: Center(child: CircularProgressIndicator()))),
      GoRoute(path: '/login', builder: (_, _) => const LoginPage()),
      GoRoute(path: '/cambiar-pin', builder: (_, _) => const CambiarPinPage()),
      StatefulShellRoute.indexedStack(
        builder: (context, state, shell) => _Estructura(shell: shell),
        branches: [
          // Las tres ramas existen siempre: quitarlas correría los índices del shell. Lo que decide qué se ve es
          // `Permitido` en cada pantalla y el filtro de la barra de abajo.
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/viajes',
              builder: (_, _) => const Permitido(capacidad: Capacidad.verViajes, child: MisViajesPage()),
              routes: [
                GoRoute(
                  path: 'buscar',
                  builder: (_, _) => const Permitido(capacidad: Capacidad.verViajes, child: BuscarVinPage()),
                ),
              ],
            ),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/inspecciones',
              builder: (_, _) => const Permitido(capacidad: Capacidad.verInspecciones, child: ListaInspeccionesPage()),
              routes: [
                GoRoute(
                  path: 'cola',
                  builder: (_, _) => const Permitido(capacidad: Capacidad.verInspecciones, child: ColaPage()),
                ),
                GoRoute(
                  path: 'vin/:vin',
                  builder: (_, state) => Permitido(
                    capacidad: Capacidad.verInspecciones,
                    child: VinPage(vin: state.pathParameters['vin']!),
                  ),
                  routes: [
                    // Capturar y firmar exigen más que mirar: es la misma lista con la que el servidor autoriza
                    // esos endpoints, así que un comercial ve los informes pero no abre la captura.
                    GoRoute(
                      path: 'captura/:etapaId',
                      builder: (_, state) => Permitido(
                        capacidad: Capacidad.registrarInspecciones,
                        child: CapturaPage(
                          vin: state.pathParameters['vin']!,
                          etapaId: state.pathParameters['etapaId']!,
                          proceso: state.uri.queryParameters['proceso'] ?? '',
                          viajeId: state.uri.queryParameters['viajeId'],
                        ),
                      ),
                    ),
                    GoRoute(
                      path: 'firma/:inspeccionId',
                      builder: (_, state) => Permitido(
                        capacidad: Capacidad.registrarInspecciones,
                        child: FirmaPage(
                          vin: state.pathParameters['vin']!,
                          inspeccionId: state.pathParameters['inspeccionId']!,
                          proceso: state.uri.queryParameters['proceso'] ?? '',
                          soloFirma: state.uri.queryParameters['soloFirma'] == '1',
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ]),
          StatefulShellBranch(routes: [
            GoRoute(
              path: '/sofasa',
              builder: (_, _) => const Permitido(capacidad: Capacidad.gestionarViajes, child: SofasaPage()),
            ),
          ]),
        ],
      ),
    ],
  );
  ref.onDispose(router.dispose);
  return router;
});

/// Barra de navegación inferior: solo las pestañas que la persona puede ver, con «Mis viajes» primera porque es la
/// entrada del trabajo, y la cantidad de envíos pendientes como insignia en Inspecciones.
class _Estructura extends ConsumerWidget {
  const _Estructura({required this.shell});

  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final pendientes = ref.watch(syncControllerProvider.select((s) => s.pendientes + s.fallidos));
    final visibles = pestanasDe(ref.watch(authControllerProvider.select((e) => e.sesion?.usuario)));

    // Con una sola pestaña no hay nada entre qué elegir (y NavigationBar exige al menos dos).
    if (visibles.length < 2) return Scaffold(body: shell);

    // La barra enumera las visibles; el shell, todas. Se traduce de una a otra en los dos sentidos.
    final actual = visibles.indexOf(Pestana.values[shell.currentIndex]);
    return Scaffold(
      body: shell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: actual < 0 ? 0 : actual,
        onDestinationSelected: (i) {
          final rama = Pestana.values.indexOf(visibles[i]);
          shell.goBranch(rama, initialLocation: rama == shell.currentIndex);
        },
        destinations: [
          for (final pestana in visibles)
            NavigationDestination(
              icon: _conInsignia(pestana, pestana.icono, pendientes),
              selectedIcon: _conInsignia(pestana, pestana.iconoActivo, pendientes),
              label: pestana.etiqueta,
            ),
        ],
      ),
    );
  }

  Widget _conInsignia(Pestana pestana, IconData icono, int pendientes) => pestana == Pestana.inspecciones
      ? Badge(isLabelVisible: pendientes > 0, label: Text('$pendientes'), child: Icon(icono))
      : Icon(icono);
}
